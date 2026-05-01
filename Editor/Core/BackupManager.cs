using System;
using System.Collections.Generic;
using System.IO;

namespace Zenject2VContainer.Core {
    public static class BackupManager {
        public const string BackupRootRel = "Temp/Zenject2VContainer/Backup";

        public static string Snapshot(string projectRoot, IEnumerable<string> filePaths) {
            var stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
            var dest = Path.Combine(projectRoot, BackupRootRel.Replace('/', Path.DirectorySeparatorChar), stamp);
            Directory.CreateDirectory(dest);
            var rootNorm = Path.GetFullPath(projectRoot).Replace('\\', '/').TrimEnd('/') + "/";
            foreach (var f in filePaths) {
                if (!File.Exists(f)) continue;
                var full = Path.GetFullPath(f).Replace('\\', '/');
                if (!full.StartsWith(rootNorm, StringComparison.OrdinalIgnoreCase)) continue;
                var rel = full.Substring(rootNorm.Length);
                var backupPath = Path.Combine(dest, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath));
                File.Copy(f, backupPath, overwrite: true);
            }
            return stamp;
        }

        public static void Restore(string projectRoot, string stamp) {
            var src = Path.Combine(projectRoot, BackupRootRel.Replace('/', Path.DirectorySeparatorChar), stamp);
            if (!Directory.Exists(src)) throw new DirectoryNotFoundException("Backup not found: " + src);
            foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories)) {
                var rel = file.Substring(src.Length).TrimStart(Path.DirectorySeparatorChar, '/');
                var dest = Path.Combine(projectRoot, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Copy(file, dest, overwrite: true);
            }
        }

        public static List<string> List(string projectRoot) {
            var root = Path.Combine(projectRoot, BackupRootRel.Replace('/', Path.DirectorySeparatorChar));
            var result = new List<string>();
            if (!Directory.Exists(root)) return result;
            foreach (var d in Directory.EnumerateDirectories(root)) {
                result.Add(Path.GetFileName(d));
            }
            result.Sort(StringComparer.Ordinal);
            result.Reverse();
            return result;
        }

        public static string LatestStamp(string projectRoot) {
            var list = List(projectRoot);
            return list.Count > 0 ? list[0] : null;
        }
    }
}
