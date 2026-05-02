using System.IO;
using NUnit.Framework;
using Zenject2VContainer.Core;
using Zenject2VContainer.Manifest;

namespace Zenject2VContainer.Tests {
    [TestFixture]
    public class ZenjectRemoverPlanTests {
        private string _projectRoot;

        [SetUp] public void Setup() {
            _projectRoot = Path.Combine(Path.GetTempPath(), "z2vc-removal-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_projectRoot, "Packages"));
            File.WriteAllText(Path.Combine(_projectRoot, "Packages", "manifest.json"), @"{
  ""scopedRegistries"": [
    { ""name"": ""Extenject"", ""url"": ""x"", ""scopes"": [ ""com.svermeulen"" ] }
  ]
}");
        }
        [TearDown] public void Cleanup() {
            if (Directory.Exists(_projectRoot)) Directory.Delete(_projectRoot, true);
        }

        [Test]
        public void Plan_UpmInstall_PopulatesUpmPackageId() {
            var info = new InstallationInfo { ZenjectViaUpm = true, UpmPackageId = "com.svermeulen.extenject" };
            var plan = new ZenjectRemover().Plan(info, _projectRoot);
            Assert.IsTrue(plan.UpmInstall);
            Assert.AreEqual("com.svermeulen.extenject", plan.UpmPackageId);
            Assert.IsFalse(plan.IsNoop);
        }

        [Test]
        public void Plan_FolderInstall_PopulatesFolderPath() {
            var info = new InstallationInfo { ZenjectViaAssets = true, AssetsFolderPath = "Assets/Plugins/Zenject" };
            var plan = new ZenjectRemover().Plan(info, _projectRoot);
            Assert.AreEqual("Assets/Plugins/Zenject", plan.FolderInstallPath);
            Assert.IsFalse(plan.IsNoop);
        }

        [Test]
        public void Plan_DetectsZenjectScopedRegistries() {
            var info = new InstallationInfo { ZenjectViaUpm = true, UpmPackageId = "com.svermeulen.extenject" };
            var plan = new ZenjectRemover().Plan(info, _projectRoot);
            Assert.That(plan.ScopedRegistryNamesToDrop, Is.EquivalentTo(new[] { "Extenject" }));
        }

        [Test]
        public void Plan_NotInstalledAtAll_IsNoop() {
            var info = new InstallationInfo();
            File.WriteAllText(Path.Combine(_projectRoot, "Packages", "manifest.json"), "{}");
            var plan = new ZenjectRemover().Plan(info, _projectRoot);
            Assert.IsTrue(plan.IsNoop);
        }
    }
}
