using System;
using System.Collections.Generic;
using System.Linq;
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

        public IReadOnlyList<FileChange> Run(CSharpCompilation compilation, IMigrationProgress progress = null) {
            progress = progress ?? NullMigrationProgress.Instance;
            var changes = new List<FileChange>();
            var trees = compilation.SyntaxTrees.ToArray();
            int total = trees.Length;
            int idx = 0;
            foreach (var originalTree in trees) {
                idx++;
                var fileName = string.IsNullOrEmpty(originalTree.FilePath) ? "<in-memory>" : System.IO.Path.GetFileName(originalTree.FilePath);
                progress.Report("Migrating C#", $"{idx}/{total}: {fileName}", total > 0 ? (float)idx / total : 1f);
                var currentTree = originalTree;
                var perFileFindings = new List<Finding>();

                // Order matters: rewriters that need semantic info on Zenject types must
                // run BEFORE UsingDirectiveRewriter strips `using Zenject;`. Otherwise the
                // SemanticModel can no longer resolve MonoInstaller, DiContainer, etc.,
                // and IsZenjectSymbol returns null for everything.
                currentTree = ApplyIfIncluded<InjectAttributeRewriter>(compilation, currentTree, perFileFindings);
                currentTree = ApplyIfIncluded<BindToAsRewriter>(compilation, currentTree, perFileFindings);
                currentTree = ApplyIfIncluded<InstallerRewriter>(compilation, currentTree, perFileFindings);
                currentTree = ApplyIfIncluded<TickableInitializableRewriter>(compilation, currentTree, perFileFindings);
                currentTree = ApplyIfIncluded<FactoryRewriter>(compilation, currentTree, perFileFindings);
                currentTree = ApplyIfIncluded<SubContainerRewriter>(compilation, currentTree, perFileFindings);
                currentTree = ApplyIfIncluded<DiContainerUsageRewriter>(compilation, currentTree, perFileFindings);
                currentTree = ApplyIfIncluded<UsingDirectiveRewriter>(compilation, currentTree, perFileFindings);

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
