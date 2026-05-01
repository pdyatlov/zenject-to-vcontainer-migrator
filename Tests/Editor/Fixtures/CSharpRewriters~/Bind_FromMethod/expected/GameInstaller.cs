using VContainer;

public class GameInstaller : MonoInstaller
{
    public override void InstallBindings()
    {
        builder.Register<IFoo>(_ => new Foo(), Lifetime.Singleton);
    }
}
