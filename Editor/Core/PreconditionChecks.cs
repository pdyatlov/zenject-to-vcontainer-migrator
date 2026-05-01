namespace Zenject2VContainer.Core {
    public sealed class PreconditionResult {
        public enum Severity { Pass, Warn, Block }
        public Severity Result;
        public string Code;
        public string Message;
    }

    public static class PreconditionChecks {
        public static PreconditionResult CheckVContainerPresence(InstallationInfo install) =>
            install != null && install.VContainerInstalled
                ? new PreconditionResult {
                    Result = PreconditionResult.Severity.Pass,
                    Code = "VC_PRESENT",
                    Message = "VContainer present at " + (install.VContainerVersion ?? "unknown") + "."
                }
                : new PreconditionResult {
                    Result = PreconditionResult.Severity.Block,
                    Code = "VC_MISSING",
                    Message = "VContainer (jp.hadashikick.vcontainer) is not installed. Add it to manifest.json before applying migration."
                };

        public static PreconditionResult CheckGitState(bool isRepo, bool isDirty) {
            if (!isRepo) return new PreconditionResult {
                Result = PreconditionResult.Severity.Warn,
                Code = "GIT_NOT_REPO",
                Message = "Project is not a git repository. Tool backups still apply, but git revert is unavailable."
            };
            return isDirty
                ? new PreconditionResult {
                    Result = PreconditionResult.Severity.Warn,
                    Code = "GIT_DIRTY",
                    Message = "Working tree has uncommitted changes. Commit or stash before applying migration to keep rollback simple."
                }
                : new PreconditionResult {
                    Result = PreconditionResult.Severity.Pass,
                    Code = "GIT_CLEAN",
                    Message = "Working tree is clean."
                };
        }
    }
}
