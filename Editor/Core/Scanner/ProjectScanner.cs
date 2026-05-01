using System.Collections.Generic;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.Core.Scanner {
    public static class ProjectScanner {
        public sealed class Input {
            public string ToolVersion;
            public string UnityVersion;
            public string ScannedAtUtc;
            public string ManifestJson;
            public IReadOnlyList<string> ExistingFolders;
            public IReadOnlyList<(string FilePath, string Source)> CSharpSources;
            public IReadOnlyList<string> ReferenceDllPaths;
            public IReadOnlyList<string> AssetFiles;
            public ZenjectScriptGuidTable GuidTable;
        }

        public static ZenjectUsageReport Run(Input input) {
            var report = new ZenjectUsageReport {
                ToolVersion = input.ToolVersion,
                UnityVersion = input.UnityVersion,
                ScannedAtUtc = input.ScannedAtUtc
            };

            var fromManifest = ZenjectInstallDetector.DetectFromManifestJson(input.ManifestJson);
            var fromFolders = ZenjectInstallDetector.DetectFolderInstall(input.ExistingFolders);
            report.Install = Merge(fromManifest, fromFolders);

            if (input.CSharpSources != null && input.CSharpSources.Count > 0) {
                var compilation = CompilationLoader.BuildFromSources(
                    "ScannedProject", input.CSharpSources, input.ReferenceDllPaths);
                foreach (var finding in CSharpScanner.Scan(compilation)) {
                    report.CSharpFindings.Add(finding);
                }
            }

            if (input.AssetFiles != null && input.GuidTable != null) {
                foreach (var path in input.AssetFiles) {
                    AssetScanner.ScanFile(path, input.GuidTable, report.AssetFindings);
                }
            }

            return report;
        }

        private static InstallationInfo Merge(InstallationInfo a, InstallationInfo b) =>
            new InstallationInfo {
                ZenjectViaUpm = a.ZenjectViaUpm || b.ZenjectViaUpm,
                UpmPackageId = a.UpmPackageId ?? b.UpmPackageId,
                UpmVersionOrUrl = a.UpmVersionOrUrl ?? b.UpmVersionOrUrl,
                ZenjectViaAssets = a.ZenjectViaAssets || b.ZenjectViaAssets,
                AssetsFolderPath = a.AssetsFolderPath ?? b.AssetsFolderPath,
                VContainerInstalled = a.VContainerInstalled || b.VContainerInstalled,
                VContainerVersion = a.VContainerVersion ?? b.VContainerVersion
            };
    }
}
