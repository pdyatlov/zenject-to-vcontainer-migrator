using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using Zenject2VContainer.Core.Scanner;
using Zenject2VContainer.CSharp;
using Zenject2VContainer.Yaml;

namespace Zenject2VContainer.Core {
    public static class MigrationPipeline {
        public static readonly string ToolVersion = ResolveVersion();

        private static string ResolveVersion() {
            try {
                var info = UnityEditor.PackageManager.PackageInfo
                    .FindForAssembly(typeof(MigrationPipeline).Assembly);
                if (info != null && !string.IsNullOrEmpty(info.version))
                    return info.version;
            } catch {
                // PackageInfo unavailable in some headless / test contexts — fall through.
            }
            return "0.1.0";
        }

        public static MigrationPlan RunCSharpHeadless(IMigrationProgress progress = null) {
            progress = progress ?? NullMigrationProgress.Instance;
            progress.Report("Migrating C#", "Building host compilation…", 0f);
            var compilation = BuildHostCompilation();
            var pipeline = new RewritePipeline(new[] { "*" });
            var changes = pipeline.Run(compilation, progress);
            var plan = new MigrationPlan();
            plan.Changes.AddRange(changes);
            foreach (var change in changes) {
                plan.Unsupported.AddRange(change.RelatedFindings);
            }
            return plan;
        }

        public static MigrationPlan RunYamlHeadless(IMigrationProgress progress = null) {
            progress = progress ?? NullMigrationProgress.Instance;
            progress.Report("Migrating YAML assets", "Resolving GUIDs…", 0f);
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var assetsRoot = Path.Combine(projectRoot, "Assets");
            var zenjectTable = ZenjectScriptGuidTable.LoadBundled();
            var lookup = ScriptGuidLookup.Resolve();
            var migrator = new AssetMigrator(zenjectTable, lookup);
            return migrator.MigrateAssetsDirectory(assetsRoot, progress);
        }

        public static MigrationPlan RunFullHeadless(IMigrationProgress progress = null) {
            var csharp = RunCSharpHeadless(progress);
            var yaml = RunYamlHeadless(progress);
            var combined = new MigrationPlan();
            combined.Changes.AddRange(csharp.Changes);
            combined.Changes.AddRange(yaml.Changes);
            combined.Unsupported.AddRange(csharp.Unsupported);
            combined.Unsupported.AddRange(yaml.Unsupported);
            return combined;
        }

        private static Microsoft.CodeAnalysis.CSharp.CSharpCompilation BuildHostCompilation() {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var sources = new List<(string, string)>();
            var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var scriptAssembliesDir = Path.Combine(projectRoot, "Library", "ScriptAssemblies");
            foreach (var asm in CompilationPipeline.GetAssemblies(AssembliesType.Editor)) {
                if (asm.name.StartsWith("Zenject2VContainer", StringComparison.Ordinal)) continue;
                if (IsThirdPartyScanIgnoredAssembly(asm.name)) {
                    var compiledDll = Path.Combine(scriptAssembliesDir, asm.name + ".dll");
                    if (File.Exists(compiledDll)) refs.Add(compiledDll);
                } else {
                    foreach (var src in asm.sourceFiles) {
                        if (!File.Exists(src)) continue;
                        // Belt-and-braces: even if the asmdef name doesn't match the third-party
                        // skip-list, files that physically live inside the Zenject / Extenject /
                        // VContainer install folders are not user code. Skipping here prevents
                        // the rewriter from touching their tests / samples (those will be wiped
                        // by M5's removal step anyway).
                        if (IsThirdPartyVendoredFile(src)) continue;
                        sources.Add((src, File.ReadAllText(src)));
                    }
                }
                foreach (var r in asm.compiledAssemblyReferences) {
                    if (File.Exists(r)) refs.Add(r);
                }
            }
            return CompilationLoader.BuildFromSources("HostCompilation", sources, refs);
        }

