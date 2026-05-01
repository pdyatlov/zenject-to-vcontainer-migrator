using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Zenject2VContainer.Core.Scanner;

namespace Zenject2VContainer.Tests {
    public class AssetScannerTests {
        private static string FixtureRoot =>
            Path.Combine(Application.dataPath, "..", "Packages",
                "com.zenject2vcontainer.migrator", "Tests", "Editor",
                "Fixtures", "ScannerInputs", "Yaml");

        [Test]
        public void Finds_Zenject_GuidLine_In_Scene() {
            var table = ZenjectScriptGuidTable.LoadBundled();
            var path = Path.Combine(FixtureRoot, "with-scene-context.unity");
            var findings = new List<Core.AssetFinding>();
            AssetScanner.ScanFile(path, table, findings);

            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual("SceneContext", findings[0].ZenjectScriptName);
            Assert.AreEqual(path, findings[0].FilePath);
            Assert.Greater(findings[0].Line, 0);
        }

        [Test]
        public void Finds_Nothing_When_No_Zenject_References() {
            var table = ZenjectScriptGuidTable.LoadBundled();
            var path = Path.Combine(FixtureRoot, "no-zenject.unity");
            var findings = new List<Core.AssetFinding>();
            AssetScanner.ScanFile(path, table, findings);
            Assert.AreEqual(0, findings.Count);
        }
    }
}
