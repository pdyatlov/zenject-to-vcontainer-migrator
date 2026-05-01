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

        public override SyntaxNode VisitCompilationUnit(CompilationUnitSyntax node) {
            var visited = (CompilationUnitSyntax)base.VisitCompilationUnit(node);
            if (_needsVContainerUnity) {
                visited = UsingDirectiveRewriter.EnsureVContainerUnityUsing(visited);
            }
            return visited;
        }

        public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node) {
            _classImplementsInitializable = false;

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

            var visited = (ClassDeclarationSyntax)base.VisitClassDeclaration(node);

            if (_classImplementsInitializable && node.BaseList != null) {
                visited = ReplaceBaseTypeName(visited, "IInitializable", "IStartable");
                _needsVContainerUnity = true;
            }
            return visited;
        }

        public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node) {
            var visited = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node);
            if (_classImplementsInitializable && visited.Identifier.Text == "Initialize") {
                visited = visited.WithIdentifier(SyntaxFactory.Identifier("Start").WithTriviaFrom(visited.Identifier));
            }
            return visited;
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
