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

        public ApplyResult Apply(string projectRoot, MigrationPlan csharp, MigrationPlan yaml, MigrationLog log) {
            var paths = new List<string>();
            foreach (var c in csharp.Changes) paths.Add(c.OriginalPath);
            foreach (var c in yaml.Changes) paths.Add(c.OriginalPath);
            var stamp = BackupManager.Snapshot(projectRoot, paths);
            log.Info("Apply.Backup", "Snapshot " + stamp + " (" + paths.Count + " files)");

            // C# phase
            try {
                foreach (var c in csharp.Changes) WriteFile(c.OriginalPath, c.NewText, log, "Apply.CSharp");
            } catch (System.Exception ex) {
                log.Error("Apply.CSharp", "Write failed: " + ex.Message);
                BackupManager.Restore(projectRoot, stamp);
                return new ApplyResult { Success = false, Message = "C# write failed; rolled back.", BackupTimestamp = stamp };
            }

            var compile = _compileWaiter.WaitForCompile(log);
            if (!compile.Succeeded) {
                foreach (var e in compile.ErrorMessages) log.Error("Apply.CSharp", e);
                BackupManager.Restore(projectRoot, stamp);
                return new ApplyResult { Success = false, Message = "Compile failed after C# write; rolled back.", BackupTimestamp = stamp, CompileErrors = compile.ErrorMessages };
            }

            // YAML phase
            try {
                foreach (var c in yaml.Changes) WriteFile(c.OriginalPath, c.NewText, log, "Apply.Yaml");
            } catch (System.Exception ex) {
                log.Error("Apply.Yaml", "Write failed: " + ex.Message);
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
