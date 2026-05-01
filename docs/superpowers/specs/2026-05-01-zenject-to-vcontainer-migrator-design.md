# Zenject → VContainer Migrator — Design Spec

**Date:** 2026-05-01
**Status:** Draft, awaiting user review
**Target audience:** Implementers and contributors

---

## 1. Goal

Provide a Unity Editor tool, distributed as a UPM package, that migrates an
existing Unity project from **Zenject / Extenject** to **VContainer**.

The tool offers an interactive wizard:

1. **Scan** the project for Zenject usage (C# code, scenes, prefabs,
   ScriptableObject assets, package manifest).
2. **Preview** the proposed changes as per-file diffs, classified by
   confidence and category.
3. **Apply** the migration with backup and rollback support.
4. **Verify** the resulting project compiles cleanly and contains no
   remaining Zenject references in the in-scope feature set.
5. **Optionally remove** Zenject (UPM package or `Assets/Plugins/Zenject`
   folder) once verification is green.

A `MIGRATION_REPORT.md` is produced summarising file changes, manual
follow-ups, and unsupported features encountered.

---

## 2. Scope

### 2.1 In scope (v1)

| Zenject feature                          | VContainer equivalent                                    |
|------------------------------------------|----------------------------------------------------------|
| `[Inject]` field / property / method     | `[Inject]` field / property / method (same name)         |
| `[Inject]` on constructor                | Constructor injection (preferred)                        |
| `Bind<T>().To<U>().AsSingle()`           | `builder.Register<U>(Lifetime.Singleton).As<T>()`        |
| `AsTransient`                            | `Lifetime.Transient`                                     |
| `AsCached`                               | `Lifetime.Scoped`                                        |
| `BindInterfacesTo<T>`                    | `Register<T>(...).AsImplementedInterfaces()`             |
| `BindInterfacesAndSelfTo<T>`             | `Register<T>(...).AsImplementedInterfaces().AsSelf()`    |
| `FromInstance(x)`                        | `RegisterInstance(x)`                                    |
| `FromMethod(...)`                        | `Register<T>(resolver => ..., Lifetime.X)`               |
| `FromComponentInHierarchy`               | `RegisterComponentInHierarchy<T>()`                      |
| `FromComponentInNewPrefab`               | `RegisterComponentInNewPrefab<T>(prefab, Lifetime.X)`    |
| `FromNewComponentOnNewGameObject`        | `RegisterComponentOnNewGameObject<T>(Lifetime.X, name)`  |
| `MonoInstaller`                          | `LifetimeScope.Configure` or `IInstaller`                |
| `ScriptableObjectInstaller`              | `ScriptableObject` implementing `IInstaller`             |
| `Installer<T>` (POCO)                    | `IInstaller` implementation                              |
| `SceneContext`                           | `LifetimeScope` (script GUID swap in YAML)               |
| `ProjectContext`                         | Generated `ProjectLifetimeScope` + bootstrap method      |
| `GameObjectContext`                      | `LifetimeScope` (parent autowire is default)             |
| `ZenjectBinding`                         | Generated `AutoRegisterComponent` (runtime helper)       |
| `IInitializable.Initialize`              | `IStartable.Start`                                       |
| `ITickable` / `ILateTickable` / `IFixedTickable` | Same names in `VContainer.Unity`                 |
| `IDisposable.Dispose`                    | `IDisposable.Dispose` (scope-managed)                    |
| `PlaceholderFactory<TArgs..., TOut>`     | `Func<TArgs..., TOut>` + `RegisterFactory` (wrapper class preserved when externally referenced) |
| Simple sub-containers (`FromSubContainerResolve.ByInstaller<>`) | Programmatic child `LifetimeScope`            |
| `DiContainer.Resolve<T>`                 | `IObjectResolver.Resolve<T>`                             |
| `DiContainer.Instantiate(prefab)`        | `IObjectResolver.Instantiate(prefab)`                    |
| `using Zenject`                          | `using VContainer; using VContainer.Unity;`              |
| `.WithId(string)`                        | `.Keyed<string>(value)`                                  |

### 2.2 Out of scope (v1) — emit `// TODO: MIGRATE-MANUAL` markers

