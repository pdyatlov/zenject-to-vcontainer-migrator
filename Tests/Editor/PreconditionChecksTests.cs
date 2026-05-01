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
    }
}
