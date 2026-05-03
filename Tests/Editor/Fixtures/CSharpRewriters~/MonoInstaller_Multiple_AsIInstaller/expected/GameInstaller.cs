using VContainer;
using UnityEngine;
using VContainer.Unity;

public class GameInstaller : MonoBehaviour, IInstaller
{
    public void Install(IContainerBuilder builder)
    {
        builder.Register<Foo>(Lifetime.Singleton).As<IFoo>();
    }
}
