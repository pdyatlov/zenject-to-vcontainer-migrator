using NUnit.Framework;
using Zenject2VContainer.UI;

namespace Zenject2VContainer.Tests {
    [TestFixture]
    public class DiffViewTests {
        [Test]
        public void Diff_MarksAddedLinesWithPlus_RemovedWithMinus_UnchangedWithSpace() {
            var lines = DiffView.BuildUnifiedDiff("a\nb\nc\n", "a\nB\nc\n");
            Assert.That(lines, Is.EqualTo(new[] { " a", "-b", "+B", " c" }));
        }

        [Test]
        public void Diff_AppendOnly_ShowsAllNewLinesAsPlus() {
            var lines = DiffView.BuildUnifiedDiff("", "x\ny\n");
            Assert.That(lines, Is.EqualTo(new[] { "+x", "+y" }));
        }

        [Test]
        public void Diff_DeleteOnly_ShowsAllOldLinesAsMinus() {
            var lines = DiffView.BuildUnifiedDiff("x\ny\n", "");
            Assert.That(lines, Is.EqualTo(new[] { "-x", "-y" }));
        }
    }
}
