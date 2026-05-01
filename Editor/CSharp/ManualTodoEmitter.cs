using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.CSharp {
    public static class ManualTodoEmitter {
        // Categories live as constants so rewriters reference them through one place.
        public const string SignalBus = "SignalBus";
        public const string MemoryPool = "MemoryPool";
        public const string ConditionalBind = "ConditionalBind";
        public const string InjectOptional = "InjectOptional";
        public const string ComplexSubContainer = "ComplexSubContainer";
        public const string InstantiateUnregistered = "InstantiateUnregistered";
        public const string Decorator = "Decorator";
        public const string CustomFactory = "CustomFactory";
        public const string CustomDiContainerExtension = "CustomDiContainerExtension";

        public static SyntaxTriviaList Build(string category, string reason, string docLink, string indent = "") {
            var lines = new[] {
                "// TODO: MIGRATE-MANUAL [" + category + "]",
                "// Reason: " + reason,
                "// Suggested: see " + docLink,
                "// Original code preserved below — review and rewrite."
            };
            var triviaList = new System.Collections.Generic.List<SyntaxTrivia>();
            foreach (var line in lines) {
                triviaList.Add(SyntaxFactory.Comment(line));
                triviaList.Add(SyntaxFactory.EndOfLine("\n"));
                if (!string.IsNullOrEmpty(indent)) {
                    triviaList.Add(SyntaxFactory.Whitespace(indent));
                }
            }
            return SyntaxFactory.TriviaList(triviaList);
        }

        // Returns the trailing whitespace on the same line as the anchor token —
        // i.e. the indent the caller should use when prepending comment lines to
        // the anchor's leading trivia so each line lines up with the anchor.
        public static string ExtractLineIndent(Microsoft.CodeAnalysis.SyntaxTriviaList leading) {
            string result = "";
            foreach (var t in leading) {
                if (t.IsKind(SyntaxKind.EndOfLineTrivia)) {
                    result = "";
                } else if (t.IsKind(SyntaxKind.WhitespaceTrivia)) {
                    result = t.ToString();
                }
            }
            return result;
        }

        public static Finding ToFinding(string category, string filePath, int line, string reason) {
            return new Finding {
                Category = category,
                FilePath = filePath,
                Line = line,
                Reason = reason,
                DocLink = "https://github.com/<owner>/<repo>/blob/main/docs/manual-todos/" + category + ".md"
            };
        }
    }
}
