// Translates Zenject factory shapes to VContainer equivalents (per spec §5.7).
//
// Conservative M2 strategy:
//   - `class FooFactory : PlaceholderFactory<TArg, TOut> { }` is rewritten in place
//     to a thin wrapper around `Func<TArg, TOut>`. The "drop wrapper if unused"
//     optimisation requires whole-compilation reference tracking and is skipped
//     here — preserving the wrapper is always a safe refactor.
//   - `Container.BindFactory<...>()` chains are flagged with manual TODO
//     `[CustomFactory]`; the binding chain itself is left alone for the user to
//     replace with `builder.RegisterFactory<TArg, TOut>(...)`. Automating this
//     well requires the BindingRegistry threading planned for M3.

using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Zenject2VContainer.Core.Scanner;

namespace Zenject2VContainer.CSharp.Rewriters {
    public sealed class FactoryRewriter : RewriterBase {
        public override string Name => nameof(FactoryRewriter);

        public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node) {
            // Rewrite a class whose direct base is Zenject's PlaceholderFactory<...>.
            // Supports 1..N type arguments — the last is the output type, all preceding
            // are arguments fed into Func<...>.
            if (node.BaseList != null) {
                foreach (var bt in node.BaseList.Types) {
                    if (!(bt.Type is GenericNameSyntax gn)) continue;
                    if (gn.Identifier.Text != "PlaceholderFactory") continue;

                    var sym = Model.GetSymbolInfo(gn).Symbol as INamedTypeSymbol;
                    if (sym == null || !SymbolMatchers.IsZenjectSymbol(sym)) continue;
                    if (sym.Name != "PlaceholderFactory") continue;
                    if (gn.TypeArgumentList.Arguments.Count < 1) continue;

                    return BuildWrapperClass(node, gn);
                }
            }
            return base.VisitClassDeclaration(node);
        }

        // Container.BindFactory<...>() detection lives in BindToAsRewriter — it
        // arrives there as a regular Bind-chain head and is flagged with a manual
        // TODO [CustomFactory] alongside the other unsupported chain heads.

        private static ClassDeclarationSyntax BuildWrapperClass(ClassDeclarationSyntax cls, GenericNameSyntax placeholderBase) {
            var typeArgs = placeholderBase.TypeArgumentList.Arguments;
            int argCount = typeArgs.Count - 1; // last arg is the output type
            var tOut = typeArgs[typeArgs.Count - 1];
            var tArgs = new List<TypeSyntax>();
            for (int i = 0; i < argCount; i++) tArgs.Add(typeArgs[i]);

            // Strip the PlaceholderFactory<TArg, TOut> base list. Removing the base
            // list also drops its trivia (the `\n` after `>` lived there), so set
            // identifier trailing to empty and force the open brace onto its own line.
            ClassDeclarationSyntax stripped = cls.WithBaseList(null)
                .WithIdentifier(cls.Identifier.WithTrailingTrivia(SyntaxTriviaList.Empty))
                .WithOpenBraceToken(
                    SyntaxFactory.Token(
                        SyntaxFactory.TriviaList(SyntaxFactory.EndOfLine("\n")),
                        SyntaxKind.OpenBraceToken,
                        SyntaxFactory.TriviaList()))
                .WithCloseBraceToken(
                    SyntaxFactory.Token(
                        SyntaxFactory.TriviaList(SyntaxFactory.EndOfLine("\n")),
                        SyntaxKind.CloseBraceToken,
                        SyntaxFactory.TriviaList(SyntaxFactory.EndOfLine("\n"))));

            // Build Func<TArg1, ..., TArgN, TOut>. Zero-arg factories produce just Func<TOut>.
            var funcTypeArgs = new List<SyntaxNodeOrToken>();
            for (int i = 0; i < tArgs.Count; i++) {
                funcTypeArgs.Add(tArgs[i].WithoutTrivia());
                funcTypeArgs.Add(SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space));
            }
            funcTypeArgs.Add(tOut.WithoutTrivia());
            var funcType = SyntaxFactory.GenericName(SyntaxFactory.Identifier("Func"))
                .WithTypeArgumentList(SyntaxFactory.TypeArgumentList(
                    SyntaxFactory.SeparatedList<TypeSyntax>(funcTypeArgs)));

            var memberLeading = SyntaxFactory.TriviaList(
                SyntaxFactory.EndOfLine("\n"),
                SyntaxFactory.Whitespace("    "));

