using NUnit.Framework;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.Tests {
    public class PreconditionChecksTests {
        [Test]
        public void Block_When_VContainer_Missing() {
            var info = new InstallationInfo { VContainerInstalled = false };
            var result = PreconditionChecks.CheckVContainerPresence(info);
            Assert.AreEqual(PreconditionResult.Severity.Block, result.Result);
            StringAssert.Contains("VContainer", result.Message);
        }

        [Test]
        public void Pass_When_VContainer_Present() {
            var info = new InstallationInfo { VContainerInstalled = true, VContainerVersion = "1.15.0" };
            var result = PreconditionChecks.CheckVContainerPresence(info);
            Assert.AreEqual(PreconditionResult.Severity.Pass, result.Result);
        }

        [Test]
        public void Warn_When_Git_Working_Tree_Dirty() {
            var result = PreconditionChecks.CheckGitState(isRepo: true, isDirty: true);
            Assert.AreEqual(PreconditionResult.Severity.Warn, result.Result);
        }

        [Test]
        public void Warn_When_Not_A_Git_Repo() {
            var result = PreconditionChecks.CheckGitState(isRepo: false, isDirty: false);
            Assert.AreEqual(PreconditionResult.Severity.Warn, result.Result);
        }

        [Test]
        public void Pass_When_Git_Clean() {
            var result = PreconditionChecks.CheckGitState(isRepo: true, isDirty: false);
            Assert.AreEqual(PreconditionResult.Severity.Pass, result.Result);
        }

        [Test] public void PlayMode_blocked_when_playing() {
            var r = PreconditionChecks.CheckPlayMode(isPlaying: true);
            Assert.AreEqual(PreconditionResult.Severity.Block, r.Result);
            Assert.AreEqual("PLAY_MODE", r.Code);
        }
        [Test] public void PlayMode_passes_when_not_playing() {
            Assert.AreEqual(PreconditionResult.Severity.Pass, PreconditionChecks.CheckPlayMode(isPlaying: false).Result);
        }
        [Test] public void Compiling_blocked() {
            Assert.AreEqual(PreconditionResult.Severity.Block, PreconditionChecks.CheckCompiling(isCompiling: true).Result);
        }
        [Test] public void ProjectCompiles_blocked_when_broken() {
            Assert.AreEqual(PreconditionResult.Severity.Block, PreconditionChecks.CheckProjectCompiles(hasCompileErrors: true).Result);
        }
        [Test] public void AssetSerialization_blocked_when_not_force_text() {
            Assert.AreEqual(PreconditionResult.Severity.Block, PreconditionChecks.CheckAssetSerialization(isForceText: false).Result);
        }
    }
}
