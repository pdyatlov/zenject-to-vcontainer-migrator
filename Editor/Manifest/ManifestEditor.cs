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
            var blockMatch = Regex.Match(manifestJson,
                @"""scopedRegistries""\s*:\s*\[(?<body>.*?)\](\s*,)?",
                RegexOptions.Singleline);
            if (!blockMatch.Success) return result;
            var body = blockMatch.Groups["body"].Value;

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
            if (keep.Count == 0) {
                // Drop the whole scopedRegistries property + any preceding comma in the parent object.
                var prefix = manifestJson.Substring(0, blockMatch.Index);
                var trimmedPrefix = Regex.Replace(prefix, @",\s*$", "");
                result.NewText = trimmedPrefix + manifestJson.Substring(blockMatch.Index + blockMatch.Length);
            } else {
                var replacement = "\"scopedRegistries\": [" + string.Join(",", keep) + "]";
                if (blockMatch.Value.TrimEnd().EndsWith(",")) replacement += ",";
                result.NewText = manifestJson.Remove(blockMatch.Index, blockMatch.Length).Insert(blockMatch.Index, replacement);
            }
            return result;
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
