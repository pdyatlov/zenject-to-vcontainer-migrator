using VContainer;

public class GameSettingsInstaller : ScriptableObject, IInstaller
{
    public override void InstallBindings() { /* legacy entry */ }

    public void Install(IContainerBuilder builder)
    {
        builder.Register<Foo>(Lifetime.Singleton).As<IFoo>();
    }
}
