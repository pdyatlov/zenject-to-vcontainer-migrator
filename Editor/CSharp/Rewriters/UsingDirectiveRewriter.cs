using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Zenject2VContainer.CSharp.Rewriters {
    public sealed class UsingDirectiveRewriter : RewriterBase {
        public override string Name => nameof(UsingDirectiveRewriter);

        public bool RequiresVContainerUnity { get; private set; }

        public override SyntaxNode VisitUsingDirective(UsingDirectiveSyntax node) {
            var name = node.Name?.ToString();
            if (string.Equals(name, "Zenject", System.StringComparison.Ordinal)) {
                return node.WithName(SyntaxFactory.ParseName("VContainer")
                    .WithTriviaFrom(node.Name));
            }
            return base.VisitUsingDirective(node);
        }

        // Caller may also call this to add `using VContainer.Unity;` after another
        // rewriter has signalled that Unity-specific surface is needed.
        public static CompilationUnitSyntax EnsureVContainerUnityUsing(CompilationUnitSyntax root) {
            foreach (var u in root.Usings) {
                if (u.Name?.ToString() == "VContainer.Unity") return root;
            }
            var newUsing = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("VContainer.Unity"))
                .WithTrailingTrivia(SyntaxFactory.EndOfLine("\n"));
            return root.AddUsings(newUsing);
        }
    }
}
