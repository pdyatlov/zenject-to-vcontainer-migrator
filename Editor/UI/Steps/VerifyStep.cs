using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Zenject2VContainer.Core;
using Zenject2VContainer.Reporting;

namespace Zenject2VContainer.UI.Steps {
    public sealed class VerifyStep : IWizardStep {
        public string Title => "4. Verify migration";
        public WizardState State => WizardState.Verify;

        private ZenjectUsageReport _post;
        private bool _ranThisVisit;

        public void OnGUI(MigrationContext ctx) {
            EditorGUILayout.HelpBox("Re-runs the scanner. Expectation: zero remaining `using Zenject` for in-scope features. Anything still flagged is either an out-of-scope feature or a manual TODO surfaced earlier.", MessageType.Info);

            if (GUILayout.Button("Re-run scan")) {
                _post = MigrationPipeline.RunScanHeadless();
                _ranThisVisit = true;
                ctx.RemainingZenjectFiles.Clear();
                foreach (var path in EnumerateRemainingFiles(_post)) ctx.RemainingZenjectFiles.Add(path);
                ctx.Log.Info("Verify", $"Remaining Zenject references: {ctx.RemainingZenjectFiles.Count} file(s) across {_post.CSharpFindings.Count} C# + {_post.AssetFindings.Count} asset findings");

                // Write the migration report now that verification stats are known.
                var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                var combined = MigrationPlan.Empty();
                if (ctx.CSharpPlan != null) { combined.Changes.AddRange(ctx.CSharpPlan.Changes); combined.Unsupported.AddRange(ctx.CSharpPlan.Unsupported); }
                if (ctx.YamlPlan != null)   { combined.Changes.AddRange(ctx.YamlPlan.Changes);   combined.Unsupported.AddRange(ctx.YamlPlan.Unsupported); }

                // Derive skipped files: scanned files minus files that had changes applied.
                var changedPaths = new HashSet<string>(
                    combined.Changes.Select(c => Rel(projectRoot, c.OriginalPath)),
                    System.StringComparer.OrdinalIgnoreCase);
                ctx.SkippedFiles = _post.AllFilesScanned
                    .Where(f => !changedPaths.Contains(f))
                    .ToArray();

                var reportPath = MigrationReportWriter.WriteToDisk(projectRoot, combined, new MigrationReportContext {
                    ProjectPath      = projectRoot,
                    UnityVersion     = Application.unityVersion,
                    ToolVersion      = MigrationPipeline.ToolVersion,
                    RunUtc           = System.DateTime.UtcNow.ToString("o"),
                    BackupTimestamp  = ctx.BackupTimestamp,
                    RemainingZenjectFiles = ctx.RemainingZenjectFiles.Count,
                    CompileErrorCount     = ctx.CompileErrors != null ? ctx.CompileErrors.Count : 0,
                    SkippedFiles          = ctx.SkippedFiles
                });
                ctx.Log.Info("Report", "Wrote " + reportPath);
            }

            if (_ranThisVisit) {
                int bindCalls = 0;
                foreach (var f in _post.CSharpFindings) if (f.Kind == CSharpFindingKind.BindCall) bindCalls++;
                EditorGUILayout.LabelField("Bind calls after migration", bindCalls.ToString());
                EditorGUILayout.LabelField("Asset GUID references after migration", _post.AssetFindings.Count.ToString());

                if (ctx.CompileErrors != null && ctx.CompileErrors.Count > 0) {
                    EditorGUILayout.HelpBox("Apply phase reported compile errors:", MessageType.Error);
                    foreach (var e in ctx.CompileErrors) EditorGUILayout.LabelField("  " + e);
                }

                if (ctx.RemainingZenjectFiles.Count > 0) {
                    EditorGUILayout.HelpBox(ctx.RemainingZenjectFiles.Count + " file(s) still reference Zenject — review the report for details.", MessageType.Warning);
                    foreach (var f in ctx.RemainingZenjectFiles) EditorGUILayout.LabelField("  " + f);
                } else {
                    EditorGUILayout.HelpBox("Verify clean — no residual in-scope Zenject usage.", MessageType.Info);
                }
            }
        }

        private static string Rel(string projectRoot, string p) {
            if (string.IsNullOrEmpty(p) || string.IsNullOrEmpty(projectRoot)) return p;
            var root = projectRoot.Replace('\\', '/').TrimEnd('/') + "/";
            var n = p.Replace('\\', '/');
            return n.StartsWith(root, System.StringComparison.OrdinalIgnoreCase)
                ? n.Substring(root.Length) : n;
        }

        private static IEnumerable<string> EnumerateRemainingFiles(ZenjectUsageReport report) {
            var seen = new HashSet<string>();
            if (report.CSharpFindings != null) {
                foreach (var f in report.CSharpFindings) {
                    if (!string.IsNullOrEmpty(f.FilePath) && seen.Add(f.FilePath)) yield return f.FilePath;
                }
            }
            if (report.AssetFindings != null) {
                foreach (var f in report.AssetFindings) {
                    if (!string.IsNullOrEmpty(f.FilePath) && seen.Add(f.FilePath)) yield return f.FilePath;
                }
            }
        }

        public bool CanAdvance(MigrationContext ctx) => _ranThisVisit;
    }
}
