using Zenject2VContainer.Core;

namespace Zenject2VContainer.UI {
    public interface IWizardStep {
        string Title { get; }
        WizardState State { get; }
        void OnGUI(MigrationContext ctx);
        bool CanAdvance(MigrationContext ctx);
    }
}
