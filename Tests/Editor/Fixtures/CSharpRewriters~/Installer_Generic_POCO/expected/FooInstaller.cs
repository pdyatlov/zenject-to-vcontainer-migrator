using VContainer;

public class FooInstaller : IInstaller
{
    public void Install(IContainerBuilder builder)
    {
        builder.Register<Foo>(Lifetime.Singleton).As<IFoo>();
    }
}
