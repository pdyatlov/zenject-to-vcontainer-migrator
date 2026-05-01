using VContainer;

public class GameInstaller : MonoInstaller
{
    public override void InstallBindings()
    {
        // TODO: MIGRATE-MANUAL [ConditionalBind]
        // Reason: VContainer has no equivalent for .WhenInjectedInto / .WhenNotInjectedInto.
        // Suggested: see https://github.com/<owner>/<repo>/blob/main/docs/manual-todos/ConditionalBind.md
        // Original code preserved below — review and rewrite.
        Container.Bind<IFoo>().To<EditorFoo>().AsSingle().WhenInjectedInto<SomeClass>();
    }
}
