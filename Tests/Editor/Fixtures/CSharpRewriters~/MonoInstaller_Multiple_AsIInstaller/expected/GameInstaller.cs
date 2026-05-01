using VContainer;

public class GameInstaller : MonoInstaller, IInstaller
{
    public override void InstallBindings() { /* legacy entry */ }

    public void Install(IContainerBuilder builder)
    {
        builder.Register<Foo>(Lifetime.Singleton).As<IFoo>();
    }
}
