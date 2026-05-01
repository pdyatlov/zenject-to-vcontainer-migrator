using System.Collections.Generic;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.Core.Scanner {
    public static class ZenjectInstallDetector {
        // Known Zenject / Extenject UPM identifiers.
        private static readonly string[] KnownZenjectIds = {
            "com.svermeulen.extenject",
            "com.mathijs-bakker.extenject",
            "com.modesttree.zenject"
        };

        private const string VContainerId = "jp.hadashikick.vcontainer";
        private const string ZenjectGitUrlMarker = "Mathijs-Bakker/Extenject";

        public static InstallationInfo DetectFromManifestJson(string manifestJson) {
            var info = new InstallationInfo();
            var deps = ParseDependencies(manifestJson);

            foreach (var id in KnownZenjectIds) {
                if (!deps.TryGetValue(id, out var versionOrUrl)) continue;
                info.ZenjectViaUpm = true;
                info.UpmPackageId = id;
                info.UpmVersionOrUrl = versionOrUrl;
                break;
            }

            // Fallback: any dependency value referencing the well-known fork URL
            // counts as a Zenject install even when the package id is unconventional.
            if (!info.ZenjectViaUpm) {
                foreach (var kv in deps) {
                    if (!kv.Value.Contains(ZenjectGitUrlMarker)) continue;
                    info.ZenjectViaUpm = true;
                    info.UpmPackageId = kv.Key;
                    info.UpmVersionOrUrl = kv.Value;
                    break;
                }
            }

            if (deps.TryGetValue(VContainerId, out var vcVersion)) {
                info.VContainerInstalled = true;
                info.VContainerVersion = vcVersion;
            }

            return info;
        }

        // Probe candidate folder paths in priority order. Caller passes the list
        // of folders that exist on disk so this method stays Unity-free and
        // testable without AssetDatabase.
        private static readonly string[] CandidateFolders = {
            "Assets/Plugins/Zenject",
            "Assets/Zenject",
            "Assets/Plugins/Extenject",
            "Assets/Extenject"
        };

        public static InstallationInfo DetectFolderInstall(IReadOnlyList<string> existingFolders) {
            var info = new InstallationInfo();
            foreach (var candidate in CandidateFolders) {
                foreach (var existing in existingFolders) {
                    if (!string.Equals(existing, candidate, System.StringComparison.OrdinalIgnoreCase)) continue;
                    info.ZenjectViaAssets = true;
                    info.AssetsFolderPath = candidate;
                    return info;
                }
            }
            return info;
        }

        // Minimal manifest.json shape: { "dependencies": { "id": "version-or-url", ... } }
        // We deliberately avoid Newtonsoft.Json — JsonUtility cannot deserialise
        // a Dictionary<string,string> directly, so we use a tiny structural parser.
        private static Dictionary<string, string> ParseDependencies(string manifestJson) {
            var deps = new Dictionary<string, string>();
            var depsKeyIndex = manifestJson.IndexOf("\"dependencies\"", System.StringComparison.Ordinal);
            if (depsKeyIndex < 0) return deps;

            var openBrace = manifestJson.IndexOf('{', depsKeyIndex);
            if (openBrace < 0) return deps;

            int depth = 0;
            int closeBrace = -1;
            for (var i = openBrace; i < manifestJson.Length; i++) {
                if (manifestJson[i] == '{') depth++;
                else if (manifestJson[i] == '}') {
                    depth--;
                    if (depth == 0) { closeBrace = i; break; }
                }
            }
            if (closeBrace < 0) return deps;

            var body = manifestJson.Substring(openBrace + 1, closeBrace - openBrace - 1);
            var entries = body.Split(',');
            foreach (var rawEntry in entries) {
                var entry = rawEntry.Trim();
                if (entry.Length == 0) continue;
                var colon = entry.IndexOf(':');
                if (colon < 0) continue;
                var key = entry.Substring(0, colon).Trim().Trim('"');
                var value = entry.Substring(colon + 1).Trim().Trim('"');
                deps[key] = value;
            }
            return deps;
        }
    }
}
