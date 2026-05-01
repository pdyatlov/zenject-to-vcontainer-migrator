// Run with `dotnet script build-stubs.csx` (requires `dotnet-script`).
// Produces UnityEngine.dll, Zenject.dll, and VContainer.dll as MetadataReference fodder.
// UnityEngine.dll is shared by both Zenject and VContainer to avoid duplicate-type
// conflicts when tests reference both stub assemblies simultaneously.

#r "nuget: Microsoft.CodeAnalysis.CSharp, 4.8.0"

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Generic;

void Compile(string assemblyName, string source, string outputPath, IEnumerable<MetadataReference> extraRefs = null) {
    var tree = CSharpSyntaxTree.ParseText(source);
    var refs = new List<MetadataReference> {
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(System.Runtime.GCSettings).Assembly.Location)
    };
    if (extraRefs != null) refs.AddRange(extraRefs);
    var compilation = CSharpCompilation.Create(assemblyName,
        new[] { tree }, refs,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    var emitResult = compilation.Emit(outputPath);
    if (!emitResult.Success) {
        foreach (var d in emitResult.Diagnostics) System.Console.Error.WriteLine(d);
        throw new System.Exception("emit failed for " + assemblyName);
    }
}

var unityEngineSource = """
namespace UnityEngine {
    public class MonoBehaviour {}
    public class ScriptableObject {}
}
""";

var zenjectSource = """
namespace Zenject {
    public class InjectAttribute : System.Attribute {}
    public class InjectOptionalAttribute : System.Attribute {}
    public class DiContainer {
        public T Resolve<T>() => default;
        public T Instantiate<T>() => default;
        public BindStatement<T> Bind<T>() => default;
        public BindStatement<T> BindInterfacesTo<T>() => default;
        public BindStatement<T> BindInterfacesAndSelfTo<T>() => default;
    }
    public class BindStatement<T> {
        public BindStatement<T> To<U>() => default;
        public BindStatement<T> AsSingle() => default;
        public BindStatement<T> AsTransient() => default;
        public BindStatement<T> AsCached() => default;
        public BindStatement<T> FromInstance(object o) => default;
        public BindStatement<T> WithId(string id) => default;
    }
    public abstract class Installer {
        public DiContainer Container { get; }
        public abstract void InstallBindings();
    }
    public abstract class MonoInstaller : UnityEngine.MonoBehaviour {
        public DiContainer Container { get; }
        public abstract void InstallBindings();
    }
    public abstract class Installer<T> : Installer where T : Installer<T> {}
    public abstract class ScriptableObjectInstaller : UnityEngine.ScriptableObject {
        public DiContainer Container { get; }
        public abstract void InstallBindings();
    }
    public interface IInitializable { void Initialize(); }
    public interface ITickable { void Tick(); }
    public interface ILateTickable { void LateTick(); }
    public interface IFixedTickable { void FixedTick(); }
    public class PlaceholderFactory<TArg, TOut> {
        public TOut Create(TArg a) => default;
    }
    public class SignalBus {
        public void Fire<T>(T signal) {}
        public void Subscribe<T>(System.Action<T> callback) {}
    }
}
""";

var vcontainerSource = """
namespace VContainer {
    public class InjectAttribute : System.Attribute {}
    public interface IObjectResolver { T Resolve<T>(); }
    public interface IContainerBuilder {
        RegistrationBuilder<T> Register<T>(Lifetime lifetime);
        RegistrationBuilder<T> RegisterInstance<T>(T instance);
    }
    public enum Lifetime { Singleton, Transient, Scoped }
    public class RegistrationBuilder<T> {
        public RegistrationBuilder<T> As<U>() => this;
        public RegistrationBuilder<T> AsSelf() => this;
        public RegistrationBuilder<T> AsImplementedInterfaces() => this;
        public RegistrationBuilder<T> Keyed<K>(K k) => this;
    }
    public interface IInstaller { void Install(IContainerBuilder builder); }
}
namespace VContainer.Unity {
    public abstract class LifetimeScope : UnityEngine.MonoBehaviour {
        protected virtual void Configure(VContainer.IContainerBuilder builder) {}
    }
    public interface IStartable { void Start(); }
    public interface ITickable { void Tick(); }
    public interface ILateTickable { void LateTick(); }
    public interface IFixedTickable { void FixedTick(); }
}
""";

Compile("UnityEngine", unityEngineSource, "UnityEngine.dll");
var unityEngineRef = MetadataReference.CreateFromFile(System.IO.Path.GetFullPath("UnityEngine.dll"));
Compile("Zenject", zenjectSource, "Zenject.dll", new[] { unityEngineRef });
Compile("VContainer", vcontainerSource, "VContainer.dll", new[] { unityEngineRef });
System.Console.WriteLine("Stubs built (UnityEngine.dll, Zenject.dll, VContainer.dll).");
