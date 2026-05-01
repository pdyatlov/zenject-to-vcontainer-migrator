using Zenject;

public class GameInstaller : MonoInstaller
{
    public object prefab;
    public override void InstallBindings()
    {
        Container.Bind<Foo>().FromComponentInNewPrefab(prefab).AsSingle();
    }
}
