using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
            foreach (var originalTree in compilation.SyntaxTrees) {
                var currentTree = originalTree;
                var perFileFindings = new List<Finding>();

                currentTree = ApplyIfIncluded<UsingDirectiveRewriter>(compilation, currentTree, perFileFindings);
                currentTree = ApplyIfIncluded<InjectAttributeRewriter>(compilation, currentTree, perFileFindings);
                currentTree = ApplyIfIncluded<BindToAsRewriter>(compilation, currentTree, perFileFindings);

                var originalRoot = originalTree.GetRoot();
                var currentRoot = currentTree.GetRoot();
                if (currentRoot != originalRoot) {
                    var fc = new FileChange {
                        OriginalPath = originalTree.FilePath,
                        OriginalText = originalRoot.ToFullString(),
                        NewText = currentRoot.ToFullString(),
                        Category = FileChangeCategory.CSharp,
                        Confidence = ChangeConfidence.High
                    };
                    fc.RelatedFindings.AddRange(perFileFindings);
                    changes.Add(fc);
                }
            }
            return changes;
        }

        // Each rewriter must run against a SemanticModel bound to the CURRENT tree —
        // a previous rewriter may have mutated the syntax. Otherwise GetSymbolInfo
        // throws "Syntax node is not within syntax tree".
        private SyntaxTree ApplyIfIncluded<TRewriter>(
                CSharpCompilation baseCompilation,
                SyntaxTree currentTree,
                List<Finding> findings) where TRewriter : RewriterBase, new() {
            if (!Includes(typeof(TRewriter).Name)) return currentTree;

            var freshCompilation = baseCompilation.RemoveAllSyntaxTrees().AddSyntaxTrees(currentTree);
            var model = freshCompilation.GetSemanticModel(currentTree);
            var rewriter = new TRewriter();
            var newRoot = rewriter.Apply(currentTree.GetRoot(), model, currentTree);
            findings.AddRange(rewriter.Findings);

            if (newRoot == currentTree.GetRoot()) return currentTree;
            return currentTree.WithRootAndOptions(newRoot, currentTree.Options);
        }

        private bool Includes(string name) {
            for (int i = 0; i < _rewriterFilter.Length; i++) {
                if (_rewriterFilter[i] == "*" || _rewriterFilter[i] == name) return true;
            }
            return false;
        }
    }
}
