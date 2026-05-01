using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Zenject2VContainer.Core.Scanner {
    public sealed class ZenjectScriptGuidTable {
        [Serializable]
        private sealed class Entry {
            public string name;
            public string guid;
        }

        [Serializable]
        private sealed class FileShape {
            public Entry[] scripts;
            public string notes;
        }

        private readonly Dictionary<string, string> _byName;
        private readonly HashSet<string> _allGuids;

        private ZenjectScriptGuidTable(Dictionary<string, string> byName) {
            _byName = byName;
            _allGuids = new HashSet<string>(byName.Values, StringComparer.OrdinalIgnoreCase);
        }

        public bool ContainsScript(string scriptName) => _byName.ContainsKey(scriptName);

        public string GetGuid(string scriptName) =>
            _byName.TryGetValue(scriptName, out var g) ? g : null;

        public bool IsZenjectGuid(string guid) =>
            !string.IsNullOrEmpty(guid) && _allGuids.Contains(guid);

        public IEnumerable<string> AllGuids => _allGuids;

        public static ZenjectScriptGuidTable LoadBundled() {
            // Resolve the JSON path relative to this assembly's source location.
            // The file is shipped alongside the assembly under Editor/Core/Resources/.
            var packageRoot = ResolvePackageRoot();
            var path = Path.Combine(packageRoot, "Editor", "Core", "Resources",
                "zenject-script-guids.json");
            var json = File.ReadAllText(path);
            return Parse(json);
        }

        public static ZenjectScriptGuidTable Parse(string json) {
            var shape = JsonUtility.FromJson<FileShape>(json);
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            if (shape?.scripts != null) {
                foreach (var e in shape.scripts) {
                    if (string.IsNullOrEmpty(e.name) || string.IsNullOrEmpty(e.guid)) continue;
                    dict[e.name] = e.guid;
                }
            }
            return new ZenjectScriptGuidTable(dict);
        }

        private static string ResolvePackageRoot() {
            // Search every Packages/* and the Assets/ tree for our package.json.
            // Cheap because we only do this once at startup.
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            foreach (var candidate in new[] {
                Path.Combine(projectRoot, "Packages", "com.zenject2vcontainer.migrator"),
                Path.Combine(projectRoot, "Assets", "com.zenject2vcontainer.migrator"),
                Path.Combine(projectRoot, "Assets", "Plugins", "com.zenject2vcontainer.migrator"),
                Path.Combine(projectRoot, "Assets", "Plugins", "zenject-to-vcontainer-migrator")
            }) {
                if (File.Exists(Path.Combine(candidate, "package.json"))) return candidate;
            }
            // Fallback: look at every package directory.
            var packagesRoot = Path.Combine(projectRoot, "Packages");
            if (Directory.Exists(packagesRoot)) {
                foreach (var dir in Directory.GetDirectories(packagesRoot)) {
                    var manifest = Path.Combine(dir, "package.json");
                    if (!File.Exists(manifest)) continue;
                    if (File.ReadAllText(manifest).Contains("com.zenject2vcontainer.migrator"))
                        return dir;
                }
            }
            throw new FileNotFoundException(
                "Could not locate Zenject2VContainer package root from: " + projectRoot);
        }
    }
}
