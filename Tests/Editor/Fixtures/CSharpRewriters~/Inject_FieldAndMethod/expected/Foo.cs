using VContainer;

public class Foo
{
    [Inject] private IBar _bar;
    [Inject] public IBaz Baz { get; private set; }

    [Inject]
    public void Construct(IQux qux) { }
}