- `SignalBus` — recommended replacement: MessagePipe.
- `MemoryPool<T>` — manual replacement (custom pool or `IObjectPool<T>`).
- Conditional bindings (`WhenInjectedInto<T>`, `WhenNotInjectedInto<T>`).
- `[InjectOptional]` on field / method (constructor default values still
  work; flagged elsewhere).
- Complex sub-containers (`ByMethod`, `ByPrefab`, dynamic factory args).
- Decorator pattern.
- Custom user-defined extension methods on `DiContainer`.
- `DiContainer.Instantiate<T>` for unregistered types (semantic difference;
  VContainer requires registration).
- User-modified Zenject sources in `Assets/Plugins/Zenject` (best-effort
  detection and warning).

### 2.3 Supported environments

- **Unity:** 2021.3 LTS minimum, Unity 6 (6000.0) recommended and tested.
- **Zenject / Extenject:** Extenject 9.x (Mathijs-Bakker fork) primary
  target. Older modesttree Zenject best-effort.
- **VContainer:** 1.x. Must be installed in user project before migration
  apply step (precondition check).
- **Asset serialization mode:** `ForceText` required (block migration with
  clear instructions if the project uses binary serialization).

---

## 3. Architecture

### 3.1 Repository layout

```
Zenject2VContainer/
├── package.json                          UPM manifest
├── README.md                             User-facing readme
├── LICENSE
├── Editor/
│   ├── Zenject2VContainer.Editor.asmdef  Editor-only, references Roslyn
│   ├── UI/
│   │   ├── MigrationWizardWindow.cs      Multi-step EditorWindow
│   │   ├── Steps/
│   │   │   ├── ScanStep.cs
│   │   │   ├── PreviewStep.cs
│   │   │   ├── ApplyStep.cs
│   │   │   ├── VerifyStep.cs
│   │   │   └── RemoveZenjectStep.cs
│   │   └── DiffView.cs
│   ├── Core/
│   │   ├── MigrationPipeline.cs          Orchestrator (UI-agnostic)
│   │   ├── ProjectScanner.cs             Finds usages in C# + assets
│   │   ├── ZenjectUsageReport.cs         Data model
│   │   ├── PreconditionChecks.cs
│   │   └── BackupManager.cs              Temp/Zenject2VContainer/Backup/
│   ├── CSharp/
│   │   ├── CompilationLoader.cs          Builds CSharpCompilation per asmdef
│   │   ├── Rewriters/
│   │   │   ├── InjectAttributeRewriter.cs
│   │   │   ├── BindToAsRewriter.cs
│   │   │   ├── InstallerRewriter.cs
│   │   │   ├── TickableInitializableRewriter.cs
│   │   │   ├── FactoryRewriter.cs
│   │   │   ├── SubContainerRewriter.cs
│   │   │   └── UsingDirectiveRewriter.cs
│   │   ├── SymbolMatchers.cs             SemanticModel-based matching
│   │   └── ManualTodoEmitter.cs
│   ├── Assets/
│   │   ├── ScriptGuidMapper.cs
│   │   ├── SceneContextMigrator.cs
│   │   ├── ZenjectBindingMigrator.cs
│   │   ├── ProjectContextMigrator.cs
│   │   └── YamlPatcher.cs
│   ├── Manifest/
│   │   ├── ZenjectInstallDetector.cs
│   │   └── ZenjectRemover.cs
│   └── Reporting/
│       └── MigrationReportWriter.cs
├── Runtime/
│   ├── Zenject2VContainer.Runtime.asmdef
│   └── AutoRegisterComponent.cs          ZenjectBinding replacement helper
└── Tests/
    ├── Editor/
    │   └── Zenject2VContainer.Tests.asmdef
    ├── Fixtures/                         Snapshot pairs
    └── Integration/                      Mini Unity projects
```

### 3.2 Layer dependency rules

- `Core`, `CSharp`, `Assets`, `Manifest`, `Reporting` are pure data /
  algorithm layers. Allowed Unity dependencies: `UnityEngine.AssetDatabase`,
  `UnityEditor.PackageManager`, `UnityEditor.Compilation` only where
  absolutely required (mostly `Manifest` and `Core` orchestration). Roslyn
  rewriters and YAML patchers must be Unity-free for direct unit testability.