            // private readonly Func<TArg, TOut> _factory;
            var fieldDecl = SyntaxFactory.FieldDeclaration(
                SyntaxFactory.VariableDeclaration(funcType.WithTrailingTrivia(SyntaxFactory.Space))
                    .WithVariables(SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.VariableDeclarator("_factory"))))
                .WithModifiers(SyntaxFactory.TokenList(
                    SyntaxFactory.Token(SyntaxKind.PrivateKeyword).WithTrailingTrivia(SyntaxFactory.Space),
                    SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword).WithTrailingTrivia(SyntaxFactory.Space)))
                .WithLeadingTrivia(memberLeading);

            // public FooFactory(Func<TArg, TOut> factory) { _factory = factory; }
            var ctorParam = SyntaxFactory.Parameter(SyntaxFactory.Identifier("factory"))
                .WithType(funcType.WithTrailingTrivia(SyntaxFactory.Space));
            var ctorBody = SyntaxFactory.Block(
                SyntaxFactory.ExpressionStatement(
                    SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                        SyntaxFactory.IdentifierName("_factory").WithTrailingTrivia(SyntaxFactory.Space),
                        SyntaxFactory.IdentifierName("factory").WithLeadingTrivia(SyntaxFactory.Space))));
            ctorBody = ctorBody
                .WithOpenBraceToken(SyntaxFactory.Token(
                    SyntaxFactory.TriviaList(SyntaxFactory.Space),
                    SyntaxKind.OpenBraceToken,
                    SyntaxFactory.TriviaList(SyntaxFactory.Space)))
                .WithCloseBraceToken(SyntaxFactory.Token(
                    SyntaxFactory.TriviaList(SyntaxFactory.Space),
                    SyntaxKind.CloseBraceToken,
                    SyntaxFactory.TriviaList()));
            var ctor = SyntaxFactory.ConstructorDeclaration(cls.Identifier.Text)
                .WithModifiers(SyntaxFactory.TokenList(
                    SyntaxFactory.Token(SyntaxKind.PublicKeyword).WithTrailingTrivia(SyntaxFactory.Space)))
                .WithParameterList(SyntaxFactory.ParameterList(
                    SyntaxFactory.SingletonSeparatedList(ctorParam)))
                .WithBody(ctorBody)
                .WithLeadingTrivia(memberLeading);

            // public TOut Create(TArg1 a1, ..., TArgN aN) => _factory(a1, ..., aN);
            var arrowToken = SyntaxFactory.Token(SyntaxKind.EqualsGreaterThanToken)
                .WithLeadingTrivia(SyntaxFactory.Space)
                .WithTrailingTrivia(SyntaxFactory.Space);

            var createParamNodes = new List<SyntaxNodeOrToken>();
            var invokeArgNodes = new List<SyntaxNodeOrToken>();
            for (int i = 0; i < tArgs.Count; i++) {
                var name = "a" + (i + 1);
                createParamNodes.Add(SyntaxFactory.Parameter(SyntaxFactory.Identifier(name))
                    .WithType(tArgs[i].WithTrailingTrivia(SyntaxFactory.Space)));
                invokeArgNodes.Add(SyntaxFactory.Argument(SyntaxFactory.IdentifierName(name)));
                if (i < tArgs.Count - 1) {
                    var comma = SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(SyntaxFactory.Space);
                    createParamNodes.Add(comma);
                    invokeArgNodes.Add(comma);
                }
            }
            var createParams = SyntaxFactory.ParameterList(
                SyntaxFactory.SeparatedList<ParameterSyntax>(createParamNodes));
            var invokeArgs = SyntaxFactory.ArgumentList(
                SyntaxFactory.SeparatedList<ArgumentSyntax>(invokeArgNodes));

            var createMethod = SyntaxFactory.MethodDeclaration(
                    tOut.WithTrailingTrivia(SyntaxFactory.Space),
                    SyntaxFactory.Identifier("Create"))
                .WithModifiers(SyntaxFactory.TokenList(
                    SyntaxFactory.Token(SyntaxKind.PublicKeyword).WithTrailingTrivia(SyntaxFactory.Space)))
                .WithParameterList(createParams)
                .WithExpressionBody(SyntaxFactory.ArrowExpressionClause(arrowToken,
                    SyntaxFactory.InvocationExpression(SyntaxFactory.IdentifierName("_factory"))
                        .WithArgumentList(invokeArgs)))
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken))
                .WithLeadingTrivia(memberLeading);

            var newMembers = new List<MemberDeclarationSyntax> { fieldDecl, ctor, createMethod };
            // Append any other members the original class had (rare but possible).
            foreach (var m in cls.Members) newMembers.Add(m);

            return stripped.WithMembers(SyntaxFactory.List(newMembers));
        }
    }
}
