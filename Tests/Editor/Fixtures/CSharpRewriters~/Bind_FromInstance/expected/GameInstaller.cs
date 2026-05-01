using VContainer;

public class GameInstaller : MonoInstaller
{
    public IFoo myInstance;
    public override void InstallBindings()
    {
        builder.RegisterInstance<IFoo>(myInstance);
    }
}
