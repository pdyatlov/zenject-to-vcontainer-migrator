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

        [MenuItem("Window/Zenject2VContainer/Preview C# Migration…")]
        public static void PreviewCSharp() {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var outDir = Path.Combine(projectRoot, "Library", "Zenject2VContainer", "preview-csharp");
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            Directory.CreateDirectory(outDir);

            var plan = MigrationPipeline.RunCSharpHeadless();
            foreach (var change in plan.Changes) {
                var rel = MakeRelative(projectRoot, change.OriginalPath);
                var dest = Path.Combine(outDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.WriteAllText(dest, change.NewText);
            }
            EditorUtility.RevealInFinder(outDir);
            Debug.Log($"[Zenject2VContainer] {plan.Changes.Count} files rewritten under {outDir} " +
                      $"({plan.Unsupported.Count} manual TODO findings).");
        }

        [MenuItem("Window/Zenject2VContainer/Preview YAML Migration…")]
        public static void PreviewYaml() {
            var lookup = Zenject2VContainer.Yaml.ScriptGuidLookup.Resolve();
            if (string.IsNullOrEmpty(lookup.LifetimeScopeGuid)) {
                Debug.LogWarning(
                    "[Zenject2VContainer] VContainer's LifetimeScope script GUID could not be resolved. " +
                    "Install VContainer (jp.hadashikick.vcontainer) into the project before running YAML migration; " +
                    "otherwise SceneContext / GameObjectContext GUIDs cannot be swapped. " +
                    "Run will continue but only emit findings.");
            }

            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var outDir = Path.Combine(projectRoot, "Library", "Zenject2VContainer", "preview-yaml");
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            Directory.CreateDirectory(outDir);

            var plan = MigrationPipeline.RunYamlHeadless();
            foreach (var change in plan.Changes) {
                var rel = MakeRelative(projectRoot, change.OriginalPath);
                var dest = Path.Combine(outDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.WriteAllText(dest, change.NewText);
            }
            EditorUtility.RevealInFinder(outDir);
            Debug.Log($"[Zenject2VContainer] {plan.Changes.Count} YAML assets rewritten under {outDir} " +
                      $"({plan.Unsupported.Count} findings).");
        }

        [MenuItem("Window/Zenject2VContainer/Preview Full Migration…")]
        public static void PreviewFull() {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var outDir = Path.Combine(projectRoot, "Library", "Zenject2VContainer", "preview-full");
            if (Directory.Exists(outDir)) Directory.Delete(outDir, true);
            Directory.CreateDirectory(outDir);

            var plan = MigrationPipeline.RunFullHeadless();
            foreach (var change in plan.Changes) {
                var rel = MakeRelative(projectRoot, change.OriginalPath);
                var dest = Path.Combine(outDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.WriteAllText(dest, change.NewText);
            }
            EditorUtility.RevealInFinder(outDir);
            Debug.Log($"[Zenject2VContainer] {plan.Changes.Count} files rewritten under {outDir} " +
                      $"({plan.Unsupported.Count} findings).");
        }

        private static string MakeRelative(string root, string fullPath) {
            if (string.IsNullOrEmpty(fullPath)) return "unknown.cs";
            var normalisedRoot = root.Replace('\\', '/').TrimEnd('/') + "/";
            var normalised = fullPath.Replace('\\', '/');
            if (normalised.StartsWith(normalisedRoot, System.StringComparison.OrdinalIgnoreCase)) {
                return normalised.Substring(normalisedRoot.Length).Replace('/', Path.DirectorySeparatorChar);
            }
            return Path.GetFileName(fullPath);
        }
    }
}
