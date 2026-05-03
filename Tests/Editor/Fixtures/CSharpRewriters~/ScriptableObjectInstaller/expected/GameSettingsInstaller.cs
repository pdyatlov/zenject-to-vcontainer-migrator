using VContainer;
using UnityEngine;
using VContainer.Unity;

public class GameSettingsInstaller : ScriptableObject, IInstaller
{
    public void Install(IContainerBuilder builder)
    {
        builder.Register<Foo>(Lifetime.Singleton).As<IFoo>();
    }
}
