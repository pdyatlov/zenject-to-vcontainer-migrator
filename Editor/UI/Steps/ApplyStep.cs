using System.IO;
using UnityEditor;
using UnityEngine;
using Zenject2VContainer.Core;
using Zenject2VContainer.Reporting;

namespace Zenject2VContainer.UI.Steps {
    public sealed class ApplyStep : IWizardStep {
        public string Title => "3. Apply changes";
        public WizardState State => WizardState.Apply;

        private PreconditionReport _preconditions;

        public void OnGUI(MigrationContext ctx) {
            EditorGUILayout.HelpBox("Snapshots all touched files into Temp/Zenject2VContainer/Backup/<timestamp>/. Writes C# changes, awaits compile, then writes YAML. Auto-rolls back on compile errors.", MessageType.Info);

            if (GUILayout.Button("Run preconditions")) {
                EditorUtility.DisplayProgressBar("Preconditions", "Running checks…", 0.5f);
                try {
                    _preconditions = PreconditionRunner.Run(ctx.ScanReport?.Install, ctx.UserOverrodeGitDirty);
                } finally {
                    EditorUtility.ClearProgressBar();
                }
                foreach (var r in _preconditions.Results) ctx.Log.Info("Precondition", $"{r.Code}: {r.Message}");
            }

            if (_preconditions != null) {
                EditorGUILayout.LabelField("Preconditions", EditorStyles.boldLabel);
                foreach (var r in _preconditions.Results) {
                    var t = r.Result switch { PreconditionResult.Severity.Block => MessageType.Error, PreconditionResult.Severity.Warn => MessageType.Warning, _ => MessageType.Info };
                    EditorGUILayout.HelpBox($"[{r.Code}] {r.Message}", t);
                }
                if (_preconditions.Results.Find(r => r.Code == "GIT_DIRTY") != null) {
                    ctx.UserOverrodeGitDirty = EditorGUILayout.Toggle("Override dirty working tree warning", ctx.UserOverrodeGitDirty);
                }
            }

            GUI.enabled = _preconditions != null && !_preconditions.HasBlock && (ctx.CSharpPlan != null || ctx.YamlPlan != null) && !ctx.ApplySucceeded;
            if (GUILayout.Button("Apply migration")) {
                var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                var svc = new MigrationApplyService(new EditorCompileWaiter());
                var p = new EditorMigrationProgress();
                ApplyResult result;
                try {
                    result = svc.Apply(projectRoot, ctx.CSharpPlan ?? MigrationPlan.Empty(), ctx.YamlPlan ?? MigrationPlan.Empty(), ctx.Log, p);
                } finally {
                    p.Clear();
                }
                ctx.BackupTimestamp = result.BackupTimestamp;
                ctx.ApplySucceeded = result.Success;
                ctx.CompileErrors = result.CompileErrors;
                ctx.Log.Info("Apply", result.Message);

                var combined = MigrationPlan.Empty();
                if (ctx.CSharpPlan != null) { combined.Changes.AddRange(ctx.CSharpPlan.Changes); combined.Unsupported.AddRange(ctx.CSharpPlan.Unsupported); }
                if (ctx.YamlPlan != null)   { combined.Changes.AddRange(ctx.YamlPlan.Changes);   combined.Unsupported.AddRange(ctx.YamlPlan.Unsupported); }
                if (result.Success) {
                    var reportPath = MigrationReportWriter.WriteToDisk(projectRoot, combined, new MigrationReportContext {
                        ProjectPath = projectRoot,
                        UnityVersion = Application.unityVersion,
                        ToolVersion = MigrationPipeline.ToolVersion,
                        RunUtc = System.DateTime.UtcNow.ToString("o"),
                        BackupTimestamp = result.BackupTimestamp
                    });
                    ctx.Log.Info("Report", "Wrote " + reportPath);
                    AssetDatabase.Refresh();
                }
            }
            GUI.enabled = true;

            if (!string.IsNullOrEmpty(ctx.BackupTimestamp)) {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Last backup: " + ctx.BackupTimestamp);
                if (GUILayout.Button("Rollback last apply")) {
                    var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                    BackupManager.Restore(projectRoot, ctx.BackupTimestamp);
                    AssetDatabase.Refresh();
                    ctx.Log.Warn("Apply", "Manually rolled back to " + ctx.BackupTimestamp);
                    ctx.ApplySucceeded = false;
                }
            }
        }

        public bool CanAdvance(MigrationContext ctx) => ctx.ApplySucceeded;
    }
}
