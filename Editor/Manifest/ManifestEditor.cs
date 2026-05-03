using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Zenject2VContainer.Manifest {
    public sealed class ManifestEditResult {
        public string NewText;
        public bool Modified;
        public List<string> RemovedRegistryNames = new List<string>();
    }

    public static class ManifestEditor {
        private static readonly string[] ZenjectScopePrefixes = {
            "com.svermeulen", "com.extenject", "com.zenject"
        };

        public static ManifestEditResult StripZenjectScopedRegistries(string manifestJson) {
            var result = new ManifestEditResult { NewText = manifestJson };
            // Locate `"scopedRegistries"` and bracket-match the array. Regex with
            // `.*?\]` stops at the first inner `]` (the per-entry scopes array),
            // truncating the body — so do it manually.
            var keyMatch = Regex.Match(manifestJson, @"""scopedRegistries""\s*:\s*\[");
            if (!keyMatch.Success) return result;
            int arrayOpen = keyMatch.Index + keyMatch.Length - 1; // index of '['
            int arrayClose = FindMatchingBracket(manifestJson, arrayOpen);
            if (arrayClose < 0) return result;
            int blockStart = keyMatch.Index;
            int blockEnd = arrayClose + 1;
            // Consume one trailing comma after `]` if present.
            var trailing = Regex.Match(manifestJson.Substring(blockEnd), @"^\s*,");
            if (trailing.Success) blockEnd += trailing.Length;
            int blockLen = blockEnd - blockStart;
            var body = manifestJson.Substring(arrayOpen + 1, arrayClose - arrayOpen - 1);

            var entries = SplitTopLevelObjects(body);
            var keep = new List<string>();
            foreach (var raw in entries) {
                if (IsZenjectOnlyRegistry(raw, out var name)) {
                    result.RemovedRegistryNames.Add(name);
                } else {
                    keep.Add(raw);
                }
            }
            if (result.RemovedRegistryNames.Count == 0) return result;

            result.Modified = true;
            string blockText = manifestJson.Substring(blockStart, blockLen);
            if (keep.Count == 0) {
                // Drop the whole scopedRegistries property + any preceding comma in the parent object.
                var prefix = manifestJson.Substring(0, blockStart);
                var trimmedPrefix = Regex.Replace(prefix, @",\s*$", "");
                result.NewText = trimmedPrefix + manifestJson.Substring(blockEnd);
            } else {
                var replacement = "\"scopedRegistries\": [" + string.Join(",", keep) + "]";
                if (blockText.TrimEnd().EndsWith(",")) replacement += ",";
                result.NewText = manifestJson.Remove(blockStart, blockLen).Insert(blockStart, replacement);
            }
            return result;
        }

        private static int FindMatchingBracket(string s, int openIdx) {
            int depth = 0;
            bool inString = false;
            for (int i = openIdx; i < s.Length; i++) {
                char c = s[i];
                if (inString) {
                    if (c == '\\') { i++; continue; }
                    if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') { inString = true; continue; }
                if (c == '[') depth++;
                else if (c == ']') {
                    depth--;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        private static List<string> SplitTopLevelObjects(string body) {
            var list = new List<string>();
            int depth = 0;
            int start = -1;
            for (int i = 0; i < body.Length; i++) {
                var c = body[i];
                if (c == '{') {
                    if (depth == 0) start = i;
                    depth++;
                } else if (c == '}') {
                    depth--;
                    if (depth == 0 && start >= 0) {
                        list.Add(body.Substring(start, i - start + 1));
                        start = -1;
                    }
                }
            }
            return list;
        }

        private static bool IsZenjectOnlyRegistry(string raw, out string name) {
            name = ExtractStringField(raw, "name") ?? "";
            var scopes = ExtractStringArray(raw, "scopes");
            if (scopes.Count == 0) return false;
            foreach (var s in scopes) {
                bool match = false;
                foreach (var p in ZenjectScopePrefixes) {
                    if (s.StartsWith(p, System.StringComparison.OrdinalIgnoreCase)) { match = true; break; }
                }
                if (!match) return false;
            }
            return true;
        }

        private static string ExtractStringField(string raw, string field) {
            var m = Regex.Match(raw, "\"" + Regex.Escape(field) + "\"\\s*:\\s*\"(?<v>[^\"]*)\"");
            return m.Success ? m.Groups["v"].Value : null;
        }

        private static List<string> ExtractStringArray(string raw, string field) {
            var list = new List<string>();
            var m = Regex.Match(raw, "\"" + Regex.Escape(field) + "\"\\s*:\\s*\\[(?<body>[^\\]]*)\\]");
            if (!m.Success) return list;
            foreach (Match s in Regex.Matches(m.Groups["body"].Value, "\"(?<v>[^\"]*)\"")) {
                list.Add(s.Groups["v"].Value);
            }
            return list;
        }
    }
}
