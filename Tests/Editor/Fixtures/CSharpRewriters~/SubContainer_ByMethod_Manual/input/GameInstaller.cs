using Zenject;

public class GameInstaller : MonoInstaller
{
    public override void InstallBindings()
    {
        Container.Bind<IFoo>().FromSubContainerResolve().ByInstaller<SubInstaller>().AsSingle();
    }
}
