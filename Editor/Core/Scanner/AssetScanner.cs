using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.Core.Scanner {
    public static class AssetScanner {
        private static readonly Regex GuidLine = new Regex(
            @"guid:\s*([0-9a-fA-F]{32})",
            RegexOptions.Compiled);

        public static void ScanFile(string filePath, ZenjectScriptGuidTable table,
                                    List<AssetFinding> output) {
            using var reader = new StreamReader(filePath);
            string line;
            int lineNumber = 0;
            while ((line = reader.ReadLine()) != null) {
                lineNumber++;
                var match = GuidLine.Match(line);
                if (!match.Success) continue;
                var guid = match.Groups[1].Value;
                if (!table.IsZenjectGuid(guid)) continue;
                output.Add(new AssetFinding {
                    FilePath = filePath,
                    Line = lineNumber,
                    ZenjectGuid = guid,
                    ZenjectScriptName = ResolveName(table, guid)
                });
            }
        }

        public static void ScanDirectory(string rootDirectory,
                                         ZenjectScriptGuidTable table,
                                         List<AssetFinding> output) {
            string[] extensions = { ".unity", ".prefab", ".asset" };
            foreach (var path in Directory.EnumerateFiles(rootDirectory, "*",
                         SearchOption.AllDirectories)) {
                var ext = Path.GetExtension(path);
                if (System.Array.IndexOf(extensions, ext) < 0) continue;
                ScanFile(path, table, output);
            }
        }

        private static string ResolveName(ZenjectScriptGuidTable table, string guid) {
            // Linear scan over a tiny dictionary; optimise only if profiling shows a hotspot.
            foreach (var name in new[] {
                "SceneContext", "ProjectContext", "GameObjectContext",
                "ZenjectBinding", "MonoInstaller"
            }) {
                if (string.Equals(table.GetGuid(name), guid,
                        System.StringComparison.OrdinalIgnoreCase)) return name;
            }
            return "Unknown";
        }
    }
}
