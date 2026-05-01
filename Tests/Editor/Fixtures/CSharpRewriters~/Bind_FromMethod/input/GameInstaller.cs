using Zenject;

public class GameInstaller : MonoInstaller
{
    public override void InstallBindings()
    {
        Container.Bind<IFoo>().FromMethod(_ => new Foo()).AsSingle();
    }
}
