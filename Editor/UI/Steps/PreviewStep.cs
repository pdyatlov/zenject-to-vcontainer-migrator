using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.UI.Steps {
    public sealed class PreviewStep : IWizardStep {
        public string Title => "2. Preview migration changes";
        public WizardState State => WizardState.Preview;

        private FileChangeCategory _filter = FileChangeCategory.CSharp;
        private int _selected = -1;
        private Vector2 _listScroll;
        private Vector2 _diffScroll;
        private readonly HashSet<string> _approvedLowConfidence = new HashSet<string>();

        public void OnGUI(MigrationContext ctx) {
            EditorGUILayout.HelpBox("Builds the C# and YAML migration plans. Diff each change, approve any LowFlagged items, then advance to Apply.", MessageType.Info);

            using (new EditorGUILayout.HorizontalScope()) {
                if (GUILayout.Button("Build C# plan")) {
                    ctx.CSharpPlan = MigrationPipeline.RunCSharpHeadless();
                    ctx.Log.Info("Preview.CSharp", $"{ctx.CSharpPlan.Changes.Count} files / {ctx.CSharpPlan.Unsupported.Count} findings");
                }
                if (GUILayout.Button("Build YAML plan")) {
                    ctx.YamlPlan = MigrationPipeline.RunYamlHeadless();
                    ctx.Log.Info("Preview.Yaml", $"{ctx.YamlPlan.Changes.Count} assets / {ctx.YamlPlan.Unsupported.Count} findings");
                }
            }
            if (ctx.CSharpPlan == null && ctx.YamlPlan == null) return;

            _filter = (FileChangeCategory)EditorGUILayout.EnumPopup("Show category", _filter);
            var combined = Filtered(ctx, _filter);

            using (new EditorGUILayout.HorizontalScope()) {
                _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.Width(280), GUILayout.MinHeight(280));
                for (int i = 0; i < combined.Count; i++) {
                    var c = combined[i];
                    var label = $"{ConfBadge(c.Confidence)} {System.IO.Path.GetFileName(c.OriginalPath)}";
                    if (GUILayout.Toggle(_selected == i, label, "Button")) _selected = i;
                }
                EditorGUILayout.EndScrollView();

                using (new EditorGUILayout.VerticalScope()) {
                    if (_selected >= 0 && _selected < combined.Count) {
                        var c = combined[_selected];
                        DiffView.Draw(ref _diffScroll, c);
                        if (c.Confidence == ChangeConfidence.LowFlagged) {
                            var key = c.OriginalPath;
                            var approved = _approvedLowConfidence.Contains(key);
                            var newApproved = EditorGUILayout.Toggle("Approve LowFlagged change", approved);
                            if (newApproved && !approved) _approvedLowConfidence.Add(key);
                            else if (!newApproved && approved) _approvedLowConfidence.Remove(key);
                        }
                        if (c.RelatedFindings != null && c.RelatedFindings.Count > 0) {
                            EditorGUILayout.LabelField("Manual TODOs:", EditorStyles.boldLabel);
                            foreach (var f in c.RelatedFindings) EditorGUILayout.LabelField($"  [{f.Category}] line {f.Line}: {f.Reason}");
                        }
                    } else {
                        EditorGUILayout.HelpBox("Select a file on the left to view its diff.", MessageType.None);
                    }
                }
            }
        }

        private static List<FileChange> Filtered(MigrationContext ctx, FileChangeCategory filter) {
            var list = new List<FileChange>();
            if (ctx.CSharpPlan != null) foreach (var c in ctx.CSharpPlan.Changes) if (c.Category == filter) list.Add(c);
            if (ctx.YamlPlan != null)   foreach (var c in ctx.YamlPlan.Changes)   if (c.Category == filter) list.Add(c);
            return list;
        }

        private static string ConfBadge(ChangeConfidence c) => c switch {
            ChangeConfidence.High => "[H]",
            ChangeConfidence.Medium => "[M]",
            ChangeConfidence.LowFlagged => "[L!]",
            _ => "[?]"
        };

        public bool CanAdvance(MigrationContext ctx) {
            if (ctx.CSharpPlan == null && ctx.YamlPlan == null) return false;
            // Every LowFlagged change must be approved before advancing.
            foreach (var p in new[] { ctx.CSharpPlan, ctx.YamlPlan }) {
                if (p == null) continue;
                foreach (var c in p.Changes) {
                    if (c.Confidence == ChangeConfidence.LowFlagged && !_approvedLowConfidence.Contains(c.OriginalPath)) return false;
                }
            }
            return true;
        }
    }
}
