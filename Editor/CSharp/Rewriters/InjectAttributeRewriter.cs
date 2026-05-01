using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Zenject2VContainer.Core.Scanner;

namespace Zenject2VContainer.CSharp.Rewriters {
    public sealed class InjectAttributeRewriter : RewriterBase {
        public override string Name => nameof(InjectAttributeRewriter);

        public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node) {
            return TagOptionalIfNeeded(node, base.VisitFieldDeclaration(node));
        }

        public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node) {
            return TagOptionalIfNeeded(node, base.VisitMethodDeclaration(node));
        }

        public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node) {
            return TagOptionalIfNeeded(node, base.VisitPropertyDeclaration(node));
        }

        private SyntaxNode TagOptionalIfNeeded(SyntaxNode original, SyntaxNode visited) {
            var member = visited as MemberDeclarationSyntax;
            if (member == null) return visited;

            foreach (var attrList in member.AttributeLists) {
                foreach (var attr in attrList.Attributes) {
                    var symbol = Model.GetSymbolInfo(attr).Symbol?.ContainingType;
                    if (symbol == null) continue;
                    if (!SymbolMatchers.IsZenjectSymbol(symbol)) continue;
                    if (symbol.Name != "InjectOptionalAttribute") continue;

                    EmitManualTodo(ManualTodoEmitter.InjectOptional, original,
                        "VContainer has no direct field/method [InjectOptional]");
                    var trivia = ManualTodoEmitter.Build(
                        ManualTodoEmitter.InjectOptional,
                        "VContainer has no direct field/method [InjectOptional]; constructor defaults work.",
                        "https://github.com/<owner>/<repo>/blob/main/docs/manual-todos/InjectOptional.md");
                    return visited.WithLeadingTrivia(visited.GetLeadingTrivia().AddRange(trivia));
                }
            }
            return visited;
        }
    }
}
