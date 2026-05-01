using VContainer;

public class GameInstaller : MonoInstaller
{
    public override void InstallBindings()
    {
        builder.Register<EditorFoo>(Lifetime.Singleton).As<IFoo>().Keyed<string>("editor");
    }
}
