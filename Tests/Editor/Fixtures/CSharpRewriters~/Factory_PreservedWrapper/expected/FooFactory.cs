using VContainer;
using System;

public class FooFactory
{
    private readonly Func<int, string> _factory;
    public FooFactory(Func<int, string> factory) { _factory = factory; }
    public string Create(int a1) => _factory(a1);
}
