using System.IO;
using UnityEditor;
using UnityEngine;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.UI.Steps {
    public sealed class ScanStep : IWizardStep {
        public string Title => "1. Scan project for Zenject usage";
        public WizardState State => WizardState.Scan;

        public void OnGUI(MigrationContext ctx) {
            EditorGUILayout.HelpBox("Scans Assets/ and writable Packages/ for Zenject C# usage and asset GUIDs. Result feeds the Preview step.", MessageType.Info);

            if (GUILayout.Button("Run scan")) {
                ctx.Log.Info("Scan", "Starting scan…");
                ctx.ScanReport = MigrationPipeline.RunScanHeadless();
                ctx.Log.Info("Scan", $"Done. C# findings: {ctx.ScanReport.CSharpFindings.Count}, asset findings: {ctx.ScanReport.AssetFindings.Count}, unsupported: {ctx.ScanReport.Unsupported.Count}");
            }

            if (ctx.ScanReport == null) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Findings", EditorStyles.boldLabel);

            int bindCalls = 0, installerSubclasses = 0, injectMembers = 0, factoryRefs = 0, subContainerCalls = 0, diContainerUsages = 0, lifecycle = 0;
            foreach (var f in ctx.ScanReport.CSharpFindings) {
                switch (f.Kind) {
                    case CSharpFindingKind.BindCall:           bindCalls++;          break;
                    case CSharpFindingKind.InstallerSubclass:  installerSubclasses++; break;
                    case CSharpFindingKind.InjectAttribute:    injectMembers++;       break;
                    case CSharpFindingKind.FactoryReference:   factoryRefs++;         break;
                    case CSharpFindingKind.SubContainerCall:   subContainerCalls++;   break;
                    case CSharpFindingKind.DiContainerUsage:   diContainerUsages++;   break;
                    case CSharpFindingKind.LifecycleInterface: lifecycle++;           break;
                }
            }
            EditorGUILayout.LabelField("Bind calls", bindCalls.ToString());
            EditorGUILayout.LabelField("Installer subclasses", installerSubclasses.ToString());
            EditorGUILayout.LabelField("[Inject] members", injectMembers.ToString());
            EditorGUILayout.LabelField("Factory references", factoryRefs.ToString());
            EditorGUILayout.LabelField("SubContainer calls", subContainerCalls.ToString());
            EditorGUILayout.LabelField("DiContainer usages", diContainerUsages.ToString());
            EditorGUILayout.LabelField("Lifecycle interfaces", lifecycle.ToString());
            EditorGUILayout.LabelField("Asset GUID references", ctx.ScanReport.AssetFindings.Count.ToString());
            EditorGUILayout.Space();

            var inst = ctx.ScanReport.Install;
            string zenject = inst == null ? "not detected"
                : inst.ZenjectViaUpm ? $"UPM ({inst.UpmPackageId} {inst.UpmVersionOrUrl})"
                : inst.ZenjectViaAssets ? $"folder ({inst.AssetsFolderPath})"
                : "not detected";
            string vcontainer = inst != null && inst.VContainerInstalled ? (inst.VContainerVersion ?? "yes") : "missing";
            EditorGUILayout.LabelField("Zenject install", zenject);
            EditorGUILayout.LabelField("VContainer install", vcontainer);

            if (GUILayout.Button("Save scan.json")) {
                var defaultPath = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), "Library", "Zenject2VContainer", "scan.json");
                Directory.CreateDirectory(Path.GetDirectoryName(defaultPath));
                MigrationPipeline.WriteScanReport(ctx.ScanReport, defaultPath);
                ctx.Log.Info("Scan", "Wrote " + defaultPath);
                EditorUtility.RevealInFinder(defaultPath);
            }
        }

        public bool CanAdvance(MigrationContext ctx) => ctx.ScanReport != null;
    }
}
