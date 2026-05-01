using Zenject2VContainer.Core;

namespace Zenject2VContainer.Manifest {
    public sealed class StubZenjectRemover : IZenjectRemover {
        public RemovalPlan Plan(InstallationInfo install, string projectRoot) =>
            new RemovalPlan { IsNoop = true };
        public RemovalResult Apply(RemovalPlan plan, string projectRoot) =>
            new RemovalResult {
                Success = false,
                Message = "Zenject removal not yet available — ships in M5. Remove manually for now."
            };
    }
}
