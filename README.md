# Zenject2VContainer Migrator

A Unity Editor tool that scans a Unity project for Zenject / Extenject usage and rewrites C# source files, YAML scene/prefab assets, and `Packages/manifest.json` to use VContainer. The migration runs through a five-step wizard (`Window > Zenject2VContainer > Migration Wizard…`) and exposes a headless CLI entry point for CI pipelines that need a preview run without opening the Editor UI.

## Status

- **Version:** 0.1.0
- **Unity minimum:** 2021.3+
- **Source DI:** Zenject 9.x / Extenject 9.x
- **Target DI:** VContainer 1.x (`jp.hadashikick.vcontainer`)

## Supported scope

- Core bindings: `Bind`/`AsSingle`/`AsTransient`/`AsCached`/`BindInterfacesTo`/`BindInterfacesAndSelfTo`/`FromInstance`/`FromMethod`/`FromComponentInHierarchy`/`FromComponentInNewPrefab`/`FromNewComponentOnNewGameObject`/`WithId`
- `MonoInstaller`, `ScriptableObjectInstaller`, and `Installer<T>` rewrites to `LifetimeScope` / `IInstaller`
- Lifecycle interface rename: `IInitializable` → `IStartable`, `ITickable` → `ITickable`
- `PlaceholderFactory<TArg, TOut>` → `Func<TArg, TOut>` wrapper
- `FromSubContainerResolve().ByMethod` — trivial cases only; non-trivial cases emit a manual TODO
- `DiContainer` → `IObjectResolver` rename
- Scene, prefab, and ScriptableObject YAML asset GUID swaps
- Scoped registry stripping from `Packages/manifest.json`
- Zenject removal: UPM uninstall via `Client.Remove`, Zenject folder deletion, manifest cleanup

Full feature matrix: see [spec §5](docs/superpowers/specs/2026-05-01-zenject-to-vcontainer-migrator-design.md).

## Install

Add the package to `Packages/manifest.json`. Choose the local-file form when working from a clone of this repo, or the git URL form for a remote reference:

```json
{
  "dependencies": {
    "com.zenject2vcontainer.migrator": "file:../../zenject-to-vcontainer-migrator",
    "jp.hadashikick.vcontainer": "https://github.com/hadashiA/VContainer.git?path=VContainer/Assets/VContainer"
  },
  "testables": [
    "com.zenject2vcontainer.migrator"
  ]
}
```

Git URL form (pinned to a tag):

```json
"com.zenject2vcontainer.migrator": "https://github.com/<owner>/zenject-to-vcontainer-migrator.git#0.1.0"
```

The `testables` array entry is required for Unity Test Runner to discover the package's EditMode tests.

## Quick start

1. Open `Window > Zenject2VContainer > Migration Wizard…`.
2. Walk through Scan → Preview → Apply → Verify → Remove.
3. Read `Assets/Zenject2VContainer/MIGRATION_REPORT.md` for a record of what changed and which manual TODOs need follow-up.

## Wizard walkthrough

### Scan

The Scan step runs the static scanner across all `.cs`, `.unity`, `.prefab`, and `.asset` files under `Assets/` and any local packages. It builds a `ZenjectUsageReport` that lists every file containing Zenject references along with a per-file binding inventory. Advancement is blocked until the scan completes without file-access errors. Results are stored in `MigrationContext.ScanReport`.

![Scan step](Docs~/img/step-scan.png)

_Screenshots are not yet bundled in v0.1._

### Preview

Preview feeds the scan report through the C# and YAML rewriters and produces a `MigrationPlan` — a list of proposed changes, each annotated with a confidence level (High, Medium, or LowFlagged). Files with LowFlagged changes are shown for per-file approval; advancement to Apply is blocked until every LowFlagged change is either approved or rejected. The plan is stored in `MigrationContext.Plan`.

![Preview step](Docs~/img/step-preview.png)

_Screenshots are not yet bundled in v0.1._

### Apply

Apply writes the rewritten file contents to disk and creates a file-level backup under `Temp/Zenject2VContainer/Backup/<timestamp>/`. Only changes that passed the Preview approval step are written. The step exposes a Rollback button that restores from the backup while the same Editor session is open. Apply does not trigger an asset reimport; that happens automatically once the step completes.

![Apply step](Docs~/img/step-apply.png)

_Screenshots are not yet bundled in v0.1._

### Verify

Verify waits for the Unity asset pipeline to finish importing the rewritten files, then checks for remaining Zenject references and compile errors. Results are stored in `MigrationContext.VerifyResult`. Advancement to Remove is blocked if compile errors are present. The migration report (`Assets/Zenject2VContainer/MIGRATION_REPORT.md`) is written at the end of this step once verification stats are known.

![Verify step](Docs~/img/step-verify.png)

_Screenshots are not yet bundled in v0.1._

### Remove

Remove uninstalls the Zenject UPM package via `Client.Remove`, deletes any remaining Zenject-owned folders via `AssetDatabase.DeleteAsset`, and strips the Zenject scoped registry from `Packages/manifest.json`. The step is idempotent — running it again when nothing remains is a no-op.

![Remove step](Docs~/img/step-remove.png)

_Screenshots are not yet bundled in v0.1._

## Manual TODOs

The rewriters emit `// TODO: MIGRATE-MANUAL [Category]` comments wherever a Zenject construct cannot be translated automatically. The table below lists all 11 categories.

