// Translates Zenject `DiContainer` consumers to VContainer `IObjectResolver`
// (spec §5.9). Conservative M2 strategy:
//   - Field / parameter / variable types annotated as `DiContainer` rename to
//     `IObjectResolver` whenever the declared symbol resolves to Zenject's
//     DiContainer.
//   - `Resolve<T>()` calls are left alone — name and arity match.
//   - `Instantiate<T>()` and `InstantiatePrefab(...)` are flagged with manual
//     TODO `[InstantiateUnregistered]`. Determining whether T is registered
//     vs unregistered requires the BindingRegistry threading planned for M3,
//     so we conservatively defer the decision to the developer.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Zenject2VContainer.Core.Scanner;

namespace Zenject2VContainer.CSharp.Rewriters {
    public sealed class DiContainerUsageRewriter : RewriterBase {
        public override string Name => nameof(DiContainerUsageRewriter);

        public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node) {
            if (node.Identifier.Text != "DiContainer") return base.VisitIdentifierName(node);
            var sym = Model.GetSymbolInfo(node).Symbol as INamedTypeSymbol;
            if (sym == null || !SymbolMatchers.IsZenjectSymbol(sym) || sym.Name != "DiContainer") {
                return base.VisitIdentifierName(node);
            }
            return SyntaxFactory.IdentifierName("IObjectResolver").WithTriviaFrom(node);
        }

        public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node) {
            var visited = (InvocationExpressionSyntax)base.VisitInvocationExpression(node);

            if (visited.Expression is MemberAccessExpressionSyntax ma) {
                var nameNode = ma.Name;
                string callName = nameNode is GenericNameSyntax g ? g.Identifier.Text
                                : nameNode is IdentifierNameSyntax i ? i.Identifier.Text
                                : null;
                if (callName == "Instantiate" || callName == "InstantiatePrefab") {
                    EmitManualTodo(ManualTodoEmitter.InstantiateUnregistered, node,
                        callName + " has no direct translation; review whether Resolve<T>() or VContainer.Unity.Instantiate(prefab) applies.");
                }
            }
            return visited;
        }
    }
}