- `UI` depends on all other layers. No other layer depends on `UI`.
- `Runtime` is the only assembly shipped to the user's runtime build.
  Editor-only code never references it except for type GUID lookups.

### 3.3 Pipeline data flow

```
Scan ──► ZenjectUsageReport ──► Plan ──► MigrationPlan (FileChange[])
                                           │
                                           ▼
                                    Preview (DiffSet)
                                           │
                            user approves  ▼
                                    Backup → Apply C# →
                                    Wait Compile → Apply YAML →
                                    Verify → Report
                                           │
                                           ▼
                                    (optional) Remove Zenject
```

Each phase is idempotent: re-running on an already-migrated project is a
no-op.

---

## 4. Pipeline phases

### 4.1 Scan

- Enumerates C# files in `Assets/` and writable `Packages/` directories.
- Builds a `CSharpCompilation` per `.asmdef`, loading references via
  Unity's `CompilationPipeline.GetAssemblies`.
- Walks each syntax tree, queries `SemanticModel` for symbol identity. A
  symbol is treated as Zenject if its containing assembly matches the
  Zenject assembly name(s).
- Scans `.unity`, `.prefab`, `.asset` files for `m_Script` references whose
  `guid` is in a known Zenject GUID set (shipped table, periodically
  refreshed against Extenject 9.x).
- Reads `Packages/manifest.json` and probes `Assets/Plugins/Zenject`,
  `Assets/Zenject` for folder-installed Zenject.
- Result: `ZenjectUsageReport` (JSON-serialisable, persisted to
  `Library/Zenject2VContainer/scan.json`).

### 4.2 Plan + Preview

- `MigrationPlanner` consumes the report and produces `MigrationPlan`, a
  list of `FileChange { OriginalPath, OriginalText, NewText, Category,
  Confidence, RelatedFindings }`.
- `Confidence` levels: `High` (mechanical syntactic match), `Medium`
  (semantic resolution required, mapping unambiguous), `LowFlagged`
  (pattern detected but multiple plausible translations; needs user
  decision).
- UI presents diffs grouped by category (CSharp, Yaml, Manifest) with
  confidence badges. `LowFlagged` items require explicit per-file approval.

### 4.3 Apply

Apply order is fixed:

1. Run preconditions (see §4.6).
2. Snapshot all touched files into
   `Temp/Zenject2VContainer/Backup/<ISO-timestamp>/`.
3. Write all C# changes in a single batch.
4. `AssetDatabase.Refresh()`; await
   `CompilationPipeline.compilationFinished`.
5. If compile errors: default action is auto-rollback. Alternative
   (user-selected) is keep-and-show, which blocks the YAML phase.
6. Write all YAML asset changes (script GUID swap requires the new
   VContainer-derived classes to be compiled in step 4).
7. Final `AssetDatabase.Refresh()`.

### 4.4 Verify

- `CompilationPipeline.GetMessages` checked for errors.
- Re-run `ProjectScanner`. Expectation: zero remaining `using Zenject` in
  the in-scope feature set. Out-of-scope features remain (and are listed in
  the report as manual TODOs).
- All emitted `// TODO: MIGRATE-MANUAL` markers are collected and
  cross-referenced against the report.

### 4.5 Report

`MIGRATION_REPORT.md` written to `Assets/Zenject2VContainer/`:

- Header: project paths, Unity version, tool version, run timestamp.
- Summary counts: files changed, assets changed, manual TODOs, unsupported
  features encountered.
- Per-file change list with confidence levels.
- Manual TODO list with `file:line` references and doc links.
- Unsupported feature list with recommended replacements.
- Rollback instructions.

### 4.6 Preconditions

| Check                                    | Behaviour on violation                         |
|------------------------------------------|------------------------------------------------|
| Unity in Play mode                       | Block. Ask user to exit play mode.             |
| Editor compiling                         | Wait, retry.                                   |
| Project does not currently compile       | Block. SemanticModel needs a valid build.      |
| VContainer not in `manifest.json`        | Block. Offer one-click install (UPM git URL).  |
| Asset serialization mode != ForceText    | Block. Show instructions to switch.            |
| Git working tree dirty                   | Warn. Override checkbox available.             |
| Not a git repository                     | Warn. Force Temp/-only backup mode.            |

