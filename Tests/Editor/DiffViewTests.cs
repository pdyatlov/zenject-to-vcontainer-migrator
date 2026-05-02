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

        [Test]
        public void FilterToHunks_KeepsContextAroundChange_AndDropsDistantUnchanged() {
            var diff = new[] { " 1", " 2", " 3", " 4", " 5", " 6", " 7", "-8", "+8b", " 9", " 10", " 11", " 12", " 13", " 14" };
            var hunks = DiffView.FilterToHunks(diff, context: 3);
            Assert.That(hunks, Is.EqualTo(new[] { "…", " 5", " 6", " 7", "-8", "+8b", " 9", " 10", " 11" }));
        }

        [Test]
        public void FilterToHunks_NoChanges_ReturnsEmpty() {
            var diff = new[] { " a", " b", " c" };
            Assert.That(DiffView.FilterToHunks(diff), Is.Empty);
        }

        [Test]
        public void FilterToHunks_AllChanges_NoSeparators() {
            var diff = new[] { "-a", "+A", "-b", "+B" };
            Assert.That(DiffView.FilterToHunks(diff), Is.EqualTo(diff));
        }

        [Test]
        public void BuildUnifiedDiff_HugeInput_SuppressesDiffWithMessage() {
            // Build two strings whose line product exceeds LcsCellBudget (5M).
            var big1 = new System.Text.StringBuilder();
            var big2 = new System.Text.StringBuilder();
            for (int i = 0; i < 2300; i++) { big1.Append("a\n"); big2.Append("b\n"); }
            var lines = DiffView.BuildUnifiedDiff(big1.ToString(), big2.ToString());
            Assert.AreEqual(1, lines.Length);
            StringAssert.StartsWith("(diff suppressed", lines[0]);
        }
    }
}
