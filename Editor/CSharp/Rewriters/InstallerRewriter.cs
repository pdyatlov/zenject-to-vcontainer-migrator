// Translates Zenject installer subclasses into VContainer IInstaller form.
//
// Three input shapes (all detected via SemanticModel base-type walk):
//   - MonoInstaller         — keep MonoInstaller base for asset compatibility,
//                             add IInstaller, retain InstallBindings as a
//                             /* legacy entry */ stub, emit Install method.
//   - ScriptableObjectInstaller — replace base with UnityEngine.ScriptableObject
//                             (asset GUID is on the .cs.meta and untouched),
//                             add IInstaller, same legacy stub + Install method.
//   - Installer<T> POCO     — drop the base type entirely (replace with
//                             IInstaller). Has no Unity-side caller, so the
//                             legacy InstallBindings stub is dropped — only
//                             Install method remains.
//
// BindToAsRewriter has already rewritten Container.Bind→builder.Register inside
// the original InstallBindings body before this rewriter runs, so the body we
// transplant into Install already carries the translated chain.
//
// The folded LifetimeScope variant for single-MonoInstaller scenes is produced
// in M3 once YAML scanning supplies the fold hint.

using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Zenject2VContainer.Core.Scanner;

namespace Zenject2VContainer.CSharp.Rewriters {
    public sealed class InstallerRewriter : RewriterBase {
        public override string Name => nameof(InstallerRewriter);

        private enum InstallerKind { None, MonoInstaller, ScriptableObjectInstaller, GenericInstaller }

        public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node) {
            var symbol = Model.GetDeclaredSymbol(node) as INamedTypeSymbol;
            if (symbol == null) return base.VisitClassDeclaration(node);
            var kind = DetectInstallerKind(symbol);
            if (kind == InstallerKind.None) return base.VisitClassDeclaration(node);
            return TransformInstaller(node, kind) ?? (SyntaxNode)node;
        }

        private static InstallerKind DetectInstallerKind(INamedTypeSymbol cls) {
            for (var b = cls.BaseType; b != null; b = b.BaseType) {
                if (!SymbolMatchers.IsZenjectSymbol(b)) continue;
                if (b.Name == "MonoInstaller") return InstallerKind.MonoInstaller;
                if (b.Name == "ScriptableObjectInstaller") return InstallerKind.ScriptableObjectInstaller;
                if (b.Name == "Installer" && b.TypeArguments.Length == 1) return InstallerKind.GenericInstaller;
            }
            return InstallerKind.None;
        }

        private ClassDeclarationSyntax TransformInstaller(ClassDeclarationSyntax cls, InstallerKind kind) {
            MethodDeclarationSyntax installBindings = null;
            foreach (var m in cls.Members) {
                if (m is MethodDeclarationSyntax md && md.Identifier.Text == "InstallBindings") {
                    installBindings = md;
                    break;
                }
            }
            if (installBindings == null || installBindings.Body == null) return null;

            BaseListSyntax newBaseList;
            switch (kind) {
                case InstallerKind.MonoInstaller:
                    newBaseList = AddIInstallerBase(cls.BaseList);
                    break;
                case InstallerKind.ScriptableObjectInstaller:
                    newBaseList = ReplaceBaseAndAddIInstaller(cls.BaseList,
                        "ScriptableObjectInstaller", "ScriptableObject");
                    break;
                case InstallerKind.GenericInstaller:
                    newBaseList = ReplaceGenericInstallerWithIInstaller(cls.BaseList);
                    break;
                default: return null;
            }

            // Derive sibling indent (e.g. "    " at file root, "        " inside a
            // namespace) from InstallBindings' own leading trivia so the synthesised
            // Install method aligns with the other class members.
            var siblingIndent = ManualTodoEmitter.ExtractLineIndent(installBindings.GetLeadingTrivia());
            if (string.IsNullOrEmpty(siblingIndent)) siblingIndent = "    ";
            // Body content is one indent level deeper than its enclosing method.
            var bodyIndent = siblingIndent + "    ";
            var installMethod = BuildInstallMethod(installBindings.Body, siblingIndent, bodyIndent);
            var newMembers = new List<MemberDeclarationSyntax>(cls.Members.Count + 1);

            if (kind == InstallerKind.GenericInstaller) {
                // POCO: no Unity-side caller. Drop legacy InstallBindings stub —
                // replace it directly with Install method, preserving original leading trivia.
                installMethod = installMethod.WithLeadingTrivia(installBindings.GetLeadingTrivia());
                foreach (var m in cls.Members) {
                    if (m == installBindings) {
                        newMembers.Add(installMethod);
                    } else {
                        newMembers.Add(m);
                    }
                }
            } else {
                // MonoInstaller / ScriptableObjectInstaller: keep legacy stub for
                // asset / Unity callsite compatibility.
                var origParamList = installBindings.ParameterList;
                var slimParamList = origParamList.WithCloseParenToken(
                    origParamList.CloseParenToken.WithTrailingTrivia(SyntaxTriviaList.Empty));
                var legacyMethod = installBindings
                    .WithParameterList(slimParamList)
                    .WithBody(BuildLegacyEntryBlock());
                foreach (var m in cls.Members) {
                    if (m == installBindings) {
                        newMembers.Add(legacyMethod);
                        newMembers.Add(installMethod);
                    } else {
                        newMembers.Add(m);
                    }
                }
            }

            return cls.WithBaseList(newBaseList).WithMembers(SyntaxFactory.List(newMembers));
        }

