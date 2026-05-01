using System;
using System.Collections.Generic;

namespace Zenject2VContainer.Core {
    public enum FileChangeCategory { CSharp, Yaml, Manifest }
    public enum ChangeConfidence { High, Medium, LowFlagged }

    [Serializable]
    public sealed class Finding {
        public string Category;       // e.g. "SignalBus", "InjectOptional"
        public string FilePath;
        public int Line;
        public string Reason;
        public string DocLink;
    }

    [Serializable]
    public sealed class FileChange {
        public string OriginalPath;
        public string OriginalText;
        public string NewText;
        public FileChangeCategory Category;
        public ChangeConfidence Confidence;
        public List<Finding> RelatedFindings = new List<Finding>();
    }

    [Serializable]
    public sealed class MigrationPlan {
        public List<FileChange> Changes = new List<FileChange>();
        public List<Finding> Unsupported = new List<Finding>();

        public static MigrationPlan Empty() => new MigrationPlan();
    }
}
