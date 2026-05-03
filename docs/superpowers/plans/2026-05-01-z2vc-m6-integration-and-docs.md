# M6 — Integration Tests, Docs and Polish Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task.
>
> **Commit policy:** Default is stage-only per project policy (`git add` then leave for owner review). If the owner authorises commits mid-session, use the M5 imperative-subject style with `Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>` trailer.

**Goal:** Close the migrator package: ship a single-page manual-TODO guide with anchor links from every emitted finding, polish `MIGRATION_REPORT.md` to be readable on a real-size project, expose two headless invocation paths (in-process CLI and `Unity.exe -batchmode -runTests`), and rewrite the repo `README.md` per spec §15.

**Out of scope of integration work:** Per owner decision (2026-05-03), the originally-planned three Unity fixture projects under `Tests/Integration/` are dropped. Existing snapshot tests + the manual smoke against PartyQuiz Host (M4 Task 15 / M5 Task 4 — completed) cover the integration surface; the headless CLI in this milestone gives a reproducible re-run path against any host project without bundling fixtures.

**Architecture:**

- `Docs~/manual-todos.md` is a single Markdown page with one section per `ManualTodoEmitter` category. The `~` suffix keeps it Unity-ignored (not imported as an asset). `Finding.DocLink` shifts from `…/manual-todos/<Category>.md` (per-file) to `…/manual-todos.md#<lowercased-category>` (anchor).
- `MigrationReportWriter` polish lands in-place — same render signature, richer output (TOC, per-category groupings, escaped markdown, project-relative paths, post-apply verification block).
- `Editor/Headless/MigrationCli.cs` exposes `RunFull(string projectRoot, string outputDir)` callable from Unity's `-executeMethod`. It chains `RunFullHeadless` + `MigrationReportWriter.WriteToDisk`, writing the report and a JSON change manifest under `outputDir`. No Editor UI is opened.
- `Scripts/run-tests.ps1` and `Scripts/run-migration.ps1` shell wrappers boot Unity in batch mode against a target project. `run-tests.ps1` calls `-runTests -testPlatform EditMode -testResults <path>`. `run-migration.ps1` calls `-executeMethod Zenject2VContainer.Headless.MigrationCli.RunFullEntry`. Both scripts honour an `$env:UNITY_PATH` override; default looks up Unity 2021.3+ via Unity Hub install dirs.
- `README.md` — full replacement covering project purpose, supported scope, supported environments, install, quick start, wizard walkthrough, manual-TODO link table, removal step, FAQ, contributing pointer, license. Keep tone factual.

**Tech Stack:** C# 9, NUnit (already present), Unity 2021.3+ batch mode, PowerShell 7+ for runner scripts. No new package dependencies.

**Definition of done:**

- Every `ManualTodoEmitter` category constant has a corresponding section in `Docs~/manual-todos.md`. Anchor link computed from the category name resolves inside the page.
- `Finding.DocLink` for emitted findings points at the anchored single page.
- `MIGRATION_REPORT.md` written by the wizard against PartyQuiz Host renders without broken markdown, with project-relative paths, grouped Changes table, Confidence breakdown in Summary, post-apply verification block.
- `Scripts/run-tests.ps1 -ProjectPath <path>` returns exit 0 when all tests pass.
- `Scripts/run-migration.ps1 -ProjectPath <path>` writes a `MIGRATION_REPORT.md` and `manifest.json` (change list) under `Library/Zenject2VContainer/headless/` of the target project.
- `README.md` covers all sections from spec §15.
- Snapshot tests + `MigrationReportWriterTests` are green.

---

## File map

