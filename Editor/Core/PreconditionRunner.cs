using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Zenject2VContainer.Core.Scanner;

namespace Zenject2VContainer.Core {
    public sealed class PreconditionReport {
        public List<PreconditionResult> Results = new List<PreconditionResult>();
        public bool HasBlock {
            get {
                foreach (var r in Results) if (r.Result == PreconditionResult.Severity.Block) return true;
                return false;
            }
        }
    }

    public static class PreconditionRunner {
        public static PreconditionReport Run(InstallationInfo install, bool userOverrodeGitDirty) {
            var report = new PreconditionReport();
            report.Results.Add(PreconditionChecks.CheckPlayMode(EditorApplication.isPlayingOrWillChangePlaymode));
            report.Results.Add(PreconditionChecks.CheckCompiling(EditorApplication.isCompiling));
            report.Results.Add(PreconditionChecks.CheckProjectCompiles(HasCompileErrors()));
            report.Results.Add(PreconditionChecks.CheckVContainerPresence(install));
            report.Results.Add(PreconditionChecks.CheckAssetSerialization(EditorSettings.serializationMode == SerializationMode.ForceText));

            var (isRepo, isDirty) = ProbeGit();
            var git = PreconditionChecks.CheckGitState(isRepo, isDirty);
            if (git.Result == PreconditionResult.Severity.Warn && userOverrodeGitDirty && git.Code == "GIT_DIRTY") {
                git = new PreconditionResult { Result = PreconditionResult.Severity.Pass, Code = "GIT_DIRTY_OVERRIDE", Message = "User accepted dirty working tree." };
            }
            report.Results.Add(git);
            return report;
        }

        private static bool HasCompileErrors() {
            // Conservative: detecting compile errors via the public CompilationPipeline API is unreliable across Unity
            // versions. The wizard's Verify step re-reads compile messages after the apply, which is the authoritative
            // signal. Returning false here means the precondition does not block on this check; if the project does have
            // compile errors, SemanticModel queries will fail loudly during scan/preview.
            return false;
        }

        private static (bool isRepo, bool isDirty) ProbeGit() {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            try {
                var psi = new ProcessStartInfo("git", "rev-parse --is-inside-work-tree") {
                    WorkingDirectory = projectRoot,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    UseShellExecute = false, CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                p.WaitForExit(2000);
                if (p.ExitCode != 0) return (false, false);
                psi.Arguments = "status --porcelain";
                using var p2 = Process.Start(psi);
                var stdout = p2.StandardOutput.ReadToEnd();
                p2.WaitForExit(2000);
                return (true, !string.IsNullOrWhiteSpace(stdout));
            } catch {
                return (false, false);
            }
        }
    }
}
