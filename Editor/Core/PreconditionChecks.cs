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

        public static PreconditionResult CheckPlayMode(bool isPlaying) =>
            isPlaying
                ? new PreconditionResult {
                    Result = PreconditionResult.Severity.Block,
                    Code = "PLAY_MODE",
                    Message = "Unity is in Play mode. Exit Play mode before applying migration."
                }
                : new PreconditionResult { Result = PreconditionResult.Severity.Pass, Code = "PLAY_MODE_OFF", Message = "Editor is not in Play mode." };

        public static PreconditionResult CheckCompiling(bool isCompiling) =>
            isCompiling
                ? new PreconditionResult {
                    Result = PreconditionResult.Severity.Block,
                    Code = "COMPILING",
                    Message = "Editor is currently compiling. Wait for compile to finish, then retry."
                }
                : new PreconditionResult { Result = PreconditionResult.Severity.Pass, Code = "COMPILE_IDLE", Message = "Editor is idle." };

        public static PreconditionResult CheckProjectCompiles(bool hasCompileErrors) =>
            hasCompileErrors
                ? new PreconditionResult {
                    Result = PreconditionResult.Severity.Block,
                    Code = "PROJECT_BROKEN",
                    Message = "Project has unresolved compile errors. Roslyn SemanticModel needs a clean build to resolve Zenject symbols."
                }
                : new PreconditionResult { Result = PreconditionResult.Severity.Pass, Code = "PROJECT_OK", Message = "Project compiles cleanly." };

        public static PreconditionResult CheckAssetSerialization(bool isForceText) =>
            isForceText
                ? new PreconditionResult { Result = PreconditionResult.Severity.Pass, Code = "SERIALIZATION_OK", Message = "Asset serialization mode is ForceText." }
                : new PreconditionResult {
                    Result = PreconditionResult.Severity.Block,
                    Code = "SERIALIZATION_BAD",
                    Message = "Asset serialization mode must be ForceText. Switch via Edit → Project Settings → Editor → Asset Serialization → Mode."
                };
    }
}
