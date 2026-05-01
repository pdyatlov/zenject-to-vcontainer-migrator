using System.Collections.Generic;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.Manifest {
    public sealed class RemovalPlan {
        public bool UpmInstall;
        public string UpmPackageId;          // e.g. "com.svermeulen.extenject"
        public string FolderInstallPath;     // e.g. "Assets/Plugins/Zenject"
        public List<string> ScopedRegistryNamesToDrop = new List<string>();
        public bool IsNoop;                  // already removed
    }

    public sealed class RemovalResult {
        public bool Success;
        public string Message;
        public List<string> ActionsTaken = new List<string>();
    }

    public interface IZenjectRemover {
        RemovalPlan Plan(InstallationInfo install, string projectRoot);
        RemovalResult Apply(RemovalPlan plan, string projectRoot);
    }
}
