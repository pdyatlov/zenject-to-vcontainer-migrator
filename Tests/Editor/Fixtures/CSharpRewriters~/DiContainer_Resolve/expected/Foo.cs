using VContainer;

public class Foo
{
    private IObjectResolver _container;

    public Foo(IObjectResolver container) { _container = container; }

    public IBar GetBar() => _container.Resolve<IBar>();
}
