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
            // Head must be Container.Bind<T>() / BindInterfacesTo<T>() / BindInterfacesAndSelfTo<T>().
            var head = chain[0];
            if (!(head.Expression is MemberAccessExpressionSyntax headMa)) return null;
            if (!(headMa.Name is GenericNameSyntax bindGeneric)) return null;

            var headName = bindGeneric.Identifier.Text;
            bool isBind = headName == "Bind";
            bool isInterfaces = headName == "BindInterfacesTo";
            bool isInterfacesAndSelf = headName == "BindInterfacesAndSelfTo";
            bool isBindFactory = headName == "BindFactory";
            if (!(isBind || isInterfaces || isInterfacesAndSelf || isBindFactory)) return null;

            // Confirm via semantic model.
            var headSym = Model.GetSymbolInfo(head).Symbol;
            if (headSym == null || !SymbolMatchers.IsZenjectSymbol(headSym)) return null;
            if (headSym.Name != headName) return null;

            var lastInChainEarly = chain[chain.Count - 1];
            if (isBindFactory) {
                // BindFactory<...>() requires hand-rewriting to RegisterFactory<TArg, TOut>(...).
                // Preserve the original chain and emit a manual TODO.
                EmitManualTodo(ManualTodoEmitter.CustomFactory, lastInChainEarly,
                    "Container.BindFactory<...> has no automated translation; rewrite as builder.RegisterFactory<TArg, TOut>(...).");
                var bfIndent = ManualTodoEmitter.ExtractLineIndent(lastInChainEarly.GetLeadingTrivia());
                var bfTrivia = ManualTodoEmitter.Build(
                    ManualTodoEmitter.CustomFactory,
                    "Container.BindFactory<...> has no automated translation; rewrite as builder.RegisterFactory<TArg, TOut>(...).",
                    bfIndent);
                return lastInChainEarly.WithLeadingTrivia(lastInChainEarly.GetLeadingTrivia().AddRange(bfTrivia));
            }

            var bindT = bindGeneric.TypeArgumentList.Arguments[0];

            TypeSyntax toU = null;
            string lifetime = null;
            string sourceKind = null;
            ArgumentListSyntax sourceArgs = null;
            ArgumentSyntax withIdArg = null;
            TypeSyntax withIdKeyType = null;
            bool hasConditional = false;
            string unsupportedReason = null;
            int i = 1;
            while (i < chain.Count) {
                var inv = chain[i];
                if (!(inv.Expression is MemberAccessExpressionSyntax ma)) return null;
                var name = ma.Name;
                if (name is GenericNameSyntax g) {
                    var gName = g.Identifier.Text;
                    if (gName == "To" && g.TypeArgumentList.Arguments.Count == 1) {
                        if (!isBind) return null; // BindInterfacesTo<T> head doesn't take .To<U>
                        toU = g.TypeArgumentList.Arguments[0];
                    } else if (gName == "WhenInjectedInto" || gName == "WhenNotInjectedInto") {
                        hasConditional = true;
                    } else {
                        unsupportedReason = "." + gName + "<...> has no automated translation in M2.";
                    }
                } else if (name is IdentifierNameSyntax idn) {
                    var n = idn.Identifier.Text;
                    switch (n) {
                        case "AsSingle": lifetime = "Singleton"; break;
                        case "AsTransient": lifetime = "Transient"; break;
                        case "AsCached": lifetime = "Scoped"; break;
                        case "FromInstance":
                        case "FromMethod":
                        case "FromComponentInHierarchy":
                        case "FromComponentInNewPrefab":
                        case "FromNewComponentOnNewGameObject":
                            if (sourceKind != null) return null; // multiple source clauses unsupported
                            sourceKind = n;
                            sourceArgs = inv.ArgumentList;
                            break;
                        case "WithId":
                            if (inv.ArgumentList.Arguments.Count != 1) return null;
                            withIdArg = inv.ArgumentList.Arguments[0];
                            var keyTypeSym = Model.GetTypeInfo(withIdArg.Expression).Type;
                            if (keyTypeSym == null || keyTypeSym.TypeKind == TypeKind.Error) {
                                withIdKeyType = SyntaxFactory.PredefinedType(
                                    SyntaxFactory.Token(SyntaxKind.StringKeyword));
                            } else {
                                withIdKeyType = SyntaxFactory.ParseTypeName(keyTypeSym.ToDisplayString());
                            }
                            break;
                        default:
                            unsupportedReason = "." + n + "(...) has no automated translation in M2.";
                            break;
                    }
                } else {
                    return null;
                }
                i++;
            }

            ExpressionSyntax tail;
            var lastInChain = chain[chain.Count - 1];

            if (hasConditional || unsupportedReason != null) {
                // Manual TODO + preserve chain. WhenInjectedInto chains use the
                // ConditionalBind category; arbitrary unsupported clauses (NonLazy,
                // WithArguments, etc.) reuse the same catch-all so the developer
                // sees a flag at the call site.
                var reason = hasConditional
                    ? "VContainer has no equivalent for .WhenInjectedInto / .WhenNotInjectedInto."
                    : unsupportedReason;
                EmitManualTodo(ManualTodoEmitter.ConditionalBind, lastInChain, reason);
                var indent = ManualTodoEmitter.ExtractLineIndent(lastInChain.GetLeadingTrivia());
                var trivia = ManualTodoEmitter.Build(
                    ManualTodoEmitter.ConditionalBind,
                    reason,
                    indent);
                return lastInChain.WithLeadingTrivia(lastInChain.GetLeadingTrivia().AddRange(trivia));
            }

            if (sourceKind == null) {
                // No source clause: keep existing Register<T>(Lifetime.X) form.
                if (lifetime == null) return null;

                TypeSyntax registerType = isBind ? (toU ?? bindT) : bindT;
                tail = BuildRegister(registerType, lifetime);
                if (isBind) {
                    if (!registerType.IsEquivalentTo(bindT)) {
                        tail = AppendGenericCall(tail, "As", bindT);
                    }
                } else {
                    tail = AppendIdentifierCall(tail, "AsImplementedInterfaces");
                    if (isInterfacesAndSelf) {
                        tail = AppendIdentifierCall(tail, "AsSelf");
                    }
                }
                if (withIdArg != null && withIdKeyType != null) {
                    tail = AppendGenericCallWithArg(tail, "Keyed", withIdKeyType, withIdArg);
                }
                return ((InvocationExpressionSyntax)tail).WithTriviaFrom(lastInChain);
            }

            // Source-based chains. Plain Bind<T> heads support all source variants;
            // BindInterfacesTo / BindInterfacesAndSelfTo currently support FromInstance only.
            if (!isBind && sourceKind != "FromInstance") return null;

            TypeSyntax implType = toU ?? bindT;

            switch (sourceKind) {
                case "FromInstance":
                    // Plain head: builder.RegisterInstance<bindT>(arg)
                    // Interface head: builder.RegisterInstance(arg).AsImplementedInterfaces[.AsSelf]()
                    if (sourceArgs == null || sourceArgs.Arguments.Count != 1) return null;
                    if (isBind) {
                        tail = SyntaxFactory.InvocationExpression(
                            SyntaxFactory.MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                SyntaxFactory.IdentifierName("builder"),
                                GenericTypeName("RegisterInstance", bindT)))
                            .WithArgumentList(sourceArgs);
                    } else {
                        // Interface heads: drop explicit type argument so VContainer
                        // infers the concrete type from the instance.
                        tail = SyntaxFactory.InvocationExpression(
                            SyntaxFactory.MemberAccessExpression(
                                SyntaxKind.SimpleMemberAccessExpression,
                                SyntaxFactory.IdentifierName("builder"),
                                SyntaxFactory.IdentifierName("RegisterInstance")))
                            .WithArgumentList(sourceArgs);
                        tail = AppendIdentifierCall(tail, "AsImplementedInterfaces");
                        if (isInterfacesAndSelf) {
                            tail = AppendIdentifierCall(tail, "AsSelf");
                        }
                        if (withIdArg != null && withIdKeyType != null) {
                            tail = AppendGenericCallWithArg(tail, "Keyed", withIdKeyType, withIdArg);
                        }
                        return ((InvocationExpressionSyntax)tail).WithTriviaFrom(lastInChain);
                    }
                    break;

                case "FromMethod": {
                    // builder.Register<bindT>(λ, Lifetime.X)
                    if (sourceArgs == null || sourceArgs.Arguments.Count != 1) return null;
                    var resolvedLifetime = lifetime ?? "Singleton";
                    tail = SyntaxFactory.InvocationExpression(
                        SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.IdentifierName("builder"),
                            GenericTypeName("Register", bindT)))
                        .WithArgumentList(SyntaxFactory.ArgumentList(TwoArgList(
                            sourceArgs.Arguments[0],
                            SyntaxFactory.Argument(LifetimeMember(resolvedLifetime)))));
                    break;
                }

                case "FromComponentInHierarchy":
                    // builder.RegisterComponentInHierarchy<impl>().As<bindT>() (skip .As if same)
                    tail = SyntaxFactory.InvocationExpression(
                        SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.IdentifierName("builder"),
                            GenericTypeName("RegisterComponentInHierarchy", implType)))
                        .WithArgumentList(SyntaxFactory.ArgumentList());
                    if (!implType.IsEquivalentTo(bindT)) {
                        tail = AppendGenericCall(tail, "As", bindT);
                    }
                    break;

                case "FromComponentInNewPrefab": {
                    // builder.RegisterComponentInNewPrefab<impl>(prefab, Lifetime.X)
                    if (sourceArgs == null || sourceArgs.Arguments.Count != 1) return null;
                    var resolvedLifetime = lifetime ?? "Singleton";
                    tail = SyntaxFactory.InvocationExpression(
                        SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.IdentifierName("builder"),
                            GenericTypeName("RegisterComponentInNewPrefab", implType)))
                        .WithArgumentList(SyntaxFactory.ArgumentList(TwoArgList(
                            sourceArgs.Arguments[0],
                            SyntaxFactory.Argument(LifetimeMember(resolvedLifetime)))));
                    if (!implType.IsEquivalentTo(bindT)) {
                        tail = AppendGenericCall(tail, "As", bindT);
                    }
                    break;
                }

                case "FromNewComponentOnNewGameObject": {
                    // builder.RegisterComponentOnNewGameObject<impl>(Lifetime.X, "<impl>")
                    var resolvedLifetime = lifetime ?? "Singleton";
                    var nameLiteral = SyntaxFactory.Argument(
                        SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression,
                            SyntaxFactory.Literal(implType.ToString())));
                    tail = SyntaxFactory.InvocationExpression(
                        SyntaxFactory.MemberAccessExpression(
                            SyntaxKind.SimpleMemberAccessExpression,
                            SyntaxFactory.IdentifierName("builder"),
                            GenericTypeName("RegisterComponentOnNewGameObject", implType)))
                        .WithArgumentList(SyntaxFactory.ArgumentList(TwoArgList(
                            SyntaxFactory.Argument(LifetimeMember(resolvedLifetime)),
                            nameLiteral)));
                    if (!implType.IsEquivalentTo(bindT)) {
                        tail = AppendGenericCall(tail, "As", bindT);
                    }
                    break;
                }

                default: return null;
            }

            if (withIdArg != null && withIdKeyType != null) {
                tail = AppendGenericCallWithArg(tail, "Keyed", withIdKeyType, withIdArg);
            }

            return ((InvocationExpressionSyntax)tail).WithTriviaFrom(lastInChain);
        }

        private static InvocationExpressionSyntax AppendGenericCallWithArg(ExpressionSyntax receiver, string methodName, TypeSyntax typeArg, ArgumentSyntax arg) {
            return SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    receiver,
                    GenericTypeName(methodName, typeArg)))
                .WithArgumentList(SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(arg.WithoutTrivia())));
        }

        private static InvocationExpressionSyntax BuildRegister(TypeSyntax registerType, string lifetime) {
            return SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    SyntaxFactory.IdentifierName("builder"),
                    GenericTypeName("Register", registerType)))
                .WithArgumentList(SyntaxFactory.ArgumentList(
                    SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument(LifetimeMember(lifetime)))));
        }

        private static GenericNameSyntax GenericTypeName(string methodName, TypeSyntax typeArg) {
            return SyntaxFactory.GenericName(SyntaxFactory.Identifier(methodName))
                .WithTypeArgumentList(SyntaxFactory.TypeArgumentList(SyntaxFactory.SingletonSeparatedList(typeArg)));
        }

        private static SeparatedSyntaxList<ArgumentSyntax> TwoArgList(ArgumentSyntax a, ArgumentSyntax b) {
            var comma = SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space);
            return SyntaxFactory.SeparatedList<ArgumentSyntax>(new SyntaxNodeOrToken[] { a, comma, b });
        }

        private static MemberAccessExpressionSyntax LifetimeMember(string lifetime) {
            return SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.IdentifierName("Lifetime"),
                SyntaxFactory.IdentifierName(lifetime));
        }

        private static InvocationExpressionSyntax AppendGenericCall(ExpressionSyntax receiver, string methodName, TypeSyntax typeArg) {
            var generic = SyntaxFactory.GenericName(SyntaxFactory.Identifier(methodName))
                .WithTypeArgumentList(SyntaxFactory.TypeArgumentList(SyntaxFactory.SingletonSeparatedList(typeArg)));
            return SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    receiver,
                    generic));
        }

        private static InvocationExpressionSyntax AppendIdentifierCall(ExpressionSyntax receiver, string methodName) {
            return SyntaxFactory.InvocationExpression(
                SyntaxFactory.MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    receiver,
                    SyntaxFactory.IdentifierName(methodName)));
        }
    }
}
