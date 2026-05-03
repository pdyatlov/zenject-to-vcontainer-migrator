# Manual TODO Guide

The migrator emits `// TODO: MIGRATE-MANUAL [Category]` comments wherever
it cannot translate a Zenject construct safely. Each category links here
for context, recommended VContainer pattern, and a worked example where applicable.

## Table of contents

- [SignalBus](#signalbus)
- [MemoryPool](#memorypool)
- [ConditionalBind](#conditionalbind)
- [InjectOptional](#injectoptional)
- [ComplexSubContainer](#complexsubcontainer)
- [InstantiateUnregistered](#instantiateunregistered)
- [Decorator](#decorator)
- [CustomFactory](#customfactory)
- [CustomDiContainerExtension](#customdicontainerextension)
- [LifecycleStartCollision](#lifecyclestartcollision)
- [InstallerWiring](#installerwiring)

---

## SignalBus

**Why manual:** VContainer ships no `SignalBus` equivalent. Use a plain C#
event aggregator (`MessagePipe` is the common partner) and register it as
a singleton.

**Recommended:** Replace `Container.DeclareSignal<T>()` with a
`MessagePipe.IPublisher<T>` / `ISubscriber<T>` pair, or a hand-rolled
event aggregator.

```csharp
// Before (Zenject)
Container.DeclareSignal<ScoreChanged>();
Container.BindSignal<ScoreChanged>().ToMethod<UI>(x => x.OnScore).FromResolve();

// After (VContainer + MessagePipe)
builder.RegisterMessagePipe();
builder.RegisterMessageBroker<ScoreChanged>(options);
```

---

## MemoryPool

**Why manual:** Zenject's `MemoryPool<T>` has no direct VContainer counter-
part. Replace with `UnityEngine.Pool.ObjectPool<T>` or a hand-rolled pool.

**Recommended:** Move pooling out of DI; have the consumer hold an
`ObjectPool<T>` field initialised in `Construct`.

---

## ConditionalBind

**Why manual:** Zenject's `When(...)` / `WhenInjectedInto<T>()` predicates
have no direct equivalent. Choose an approach below based on the
predicate's specificity.

**Recommended:** For `WhenInjectedInto<T>`, register a keyed instance and
inject by key. For everything else, restructure to two distinct types or
move the choice into a factory method.

---

## InjectOptional

**Why manual:** VContainer requires presence — there is no `[InjectOptional]`
attribute. Either guarantee the dependency is registered or pull it
lazily through `IObjectResolver.TryResolve`.

**Recommended:**

```csharp
public class Foo {
    private readonly IBar _bar;
    public Foo(IObjectResolver resolver) {
        resolver.TryResolve(out _bar); // null when unbound
    }
}
```

---

## ComplexSubContainer

**Why manual:** Sub-containers from method (`FromSubContainerResolve().ByMethod`)
with non-trivial install logic are flagged. VContainer's parent/child
`LifetimeScope` model is the destination, but the bind-graph translation
must be done by hand.

**Recommended:** Create a child `LifetimeScope` and `Configure(...)` it
where the original `ByMethod` action ran. Lift each `Container.Bind` to a
`builder.Register` inside `Configure`.

---

## InstantiateUnregistered

**Why manual:** `DiContainer.Instantiate<T>(...)` builds a type that was
never registered. VContainer's resolver demands registration.

**Recommended:** Either register the type and `Resolve<T>`, or hand-
construct it and pass dependencies explicitly.

---

## Decorator

**Why manual:** Zenject's decorator install (`InstallDecoratorContext`) has
no direct counterpart. Use VContainer's keyed registration or the
decorator pattern in code.

---

## CustomFactory

**Why manual:** `Container.BindFactory<...>().FromFactory<T>()` chains use a
custom factory class. VContainer's `RegisterFactory<TArg, TOut>` only
accepts a `Func<TArg, TOut>`, so any per-argument logic must live in a
plain method.

**Recommended:**

```csharp
builder.RegisterFactory<int, IFoo>(container =>
    arg => new Foo(arg, container.Resolve<IDep>()),
    Lifetime.Scoped);
```

---

## CustomDiContainerExtension

**Why manual:** Custom `IInstaller`-adjacent DI extensions that mutate the
`DiContainer` directly do not survive the rename to `IObjectResolver`.

**Recommended:** Lift each extension into a static helper that operates on
`IContainerBuilder` at registration time.

---

## LifecycleStartCollision

**Why manual:** A type implementing both `IInitializable` and a
`MonoBehaviour.Start()` method would have `Initialize` renamed to `Start`,
colliding with the existing Unity message. The migrator skips the rename
and flags it.

**Recommended:** Pick one entry point. If you keep `IInitializable`, rename
the existing `Start` method. If you keep Unity's `Start`, drop
`IInitializable` and inline the init code.

---

## InstallerWiring

**Why manual:** A `MonoInstaller` retyped as `MonoBehaviour : IInstaller`
no longer auto-registers. A parent `LifetimeScope` must call
`builder.UseInstaller(this)` (or override `Configure`) to pick it up.

**Recommended:** Add the MonoBehaviour to the LifetimeScope's
`autoInjectGameObjects` field, then in `Configure`:

```csharp
protected override void Configure(IContainerBuilder builder) {
    foreach (var inst in GetComponentsInChildren<IInstaller>()) {
        builder.UseInstaller(inst);
    }
}
```
