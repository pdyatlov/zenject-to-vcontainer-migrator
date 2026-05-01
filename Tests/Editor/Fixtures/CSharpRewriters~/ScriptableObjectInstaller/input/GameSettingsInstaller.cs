using Zenject;

public class GameSettingsInstaller : ScriptableObjectInstaller
{
    public override void InstallBindings()
    {
        Container.Bind<IFoo>().To<Foo>().AsSingle();
    }
}
