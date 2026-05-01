using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Zenject2VContainer.Core.Scanner;

namespace Zenject2VContainer.CSharp.Rewriters {
    public sealed class BindToAsRewriter : RewriterBase {
        public override string Name => nameof(BindToAsRewriter);

        public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node) {
            // Visit children first so nested invocations are processed.
            var visited = (InvocationExpressionSyntax)base.VisitInvocationExpression(node);

            // Only top-most invocations (those whose parent is NOT a MemberAccessExpression
            // continuing a fluent chain) should be transformed; otherwise we'd transform
            // mid-chain pieces redundantly.
            if (visited.Parent is MemberAccessExpressionSyntax parentMa
                && parentMa.Expression == visited
                && parentMa.Parent is InvocationExpressionSyntax) {
                return visited;
            }

            var chain = CollectChain(visited);
            if (chain.Count < 2) return visited; // need at least Bind + lifetime

            var rewritten = TryRewriteBindChain(chain);
            return rewritten ?? (SyntaxNode)visited;
        }

        // Returns invocations from head to tail (Bind first, AsSingle last).
        private static List<InvocationExpressionSyntax> CollectChain(InvocationExpressionSyntax tail) {
            var list = new List<InvocationExpressionSyntax>();
            InvocationExpressionSyntax cur = tail;
            while (cur != null) {
                list.Insert(0, cur);
                if (cur.Expression is MemberAccessExpressionSyntax m
                    && m.Expression is InvocationExpressionSyntax inner) {
                    cur = inner;
                } else {
                    break;
                }
            }
            return list;
        }

        private InvocationExpressionSyntax TryRewriteBindChain(IReadOnlyList<InvocationExpressionSyntax> chain) {
            // Head must be Container.Bind<T>().
            var head = chain[0];
            if (!(head.Expression is MemberAccessExpressionSyntax headMa)) return null;
            if (!(headMa.Name is GenericNameSyntax bindGeneric)) return null;
            if (bindGeneric.Identifier.Text != "Bind") return null;
            // Confirm via semantic model.
            var headSym = Model.GetSymbolInfo(head).Symbol;
            if (headSym == null || !SymbolMatchers.IsZenjectSymbol(headSym)) return null;
            if (headSym.Name != "Bind") return null;

            var bindT = bindGeneric.TypeArgumentList.Arguments[0];

            TypeSyntax toU = null;
            string lifetime = null;
            int i = 1;
            while (i < chain.Count) {
                var inv = chain[i];
                if (!(inv.Expression is MemberAccessExpressionSyntax ma)) return null;
                var name = ma.Name;
                if (name is GenericNameSyntax g && g.Identifier.Text == "To" && g.TypeArgumentList.Arguments.Count == 1) {
                    toU = g.TypeArgumentList.Arguments[0];
                } else if (name is IdentifierNameSyntax idn) {
                    switch (idn.Identifier.Text) {
                        case "AsSingle": lifetime = "Singleton"; break;
                        case "AsTransient": lifetime = "Transient"; break;
                        case "AsCached": lifetime = "Scoped"; break;
                        default: return null; // unsupported in Task 6
                    }
                } else {
                    return null;
                }
                i++;
            }

            if (lifetime == null) return null;
            if (toU == null) toU = bindT;

            // Build: builder.Register<toU>(Lifetime.lifetime).As<bindT>()  -- skip As<> if toU == bindT.
            var registerGeneric = SyntaxFactory.GenericName(SyntaxFactory.Identifier("Register"))
                .WithTypeArgumentList(SyntaxFactory.TypeArgumentList(SyntaxFactory.SingletonSeparatedList(toU)));
            var registerCall = SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName("builder"),
                    registerGeneric))
                .WithArgumentList(SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(
                            SyntaxFactory.MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                SyntaxFactory.IdentifierName("Lifetime"),
                                SyntaxFactory.IdentifierName(lifetime))))));

            ExpressionSyntax tail = registerCall;
            if (!toU.IsEquivalentTo(bindT)) {
                var asGeneric = SyntaxFactory.GenericName(SyntaxFactory.Identifier("As"))
                    .WithTypeArgumentList(SyntaxFactory.TypeArgumentList(SyntaxFactory.SingletonSeparatedList(bindT)));
                var asCall = SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(
                        SyntaxKind.SimpleMemberAccessExpression,
                        tail,
                        asGeneric));
                tail = asCall;
            }

            // Preserve trivia from the original chain's leading position.
            var lastInChain = chain[chain.Count - 1];
            return ((InvocationExpressionSyntax)tail).WithTriviaFrom(lastInChain);
        }
    }
}
