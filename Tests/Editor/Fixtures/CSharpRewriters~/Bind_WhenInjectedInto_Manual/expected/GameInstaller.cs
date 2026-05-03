using VContainer;

public class GameInstaller : MonoInstaller
{
    public override void InstallBindings()
    {
        // TODO: MIGRATE-MANUAL [ConditionalBind]
        // Reason: VContainer has no equivalent for .WhenInjectedInto / .WhenNotInjectedInto.
        // Suggested: see Docs~/manual-todos.md#conditionalbind
        // Original code preserved below — review and rewrite.
        Container.Bind<IFoo>().To<EditorFoo>().AsSingle().WhenInjectedInto<SomeClass>();
    }
}
