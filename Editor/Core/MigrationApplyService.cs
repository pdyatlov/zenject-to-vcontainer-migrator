using System.Collections.Generic;
using System.IO;

namespace Zenject2VContainer.Core {
    public sealed class CompileResult {
        public bool Succeeded;
        public List<string> ErrorMessages = new List<string>();
    }

    public interface ICompileWaiter {
        CompileResult WaitForCompile(MigrationLog log);
    }

    public sealed class ApplyResult {
        public bool Success;
        public string Message;
        public string BackupTimestamp;
        public List<string> CompileErrors = new List<string>();
    }

    public sealed class MigrationApplyService {
        private readonly ICompileWaiter _compileWaiter;
        public MigrationApplyService(ICompileWaiter compileWaiter) { _compileWaiter = compileWaiter; }

        public ApplyResult Apply(string projectRoot, MigrationPlan csharp, MigrationPlan yaml, MigrationLog log, IMigrationProgress progress = null) {
            progress = progress ?? NullMigrationProgress.Instance;
            var paths = new List<string>();
            foreach (var c in csharp.Changes) paths.Add(c.OriginalPath);
            foreach (var c in yaml.Changes) paths.Add(c.OriginalPath);
            progress.Report("Apply", "Snapshotting " + paths.Count + " files…", 0f);
            var stamp = BackupManager.Snapshot(projectRoot, paths);
            log.Info("Apply.Backup", "Snapshot " + stamp + " (" + paths.Count + " files)");

            // C# phase
            int csTotal = csharp.Changes.Count;
            try {
                for (int i = 0; i < csTotal; i++) {
                    var c = csharp.Changes[i];
                    progress.Report("Writing C# changes", $"{i + 1}/{csTotal}: {Path.GetFileName(c.OriginalPath)}", csTotal > 0 ? (float)(i + 1) / csTotal : 1f);
                    WriteFile(c.OriginalPath, c.NewText, log, "Apply.CSharp");
                }
            } catch (System.Exception ex) {
                log.Error("Apply.CSharp", "Write failed: " + ex.Message);
                progress.Report("Apply", "Rolling back…", 0f);
                BackupManager.Restore(projectRoot, stamp);
                return new ApplyResult { Success = false, Message = "C# write failed; rolled back.", BackupTimestamp = stamp };
            }

            progress.Report("Apply", "Waiting for compile…", 0f);
            var compile = _compileWaiter.WaitForCompile(log);
            if (!compile.Succeeded) {
                foreach (var e in compile.ErrorMessages) log.Error("Apply.CSharp", e);
                progress.Report("Apply", "Compile failed — rolling back…", 0f);
                BackupManager.Restore(projectRoot, stamp);
                return new ApplyResult { Success = false, Message = "Compile failed after C# write; rolled back.", BackupTimestamp = stamp, CompileErrors = compile.ErrorMessages };
            }

            // YAML phase
            int yamlTotal = yaml.Changes.Count;
            try {
                for (int i = 0; i < yamlTotal; i++) {
                    var c = yaml.Changes[i];
                    progress.Report("Writing YAML assets", $"{i + 1}/{yamlTotal}: {Path.GetFileName(c.OriginalPath)}", yamlTotal > 0 ? (float)(i + 1) / yamlTotal : 1f);
                    WriteFile(c.OriginalPath, c.NewText, log, "Apply.Yaml");
                }
            } catch (System.Exception ex) {
                log.Error("Apply.Yaml", "Write failed: " + ex.Message);
                progress.Report("Apply", "Rolling back…", 0f);
                BackupManager.Restore(projectRoot, stamp);
                return new ApplyResult { Success = false, Message = "YAML write failed; rolled back.", BackupTimestamp = stamp };
            }

            return new ApplyResult { Success = true, Message = "Apply succeeded.", BackupTimestamp = stamp };
        }

        private static void WriteFile(string path, string text, MigrationLog log, string source) {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
            log.Info(source, "Wrote " + Path.GetFileName(path), path);
        }
    }
}