| Path | Responsibility |
|---|---|
| `Docs~/manual-todos.md` (new) | Single-page guide with one anchored section per category. |
| `Editor/CSharp/ManualTodoEmitter.cs` (modify) | `DocLink` URL switches to anchor form. |
| `Editor/Reporting/MigrationReportWriter.cs` (modify) | Polish: TOC, grouped Changes, Confidence breakdown, escaping, project-relative paths, post-apply verification. |
| `Editor/Reporting/MigrationReportContext.cs` (modify, same file) | Add fields: `RemainingZenjectFiles`, `CompileErrorCount`, optional `BackupCleared` flag. |
| `Editor/Headless/MigrationCli.cs` (new) | `RunFullEntry()` + `RunFull(projectRoot, outputDir)` static methods for `-executeMethod`. |
| `Editor/Headless/MigrationCli.asmdef.meta` not needed — rides `Zenject2VContainer.Editor.asmdef`. |
| `Scripts/run-tests.ps1` (new) | Wrap `Unity.exe -batchmode -runTests`. |
| `Scripts/run-migration.ps1` (new) | Wrap `Unity.exe -batchmode -executeMethod Zenject2VContainer.Headless.MigrationCli.RunFullEntry`. |
| `Scripts/find-unity.ps1` (new, sourced by both) | Locate Unity 2021.3+ install. |
| `Tests/Editor/MigrationReportWriterTests.cs` (modify) | Cover new sections + escaping + grouping. |
| `Tests/Editor/ManualTodosCoverageTests.cs` (new) | Static check: every `ManualTodoEmitter` constant has a matching anchor in `Docs~/manual-todos.md`. |
| `README.md` (rewrite) | Per spec §15. |
| `~/.claude/projects/D--REPOS-zenject-to-vcontainer-migrator/memory/project_state.md` (update) | Snapshot M6 close. |

---

## Task 1 — Manual TODO single-page guide

**Files:**
- Create: `Docs~/manual-todos.md`
- Modify: `Editor/CSharp/ManualTodoEmitter.cs` (URL change)
- Create: `Tests/Editor/ManualTodosCoverageTests.cs` (+ .meta)

**Goal:** Authoritative, anchor-linked single page covering all 11 C# manual-TODO categories. `Finding.DocLink` shifts from a per-file URL to anchor form. Coverage test guards drift.

### Step 1 — Failing coverage test

`Tests/Editor/ManualTodosCoverageTests.cs`:

```csharp
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Zenject2VContainer.CSharp;

namespace Zenject2VContainer.Tests {
    [TestFixture]
    public class ManualTodosCoverageTests {
        [Test]
        public void EveryCategoryHasAnAnchorInTheDocPage() {
            var docPath = Path.Combine(Application.dataPath, "..", "Packages",
                "com.zenject2vcontainer.migrator", "Docs~", "manual-todos.md");
            Assert.IsTrue(File.Exists(docPath), "Docs~/manual-todos.md missing");
            var md = File.ReadAllText(docPath);
            var categories = typeof(ManualTodoEmitter)
                .GetFields(BindingFlags.Public | BindingFlags.Static)
                .Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .Select(f => (string)f.GetRawConstantValue())
                .ToArray();
            Assert.IsNotEmpty(categories);
            foreach (var cat in categories) {
                // Header form `## <Category>` produces an anchor `#<category>` (lowercased).
                StringAssert.Contains("## " + cat, md, "missing section for " + cat);
            }
        }
    }
}
```

### Step 2 — Author the doc page

`Docs~/manual-todos.md`:

```markdown
# Manual TODO Guide

The migrator emits `// TODO: MIGRATE-MANUAL [Category]` comments wherever
it cannot translate a Zenject construct safely. Each category links here
for context, recommended VContainer pattern, and a worked example.

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

