using UnityEditor;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.UI.Steps {
    public sealed class VerifyStep : IWizardStep {
        public string Title => "4. Verify migration";
        public WizardState State => WizardState.Verify;
        public void OnGUI(MigrationContext ctx) { EditorGUILayout.HelpBox("Stub — filled in Task 13", MessageType.None); }
        public bool CanAdvance(MigrationContext ctx) => false;
    }
}
