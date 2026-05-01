using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using Zenject2VContainer.Core.Scanner;

namespace Zenject2VContainer.Core {
    public static class MigrationPipeline {
        public const string ToolVersion = "0.1.0";

        public static ZenjectUsageReport RunScanHeadless() {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var manifestPath = Path.Combine(projectRoot, "Packages", "manifest.json");
            var manifestJson = File.Exists(manifestPath) ? File.ReadAllText(manifestPath) : "{}";

            var existingFolders = new List<string>();
            foreach (var f in new[] {
                "Assets/Plugins/Zenject", "Assets/Zenject",
                "Assets/Plugins/Extenject", "Assets/Extenject"
            }) {
                if (Directory.Exists(Path.Combine(projectRoot, f))) existingFolders.Add(f);
            }

            // C# sources from user assemblies (skip read-only packages and tool's own asmdefs).
            var sources = new List<(string, string)>();
            var refs = new List<string>();
            foreach (var asm in CompilationPipeline.GetAssemblies(AssembliesType.Editor)) {
                if (asm.name.StartsWith("Zenject2VContainer", StringComparison.Ordinal)) continue;
                foreach (var src in asm.sourceFiles) {
                    if (!File.Exists(src)) continue;
                    sources.Add((src, File.ReadAllText(src)));
                }
                foreach (var r in asm.compiledAssemblyReferences) {
                    if (File.Exists(r)) refs.Add(r);
                }
            }

            // Asset files: every .unity / .prefab / .asset under Assets/.
            var assetFiles = new List<string>();
            foreach (var p in Directory.EnumerateFiles(Path.Combine(projectRoot, "Assets"),
                         "*", SearchOption.AllDirectories)) {
                var ext = Path.GetExtension(p);
                if (ext == ".unity" || ext == ".prefab" || ext == ".asset") assetFiles.Add(p);
            }

            return ProjectScanner.Run(new ProjectScanner.Input {
                ToolVersion = ToolVersion,
                UnityVersion = Application.unityVersion,
                ScannedAtUtc = DateTime.UtcNow.ToString("o"),
                ManifestJson = manifestJson,
                ExistingFolders = existingFolders,
                CSharpSources = sources,
                ReferenceDllPaths = refs,
                AssetFiles = assetFiles,
                GuidTable = ZenjectScriptGuidTable.LoadBundled()
            });
        }

        public static string WriteScanReport(ZenjectUsageReport report, string outputPath) {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            File.WriteAllText(outputPath, ZenjectUsageReport.ToJson(report));
            return outputPath;
        }
    }
}
