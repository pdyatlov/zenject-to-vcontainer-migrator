using Zenject;

public class FooInstaller : Installer<FooInstaller>
{
    public override void InstallBindings()
    {
        Container.Bind<IFoo>().To<Foo>().AsSingle();
    }
}
