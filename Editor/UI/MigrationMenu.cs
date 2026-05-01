using System.IO;
using UnityEditor;
using UnityEngine;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.UI {
    public static class MigrationMenu {
        [MenuItem("Window/Zenject2VContainer/Scan to JSON…")]
        public static void ScanToJson() {
            var defaultPath = Path.Combine(
                Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                "Library", "Zenject2VContainer", "scan.json");

            var picked = EditorUtility.SaveFilePanel(
                "Save Zenject scan report",
                Path.GetDirectoryName(defaultPath),
                Path.GetFileName(defaultPath),
                "json");
            if (string.IsNullOrEmpty(picked)) return;

            var report = MigrationPipeline.RunScanHeadless();
            MigrationPipeline.WriteScanReport(report, picked);
            EditorUtility.RevealInFinder(picked);
            Debug.Log($"[Zenject2VContainer] Scan written to {picked}");
        }
    }
}
