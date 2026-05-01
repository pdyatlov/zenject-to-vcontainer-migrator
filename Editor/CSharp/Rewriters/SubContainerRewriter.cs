// Sub-container handling (spec §5.8). Conservative M2 strategy: every
// `Container.Bind<...>().FromSubContainerResolve()...` chain is flagged with a
// manual TODO `[ComplexSubContainer]` and the original chain is preserved.
//
// Automating the simple `ByInstaller<X>().AsSingle()` case requires generating
// a child LifetimeScope class plus its registration in the parent scope —
// cross-file emission that doesn't fit M2's per-file rewrite model. M3 picks
// this up alongside YAML asset transformation.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Zenject2VContainer.CSharp.Rewriters {
    public sealed class SubContainerRewriter : RewriterBase {
        public override string Name => nameof(SubContainerRewriter);

        public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node) {
            if (node.Expression is MemberAccessExpressionSyntax ma
                && ma.Name is IdentifierNameSyntax idn
                && idn.Identifier.Text == "FromSubContainerResolve") {
                EmitManualTodo(ManualTodoEmitter.ComplexSubContainer, node,
                    "FromSubContainerResolve has no automated translation in M2 — manual rewrite to a child LifetimeScope is required.");
            }
            return base.VisitInvocationExpression(node);
        }
    }
}