### 4.7 Backup / rollback

- File-level backup, not git-based.
- Restore via `BackupManager.Restore(timestamp)` followed by
  `AssetDatabase.Refresh()`.
- UI exposes `Rollback last migration` while the Editor session is alive.
  `Temp/` survives Editor restart unless explicitly cleared, so the button
  remains available across restarts when a backup is found.
- For commits made after migration, document `git revert` as the
  recommended rollback path (not tool rollback).

### 4.8 Zenject removal (final, optional step)

Preconditions: Phase 4 verify is green; zero remaining `using Zenject` in
in-scope code paths; VContainer is installed.

- UPM install detected: `UnityEditor.PackageManager.Client.Remove(packageId)`.
- Folder install detected: `AssetDatabase.DeleteAsset(folderPath)` after a
  warning that lists any modified files inside.
- `manifest.json` cleanup: removes scoped registries that exist only for
  Zenject.

User can defer removal indefinitely without breaking the rest of the flow.

---

## 5. Migration rules (concrete before / after)

The following rules are the contract between the design and the
implementation. Each rule is covered by at least one snapshot fixture.

### 5.1 Using directives

```csharp
// before
using Zenject;
using ModestTree;

// after
using VContainer;
using VContainer.Unity;
// using ModestTree;  -> removed if unused after rewrite
```

`UsingDirectiveRewriter` runs last. `VContainer.Unity` is added only when
Unity-specific features (`LifetimeScope`, `IStartable`, ticks, component
registration) are emitted in the file.

### 5.2 `[Inject]` attribute

Attribute name and placement preserved. Only the `using` namespace changes.

```csharp
public class Foo {
    [Inject] private IBar _bar;
    [Inject] public IBaz Baz { get; private set; }
    [Inject] public void Construct(IQux qux) { /* ... */ }
}
```

`[InjectOptional]` on field or method emits a manual TODO comment.

### 5.3 Binding API

```csharp
// before
Container.Bind<IFoo>().To<Foo>().AsSingle();
Container.Bind<IFoo>().To<Foo>().AsTransient();
Container.Bind<IFoo>().To<Foo>().AsCached();
Container.BindInterfacesTo<Foo>().AsSingle();
Container.BindInterfacesAndSelfTo<Foo>().AsSingle();
Container.Bind<IFoo>().FromInstance(myInstance);
Container.Bind<IFoo>().FromMethod(_ => new Foo()).AsSingle();
Container.Bind<IFoo>().FromComponentInHierarchy().AsSingle();
Container.Bind<Foo>().FromComponentInNewPrefab(prefab).AsSingle();
Container.Bind<Foo>().FromNewComponentOnNewGameObject().AsSingle();
Container.Bind<IFoo>().WithId("editor").To<EditorFoo>().AsSingle();

// after
builder.Register<Foo>(Lifetime.Singleton).As<IFoo>();
builder.Register<Foo>(Lifetime.Transient).As<IFoo>();
builder.Register<Foo>(Lifetime.Scoped).As<IFoo>();
builder.Register<Foo>(Lifetime.Singleton).AsImplementedInterfaces();
builder.Register<Foo>(Lifetime.Singleton).AsImplementedInterfaces().AsSelf();
builder.RegisterInstance<IFoo>(myInstance);
builder.Register<IFoo>(_ => new Foo(), Lifetime.Singleton);
builder.RegisterComponentInHierarchy<Foo>().As<IFoo>();
builder.RegisterComponentInNewPrefab<Foo>(prefab, Lifetime.Singleton);
builder.RegisterComponentOnNewGameObject<Foo>(Lifetime.Singleton, "Foo");
builder.Register<EditorFoo>(Lifetime.Singleton).As<IFoo>().Keyed<string>("editor");
```

### 5.4 Installers

Pattern detection chooses between two output shapes.

**Pattern A — single `MonoInstaller` referenced by exactly one
`SceneContext`.** Fold the installer into a generated `LifetimeScope`:

