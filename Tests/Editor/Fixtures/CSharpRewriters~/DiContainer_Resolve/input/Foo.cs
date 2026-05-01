using Zenject;

public class Foo
{
    private DiContainer _container;

    public Foo(DiContainer container) { _container = container; }

    public IBar GetBar() => _container.Resolve<IBar>();
}
