using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Zenject2VContainer.Core;
using Zenject2VContainer.CSharp.Rewriters;

namespace Zenject2VContainer.CSharp {
    public sealed class RewritePipeline {
        private readonly string[] _rewriterFilter;

        public RewritePipeline(string[] rewriterFilter) {
            _rewriterFilter = rewriterFilter ?? new[] { "*" };
        }

        public IReadOnlyList<FileChange> Run(CSharpCompilation compilation) {
            var changes = new List<FileChange>();
            foreach (var tree in compilation.SyntaxTrees) {
                var model = compilation.GetSemanticModel(tree);
                var root = tree.GetRoot();
                var current = root;
                var perFileFindings = new List<Finding>();

                if (Includes(nameof(UsingDirectiveRewriter))) {
                    var r = new UsingDirectiveRewriter();
                    current = r.Apply(current, model, tree);
                    perFileFindings.AddRange(r.Findings);
                }

                if (Includes(nameof(InjectAttributeRewriter))) {
                    var r = new InjectAttributeRewriter();
                    current = r.Apply(current, model, tree);
                    perFileFindings.AddRange(r.Findings);
                }

                if (Includes(nameof(BindToAsRewriter))) {
                    var r = new BindToAsRewriter();
                    current = r.Apply(current, model, tree);
                    perFileFindings.AddRange(r.Findings);
                }

                if (current != root) {
                    var fc = new FileChange {
                        OriginalPath = tree.FilePath,
                        OriginalText = root.ToFullString(),
                        NewText = current.ToFullString(),
                        Category = FileChangeCategory.CSharp,
                        Confidence = ChangeConfidence.High
                    };
                    fc.RelatedFindings.AddRange(perFileFindings);
                    changes.Add(fc);
                }
            }
            return changes;
        }

        private bool Includes(string name) {
            for (int i = 0; i < _rewriterFilter.Length; i++) {
                if (_rewriterFilter[i] == "*" || _rewriterFilter[i] == name) return true;
            }
            return false;
        }
    }
}