```csharp
// before
public class GameInstaller : MonoInstaller {
    public override void InstallBindings() {
        Container.Bind<IFoo>().To<Foo>().AsSingle();
        Container.BindInterfacesTo<GameLoop>().AsSingle();
    }
}

// after
public class GameLifetimeScope : LifetimeScope {
    protected override void Configure(IContainerBuilder builder) {
        builder.Register<Foo>(Lifetime.Singleton).As<IFoo>();
        builder.RegisterEntryPoint<GameLoop>(Lifetime.Singleton).AsImplementedInterfaces();
    }
}
```

**Pattern B — multiple installers attached to one `SceneContext`.** Each
becomes an `IInstaller` class. The generated `LifetimeScope` holds a
serialised array of installer references and composes them in `Configure`.

`ScriptableObjectInstaller` becomes a `ScriptableObject` implementing
`IInstaller`. The original asset GUID is preserved so that serialised
references in scenes and prefabs continue to resolve.

`Installer<T>` (POCO) becomes a plain `IInstaller`.

Lifecycle interface mapping:

| Zenject                   | VContainer                          |
|---------------------------|-------------------------------------|
| `IInitializable.Initialize` | `IStartable.Start`                |
| `ITickable.Tick`          | `ITickable.Tick`                    |
| `ILateTickable.LateTick`  | `ILateTickable.LateTick`            |
| `IFixedTickable.FixedTick`| `IFixedTickable.FixedTick`          |
| `IDisposable.Dispose`     | `IDisposable.Dispose` (scope-managed)|

Entry-point classes need `RegisterEntryPoint` registration in the scope.

### 5.5 Scene / prefab assets

| Original script               | Replacement script                                |
|-------------------------------|---------------------------------------------------|
| `SceneContext`                | `LifetimeScope` (or generated subclass)           |
| `ProjectContext`              | Generated `ProjectLifetimeScope`                  |
| `GameObjectContext`           | `LifetimeScope`                                   |
| `ZenjectBinding`              | Generated `AutoRegisterComponent` (Runtime asm)   |

Script GUID swaps are performed line-based in YAML (no full parser; YAML
text is line-stable for these edits). Serialised fields on
`SceneContext.Installers` are remapped to the corresponding field on the
generated `LifetimeScope` subclass.

`ProjectLifetimeScope` template (generated when a `ProjectContext` is
detected):

```csharp
public class ProjectLifetimeScope : LifetimeScope {
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Bootstrap() {
        var prefab = Resources.Load<ProjectLifetimeScope>("ProjectLifetimeScope");
        if (prefab != null) Instantiate(prefab);
    }

    protected override void Configure(IContainerBuilder builder) {
        // bindings moved from the original ProjectContext installers
    }
}
```

The original `ProjectContext` prefab is renamed to `ProjectLifetimeScope`
inside the same Resources folder (its asset GUID is preserved).

### 5.6 `ZenjectBinding` replacement

`AutoRegisterComponent` (shipped in `Zenject2VContainer.Runtime`) replaces
`ZenjectBinding`. The migrated `LifetimeScope` calls
`AutoRegisterComponent.Register(builder)` for each component found in its
hierarchy during `Configure`. YAML script GUID swap and field remapping
preserve the user's bind-type choices.

### 5.7 Factories

```csharp
// before
public class FooFactory : PlaceholderFactory<Param1, Foo> { }
Container.BindFactory<Param1, Foo, FooFactory>();
[Inject] FooFactory _factory;
var foo = _factory.Create(p1);

// after — installer
builder.Register<Foo>(Lifetime.Transient);
builder.RegisterFactory<Param1, Foo>(
    container => p1 => container.Resolve<Foo>(),
    Lifetime.Singleton);

// after — consumer (preferred)
public class Consumer {
    private readonly Func<Param1, Foo> _factory;
    [Inject] public Consumer(Func<Param1, Foo> factory) { _factory = factory; }
}
```

If `FooFactory` is referenced externally (for example, in serialised
fields or other assemblies), the migrator preserves a thin wrapper:

```csharp
public class FooFactory {
    private readonly Func<Param1, Foo> _func;
    public FooFactory(Func<Param1, Foo> func) { _func = func; }
    public Foo Create(Param1 p) => _func(p);
}
```

