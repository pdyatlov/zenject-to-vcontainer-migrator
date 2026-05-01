using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Zenject2VContainer.Core;
using Zenject2VContainer.Core.Scanner;

namespace Zenject2VContainer.Tests {
    public class ProjectScannerTests {
        private static string PkgRoot =>
            Path.Combine(Application.dataPath, "..", "Packages",
                "com.zenject2vcontainer.migrator");

        [Test]
        public void End_To_End_Scan_Reports_All_Three_Layers() {
            var manifestJson = File.ReadAllText(Path.Combine(PkgRoot,
                "Tests", "Editor", "Fixtures", "ScannerInputs", "Manifests",
                "extenject-upm.json"));

            var stubRefs = new[] {
                Path.Combine(PkgRoot, "Tests", "Editor", "References~", "UnityEngine.dll"),
                Path.Combine(PkgRoot, "Tests", "Editor", "References~", "Zenject.dll"),
                Path.Combine(PkgRoot, "Tests", "Editor", "References~", "VContainer.dll")
            };

            const string src =
                "using Zenject;\n" +
                "public class GameInstaller : MonoInstaller {\n" +
                "    [Inject] private DiContainer _c;\n" +
                "    public override void InstallBindings() {}\n" +
                "}\n";

            var input = new ProjectScanner.Input {
                ToolVersion = "0.1.0",
                UnityVersion = "test",
                ScannedAtUtc = "2026-05-01T00:00:00Z",
                ManifestJson = manifestJson,
                ExistingFolders = new[] { "Assets/Plugins/Zenject" },
                CSharpSources = new[] { ("GameInstaller.cs", src) },
                ReferenceDllPaths = stubRefs,
                AssetFiles = new[] {
                    Path.Combine(PkgRoot, "Tests", "Editor", "Fixtures",
                        "ScannerInputs", "Yaml", "with-scene-context.unity")
                },
                GuidTable = ZenjectScriptGuidTable.LoadBundled()
            };

            var report = ProjectScanner.Run(input);

            Assert.IsTrue(report.Install.ZenjectViaUpm);
            Assert.IsTrue(report.Install.ZenjectViaAssets);
            Assert.Greater(report.CSharpFindings.Count, 0);
            Assert.Greater(report.AssetFindings.Count, 0);
        }
    }
}
