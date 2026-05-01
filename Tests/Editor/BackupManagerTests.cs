using System.IO;
using NUnit.Framework;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.Tests {
    [TestFixture]
    public class BackupManagerTests {
        private string _projectRoot;
        private string _backupRoot;

        [SetUp] public void Setup() {
            _projectRoot = Path.Combine(Path.GetTempPath(), "z2vc-backup-test-" + System.Guid.NewGuid().ToString("N"));
            _backupRoot = Path.Combine(_projectRoot, "Temp", "Zenject2VContainer", "Backup");
            Directory.CreateDirectory(Path.Combine(_projectRoot, "Assets"));
        }
        [TearDown] public void Cleanup() {
            if (Directory.Exists(_projectRoot)) Directory.Delete(_projectRoot, true);
        }

        [Test]
        public void SnapshotThenRestore_RecoversOriginalContent() {
            var f1 = Path.Combine(_projectRoot, "Assets", "A.cs");
            File.WriteAllText(f1, "ORIGINAL");
            var stamp = BackupManager.Snapshot(_projectRoot, new[] { f1 });
            Assert.IsNotEmpty(stamp);
            File.WriteAllText(f1, "MUTATED");
            BackupManager.Restore(_projectRoot, stamp);
            Assert.AreEqual("ORIGINAL", File.ReadAllText(f1));
        }

        [Test]
        public void Snapshot_PreservesRelativeStructure() {
            var nested = Path.Combine(_projectRoot, "Assets", "Sub", "B.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(nested));
            File.WriteAllText(nested, "X");
            var stamp = BackupManager.Snapshot(_projectRoot, new[] { nested });
            var backedUp = Path.Combine(_projectRoot, "Temp", "Zenject2VContainer", "Backup", stamp, "Assets", "Sub", "B.cs");
            Assert.IsTrue(File.Exists(backedUp), "Expected backup copy at " + backedUp);
        }

        [Test]
        public void List_ReturnsTimestampsNewestFirst() {
            File.WriteAllText(Path.Combine(_projectRoot, "Assets", "A.cs"), "x");
            var s1 = BackupManager.Snapshot(_projectRoot, new[] { Path.Combine(_projectRoot, "Assets", "A.cs") });
            System.Threading.Thread.Sleep(1100);
            var s2 = BackupManager.Snapshot(_projectRoot, new[] { Path.Combine(_projectRoot, "Assets", "A.cs") });
            var list = BackupManager.List(_projectRoot);
            Assert.AreEqual(s2, list[0]);
            Assert.AreEqual(s1, list[1]);
        }
    }
}