### 5.8 Sub-containers

```csharp
// before
Container.Bind<Game>().FromSubContainerResolve()
         .ByInstaller<GameInstaller>().AsSingle();
```

Simple cases (one installer, no parameters) are converted to a
programmatic child `LifetimeScope` plus a factory class. `ByMethod`,
`ByPrefab`, and dynamic factory args emit `// TODO: MIGRATE-MANUAL
[ComplexSubContainer]` markers with the original code preserved.

### 5.9 Direct `DiContainer` usage

```csharp
// before
[Inject] DiContainer _container;
var foo = _container.Resolve<IFoo>();
var instance = _container.Instantiate<MyClass>();
var go = _container.InstantiatePrefab(prefab);

// after
[Inject] IObjectResolver _resolver;
var foo = _resolver.Resolve<IFoo>();
var instance = _resolver.Resolve<MyClass>();    // when MyClass is registered
var go = _resolver.Instantiate(prefab);
```

`DiContainer.Instantiate<T>` for unregistered types emits a manual TODO
because VContainer does not support reflection-based ad-hoc instantiation
of unregistered types.

---

## 6. Manual TODO format

Every unsupported or ambiguous case emits a stable, greppable comment:

```csharp
// TODO: MIGRATE-MANUAL [Category]
// Reason: short, specific reason.
// Suggested: concrete replacement and docs link.
// Original code preserved below — review and rewrite.
```

Categories: `[SignalBus]`, `[MemoryPool]`, `[ConditionalBind]`,
`[InjectOptional]`, `[ComplexSubContainer]`, `[InstantiateUnregistered]`,
`[Decorator]`, `[CustomFactory]`, `[CustomDiContainerExtension]`.

Each category has a corresponding section in the project documentation
explaining the recommended manual replacement.

---

## 7. Error handling

| Phase failure                    | Action                                                             |
|----------------------------------|--------------------------------------------------------------------|
| C# write fails midway            | Restore from backup, abort.                                        |
| Compile fails after C# write     | Default: auto-rollback. Alternative: keep + show errors, block YAML.|
| YAML write fails midway          | Restore C# + YAML from backup.                                     |
| Verify finds remaining usages    | Warn, list files, offer manual fix step. Do not auto-rollback.     |

All phases write structured logs to
`Library/Zenject2VContainer/log-<timestamp>.txt` and to a UI panel.

---

## 8. C# rewriter edge cases

The following must be handled correctly:

- `using Z = Zenject;` aliased namespaces.
- Custom `MonoInstaller` subclasses (including abstract intermediates).
- Generic installer types (`MyInstaller<T>`).
- Partial classes split across files.
- `[Inject]` on inherited members (resolved through `SemanticModel.GetSymbolInfo`).
- User-defined extension methods on `DiContainer` (best-effort detection;
  flagged as `[CustomDiContainerExtension]`).
- Conditional compilation (`#if ZENJECT` blocks).
- XML doc comments and string literals containing `Zenject` mentions
  (preserved by default; only stripped when associated `using` is removed).

## 9. YAML editor edge cases

- Binary serialisation: blocked at precondition stage with clear
  instructions.
- Nested prefabs and prefab variants: `m_Modifications` arrays referencing
  Zenject scripts must be remapped.
- Multi-scene setups: every scene listed in `EditorBuildSettings` plus all
  scenes found under `Assets/`.
- Addressables-managed prefabs: detected via `addressableNames` metadata;
  treated identically to plain prefabs.

---

## 10. Distribution

- **UPM git URL** install: users add
  `"com.zenject2vcontainer.migrator": "https://github.com/<owner>/<repo>.git"`
  to `Packages/manifest.json`.
- `package.json` at repo root specifies dependencies (Roslyn analyzers,
  VContainer is *not* a hard dependency — the tool checks for it but does
  not install it implicitly).
- Roslyn (`Microsoft.CodeAnalysis.CSharp`) shipped as DLLs in the package
  under `Editor/Plugins/` to avoid NuGet for Unity setup friction.
- OpenUPM listing planned for a follow-up release; out of scope for v1.

---

## 11. Testing strategy

### 11.1 Snapshot tests (Edit Mode, NUnit)

