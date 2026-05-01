using System.Collections.Generic;
using Zenject2VContainer.Core.Scanner;

namespace Zenject2VContainer.Core {
    public sealed class MigrationContext {
        public ZenjectUsageReport ScanReport;
        public MigrationPlan CSharpPlan;
        public MigrationPlan YamlPlan;
        public string BackupTimestamp;
        public bool ApplySucceeded;
        public List<string> RemainingZenjectFiles = new List<string>();
        public List<string> CompileErrors = new List<string>();
        public MigrationLog Log = new MigrationLog();
        public bool UserOverrodeGitDirty;
    }
}
