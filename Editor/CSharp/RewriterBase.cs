using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.CSharp {
    public abstract class RewriterBase : CSharpSyntaxRewriter {
        protected SemanticModel Model { get; private set; }
        protected SyntaxTree CurrentTree { get; private set; }
        public List<Finding> Findings { get; } = new List<Finding>();

        public abstract string Name { get; }

        public SyntaxNode Apply(SyntaxNode root, SemanticModel model, SyntaxTree tree) {
            Model = model;
            CurrentTree = tree;
            return Visit(root);
        }

        protected void EmitManualTodo(string category, SyntaxNode anchor, string reason) {
            var span = CurrentTree.GetLineSpan(anchor.Span);
            Findings.Add(ManualTodoEmitter.ToFinding(category, CurrentTree.FilePath,
                span.StartLinePosition.Line + 1, reason));
        }

        protected static MemberAccessExpressionSyntax MemberAccess(string lhs, string rhs) {
            return SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.IdentifierName(lhs),
                SyntaxFactory.IdentifierName(rhs));
        }
    }
}
