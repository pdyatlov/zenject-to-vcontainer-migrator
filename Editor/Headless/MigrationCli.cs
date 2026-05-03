using System;
using System.IO;
using System.Linq;
using UnityEditor;
using Zenject2VContainer.Core;
using Zenject2VContainer.Reporting;

namespace Zenject2VContainer.Headless {
    public static class MigrationCli {
        // Entry point for `-executeMethod Zenject2VContainer.Headless.MigrationCli.RunFullEntry`.
        // Reads `-projectRoot <path>` and `-outputDir <path>` from the CLI args.
        // Falls back to `Application.dataPath/..` and `Library/Zenject2VContainer/headless`.
        public static void RunFullEntry() {
            var args = Environment.GetCommandLineArgs();
            string projectRoot = ReadFlag(args, "-projectRoot")
                ?? Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            string outDir = ReadFlag(args, "-outputDir")
                ?? Path.Combine(projectRoot, "Library", "Zenject2VContainer", "headless");
            int code = RunFull(projectRoot, outDir);
            EditorApplication.Exit(code);
        }

        public static int RunFull(string projectRoot, string outputDir) {
            try {
                Directory.CreateDirectory(outputDir);
                var plan = MigrationPipeline.RunFullHeadless();
                var ctx = new MigrationReportContext {
                    ProjectPath = projectRoot,
                    UnityVersion = UnityEngine.Application.unityVersion,
                    // Delegate version resolution to MigrationPipeline.ToolVersion which already
                    // wraps PackageInfo.FindForAssembly with a safe fallback.
                    ToolVersion = MigrationPipeline.ToolVersion,
                    RunUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    BackupTimestamp = null,
                    RemainingZenjectFiles = 0,
                    CompileErrorCount = 0,
                    SkippedFiles = new string[0]
                };
                File.WriteAllText(Path.Combine(outputDir, "MIGRATION_REPORT.md"),
                    MigrationReportWriter.Render(plan, ctx));
                File.WriteAllText(Path.Combine(outputDir, "changes.json"),
                    SerializeChanges(plan));
                UnityEngine.Debug.Log($"[Zenject2VContainer] headless report at {outputDir}");
                return 0;
            } catch (Exception ex) {
                UnityEngine.Debug.LogError("[Zenject2VContainer] headless failed: " + ex);
                return 1;
            }
        }

        private static string ReadFlag(string[] args, string flag) {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }

        private static string SerializeChanges(MigrationPlan plan) {
            // JsonUtility cannot serialise List<T> at top level; wrap.
            var wrap = new ChangesWrap {
                changes = plan.Changes.Select(c => new ChangeRow {
                    path = c.OriginalPath,
                    category = c.Category.ToString(),
                    confidence = c.Confidence.ToString()
                }).ToArray(),
                manualTodos = plan.Unsupported.Select(f => new TodoRow {
                    category = f.Category, file = f.FilePath,
                    line = f.Line, reason = f.Reason
                }).ToArray()
            };
            return UnityEngine.JsonUtility.ToJson(wrap, prettyPrint: true);
        }

        [Serializable] private class ChangesWrap { public ChangeRow[] changes; public TodoRow[] manualTodos; }
        [Serializable] private class ChangeRow { public string path; public string category; public string confidence; }
        [Serializable] private class TodoRow { public string category; public string file; public int line; public string reason; }
    }
}
