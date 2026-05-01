using System.IO;
using NUnit.Framework;
using UnityEngine;
using Zenject2VContainer.Core.Scanner;

namespace Zenject2VContainer.Tests {
    public class ZenjectInstallDetectorTests {
        private static string FixtureRoot =>
            Path.Combine(Application.dataPath, "..", "Packages",
                "com.zenject2vcontainer.migrator", "Tests", "Editor",
                "Fixtures", "ScannerInputs", "Manifests");

        [Test]
        public void Detects_Extenject_From_Upm() {
            var json = File.ReadAllText(Path.Combine(FixtureRoot, "extenject-upm.json"));
            var info = ZenjectInstallDetector.DetectFromManifestJson(json);
            Assert.IsTrue(info.ZenjectViaUpm);
            Assert.AreEqual("com.svermeulen.extenject", info.UpmPackageId);
            Assert.AreEqual("9.2.0", info.UpmVersionOrUrl);
            Assert.IsFalse(info.VContainerInstalled);
        }

        [Test]
        public void Detects_Mathijs_Bakker_Extenject_From_Git_Url() {
            var json = File.ReadAllText(Path.Combine(FixtureRoot, "zenject-git.json"));
            var info = ZenjectInstallDetector.DetectFromManifestJson(json);
            Assert.IsTrue(info.ZenjectViaUpm);
            Assert.AreEqual("com.mathijs-bakker.extenject", info.UpmPackageId);
            StringAssert.Contains("github.com/Mathijs-Bakker/Extenject", info.UpmVersionOrUrl);
        }

        [Test]
        public void Reports_No_Zenject_When_Manifest_Is_Clean() {
            var json = File.ReadAllText(Path.Combine(FixtureRoot, "no-zenject.json"));
            var info = ZenjectInstallDetector.DetectFromManifestJson(json);
            Assert.IsFalse(info.ZenjectViaUpm);
            Assert.IsFalse(info.VContainerInstalled);
        }

        [Test]
        public void Detects_VContainer_Presence() {
            var json = File.ReadAllText(Path.Combine(FixtureRoot, "vcontainer-only.json"));
            var info = ZenjectInstallDetector.DetectFromManifestJson(json);
            Assert.IsFalse(info.ZenjectViaUpm);
            Assert.IsTrue(info.VContainerInstalled);
            Assert.AreEqual("1.15.0", info.VContainerVersion);
        }

        [Test]
        public void Detects_Folder_Install_When_Asset_Path_Exists() {
            var existing = new[] { "Assets/Plugins/Zenject", "Assets/SomethingElse" };
            var info = ZenjectInstallDetector.DetectFolderInstall(existing);
            Assert.IsTrue(info.ZenjectViaAssets);
            Assert.AreEqual("Assets/Plugins/Zenject", info.AssetsFolderPath);
        }

        [Test]
        public void Reports_No_Folder_Install_When_Absent() {
            var existing = new[] { "Assets/Scripts" };
            var info = ZenjectInstallDetector.DetectFolderInstall(existing);
            Assert.IsFalse(info.ZenjectViaAssets);
            Assert.IsNull(info.AssetsFolderPath);
        }
    }
}
