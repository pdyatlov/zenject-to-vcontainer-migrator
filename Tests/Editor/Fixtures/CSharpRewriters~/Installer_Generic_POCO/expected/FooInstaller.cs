using VContainer;
using VContainer.Unity;

public class FooInstaller : IInstaller
{
    public void Install(IContainerBuilder builder)
    {
        builder.Register<Foo>(Lifetime.Singleton).As<IFoo>();
    }
}
