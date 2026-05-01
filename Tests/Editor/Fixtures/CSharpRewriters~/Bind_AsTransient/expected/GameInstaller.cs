using VContainer;

public class GameInstaller : MonoInstaller
{
    public override void InstallBindings()
    {
        builder.Register<Foo>(Lifetime.Transient).As<IFoo>();
    }
}