        private static bool IsThirdPartyVendoredFile(string path) {
            var n = path.Replace('\\', '/');
            return n.IndexOf("/Plugins/Zenject/", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("/Plugins/Extenject/", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("/Assets/Zenject/", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("/Assets/Extenject/", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("/VContainer/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static ZenjectUsageReport RunScanHeadless(IMigrationProgress progress = null) {
            progress = progress ?? NullMigrationProgress.Instance;
            progress.Report("Scanning", "Reading manifest and detecting installs…", 0.1f);
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

            // Build a single CSharpCompilation for the project. User assemblies contribute
            // source files; third-party assemblies (Zenject, plus anything matching the
            // skip-source heuristic) contribute their already-compiled DLL as a metadata
            // reference instead. Mixing all source into one compilation would attribute
            // every symbol to the synthesised assembly and break SymbolMatchers checks
            // that rely on ContainingAssembly.Name.
            var sources = new List<(string, string)>();
            var refs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var scriptAssembliesDir = Path.Combine(projectRoot, "Library", "ScriptAssemblies");
            foreach (var asm in CompilationPipeline.GetAssemblies(AssembliesType.Editor)) {
                if (asm.name.StartsWith("Zenject2VContainer", StringComparison.Ordinal)) continue;

                if (IsThirdPartyScanIgnoredAssembly(asm.name)) {
                    // Skip the assembly's source files; reference its compiled DLL instead.
                    var compiledDll = Path.Combine(scriptAssembliesDir, asm.name + ".dll");
                    if (File.Exists(compiledDll)) refs.Add(compiledDll);
                } else {
                    foreach (var src in asm.sourceFiles) {
                        if (!File.Exists(src)) continue;
                        sources.Add((src, File.ReadAllText(src)));
                    }
                }

                foreach (var r in asm.compiledAssemblyReferences) {
                    if (File.Exists(r)) refs.Add(r);
                }
            }

            // Asset files: every .unity / .prefab / .asset under Assets/.
            progress.Report("Scanning", "Enumerating asset files…", 0.4f);
            var assetFiles = new List<string>();
            foreach (var p in Directory.EnumerateFiles(Path.Combine(projectRoot, "Assets"),
                         "*", SearchOption.AllDirectories)) {
                var ext = Path.GetExtension(p);
                if (ext == ".unity" || ext == ".prefab" || ext == ".asset") assetFiles.Add(p);
            }

            progress.Report("Scanning", $"Running Roslyn over {sources.Count} C# files and {assetFiles.Count} assets…", 0.7f);
            var report = ProjectScanner.Run(new ProjectScanner.Input {
                ToolVersion = ToolVersion,
                UnityVersion = Application.unityVersion,
                ScannedAtUtc = DateTime.UtcNow.ToString("o"),
                ManifestJson = manifestJson,
                ExistingFolders = existingFolders,
                CSharpSources = sources,
                ReferenceDllPaths = new List<string>(refs),
                AssetFiles = assetFiles,
                GuidTable = ZenjectScriptGuidTable.LoadBundled()
            });

            // Populate AllFilesScanned: project-relative, forward-slash, deduped.
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rootPrefix = projectRoot.Replace('\\', '/').TrimEnd('/') + "/";
            foreach (var (filePath, _) in sources) {
                var n = filePath.Replace('\\', '/');
                var rel = n.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
                    ? n.Substring(rootPrefix.Length) : n;
                if (seen.Add(rel)) report.AllFilesScanned.Add(rel);
            }
            foreach (var filePath in assetFiles) {
                var n = filePath.Replace('\\', '/');
                var rel = n.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase)
                    ? n.Substring(rootPrefix.Length) : n;
                if (seen.Add(rel)) report.AllFilesScanned.Add(rel);
            }

            return report;
        }

        // Assemblies whose source files we deliberately skip during scan. Their compiled
        // DLLs are added as metadata references so user code still resolves against the
        // correct ContainingAssembly.Name, which SymbolMatchers needs to identify Zenject
        // and VContainer symbols.
        private static bool IsThirdPartyScanIgnoredAssembly(string asmName) {
            return asmName == "Zenject"
                || asmName.StartsWith("Zenject-", StringComparison.Ordinal)
                || asmName.StartsWith("Extenject-", StringComparison.Ordinal)
                || asmName == "VContainer"
                || asmName.StartsWith("VContainer.", StringComparison.Ordinal);
        }

        public static string WriteScanReport(ZenjectUsageReport report, string outputPath) {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            File.WriteAllText(outputPath, ZenjectUsageReport.ToJson(report));
            return outputPath;
        }
    }
}
