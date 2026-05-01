using UnityEditor;
using UnityEngine;
using Zenject2VContainer.Core;
using Zenject2VContainer.Manifest;

namespace Zenject2VContainer.UI.Steps {
    public sealed class RemoveZenjectStep : IWizardStep {
        private readonly IZenjectRemover _remover;
        private RemovalPlan _plan;
        public RemoveZenjectStep(IZenjectRemover remover) { _remover = remover; }
        public string Title => "5. Remove Zenject (optional)";
        public WizardState State => WizardState.Remove;

        public void OnGUI(MigrationContext ctx) {
            EditorGUILayout.HelpBox("Optional: removes the Zenject package + scoped registries once verify is clean. Skipping is safe — Zenject can stay installed indefinitely.", MessageType.Info);

            if (ctx.RemainingZenjectFiles.Count > 0) {
                EditorGUILayout.HelpBox("Verify reports residual Zenject usage. Removal is blocked until those references are addressed.", MessageType.Error);
                return;
            }

            if (_remover is StubZenjectRemover) {
                EditorGUILayout.HelpBox("Removal API is a stub in M4 — full implementation lands in M5. Remove Zenject manually for now.", MessageType.Warning);
            }

            if (GUILayout.Button("Plan removal")) {
                var projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, ".."));
                _plan = _remover.Plan(ctx.ScanReport?.Install, projectRoot);
                ctx.Log.Info("Remove", _plan.IsNoop ? "Already removed (or stub)." : $"UPM: {_plan.UpmInstall} ({_plan.UpmPackageId}); folder: {_plan.FolderInstallPath}");
            }
            if (_plan == null) return;

            using (new EditorGUI.DisabledScope(_plan.IsNoop)) {
                if (GUILayout.Button("Apply removal")) {
                    var projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, ".."));
                    var result = _remover.Apply(_plan, projectRoot);
                    if (result.Success) ctx.Log.Info("Remove", result.Message);
                    else ctx.Log.Error("Remove", result.Message);
                    foreach (var a in result.ActionsTaken) ctx.Log.Info("Remove", a);
                }
            }

            if (GUILayout.Button("Skip — leave Zenject installed")) {
                ctx.Log.Info("Remove", "User opted to skip Zenject removal.");
            }
        }

        public bool CanAdvance(MigrationContext ctx) => true; // optional step
    }
}
