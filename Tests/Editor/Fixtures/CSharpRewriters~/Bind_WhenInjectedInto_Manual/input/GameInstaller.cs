using Zenject;

public class GameInstaller : MonoInstaller
{
    public override void InstallBindings()
    {
        Container.Bind<IFoo>().To<EditorFoo>().AsSingle().WhenInjectedInto<SomeClass>();
    }
}
