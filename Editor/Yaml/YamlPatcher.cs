// Line-based YAML editor for Unity scenes / prefabs / .asset files.
//
// Unity serialised assets follow a stable line layout: each MonoBehaviour
// component starts with `m_Script: {fileID: <id>, guid: <hex>, type: <n>}`
// and serialised fields appear one per line beneath it. Full YAML parsing
// (with anchors, complex flow scalars, etc.) is unnecessary and risks
// reformatting the file in ways that bloat diffs. Instead, we operate on
// lines and use simple string replacement guarded by deliberate anchors.
//
// API surface:
//   - PatchScriptGuids(text, guidMap)               — swap m_Script GUIDs.
//   - ReplaceComponentBlock(text, scriptGuid, body) — overwrite serialised
//                                                     field block beneath
//                                                     a given m_Script.
// Both return the patched text plus a list of edits applied for the report.

using System.Collections.Generic;
using System.Text;

namespace Zenject2VContainer.Yaml {
    public sealed class YamlPatch {
        public int LineNumber;     // 1-based
        public string Reason;
        public string Before;
        public string After;
    }

    public sealed class YamlPatchResult {
        public string Text;
        public List<YamlPatch> Edits = new List<YamlPatch>();
    }

    public static class YamlPatcher {
        // Replace m_Script GUID references. `guidMap` maps source GUID → target GUID.
        // Lines must match the canonical Unity shape:
        //   "  m_Script: {fileID: <id>, guid: <hex>, type: <n>}"
        // Indent and surrounding whitespace are preserved verbatim.
        public static YamlPatchResult PatchScriptGuids(string text, IReadOnlyDictionary<string, string> guidMap) {
            var result = new YamlPatchResult();
            if (string.IsNullOrEmpty(text) || guidMap == null || guidMap.Count == 0) {
                result.Text = text;
                return result;
            }
            var lines = text.Split('\n');
            var sb = new StringBuilder(text.Length);
            for (int i = 0; i < lines.Length; i++) {
                var line = lines[i];
                var swapped = TrySwapScriptGuid(line, guidMap, out var oldGuid, out var newGuid);
                if (swapped != null && oldGuid != newGuid) {
                    result.Edits.Add(new YamlPatch {
                        LineNumber = i + 1,
                        Reason = "m_Script GUID " + oldGuid + " -> " + newGuid,
                        Before = line,
                        After = swapped
                    });
                    line = swapped;
                }
                sb.Append(line);
                if (i < lines.Length - 1) sb.Append('\n');
            }
            result.Text = sb.ToString();
            return result;
        }

        private static string TrySwapScriptGuid(string line, IReadOnlyDictionary<string, string> guidMap,
                                                out string oldGuid, out string newGuid) {
            oldGuid = newGuid = null;
            int idx = line.IndexOf("m_Script:");
            if (idx < 0) return null;
            int guidIdx = line.IndexOf("guid:", idx);
            if (guidIdx < 0) return null;
            int valueStart = guidIdx + "guid:".Length;
            while (valueStart < line.Length && line[valueStart] == ' ') valueStart++;
            int valueEnd = valueStart;
            while (valueEnd < line.Length && IsHex(line[valueEnd])) valueEnd++;
            if (valueEnd == valueStart) return null;
            var extracted = line.Substring(valueStart, valueEnd - valueStart);
            if (!guidMap.TryGetValue(extracted, out var replacement)) return null;
            oldGuid = extracted;
            newGuid = replacement;
            return line.Substring(0, valueStart) + replacement + line.Substring(valueEnd);
        }

        private static bool IsHex(char c) =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
    }
}
