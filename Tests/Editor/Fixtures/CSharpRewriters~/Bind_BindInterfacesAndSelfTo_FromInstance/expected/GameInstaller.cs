using VContainer;

public class GameInstaller : MonoInstaller
{
    public Foo myInstance;
    public override void InstallBindings()
    {
        builder.RegisterInstance(myInstance).AsImplementedInterfaces().AsSelf();
    }
}
