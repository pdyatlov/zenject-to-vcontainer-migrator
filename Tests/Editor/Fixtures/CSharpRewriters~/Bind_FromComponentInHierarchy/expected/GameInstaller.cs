using VContainer;

public class GameInstaller : MonoInstaller
{
    public override void InstallBindings()
    {
        builder.RegisterComponentInHierarchy<Foo>();
    }
}
