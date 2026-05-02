namespace Zenject2VContainer.Core {
    public interface IMigrationProgress {
        void Report(string title, string info, float progress);
        void Clear();
    }

    public sealed class NullMigrationProgress : IMigrationProgress {
        public static readonly NullMigrationProgress Instance = new NullMigrationProgress();
        public void Report(string title, string info, float progress) { }
        public void Clear() { }
    }
}
