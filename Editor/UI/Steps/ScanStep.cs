using UnityEditor;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.UI.Steps {
    public sealed class ScanStep : IWizardStep {
        public string Title => "1. Scan project for Zenject usage";
        public WizardState State => WizardState.Scan;
        public void OnGUI(MigrationContext ctx) { EditorGUILayout.HelpBox("Stub — filled in Task 10", MessageType.None); }
        public bool CanAdvance(MigrationContext ctx) => false;
    }
}
