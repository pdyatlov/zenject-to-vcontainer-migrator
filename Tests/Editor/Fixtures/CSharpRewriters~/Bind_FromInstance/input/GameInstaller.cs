using Zenject;

public class GameInstaller : MonoInstaller
{
    public IFoo myInstance;
    public override void InstallBindings()
    {
        Container.Bind<IFoo>().FromInstance(myInstance);
    }
}
