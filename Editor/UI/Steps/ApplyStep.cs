using UnityEditor;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.UI.Steps {
    public sealed class ApplyStep : IWizardStep {
        public string Title => "3. Apply changes";
        public WizardState State => WizardState.Apply;
        public void OnGUI(MigrationContext ctx) { EditorGUILayout.HelpBox("Stub — filled in Task 12", MessageType.None); }
        public bool CanAdvance(MigrationContext ctx) => false;
    }
}
