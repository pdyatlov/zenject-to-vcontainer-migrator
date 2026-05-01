using UnityEditor;
using Zenject2VContainer.Core;
using Zenject2VContainer.Manifest;

namespace Zenject2VContainer.UI.Steps {
    public sealed class RemoveZenjectStep : IWizardStep {
        private readonly IZenjectRemover _remover;
        public RemoveZenjectStep(IZenjectRemover remover) { _remover = remover; }
        public string Title => "5. Remove Zenject (optional)";
        public WizardState State => WizardState.Remove;
        public void OnGUI(MigrationContext ctx) { EditorGUILayout.HelpBox("Stub — filled in Task 14", MessageType.None); }
        public bool CanAdvance(MigrationContext ctx) => false;
    }
}
