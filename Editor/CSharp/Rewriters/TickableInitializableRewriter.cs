// Maps Zenject lifecycle interfaces to VContainer equivalents:
//   IInitializable.Initialize -> IStartable.Start                  (rename type + method)
//   ITickable / ILateTickable / IFixedTickable                     (same name, namespace
//     moves from Zenject -> VContainer.Unity; UsingDirectiveRewriter
//     handles the using switch)
//   IDisposable                                                    (System.IDisposable; no-op)
//
// Walks ClassDeclarationSyntax base lists and replaces matching Zenject interface
// references. When a class implements IInitializable, also renames any
// `Initialize` method to `Start`. Adds `using VContainer.Unity;` to the file when
// any tickable interface is touched (UsingDirectiveRewriter swaps the Zenject
// using; this rewriter ensures the Unity-flavoured namespace is also imported).

using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Zenject2VContainer.Core.Scanner;

namespace Zenject2VContainer.CSharp.Rewriters {
    public sealed class TickableInitializableRewriter : RewriterBase {
        public override string Name => nameof(TickableInitializableRewriter);

        private bool _needsVContainerUnity;
        private bool _classImplementsInitializable;
        private bool _classHasStartMethod;

        public override SyntaxNode VisitCompilationUnit(CompilationUnitSyntax node) {
            var visited = (CompilationUnitSyntax)base.VisitCompilationUnit(node);
            if (_needsVContainerUnity) {
                visited = UsingDirectiveRewriter.EnsureVContainerUnityUsing(visited);
            }
            return visited;
        }

        public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node) {
            _classImplementsInitializable = false;
            _classHasStartMethod = false;

            // Look at base list to detect Zenject lifecycle interfaces.
            if (node.BaseList != null) {
                foreach (var bt in node.BaseList.Types) {
                    var sym = Model.GetSymbolInfo(bt.Type).Symbol as INamedTypeSymbol;
                    if (sym == null || !SymbolMatchers.IsZenjectSymbol(sym)) continue;
                    switch (sym.Name) {
                        case "IInitializable": _classImplementsInitializable = true; break;
                        case "ITickable":
                        case "ILateTickable":
                        case "IFixedTickable":
                            _needsVContainerUnity = true;
                            break;
                    }
                }
            }

            // Detect existing parameterless Start() to avoid collision with the
            // IStartable.Start we'd produce by renaming Initialize().
            foreach (var m in node.Members) {
                if (m is MethodDeclarationSyntax md
                    && md.Identifier.Text == "Start"
                    && md.ParameterList.Parameters.Count == 0) {
                    _classHasStartMethod = true;
                    break;
                }
            }

            var visited = (ClassDeclarationSyntax)base.VisitClassDeclaration(node);

            if (_classImplementsInitializable && node.BaseList != null) {
                if (_classHasStartMethod) {
                    // Cannot rename Initialize -> Start without colliding with
                    // existing Start(). Strip IInitializable from the base list,
                    // leave Initialize() unchanged, surface a manual TODO.
                    visited = RemoveBaseTypeName(visited, "IInitializable");
                    EmitManualTodo(ManualTodoEmitter.LifecycleStartCollision, node,
                        "Class implements IInitializable but already declares Start(); removed IInitializable. Manually delegate Start() to Initialize() if you need IStartable behavior.");
                } else {
                    visited = ReplaceBaseTypeName(visited, "IInitializable", "IStartable");
                    _needsVContainerUnity = true;
                }
            }
            return visited;
        }

        public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node) {
            var visited = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node);
            if (_classImplementsInitializable && !_classHasStartMethod && visited.Identifier.Text == "Initialize") {
                visited = visited.WithIdentifier(SyntaxFactory.Identifier("Start").WithTriviaFrom(visited.Identifier));
            }
            return visited;
        }

        private static ClassDeclarationSyntax RemoveBaseTypeName(ClassDeclarationSyntax cls, string name) {
            if (cls.BaseList == null) return cls;
            BaseTypeSyntax target = null;
            foreach (var bt in cls.BaseList.Types) {
                if (bt.Type is IdentifierNameSyntax idn && idn.Identifier.Text == name) {
                    target = bt;
                    break;
                }
            }
            if (target == null) return cls;
            var newTypes = cls.BaseList.Types.Remove(target);
            if (newTypes.Count == 0) {
                // Remove the entire base list including the colon token.
                return cls.WithBaseList(null).WithIdentifier(cls.Identifier.WithTrailingTrivia(SyntaxFactory.Space));
            }
            return cls.WithBaseList(cls.BaseList.WithTypes(newTypes));
        }

        private static ClassDeclarationSyntax ReplaceBaseTypeName(ClassDeclarationSyntax cls, string oldName, string newName) {
            if (cls.BaseList == null) return cls;
            var newTypes = cls.BaseList.Types;
            for (int i = 0; i < newTypes.Count; i++) {
                var bt = newTypes[i];
                if (bt.Type is IdentifierNameSyntax idn && idn.Identifier.Text == oldName) {
                    var replacement = SyntaxFactory.SimpleBaseType(
                        SyntaxFactory.IdentifierName(newName)
                            .WithLeadingTrivia(idn.GetLeadingTrivia())
                            .WithTrailingTrivia(idn.GetTrailingTrivia()))
                        .WithTrailingTrivia(bt.GetTrailingTrivia());
                    newTypes = newTypes.Replace(bt, replacement);
                }
            }
            return cls.WithBaseList(cls.BaseList.WithTypes(newTypes));
        }
    }
}
