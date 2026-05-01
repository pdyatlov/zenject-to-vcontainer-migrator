using UnityEditor;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.UI.Steps {
    public sealed class PreviewStep : IWizardStep {
        public string Title => "2. Preview migration changes";
        public WizardState State => WizardState.Preview;
        public void OnGUI(MigrationContext ctx) { EditorGUILayout.HelpBox("Stub — filled in Task 11", MessageType.None); }
        public bool CanAdvance(MigrationContext ctx) => false;
    }
}
