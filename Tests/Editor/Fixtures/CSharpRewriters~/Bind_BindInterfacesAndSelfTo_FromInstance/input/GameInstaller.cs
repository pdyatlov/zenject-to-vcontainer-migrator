using Zenject;

public class GameInstaller : MonoInstaller
{
    public Foo myInstance;
    public override void InstallBindings()
    {
        Container.BindInterfacesAndSelfTo<Foo>().FromInstance(myInstance).AsSingle();
    }
}
