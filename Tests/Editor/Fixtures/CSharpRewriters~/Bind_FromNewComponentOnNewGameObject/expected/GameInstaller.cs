using VContainer;

public class GameInstaller : MonoInstaller
{
    public override void InstallBindings()
    {
        builder.RegisterComponentOnNewGameObject<Foo>(Lifetime.Singleton, "Foo");
    }
}
