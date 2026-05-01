using System.Collections.Generic;
using NUnit.Framework;
using Zenject2VContainer.Yaml;

namespace Zenject2VContainer.Tests {
    public class YamlPatcherTests {
        [Test]
        public void Swaps_Single_Script_Guid_And_Records_Edit() {
            var input =
                "MonoBehaviour:\n" +
                "  m_Script: {fileID: 11500000, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, type: 3}\n" +
                "  _foo: 0\n";
            var map = new Dictionary<string, string> {
                { "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" }
            };
            var result = YamlPatcher.PatchScriptGuids(input, map);
            StringAssert.Contains("guid: bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", result.Text);
            StringAssert.DoesNotContain("guid: aaaaaaaa", result.Text);
            Assert.AreEqual(1, result.Edits.Count);
            Assert.AreEqual(2, result.Edits[0].LineNumber);
        }

        [Test]
        public void Leaves_Untracked_Guids_Untouched() {
            var input =
                "  m_Script: {fileID: 11500000, guid: 11111111111111111111111111111111, type: 3}\n";
            var map = new Dictionary<string, string> {
                { "22222222222222222222222222222222", "33333333333333333333333333333333" }
            };
            var result = YamlPatcher.PatchScriptGuids(input, map);
            Assert.AreEqual(input, result.Text);
            Assert.AreEqual(0, result.Edits.Count);
        }

        [Test]
        public void Preserves_Indent_And_Surrounding_Whitespace() {
            var input =
                "    m_Script: {fileID: 11500000, guid: 4444aaaaaaaaaaaaaaaaaaaaaaaaaaaa, type: 3}";
            var map = new Dictionary<string, string> {
                { "4444aaaaaaaaaaaaaaaaaaaaaaaaaaaa", "9999bbbbbbbbbbbbbbbbbbbbbbbbbbbb" }
            };
            var result = YamlPatcher.PatchScriptGuids(input, map);
            Assert.AreEqual(
                "    m_Script: {fileID: 11500000, guid: 9999bbbbbbbbbbbbbbbbbbbbbbbbbbbb, type: 3}",
                result.Text);
        }

        [Test]
        public void Handles_Multiple_Replacements_Across_File() {
            var input =
                "  m_Script: {fileID: 11500000, guid: aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa, type: 3}\n" +
                "  _ignored: foo\n" +
                "  m_Script: {fileID: 11500000, guid: cccccccccccccccccccccccccccccccc, type: 3}\n";
            var map = new Dictionary<string, string> {
                { "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb" },
                { "cccccccccccccccccccccccccccccccc", "dddddddddddddddddddddddddddddddd" }
            };
            var result = YamlPatcher.PatchScriptGuids(input, map);
            StringAssert.Contains("guid: bbbb", result.Text);
            StringAssert.Contains("guid: dddd", result.Text);
            Assert.AreEqual(2, result.Edits.Count);
        }
    }
}
