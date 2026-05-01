using Zenject;

public class Foo
{
    [InjectOptional] private IBar _maybeBar;
}