- `Tests/Editor/Zenject2VContainer.Tests.asmdef`.
- `[TestCaseSource(nameof(Fixtures))]` enumerates `Tests/Fixtures/<name>/`
  folders. Each has `input/`, `expected/`, `meta.json`.
- Runner builds an in-memory `CSharpCompilation` with bundled minimal
  Zenject and VContainer reference assemblies, runs the pipeline, and
  diffs the result against `expected/`.
- Update mode via `Z2VC_UPDATE_SNAPSHOTS=1` env var.
- Minimum v1 fixture set (~30 fixtures): one per rule listed in §5.
- YAML rewrites covered by parallel `Tests/Fixtures/Yaml/<case>/` pairs
  exercised through `YamlPatcher` directly.

### 11.2 Integration tests (Unity batch mode)

- `Tests/Integration/<project>/` directories, each a complete Unity project
  with Zenject installed and a non-trivial DI graph.
- Runner executes:
  ```
  unity -batchmode -nographics -projectPath Tests/Integration/<project> \
        -executeMethod Zenject2VContainer.Tests.Integration.Runner.RunAndExit \
        -logFile -
  ```
- `Runner.RunAndExit` invokes `MigrationPipeline.RunHeadless()` (no UI),
  waits for compile, asserts zero compile errors, asserts zero remaining
  in-scope `using Zenject`, loads the main scene, optionally enters Play
  mode for one frame and checks that registered `IStartable.Start` ran.
- Minimum integration projects:
  - `SmallProject` — single SceneContext, single MonoInstaller, ~10
    bindings, one `IInitializable`, one `ITickable`.
  - `FactoriesProject` — `PlaceholderFactory` with arguments + custom
    factory referenced externally.
  - `SubContainersProject` — nested SceneContext + simple sub-container
    resolve.
- Frequency: snapshots on every PR; integration on nightly CI and release
  tags (Unity license required).

### 11.3 Regression workflow

Every reported bug becomes a minimal fixture before the fix lands. The
contributing section of the README documents this.

---

## 12. Logging and diagnostics

- Structured log per migration run at
  `Library/Zenject2VContainer/log-<timestamp>.txt`.
- Levels: Info, Warning, Error.
- Per-file-change entry: original path, change category, confidence,
  rewriter responsible.
- UI surfaces the log in a collapsible panel during Apply / Verify phases.

---

## 13. Idempotency

A second run on an already-migrated project is a no-op: the scanner finds
no Zenject references and the wizard advances directly to the Verify step
showing a green status. A partial migration (some files remain) continues
with the remaining items.

---

## 14. Out-of-scope and explicit non-goals (v1)

- CLI / headless trigger from outside Unity (the pipeline supports
  headless mode for tests, but no public CLI is shipped in v1).
- Reverse migration (VContainer → Zenject).
- Migrations across DI frameworks other than Zenject / Extenject.
- Migration of community Zenject add-ons (Zenject-Signals legacy package
  variants, Zenject-Asyncs).
- Automated migration of test fixtures or test bindings authored against
  Zenject's `ZenjectIntegrationTestFixture` and similar — flagged as
  manual TODO with guidance.

---

## 15. README rewrite

Replace the placeholder repo README with content covering: project
purpose, supported scope, supported environments, install instructions,
quick start (open `Window > Zenject2VContainer > Migrate`), wizard
walkthrough (one short paragraph per step with a screenshot placeholder),
manual TODO categories with links to detailed docs, removal step, FAQ,
contributing pointer, license. Tone: factual, terse, no marketing fluff.

---

## 16. Open questions for implementation phase

These are unresolved details that the implementation plan must address but
that do not change the design contract:

- Exact set of Zenject script GUIDs across Extenject 9.x point releases.
  The plan should ship a generation script that re-scans a known Extenject
  install and emits the GUID table.
- Strategy for asmdefs that reference Zenject indirectly through a chain
  (e.g., game asmdef → shared asmdef → Zenject). The scanner must walk
  references transitively.
- UI toolkit choice (IMGUI vs UIToolkit). IMGUI is Unity 2021.3-friendly
  and has lower complexity; UIToolkit is more modern. Default: IMGUI for
  v1, evaluate later.