        private static BaseListSyntax AddIInstallerBase(BaseListSyntax existing) {
            var iinstaller = SyntaxFactory.SimpleBaseType(SyntaxFactory.IdentifierName("IInstaller"));
            if (existing == null) {
                return SyntaxFactory.BaseList(
                    SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(iinstaller));
            }
            foreach (var bt in existing.Types) {
                if (bt.Type.ToString() == "IInstaller") return existing;
            }

            int lastIdx = existing.Types.Count - 1;
            var lastTyped = existing.Types[lastIdx];
            var preservedTrailing = lastTyped.GetTrailingTrivia();
            var stripped = lastTyped.WithTrailingTrivia(SyntaxTriviaList.Empty);

            var nodesAndTokens = new List<SyntaxNodeOrToken>();
            for (int i = 0; i < lastIdx; i++) {
                nodesAndTokens.Add(existing.Types[i]);
                nodesAndTokens.Add(existing.Types.GetSeparator(i));
            }
            nodesAndTokens.Add(stripped);
            var commaSpace = SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space);
            nodesAndTokens.Add(commaSpace);
            nodesAndTokens.Add(iinstaller.WithTrailingTrivia(preservedTrailing));
            return existing.WithTypes(SyntaxFactory.SeparatedList<BaseTypeSyntax>(nodesAndTokens));
        }

        private static BaseListSyntax ReplaceBaseAndAddIInstaller(BaseListSyntax existing, string oldName, string newName) {
            var iinstaller = SyntaxFactory.SimpleBaseType(SyntaxFactory.IdentifierName("IInstaller"));
            if (existing == null) {
                return SyntaxFactory.BaseList(
                    SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(iinstaller));
            }

            int targetIdx = -1;
            for (int i = 0; i < existing.Types.Count; i++) {
                var typeText = existing.Types[i].Type.ToString();
                if (typeText == oldName || typeText.StartsWith(oldName + "<")) {
                    targetIdx = i;
                    break;
                }
            }
            if (targetIdx < 0) return AddIInstallerBase(existing);

            int lastIdx = existing.Types.Count - 1;
            var preservedTrailing = existing.Types[lastIdx].GetTrailingTrivia();

            var nodesAndTokens = new List<SyntaxNodeOrToken>();
            for (int i = 0; i < existing.Types.Count; i++) {
                BaseTypeSyntax t = existing.Types[i];
                if (i == targetIdx) {
                    var origType = t.Type;
                    var replacementName = SyntaxFactory.IdentifierName(newName)
                        .WithLeadingTrivia(origType.GetLeadingTrivia())
                        .WithTrailingTrivia(origType.GetTrailingTrivia());
                    t = SyntaxFactory.SimpleBaseType(replacementName);
                }
                if (i == lastIdx) {
                    t = t.WithTrailingTrivia(SyntaxTriviaList.Empty);
                }
                nodesAndTokens.Add(t);
                if (i < existing.Types.Count - 1) {
                    nodesAndTokens.Add(existing.Types.GetSeparator(i));
                }
            }
            var commaSpace = SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space);
            nodesAndTokens.Add(commaSpace);
            nodesAndTokens.Add(iinstaller.WithTrailingTrivia(preservedTrailing));
            return existing.WithTypes(SyntaxFactory.SeparatedList<BaseTypeSyntax>(nodesAndTokens));
        }

        private static BaseListSyntax ReplaceGenericInstallerWithIInstaller(BaseListSyntax existing) {
            if (existing == null) {
                return SyntaxFactory.BaseList(SyntaxFactory.SingletonSeparatedList<BaseTypeSyntax>(
                    SyntaxFactory.SimpleBaseType(SyntaxFactory.IdentifierName("IInstaller"))));
            }
            int targetIdx = -1;
            for (int i = 0; i < existing.Types.Count; i++) {
                var typeText = existing.Types[i].Type.ToString();
                if (typeText == "Installer" || typeText.StartsWith("Installer<")) {
                    targetIdx = i;
                    break;
                }
            }
            if (targetIdx < 0) return existing;

            var target = existing.Types[targetIdx];
            var replacementType = SyntaxFactory.IdentifierName("IInstaller")
                .WithLeadingTrivia(target.Type.GetLeadingTrivia())
                .WithTrailingTrivia(target.Type.GetTrailingTrivia());
            var replacement = SyntaxFactory.SimpleBaseType(replacementType)
                .WithTrailingTrivia(target.GetTrailingTrivia());
            var newTypes = existing.Types.Replace(target, replacement);
            return existing.WithTypes(newTypes);
        }

        private static BlockSyntax BuildLegacyEntryBlock() {
            var openBrace = SyntaxFactory.Token(
                SyntaxFactory.TriviaList(SyntaxFactory.Space),
                SyntaxKind.OpenBraceToken,
                SyntaxFactory.TriviaList(
                    SyntaxFactory.Space,
                    SyntaxFactory.Comment("/* legacy entry */"),
                    SyntaxFactory.Space));
            var closeBrace = SyntaxFactory.Token(SyntaxKind.CloseBraceToken);
            return SyntaxFactory.Block().WithOpenBraceToken(openBrace).WithCloseBraceToken(closeBrace);
        }

        private static MethodDeclarationSyntax BuildInstallMethod(BlockSyntax originalBody, string siblingIndent, string bodyIndent) {
            var publicMod = SyntaxFactory.Token(SyntaxKind.PublicKeyword)
                .WithTrailingTrivia(SyntaxFactory.Space);
            var voidType = SyntaxFactory.PredefinedType(
                SyntaxFactory.Token(SyntaxKind.VoidKeyword).WithTrailingTrivia(SyntaxFactory.Space));
            var paramType = SyntaxFactory.IdentifierName("IContainerBuilder")
                .WithTrailingTrivia(SyntaxFactory.Space);
            var param = SyntaxFactory.Parameter(SyntaxFactory.Identifier("builder"))
                .WithType(paramType);
            var paramList = SyntaxFactory.ParameterList(SyntaxFactory.SingletonSeparatedList(param));

            // Roslyn attributes line-ending newlines to the preceding token's TRAILING
            // trivia, leaving the body's open brace with only the indent. Force the
            // body open brace's leading trivia back to "\n<bodyIndent>" so it lands on
            // its own line at the correct depth.
            var bodyWithNewline = originalBody.WithOpenBraceToken(
                originalBody.OpenBraceToken.WithLeadingTrivia(
                    SyntaxFactory.TriviaList(
                        SyntaxFactory.EndOfLine("\n"),
                        SyntaxFactory.Whitespace(siblingIndent))));
            var method = SyntaxFactory.MethodDeclaration(voidType, SyntaxFactory.Identifier("Install"))
                .WithModifiers(SyntaxFactory.TokenList(publicMod))
                .WithParameterList(paramList)
                .WithBody(bodyWithNewline);

            // Method declaration leading: blank line + sibling indent (used after legacy stub).
            var leading = SyntaxFactory.TriviaList(
                SyntaxFactory.EndOfLine("\n"),
                SyntaxFactory.EndOfLine("\n"),
                SyntaxFactory.Whitespace(siblingIndent));
            return method.WithLeadingTrivia(leading);
        }
    }
}