| Category | Why manual | Doc |
|---|---|---|
| SignalBus | No VContainer equivalent; use MessagePipe or a hand-rolled event aggregator | [SignalBus](Docs~/manual-todos.md#signalbus) |
| MemoryPool | No direct VContainer counterpart; replace with `ObjectPool<T>` | [MemoryPool](Docs~/manual-todos.md#memorypool) |
| ConditionalBind | Predicate-based binding (`When`/`WhenInjectedInto`) has no equivalent | [ConditionalBind](Docs~/manual-todos.md#conditionalbind) |
| InjectOptional | VContainer requires presence; no `[InjectOptional]` attribute | [InjectOptional](Docs~/manual-todos.md#injectoptional) |
| ComplexSubContainer | Non-trivial `ByMethod` sub-container graphs must be hand-translated to child `LifetimeScope` | [ComplexSubContainer](Docs~/manual-todos.md#complexsubcontainer) |
| InstantiateUnregistered | `DiContainer.Instantiate<T>` builds unregistered types; VContainer demands registration | [InstantiateUnregistered](Docs~/manual-todos.md#instantiateunregistered) |
| Decorator | `InstallDecoratorContext` has no direct counterpart | [Decorator](Docs~/manual-todos.md#decorator) |
| CustomFactory | `BindFactory.FromFactory<T>` chains require per-argument logic not expressible as `Func` | [CustomFactory](Docs~/manual-todos.md#customfactory) |
| CustomDiContainerExtension | Extensions that mutate `DiContainer` directly do not survive rename to `IObjectResolver` | [CustomDiContainerExtension](Docs~/manual-todos.md#customdicontainerextension) |
| LifecycleStartCollision | `IInitializable.Initialize` rename to `Start` collides with `MonoBehaviour.Start` | [LifecycleStartCollision](Docs~/manual-todos.md#lifecyclestartcollision) |
| InstallerWiring | `MonoInstaller` retyped as `MonoBehaviour : IInstaller` no longer auto-registers | [InstallerWiring](Docs~/manual-todos.md#installerwiring) |

## Removal step

The Remove wizard step performs three actions:

1. Calls `Client.Remove("com.unity.zenject")` (or the Extenject package name) to uninstall via UPM.
2. Calls `AssetDatabase.DeleteAsset` on any remaining Zenject-owned folders under `Assets/`.
3. Strips the Zenject scoped registry entry from `Packages/manifest.json` using `ManifestEditor`.

The step is idempotent. If Zenject is already absent when Remove runs — because it was removed manually or because Remove was run a second time — each action detects the already-absent state and skips. No error is raised.

To restore Zenject after removal, use `git revert` on the relevant commits. The tool has no in-place restore path.

## Headless / CI

Two PowerShell 7+ scripts live under `Scripts/`. Both honour a `$env:UNITY_PATH` override; when the variable is not set they locate a Unity 2021.3+ install via Unity Hub's default install directories.

Run the EditMode test suite against a target project:

```powershell
.\Scripts\run-tests.ps1 -ProjectPath D:\REPOS\MyProject
```

Produce a preview report without applying changes:

```powershell
.\Scripts\run-migration.ps1 -ProjectPath D:\REPOS\MyProject
```

`run-migration.ps1` calls `-executeMethod Zenject2VContainer.Headless.MigrationCli.RunFullEntry` and writes `MIGRATION_REPORT.md` and `changes.json` under `<ProjectPath>\Library\Zenject2VContainer\headless\`. It is preview-only — it does not write the rewritten files to disk. Apply is wizard-only.

Override the Unity executable path:

```powershell
$env:UNITY_PATH = "C:\Program Files\Unity\Hub\Editor\2022.3.10f1\Editor\Unity.exe"
.\Scripts\run-migration.ps1 -ProjectPath D:\REPOS\MyProject
```

## FAQ

**Is Apply reversible?**
Apply creates a file-level snapshot under `Temp/Zenject2VContainer/Backup/<yyyyMMddTHHmmssZ>/` before writing any changes. The Rollback button in the Apply step restores from this snapshot while the same Editor session is alive. After the session ends or after `git commit`, use `git revert` — the backup is gone at that point.

**Why does the report mention `Temp/...` for backup?**
Unity wipes the `Temp/` directory on Editor reimport and on project close. The backup is available for rollback only within the same Editor session that ran Apply. Outside that window, only `git revert` recovers the original files.

**What does `LowFlagged` confidence mean?**
A change the rewriter produced but marked as needing human review — typically because the original code used a pattern the rewriter recognises but cannot translate with full certainty. The Preview step holds advancement to Apply until every LowFlagged change in each file has been explicitly approved or rejected.

**Why was my MonoInstaller retyped as MonoBehaviour?**
VContainer has no `MonoInstaller` class. Keeping the type as a `MonoBehaviour` preserves its scene attachment. The rewriter adds `IInstaller` to the type's interface list so a parent `LifetimeScope` can pick it up via `builder.UseInstaller(this)`. The wiring between the `MonoBehaviour` and its parent `LifetimeScope` must be done by hand. See [InstallerWiring](Docs~/manual-todos.md#installerwiring).

## Contributing

Specs live under `docs/superpowers/specs/` and implementation plans under `docs/superpowers/plans/`. Plans follow a GSD-style task structure: each task lists the files it touches, the acceptance criteria, and an optional commit subject. New work should start from a plan file in that directory.

Commits follow the imperative-subject style used throughout the project history (e.g. `Add ZenjectRemover with UPM, folder and manifest cleanup`). PRs should keep each commit scoped to one task from the relevant plan.

## License

MIT. See [LICENSE](LICENSE).
