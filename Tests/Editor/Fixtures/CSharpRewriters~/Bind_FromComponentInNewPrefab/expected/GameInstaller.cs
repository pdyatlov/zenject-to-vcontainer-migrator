using VContainer;

public class GameInstaller : MonoInstaller
{
    public object prefab;
    public override void InstallBindings()
    {
        builder.RegisterComponentInNewPrefab<Foo>(prefab, Lifetime.Singleton);
    }
}
