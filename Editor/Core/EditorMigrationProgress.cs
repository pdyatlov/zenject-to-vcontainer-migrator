using UnityEditor;

namespace Zenject2VContainer.Core {
    public sealed class EditorMigrationProgress : IMigrationProgress {
        public void Report(string title, string info, float progress) {
            EditorUtility.DisplayProgressBar(title, info, progress);
        }
        public void Clear() {
            EditorUtility.ClearProgressBar();
        }
    }
}