**Recommended:** Pick one entry point. If you keep `IStartable`, rename
the existing `Start` method. If you keep Unity's `Start`, drop
`IStartable` and inline the init code.

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
```

### Step 3 — Switch DocLink to anchor form

`Editor/CSharp/ManualTodoEmitter.cs`:

```diff
-                DocLink = "https://github.com/<owner>/<repo>/blob/main/docs/manual-todos/" + category + ".md"
+                DocLink = "Docs~/manual-todos.md#" + category.ToLowerInvariant()
```

Rationale: relative path resolves both inside the package directory (when read by a developer who cloned the repo) and inside the report (Markdown viewers render relative anchors). Switching from `<owner>/<repo>` placeholder removes a never-filled-in URL.

### Step 4 — Stage / commit

Stage. Optional commit subject if authorised: `Add manual-TODO single-page guide and switch DocLink to anchor form`.

---

## Task 2 — MIGRATION_REPORT.md polish

**Files:**
- Modify: `Editor/Reporting/MigrationReportWriter.cs`
- Modify: `Tests/Editor/MigrationReportWriterTests.cs`

**Goal:** Twelve polish items below land in one rewrite of `Render`. Same public signature; `MigrationReportContext` gains three optional fields.

### Polish list (all addressed by the implementation in Step 2)

1. Convert `OriginalPath` to project-relative when it lies under `ctx.ProjectPath`.
2. Split Changes into three subsections: `### C#`, `### YAML`, `### Manifest`. Skip empty subsections.
3. Add Confidence breakdown to Summary: High / Medium / LowFlagged counts.
4. Group Manual TODOs by category with a per-category count + link to `Docs~/manual-todos.md#<category>`.
5. Markdown-escape pipe and backtick chars in cells (`Reason`, paths).
6. Hide the `Doc` column when no finding has a `DocLink`.
7. Render a Table of Contents linking to the four top sections.
8. Rollback section: warn that `Temp/Zenject2VContainer/Backup/` is wiped on Editor reimport, and that BackupTimestamp restore only works while the same Editor session is alive.
9. Add `## Skipped / Unchanged` listing scanner-found files with no rewriter changes (input: `MigrationReportContext.SkippedFiles` — caller fills from scan minus changes).
10. Render Backup line with both compact and human-friendly form: `20260501T120000Z (2026-05-01 12:00 UTC)`.
11. `ToolVersion` now sourced via `PackageManager.PackageInfo.FindForAssembly` at the call site (caller's job; writer just renders what it gets).
12. Add `## Post-Apply Verification` block reading `RemainingZenjectFiles` + `CompileErrorCount` from the context.

### Step 1 — Update tests

`Tests/Editor/MigrationReportWriterTests.cs` add cases for: project-relative path, escaped pipe in Reason, hidden Doc column when all empty, Confidence breakdown text, post-apply verification line, ToC presence, grouped sections.

### Step 2 — Implementation

Render the report with the polish list above. Pseudocode skeleton:

```csharp
public sealed class MigrationReportContext {
    public string ProjectPath;
    public string UnityVersion;
    public string ToolVersion;
    public string RunUtc;
    public string BackupTimestamp;
    public int RemainingZenjectFiles;     // new
    public int CompileErrorCount;         // new
    public string[] SkippedFiles;         // new (project-relative paths)
}

public static string Render(MigrationPlan plan, MigrationReportContext ctx) {
    var sb = new StringBuilder();
    AppendHeader(sb, ctx);
    AppendToc(sb);
    AppendSummary(sb, plan);             // adds Confidence breakdown
    AppendChanges(sb, plan, ctx);        // grouped by category, project-relative
    AppendManualTodos(sb, plan);         // grouped by category, hide empty Doc col
    AppendSkipped(sb, ctx);
    AppendVerification(sb, ctx);
    AppendRollback(sb, ctx);             // includes Temp/ wipe warning
    return sb.ToString();
}

static string Rel(string projectRoot, string p) {
    if (string.IsNullOrEmpty(p) || string.IsNullOrEmpty(projectRoot)) return p;
    var root = projectRoot.Replace('\\','/').TrimEnd('/') + "/";
    var n = p.Replace('\\','/');
    return n.StartsWith(root, System.StringComparison.OrdinalIgnoreCase)
        ? n.Substring(root.Length) : n;
}

static string Cell(string s) => string.IsNullOrEmpty(s)
    ? "" : s.Replace("|", "\\|").Replace("`", "'");

static string CategoryAnchor(string c) =>
    "Docs~/manual-todos.md#" + c.ToLowerInvariant();
```

### Step 3 — Wire new context fields

Modify `Editor/UI/Steps/ApplyStep.cs` (or wherever `MigrationReportWriter.WriteToDisk` is called) to populate the three new context fields. `RemainingZenjectFiles` and `CompileErrorCount` come from the same `MigrationContext` already shared with `VerifyStep` — Apply runs before Verify, so call the report writer from Verify (not Apply) once verification has concluded. If Apply currently emits the report, move the call to the end of VerifyStep.

For `SkippedFiles`: `scan.AllFilesScanned` minus paths present in `plan.Changes`. Add `AllFilesScanned` to `ZenjectUsageReport` if missing.

### Step 4 — Stage / commit

Stage. Optional commit subject: `Polish MIGRATION_REPORT.md with grouping, escaping and verification block`.

---

## Task 3 — Headless CLI driver

**Files:**
- Create: `Editor/Headless/MigrationCli.cs` (+ .meta + folder .meta)

**Goal:** Static methods callable from `Unity.exe -batchmode -executeMethod`. No UI dialogs, no user prompts. Writes report + JSON change manifest under a configurable output dir.

### Step 1 — Implementation

```csharp
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using Zenject2VContainer.Core;
using Zenject2VContainer.Reporting;

namespace Zenject2VContainer.Headless {
    public static class MigrationCli {
        // Entry point for `-executeMethod Zenject2VContainer.Headless.MigrationCli.RunFullEntry`.
        // Reads `-projectRoot <path>` and `-outputDir <path>` from the CLI args.
        // Falls back to `Application.dataPath/..` and `Library/Zenject2VContainer/headless`.
        public static void RunFullEntry() {
            var args = Environment.GetCommandLineArgs();
            string projectRoot = ReadFlag(args, "-projectRoot")
                ?? Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
            string outDir = ReadFlag(args, "-outputDir")
                ?? Path.Combine(projectRoot, "Library", "Zenject2VContainer", "headless");
            int code = RunFull(projectRoot, outDir);
            EditorApplication.Exit(code);
        }

        public static int RunFull(string projectRoot, string outputDir) {
            try {
                Directory.CreateDirectory(outputDir);
                var plan = MigrationPipeline.RunFullHeadless();
                var ctx = new MigrationReportContext {
                    ProjectPath = projectRoot,
                    UnityVersion = UnityEngine.Application.unityVersion,
                    ToolVersion = ResolveToolVersion(),
                    RunUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    BackupTimestamp = null,
                    RemainingZenjectFiles = 0,
                    CompileErrorCount = 0,
                    SkippedFiles = new string[0]
                };
                File.WriteAllText(Path.Combine(outputDir, "MIGRATION_REPORT.md"),
                    MigrationReportWriter.Render(plan, ctx));
                File.WriteAllText(Path.Combine(outputDir, "changes.json"),
                    SerializeChanges(plan));
                UnityEngine.Debug.Log($"[Zenject2VContainer] headless report at {outputDir}");
                return 0;
            } catch (Exception ex) {
                UnityEngine.Debug.LogError("[Zenject2VContainer] headless failed: " + ex);
                return 1;
            }
        }

        private static string ReadFlag(string[] args, string flag) {
            for (int i = 0; i < args.Length - 1; i++)
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            return null;
        }

        private static string ResolveToolVersion() {
            var info = UnityEditor.PackageManager.PackageInfo.FindForAssembly(
                typeof(MigrationCli).Assembly);
            return info?.version ?? "0.0.0";
        }

        private static string SerializeChanges(MigrationPlan plan) {
            // JsonUtility cannot serialise List<T> at top level; wrap.
            var wrap = new ChangesWrap {
                changes = plan.Changes.Select(c => new ChangeRow {
                    path = c.OriginalPath,
                    category = c.Category.ToString(),
                    confidence = c.Confidence.ToString()
                }).ToArray(),
                manualTodos = plan.Unsupported.Select(f => new TodoRow {
                    category = f.Category, file = f.FilePath,
                    line = f.Line, reason = f.Reason
                }).ToArray()
            };
            return UnityEngine.JsonUtility.ToJson(wrap, prettyPrint: true);
        }

        [Serializable] private class ChangesWrap { public ChangeRow[] changes; public TodoRow[] manualTodos; }
        [Serializable] private class ChangeRow { public string path; public string category; public string confidence; }
        [Serializable] private class TodoRow { public string category; public string file; public int line; public string reason; }
    }
}
```

The CLI does **not** apply changes — it produces a preview-style report identical to `Window/Zenject2VContainer/Preview Full Migration…`. Apply is wizard-only, since rollback depends on `BackupManager` snapshots and Editor compilation feedback, which are unsafe in batch mode without a host that handles compile failures.

### Step 2 — Stage / commit

Stage. Optional commit: `Add headless MigrationCli for batch-mode preview runs`.

---

## Task 4 — Batch-mode runner scripts

**Files:**
- Create: `Scripts/find-unity.ps1`
- Create: `Scripts/run-tests.ps1`
- Create: `Scripts/run-migration.ps1`

**Goal:** Two PowerShell wrappers around `Unity.exe`. `find-unity.ps1` is sourced by both; resolves a Unity 2021.3+ install path.

### Step 1 — find-unity.ps1

```powershell
# Resolves a Unity 2021.3+ install path. Honours $env:UNITY_PATH; otherwise
# probes Unity Hub install dirs. Writes to $script:UnityPath.

function Resolve-Unity {
    param([string]$RequiredMajor = "2021")
    if ($env:UNITY_PATH -and (Test-Path $env:UNITY_PATH)) { return $env:UNITY_PATH }
    $candidates = @(
        "C:\Program Files\Unity\Hub\Editor",
        "$env:LOCALAPPDATA\Unity\Hub\Editor"
    )
    foreach ($root in $candidates) {
        if (-not (Test-Path $root)) { continue }
        $hit = Get-ChildItem $root -Directory `
            | Where-Object { $_.Name -match "^$RequiredMajor\." } `
            | Sort-Object Name -Descending `
            | Select-Object -First 1
        if ($hit) { return Join-Path $hit.FullName "Editor\Unity.exe" }
    }
    throw "Unity $RequiredMajor.x not found. Set `$env:UNITY_PATH or install via Unity Hub."
}
```

### Step 2 — run-tests.ps1

```powershell
param(
    [Parameter(Mandatory)] [string]$ProjectPath,
    [string]$ResultsPath = "$ProjectPath\Logs\test-results.xml",
    [string]$LogPath = "$ProjectPath\Logs\unity-test.log",
    [string]$TestPlatform = "EditMode"
)
. "$PSScriptRoot\find-unity.ps1"
$unity = Resolve-Unity
New-Item -ItemType Directory -Force -Path (Split-Path $ResultsPath) | Out-Null
& $unity -batchmode -nographics -projectPath $ProjectPath `
    -runTests -testPlatform $TestPlatform `
    -testResults $ResultsPath -logFile $LogPath
exit $LASTEXITCODE
```

### Step 3 — run-migration.ps1

```powershell
param(
    [Parameter(Mandatory)] [string]$ProjectPath,
    [string]$OutputDir = "$ProjectPath\Library\Zenject2VContainer\headless",
    [string]$LogPath = "$ProjectPath\Logs\unity-migration.log"
)
. "$PSScriptRoot\find-unity.ps1"
$unity = Resolve-Unity
New-Item -ItemType Directory -Force -Path (Split-Path $LogPath) | Out-Null
& $unity -batchmode -nographics -projectPath $ProjectPath `
    -executeMethod Zenject2VContainer.Headless.MigrationCli.RunFullEntry `
    -projectRoot $ProjectPath -outputDir $OutputDir -quit -logFile $LogPath
exit $LASTEXITCODE
```

### Step 4 — Stage / commit

Stage. Optional commit: `Add PowerShell wrappers for batch-mode tests and migration`.

---

## Task 5 — README rewrite per spec §15

**Files:**
- Modify: `README.md`

**Goal:** Replace placeholder README with content covering all sections from spec §15. Tone: factual, terse.

### Step 1 — Outline (sections in order)

1. **Zenject2VContainer Migrator** — one-paragraph project purpose.
2. **Status** — version (`0.1.0`), Unity minimum (2021.3), supported source DI (Zenject 9.x / Extenject 9.x), supported target DI (VContainer 1.x).
3. **Supported scope** — bullet list of migration features (installer rewrite, factory wrappers, sub-container surface, scene/prefab YAML migration, Zenject removal). Link to spec for full matrix.
4. **Install** — UPM install via `manifest.json` (`"com.zenject2vcontainer.migrator": "file:../../zenject-to-vcontainer-migrator"` for local; git URL form for remote). `testables` array note.
5. **Quick start** — open `Window > Zenject2VContainer > Migration Wizard…`. One-paragraph wizard walkthrough per step (Scan, Preview, Apply, Verify, Remove) with screenshot placeholders (`![Scan step](Docs~/img/step-scan.png)` etc.).
6. **Manual TODOs** — short table mapping category → one-line summary → link to `Docs~/manual-todos.md#<category>`.
7. **Removal step** — what `Remove` does (UPM uninstall, folder delete, scoped-registry strip), idempotency, what to do if rolled back via git.
8. **Headless / CI** — pointer to `Scripts/run-migration.ps1` and `Scripts/run-tests.ps1`.
9. **FAQ** — 3-5 entries: "Is Apply reversible?", "Why does the report say `Temp/...` for backup?", "What does `LowFlagged` confidence mean?", "Why was my MonoInstaller retyped as MonoBehaviour?".
10. **Contributing** — pointer to `docs/superpowers/specs/...` and the GSD plan layout.
11. **License** — placeholder pointing at `LICENSE` (skip if no LICENSE file yet — note as TODO).

Screenshot placeholders are committed as `Docs~/img/step-*.png` only if the smoke run produces them; otherwise leave the markdown links pointing at not-yet-existing files (acceptable for v0.1).

### Step 2 — Stage / commit

Stage. Optional commit: `Rewrite README per spec §15`.

---

## Task 6 — Final stage

**Files:** none modified.

### Step 1 — Run all tests headlessly via the new script

```powershell
.\Scripts\run-tests.ps1 -ProjectPath D:\REPOS\PartyQuiz\Host
```

Expect exit 0. If non-zero, fix and re-stage before declaring M6 done.

### Step 2 — Run migration headlessly against PartyQuiz Host

```powershell
.\Scripts\run-migration.ps1 -ProjectPath D:\REPOS\PartyQuiz\Host
```

Open `D:\REPOS\PartyQuiz\Host\Library\Zenject2VContainer\headless\MIGRATION_REPORT.md`. Verify all twelve polish items from Task 2 are visible (TOC, grouped Changes, Confidence breakdown, escaping of any `|` chars, project-relative paths, etc.). Fix anything that looks wrong, re-run.

### Step 3 — Update memory snapshot

Update `~/.claude/projects/D--REPOS-zenject-to-vcontainer-migrator/memory/project_state.md` to mark M6 complete. Snapshot should describe: which scripts exist under `Scripts/`, where the manual-TODO doc lives, where the headless CLI exposes `RunFullEntry`, and that the only remaining work pre-tag is owner review.

### Step 4 — Final stage

`git status` should show modifications to: `Editor/CSharp/ManualTodoEmitter.cs`, `Editor/Reporting/MigrationReportWriter.cs`, `Editor/UI/Steps/VerifyStep.cs` (or wherever the writer is now invoked), `README.md`. New: `Docs~/manual-todos.md` (+ folder .meta), `Editor/Headless/MigrationCli.cs` (+ .meta + folder .meta), `Scripts/*.ps1`, `Tests/Editor/ManualTodosCoverageTests.cs` (+ .meta). Stage all.

Hand off to owner.

---

## Self-review checklist

- `Docs~/manual-todos.md` covers all 11 categories; coverage test is green.
- `Finding.DocLink` points at `Docs~/manual-todos.md#<lowercased-category>`. No emitter still uses the old per-file URL.
- `MigrationReportWriter.Render` produces project-relative paths, grouped Changes, escaped table cells, hidden empty Doc column, ToC, post-apply verification block.
- `MigrationReportWriter.WriteToDisk` call site has moved (or stayed in) a place where verification stats are known.
- `MigrationCli.RunFullEntry` exits 0 on success, 1 on exception, never opens UI, never applies changes.
- `Scripts/run-tests.ps1` and `Scripts/run-migration.ps1` honour `$env:UNITY_PATH` override and resolve Unity 2021.3+ via Unity Hub when not set.
- `README.md` has all sections from spec §15. Screenshot placeholders are explicit and broken-link-tolerant for v0.1.
- All snapshot tests + `MigrationReportWriterTests` + `ManualTodosCoverageTests` are green.
- Memory snapshot under `~/.claude/.../memory/project_state.md` reflects M6 close.
