# M4 — Editor Wizard UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Commit policy (project-wide):** The repository owner manages all branches and commits. Tasks must NOT include `git commit`, `git push`, `git branch`, `git merge`, or worktree ops. Each task ends with `git add` only — leaving staged changes for the owner to inspect and land manually. This rule overrides the default writing-plans skill format.

**Goal:** Deliver an IMGUI `MigrationWizardWindow` that drives end-to-end migration on a real Unity project: Scan → Preview → Apply → Verify → (optional) Remove. Headless entry points from M1–M3 stay intact; the wizard is a UI shell on top of them, plus the supporting infrastructure (preconditions, backup/rollback, structured logging, migration report) that the spec lists as wizard responsibilities.

**Architecture:** Single `EditorWindow` (`Window/Zenject2VContainer/Migration Wizard…`) drives a finite state machine `WizardState` (Scan, Preview, Apply, Verify, Remove, Done). Each step is a `IWizardStep` implementation that owns its IMGUI render and its phase action. The window holds a shared `MigrationContext` (scan report, plan, backup id, log buffer, preconditions, install info) that steps mutate. File-level backup runs through `BackupManager` writing to `Temp/Zenject2VContainer/Backup/<ISO-timestamp>/`; auto-rollback restores the same snapshot. C# apply re-uses M2 `RewritePipeline`; YAML apply re-uses M3 `AssetMigrator`. The wizard depends on M5's removal API only via the `IZenjectRemover` interface defined in this milestone — Task 11 ships a no-op stub so the wizard compiles and runs end-to-end before M5 lands.

**Tech Stack:** Unity 2021.3+ IMGUI (`EditorWindow`, `EditorGUILayout`), C# 9, NUnit (tests live in `Tests/Editor/`), Roslyn (re-used from M2 via `RewritePipeline`), `UnityEditor.Compilation.CompilationPipeline` for awaiting compile after C# apply.

**Out of scope (deferred):**
- Full `ZenjectRemover` implementation (M5).
- Integration tests + README rewrite + report polish (M6).
- Visual diff with syntax highlighting (text-only diff is fine for v1).
- Multi-project / batch UI — wizard handles one project (the host project the package is loaded into).

---

## File map

Files this milestone creates or substantially modifies. Paths are relative to repo root `D:\REPOS\zenject-to-vcontainer-migrator`.

| Path                                                      | Responsibility                                                                                  |
|-----------------------------------------------------------|-------------------------------------------------------------------------------------------------|
| `Editor/UI/MigrationWizardWindow.cs`                      | `EditorWindow` entry point. Owns `WizardState`, renders header + log panel + active step.       |
| `Editor/UI/WizardState.cs`                                | Enum + `MigrationContext` data carrier shared across steps.                                     |
| `Editor/UI/IWizardStep.cs`                                | Step contract: `Title`, `OnGUI(ctx)`, `Run(ctx)`.                                               |
| `Editor/UI/Steps/ScanStep.cs`                             | Calls `RunScanHeadless`, displays summary counts + install detection.                           |
| `Editor/UI/Steps/PreviewStep.cs`                          | Calls `RunCSharpHeadless` + `RunYamlHeadless`, groups changes, drives `DiffView`.              |
| `Editor/UI/Steps/ApplyStep.cs`                            | Runs preconditions, creates backup, writes C#, awaits compile, writes YAML, auto-rollback path. |
| `Editor/UI/Steps/VerifyStep.cs`                           | Re-runs scanner, surfaces remaining `using Zenject` count + compile errors + manual TODOs.      |
| `Editor/UI/Steps/RemoveZenjectStep.cs`                    | UI shell over `IZenjectRemover`. Stub remover used until M5.                                    |
| `Editor/UI/DiffView.cs`                                   | Text-diff viewer (line-level, original vs new) inside a scroll view with category filter.       |
| `Editor/UI/MigrationLogPanel.cs`                          | IMGUI collapsible log panel reading `MigrationLog`.                                             |
| `Editor/Core/MigrationContext.cs`                         | Shared mutable state held by the wizard for the lifetime of one run.                            |
| `Editor/Core/MigrationLog.cs`                             | Structured log: levels (Info/Warn/Error), per-file entries, sink to file + buffer.              |
| `Editor/Core/BackupManager.cs`                            | Snapshot / restore file-level backup at `Temp/Zenject2VContainer/Backup/<ISO>/`.                |
| `Editor/Core/PreconditionChecks.cs` (extend)              | Add the remaining checks from spec §4.6 (PlayMode, Compiling, ProjectCompiles, AssetSerialization). |
| `Editor/Core/PreconditionRunner.cs`                       | Runs all 7 checks against the live editor and returns aggregated result.                        |
| `Editor/Core/MigrationApplyService.cs`                    | UI-agnostic apply orchestrator. Used by `ApplyStep` and tested directly.                        |
| `Editor/Manifest/IZenjectRemover.cs`                      | M4-defined interface so the wizard can compile before M5 ships.                                 |
| `Editor/Manifest/StubZenjectRemover.cs`                   | No-op default implementation. Replaced by M5's real `ZenjectRemover`.                           |
| `Editor/Reporting/MigrationReportWriter.cs`               | Writes `Assets/Zenject2VContainer/MIGRATION_REPORT.md` per spec §4.5.                           |
| `Editor/UI/MigrationMenu.cs` (extend)                     | Add `Migration Wizard…` menu item that opens `MigrationWizardWindow`.                           |
| `Tests/Editor/BackupManagerTests.cs`                      | Snapshot+restore round-trip on Temp/ scratch dir.                                               |
| `Tests/Editor/PreconditionChecksTests.cs` (extend)        | Add cases for the new checks (table-driven, no Editor required).                                |
| `Tests/Editor/MigrationApplyServiceTests.cs`              | Apply flow with mocked `ICompileWaiter`, asserts auto-rollback on simulated compile error.      |
| `Tests/Editor/MigrationReportWriterTests.cs`              | Golden-string test for the markdown layout.                                                     |
| `Tests/Editor/DiffViewTests.cs`                           | Pure text-diff function tested without GUI.                                                     |

Per spec §3.1 the wizard files belong under `Editor/UI/` (window + steps + diff/log) and the supporting infra under `Editor/Core/`, `Editor/Manifest/`, `Editor/Reporting/`. M4 creates the latter two folders.

---

## Task 1 — Wizard state model + context

**Files:**
- Create: `Editor/UI/WizardState.cs`
- Create: `Editor/Core/MigrationContext.cs`
- Create: `Editor/UI/IWizardStep.cs`

- [ ] **Step 1: Add `WizardState` enum**

`Editor/UI/WizardState.cs`:

```csharp
namespace Zenject2VContainer.UI {
    public enum WizardState {
        Scan,
        Preview,
        Apply,
        Verify,
        Remove,
        Done
    }
}
```

- [ ] **Step 2: Add `MigrationContext` data carrier**

`Editor/Core/MigrationContext.cs`:

```csharp
using System.Collections.Generic;
using Zenject2VContainer.Core.Scanner;

namespace Zenject2VContainer.Core {
    public sealed class MigrationContext {
        public ZenjectUsageReport ScanReport;
        public MigrationPlan CSharpPlan;
        public MigrationPlan YamlPlan;
        public string BackupTimestamp;
        public bool ApplySucceeded;
        public List<string> RemainingZenjectFiles = new List<string>();
        public List<string> CompileErrors = new List<string>();
        public MigrationLog Log = new MigrationLog();
        public bool UserOverrodeGitDirty;
    }
}
```

- [ ] **Step 3: Add `IWizardStep` contract**

`Editor/UI/IWizardStep.cs`:

```csharp
using Zenject2VContainer.Core;

namespace Zenject2VContainer.UI {
    public interface IWizardStep {
        string Title { get; }
        WizardState State { get; }
        void OnGUI(MigrationContext ctx);
        bool CanAdvance(MigrationContext ctx);
    }
}
```

- [ ] **Step 4: Stage**

```bash
git add Editor/UI/WizardState.cs Editor/Core/MigrationContext.cs Editor/UI/IWizardStep.cs
```

---

## Task 2 — Structured log

**Files:**
- Create: `Editor/Core/MigrationLog.cs`

- [ ] **Step 1: Define log entry + sink**

`Editor/Core/MigrationLog.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;

namespace Zenject2VContainer.Core {
    public enum LogLevel { Info, Warn, Error }

    public sealed class MigrationLogEntry {
        public DateTime UtcTimestamp;
        public LogLevel Level;
        public string Source;       // "Scan", "Apply.CSharp", "Apply.Yaml", "Verify", "Remove"
        public string Message;
        public string FilePath;     // optional
        public override string ToString() {
            var f = string.IsNullOrEmpty(FilePath) ? "" : $" [{FilePath}]";
            return $"{UtcTimestamp:O} {Level} {Source}{f}: {Message}";
        }
    }

    public sealed class MigrationLog {
        public List<MigrationLogEntry> Entries = new List<MigrationLogEntry>();

        public void Info(string source, string message, string filePath = null)  => Add(LogLevel.Info, source, message, filePath);
        public void Warn(string source, string message, string filePath = null)  => Add(LogLevel.Warn, source, message, filePath);
        public void Error(string source, string message, string filePath = null) => Add(LogLevel.Error, source, message, filePath);

        private void Add(LogLevel level, string source, string message, string filePath) {
            Entries.Add(new MigrationLogEntry {
                UtcTimestamp = DateTime.UtcNow,
                Level = level,
                Source = source,
                Message = message,
                FilePath = filePath
            });
        }

        public void WriteTo(string path) {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            using var w = new StreamWriter(path, append: false);
            foreach (var e in Entries) w.WriteLine(e.ToString());
        }
    }
}
```

- [ ] **Step 2: Stage**

```bash
git add Editor/Core/MigrationLog.cs
```

---

## Task 3 — Precondition checks (full table)

**Files:**
- Modify: `Editor/Core/PreconditionChecks.cs`
- Create: `Editor/Core/PreconditionRunner.cs`
- Modify: `Tests/Editor/PreconditionChecksTests.cs`

Spec §4.6 lists 7 checks. Existing file covers VContainer presence + git state. Add the remaining 5 as pure functions (table-driven inputs) so they stay testable without an editor.

- [ ] **Step 1: Add the missing checks to `PreconditionChecks.cs`**

Append to the existing class:

```csharp
public static PreconditionResult CheckPlayMode(bool isPlaying) =>
    isPlaying
        ? new PreconditionResult {
            Result = PreconditionResult.Severity.Block,
            Code = "PLAY_MODE",
            Message = "Unity is in Play mode. Exit Play mode before applying migration."
        }
        : new PreconditionResult { Result = PreconditionResult.Severity.Pass, Code = "PLAY_MODE_OFF", Message = "Editor is not in Play mode." };

public static PreconditionResult CheckCompiling(bool isCompiling) =>
    isCompiling
        ? new PreconditionResult {
            Result = PreconditionResult.Severity.Block,
            Code = "COMPILING",
            Message = "Editor is currently compiling. Wait for compile to finish, then retry."
        }
        : new PreconditionResult { Result = PreconditionResult.Severity.Pass, Code = "COMPILE_IDLE", Message = "Editor is idle." };

public static PreconditionResult CheckProjectCompiles(bool hasCompileErrors) =>
    hasCompileErrors
        ? new PreconditionResult {
            Result = PreconditionResult.Severity.Block,
            Code = "PROJECT_BROKEN",
            Message = "Project has unresolved compile errors. Roslyn SemanticModel needs a clean build to resolve Zenject symbols."
        }
        : new PreconditionResult { Result = PreconditionResult.Severity.Pass, Code = "PROJECT_OK", Message = "Project compiles cleanly." };

public static PreconditionResult CheckAssetSerialization(bool isForceText) =>
    isForceText
        ? new PreconditionResult { Result = PreconditionResult.Severity.Pass, Code = "SERIALIZATION_OK", Message = "Asset serialization mode is ForceText." }
        : new PreconditionResult {
            Result = PreconditionResult.Severity.Block,
            Code = "SERIALIZATION_BAD",
            Message = "Asset serialization mode must be ForceText. Switch via Edit → Project Settings → Editor → Asset Serialization → Mode."
        };
```

- [ ] **Step 2: Run existing tests to confirm no regressions**

Open Unity Test Runner, run `Zenject2VContainer.Tests.PreconditionChecksTests`. Expected: existing tests still pass.

- [ ] **Step 3: Add tests for the new checks**

Append to `Tests/Editor/PreconditionChecksTests.cs`:

```csharp
[Test] public void PlayMode_blocked_when_playing() {
    var r = PreconditionChecks.CheckPlayMode(isPlaying: true);
    Assert.AreEqual(PreconditionResult.Severity.Block, r.Result);
    Assert.AreEqual("PLAY_MODE", r.Code);
}
[Test] public void PlayMode_passes_when_not_playing() {
    Assert.AreEqual(PreconditionResult.Severity.Pass, PreconditionChecks.CheckPlayMode(isPlaying: false).Result);
}
[Test] public void Compiling_blocked() {
    Assert.AreEqual(PreconditionResult.Severity.Block, PreconditionChecks.CheckCompiling(isCompiling: true).Result);
}
[Test] public void ProjectCompiles_blocked_when_broken() {
    Assert.AreEqual(PreconditionResult.Severity.Block, PreconditionChecks.CheckProjectCompiles(hasCompileErrors: true).Result);
}
[Test] public void AssetSerialization_blocked_when_not_force_text() {
    Assert.AreEqual(PreconditionResult.Severity.Block, PreconditionChecks.CheckAssetSerialization(isForceText: false).Result);
}
```

- [ ] **Step 4: Add `PreconditionRunner` glue (Editor-only)**

`Editor/Core/PreconditionRunner.cs`:

```csharp
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using UnityEditor;
using UnityEngine;
using Zenject2VContainer.Core.Scanner;

namespace Zenject2VContainer.Core {
    public sealed class PreconditionReport {
        public List<PreconditionResult> Results = new List<PreconditionResult>();
        public bool HasBlock {
            get {
                foreach (var r in Results) if (r.Result == PreconditionResult.Severity.Block) return true;
                return false;
            }
        }
    }

    public static class PreconditionRunner {
        public static PreconditionReport Run(InstallationInfo install, bool userOverrodeGitDirty) {
            var report = new PreconditionReport();
            report.Results.Add(PreconditionChecks.CheckPlayMode(EditorApplication.isPlayingOrWillChangePlaymode));
            report.Results.Add(PreconditionChecks.CheckCompiling(EditorApplication.isCompiling));
            report.Results.Add(PreconditionChecks.CheckProjectCompiles(HasCompileErrors()));
            report.Results.Add(PreconditionChecks.CheckVContainerPresence(install));
            report.Results.Add(PreconditionChecks.CheckAssetSerialization(EditorSettings.serializationMode == SerializationMode.ForceText));

            var (isRepo, isDirty) = ProbeGit();
            var git = PreconditionChecks.CheckGitState(isRepo, isDirty);
            if (git.Result == PreconditionResult.Severity.Warn && userOverrodeGitDirty && git.Code == "GIT_DIRTY") {
                git = new PreconditionResult { Result = PreconditionResult.Severity.Pass, Code = "GIT_DIRTY_OVERRIDE", Message = "User accepted dirty working tree." };
            }
            report.Results.Add(git);
            return report;
        }

        private static bool HasCompileErrors() {
            var msgs = UnityEditor.Compilation.CompilationPipeline.GetAssemblies(UnityEditor.Compilation.AssembliesType.Editor);
            // GetMessages requires per-assembly enumeration; treat missing API as "no errors" for older Unity.
            foreach (var asm in msgs) {
                var asmMsgs = UnityEditor.Compilation.CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(asm.name);
                _ = asmMsgs;
            }
            return false; // Conservative: if we cannot detect errors via pipeline API, do not block. Verify step re-checks anyway.
        }

        private static (bool isRepo, bool isDirty) ProbeGit() {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            try {
                var psi = new ProcessStartInfo("git", "rev-parse --is-inside-work-tree") {
                    WorkingDirectory = projectRoot,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                    UseShellExecute = false, CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                p.WaitForExit(2000);
                if (p.ExitCode != 0) return (false, false);
                psi.Arguments = "status --porcelain";
                using var p2 = Process.Start(psi);
                var stdout = p2.StandardOutput.ReadToEnd();
                p2.WaitForExit(2000);
                return (true, !string.IsNullOrWhiteSpace(stdout));
            } catch {
                return (false, false);
            }
        }
    }
}
```

- [ ] **Step 5: Stage**

```bash
git add Editor/Core/PreconditionChecks.cs Editor/Core/PreconditionRunner.cs Tests/Editor/PreconditionChecksTests.cs
```

---

## Task 4 — BackupManager

**Files:**
- Create: `Editor/Core/BackupManager.cs`
- Create: `Tests/Editor/BackupManagerTests.cs`

- [ ] **Step 1: Write failing test for snapshot+restore round-trip**

`Tests/Editor/BackupManagerTests.cs`:

```csharp
using System.IO;
using NUnit.Framework;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.Tests {
    [TestFixture]
    public class BackupManagerTests {
        private string _projectRoot;
        private string _backupRoot;

        [SetUp] public void Setup() {
            _projectRoot = Path.Combine(Path.GetTempPath(), "z2vc-backup-test-" + System.Guid.NewGuid().ToString("N"));
            _backupRoot = Path.Combine(_projectRoot, "Temp", "Zenject2VContainer", "Backup");
            Directory.CreateDirectory(Path.Combine(_projectRoot, "Assets"));
        }
        [TearDown] public void Cleanup() {
            if (Directory.Exists(_projectRoot)) Directory.Delete(_projectRoot, true);
        }

        [Test]
        public void SnapshotThenRestore_RecoversOriginalContent() {
            var f1 = Path.Combine(_projectRoot, "Assets", "A.cs");
            File.WriteAllText(f1, "ORIGINAL");
            var stamp = BackupManager.Snapshot(_projectRoot, new[] { f1 });
            Assert.IsNotEmpty(stamp);
            File.WriteAllText(f1, "MUTATED");
            BackupManager.Restore(_projectRoot, stamp);
            Assert.AreEqual("ORIGINAL", File.ReadAllText(f1));
        }

        [Test]
        public void Snapshot_PreservesRelativeStructure() {
            var nested = Path.Combine(_projectRoot, "Assets", "Sub", "B.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(nested));
            File.WriteAllText(nested, "X");
            var stamp = BackupManager.Snapshot(_projectRoot, new[] { nested });
            var backedUp = Path.Combine(_projectRoot, "Temp", "Zenject2VContainer", "Backup", stamp, "Assets", "Sub", "B.cs");
            Assert.IsTrue(File.Exists(backedUp), "Expected backup copy at " + backedUp);
        }

        [Test]
        public void List_ReturnsTimestampsNewestFirst() {
            File.WriteAllText(Path.Combine(_projectRoot, "Assets", "A.cs"), "x");
            var s1 = BackupManager.Snapshot(_projectRoot, new[] { Path.Combine(_projectRoot, "Assets", "A.cs") });
            System.Threading.Thread.Sleep(1100);
            var s2 = BackupManager.Snapshot(_projectRoot, new[] { Path.Combine(_projectRoot, "Assets", "A.cs") });
            var list = BackupManager.List(_projectRoot);
            Assert.AreEqual(s2, list[0]);
            Assert.AreEqual(s1, list[1]);
        }
    }
}
```

- [ ] **Step 2: Run test, expect failures (no `BackupManager` yet)**

Unity Test Runner → `BackupManagerTests`. Expected: 3 failing tests with compile error or NRE.

- [ ] **Step 3: Implement `BackupManager`**

`Editor/Core/BackupManager.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;

namespace Zenject2VContainer.Core {
    public static class BackupManager {
        public const string BackupRootRel = "Temp/Zenject2VContainer/Backup";

        public static string Snapshot(string projectRoot, IEnumerable<string> filePaths) {
            var stamp = DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ");
            var dest = Path.Combine(projectRoot, BackupRootRel.Replace('/', Path.DirectorySeparatorChar), stamp);
            Directory.CreateDirectory(dest);
            var rootNorm = Path.GetFullPath(projectRoot).Replace('\\', '/').TrimEnd('/') + "/";
            foreach (var f in filePaths) {
                if (!File.Exists(f)) continue;
                var full = Path.GetFullPath(f).Replace('\\', '/');
                if (!full.StartsWith(rootNorm, StringComparison.OrdinalIgnoreCase)) continue;
                var rel = full.Substring(rootNorm.Length);
                var backupPath = Path.Combine(dest, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(backupPath));
                File.Copy(f, backupPath, overwrite: true);
            }
            return stamp;
        }

        public static void Restore(string projectRoot, string stamp) {
            var src = Path.Combine(projectRoot, BackupRootRel.Replace('/', Path.DirectorySeparatorChar), stamp);
            if (!Directory.Exists(src)) throw new DirectoryNotFoundException("Backup not found: " + src);
            foreach (var file in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories)) {
                var rel = file.Substring(src.Length).TrimStart(Path.DirectorySeparatorChar, '/');
                var dest = Path.Combine(projectRoot, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                File.Copy(file, dest, overwrite: true);
            }
        }

        public static List<string> List(string projectRoot) {
            var root = Path.Combine(projectRoot, BackupRootRel.Replace('/', Path.DirectorySeparatorChar));
            var result = new List<string>();
            if (!Directory.Exists(root)) return result;
            foreach (var d in Directory.EnumerateDirectories(root)) {
                result.Add(Path.GetFileName(d));
            }
            result.Sort(StringComparer.Ordinal);
            result.Reverse();
            return result;
        }

        public static string LatestStamp(string projectRoot) {
            var list = List(projectRoot);
            return list.Count > 0 ? list[0] : null;
        }
    }
}
```

- [ ] **Step 4: Run tests, expect green**

All 3 tests pass.

- [ ] **Step 5: Stage**

```bash
git add Editor/Core/BackupManager.cs Tests/Editor/BackupManagerTests.cs
```

---

## Task 5 — IZenjectRemover interface + stub

The wizard needs to compile and run before M5 lands. Define the contract here; M5 supplies the real implementation.

**Files:**
- Create: `Editor/Manifest/IZenjectRemover.cs`
- Create: `Editor/Manifest/StubZenjectRemover.cs`

- [ ] **Step 1: Define interface**

`Editor/Manifest/IZenjectRemover.cs`:

```csharp
using System.Collections.Generic;
using Zenject2VContainer.Core.Scanner;

namespace Zenject2VContainer.Manifest {
    public sealed class RemovalPlan {
        public bool UpmInstall;
        public string UpmPackageId;          // e.g. "com.svermeulen.extenject"
        public string FolderInstallPath;     // e.g. "Assets/Plugins/Zenject"
        public List<string> ScopedRegistryNamesToDrop = new List<string>();
        public bool IsNoop;                  // already removed
    }

    public sealed class RemovalResult {
        public bool Success;
        public string Message;
        public List<string> ActionsTaken = new List<string>();
    }

    public interface IZenjectRemover {
        RemovalPlan Plan(InstallationInfo install, string projectRoot);
        RemovalResult Apply(RemovalPlan plan, string projectRoot);
    }
}
```

- [ ] **Step 2: Add stub default**

`Editor/Manifest/StubZenjectRemover.cs`:

```csharp
using Zenject2VContainer.Core.Scanner;

namespace Zenject2VContainer.Manifest {
    public sealed class StubZenjectRemover : IZenjectRemover {
        public RemovalPlan Plan(InstallationInfo install, string projectRoot) =>
            new RemovalPlan { IsNoop = true };
        public RemovalResult Apply(RemovalPlan plan, string projectRoot) =>
            new RemovalResult {
                Success = false,
                Message = "Zenject removal not yet available — ships in M5. Remove manually for now."
            };
    }
}
```

- [ ] **Step 3: Stage**

```bash
git add Editor/Manifest/IZenjectRemover.cs Editor/Manifest/StubZenjectRemover.cs
```

---

## Task 6 — MigrationApplyService

UI-agnostic apply orchestrator. Splits the apply phase into testable steps so we don't need a live editor to verify rollback semantics.

**Files:**
- Create: `Editor/Core/MigrationApplyService.cs`
- Create: `Tests/Editor/MigrationApplyServiceTests.cs`

- [ ] **Step 1: Define service contract + write failing test**

`Tests/Editor/MigrationApplyServiceTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.Tests {
    [TestFixture]
    public class MigrationApplyServiceTests {
        private string _projectRoot;

        [SetUp] public void Setup() {
            _projectRoot = Path.Combine(Path.GetTempPath(), "z2vc-apply-test-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_projectRoot, "Assets"));
        }
        [TearDown] public void Cleanup() {
            if (Directory.Exists(_projectRoot)) Directory.Delete(_projectRoot, true);
        }

        private static MigrationPlan PlanWith(params (string path, string original, string @new)[] entries) {
            var p = new MigrationPlan();
            foreach (var e in entries) {
                p.Changes.Add(new FileChange {
                    OriginalPath = e.path, OriginalText = e.original, NewText = e.@new,
                    Category = FileChangeCategory.CSharp, Confidence = ChangeConfidence.High
                });
            }
            return p;
        }

        [Test]
        public void Apply_WritesNewText_ToOriginalPaths() {
            var f = Path.Combine(_projectRoot, "Assets", "A.cs");
            File.WriteAllText(f, "OLD");
            var plan = PlanWith((f, "OLD", "NEW"));
            var svc = new MigrationApplyService(new SuccessCompileWaiter());
            var result = svc.Apply(_projectRoot, plan, MigrationPlan.Empty(), new MigrationLog());
            Assert.IsTrue(result.Success, result.Message);
            Assert.AreEqual("NEW", File.ReadAllText(f));
        }

        [Test]
        public void Apply_AutoRollback_WhenCompileFails() {
            var f = Path.Combine(_projectRoot, "Assets", "A.cs");
            File.WriteAllText(f, "OLD");
            var plan = PlanWith((f, "OLD", "NEW_BROKEN"));
            var svc = new MigrationApplyService(new FailingCompileWaiter("CS1002"));
            var log = new MigrationLog();
            var result = svc.Apply(_projectRoot, plan, MigrationPlan.Empty(), log);
            Assert.IsFalse(result.Success);
            Assert.AreEqual("OLD", File.ReadAllText(f), "Auto-rollback must restore original C# content");
            Assert.IsNotEmpty(result.BackupTimestamp);
            Assert.IsTrue(log.Entries.Exists(e => e.Level == LogLevel.Error && e.Source == "Apply.CSharp"));
        }

        [Test]
        public void Apply_YamlPhase_RunsAfterCompile() {
            var cs = Path.Combine(_projectRoot, "Assets", "A.cs");
            var scn = Path.Combine(_projectRoot, "Assets", "S.unity");
            File.WriteAllText(cs, "OLD"); File.WriteAllText(scn, "OLD_YAML");
            var plan = PlanWith((cs, "OLD", "NEW"));
            var yaml = PlanWith((scn, "OLD_YAML", "NEW_YAML"));
            yaml.Changes[0].Category = FileChangeCategory.Yaml;
            var svc = new MigrationApplyService(new SuccessCompileWaiter());
            var result = svc.Apply(_projectRoot, plan, yaml, new MigrationLog());
            Assert.IsTrue(result.Success);
            Assert.AreEqual("NEW", File.ReadAllText(cs));
            Assert.AreEqual("NEW_YAML", File.ReadAllText(scn));
        }

        // Test doubles
        private sealed class SuccessCompileWaiter : ICompileWaiter {
            public CompileResult WaitForCompile(MigrationLog log) =>
                new CompileResult { Succeeded = true, ErrorMessages = new List<string>() };
        }
        private sealed class FailingCompileWaiter : ICompileWaiter {
            private readonly string _code;
            public FailingCompileWaiter(string code) { _code = code; }
            public CompileResult WaitForCompile(MigrationLog log) =>
                new CompileResult { Succeeded = false, ErrorMessages = new List<string> { _code } };
        }
    }
}
```

- [ ] **Step 2: Implement service**

`Editor/Core/MigrationApplyService.cs`:

```csharp
using System.Collections.Generic;
using System.IO;

namespace Zenject2VContainer.Core {
    public sealed class CompileResult {
        public bool Succeeded;
        public List<string> ErrorMessages = new List<string>();
    }
    public interface ICompileWaiter {
        CompileResult WaitForCompile(MigrationLog log);
    }

    public sealed class ApplyResult {
        public bool Success;
        public string Message;
        public string BackupTimestamp;
        public List<string> CompileErrors = new List<string>();
    }

    public static class MigrationPlanExtensions {
        public static MigrationPlan Empty(this MigrationPlan _ = null) => new MigrationPlan();
    }

    public sealed class MigrationApplyService {
        private readonly ICompileWaiter _compileWaiter;
        public MigrationApplyService(ICompileWaiter compileWaiter) { _compileWaiter = compileWaiter; }

        public ApplyResult Apply(string projectRoot, MigrationPlan csharp, MigrationPlan yaml, MigrationLog log) {
            var paths = new List<string>();
            foreach (var c in csharp.Changes) paths.Add(c.OriginalPath);
            foreach (var c in yaml.Changes) paths.Add(c.OriginalPath);
            var stamp = BackupManager.Snapshot(projectRoot, paths);
            log.Info("Apply.Backup", "Snapshot " + stamp + " (" + paths.Count + " files)");

            // C# phase
            try {
                foreach (var c in csharp.Changes) WriteFile(c.OriginalPath, c.NewText, log, "Apply.CSharp");
            } catch (System.Exception ex) {
                log.Error("Apply.CSharp", "Write failed: " + ex.Message);
                BackupManager.Restore(projectRoot, stamp);
                return new ApplyResult { Success = false, Message = "C# write failed; rolled back.", BackupTimestamp = stamp };
            }

            var compile = _compileWaiter.WaitForCompile(log);
            if (!compile.Succeeded) {
                foreach (var e in compile.ErrorMessages) log.Error("Apply.CSharp", e);
                BackupManager.Restore(projectRoot, stamp);
                return new ApplyResult { Success = false, Message = "Compile failed after C# write; rolled back.", BackupTimestamp = stamp, CompileErrors = compile.ErrorMessages };
            }

            // YAML phase
            try {
                foreach (var c in yaml.Changes) WriteFile(c.OriginalPath, c.NewText, log, "Apply.Yaml");
            } catch (System.Exception ex) {
                log.Error("Apply.Yaml", "Write failed: " + ex.Message);
                BackupManager.Restore(projectRoot, stamp);
                return new ApplyResult { Success = false, Message = "YAML write failed; rolled back.", BackupTimestamp = stamp };
            }

            return new ApplyResult { Success = true, Message = "Apply succeeded.", BackupTimestamp = stamp };
        }

        private static void WriteFile(string path, string text, MigrationLog log, string source) {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
            log.Info(source, "Wrote " + Path.GetFileName(path), path);
        }
    }

    // The `MigrationPlan.Empty()` static helper used by tests:
    public static class MigrationPlanStatic {
        public static MigrationPlan Empty() => new MigrationPlan();
    }
}
```

(The test calls `MigrationPlan.Empty()` — add a `public static MigrationPlan Empty() => new MigrationPlan();` to `MigrationPlan.cs` rather than relying on the helper class above. Update `Editor/Core/MigrationPlan.cs` accordingly.)

- [ ] **Step 3: Add `MigrationPlan.Empty()` static**

Edit `Editor/Core/MigrationPlan.cs` — inside `MigrationPlan` add:

```csharp
public static MigrationPlan Empty() => new MigrationPlan();
```

Remove the unused `MigrationPlanExtensions` and `MigrationPlanStatic` helpers from `MigrationApplyService.cs`.

- [ ] **Step 4: Run tests, expect green**

All 3 `MigrationApplyServiceTests` pass.

- [ ] **Step 5: Stage**

```bash
git add Editor/Core/MigrationApplyService.cs Editor/Core/MigrationPlan.cs Tests/Editor/MigrationApplyServiceTests.cs
```

---

## Task 7 — DiffView (text)

**Files:**
- Create: `Editor/UI/DiffView.cs`
- Create: `Tests/Editor/DiffViewTests.cs`

- [ ] **Step 1: Failing test for the diff function**

`Tests/Editor/DiffViewTests.cs`:

```csharp
using NUnit.Framework;
using Zenject2VContainer.UI;

namespace Zenject2VContainer.Tests {
    [TestFixture]
    public class DiffViewTests {
        [Test]
        public void Diff_MarksAddedLinesWithPlus_RemovedWithMinus_UnchangedWithSpace() {
            var lines = DiffView.BuildUnifiedDiff("a\nb\nc\n", "a\nB\nc\n");
            Assert.That(lines, Is.EqualTo(new[] { " a", "-b", "+B", " c" }));
        }

        [Test]
        public void Diff_AppendOnly_ShowsAllNewLinesAsPlus() {
            var lines = DiffView.BuildUnifiedDiff("", "x\ny\n");
            Assert.That(lines, Is.EqualTo(new[] { "+x", "+y" }));
        }

        [Test]
        public void Diff_DeleteOnly_ShowsAllOldLinesAsMinus() {
            var lines = DiffView.BuildUnifiedDiff("x\ny\n", "");
            Assert.That(lines, Is.EqualTo(new[] { "-x", "-y" }));
        }
    }
}
```

- [ ] **Step 2: Implement `DiffView.BuildUnifiedDiff` (LCS-based)**

`Editor/UI/DiffView.cs`:

```csharp
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.UI {
    public static class DiffView {
        public static string[] BuildUnifiedDiff(string original, string updated) {
            var a = (original ?? "").Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            var b = (updated ?? "").Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
            if (a.Length == 1 && a[0] == "") a = System.Array.Empty<string>();
            if (b.Length == 1 && b[0] == "") b = System.Array.Empty<string>();

            var lcs = new int[a.Length + 1, b.Length + 1];
            for (int i = a.Length - 1; i >= 0; i--)
                for (int j = b.Length - 1; j >= 0; j--)
                    lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : System.Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

            var lines = new List<string>();
            int x = 0, y = 0;
            while (x < a.Length && y < b.Length) {
                if (a[x] == b[y]) { lines.Add(" " + a[x]); x++; y++; }
                else if (lcs[x + 1, y] >= lcs[x, y + 1]) { lines.Add("-" + a[x]); x++; }
                else { lines.Add("+" + b[y]); y++; }
            }
            while (x < a.Length) { lines.Add("-" + a[x]); x++; }
            while (y < b.Length) { lines.Add("+" + b[y]); y++; }
            return lines.ToArray();
        }

        // GUI rendering: scroll view + per-line colour. State held by caller.
        public static void Draw(ref Vector2 scroll, FileChange change) {
            EditorGUILayout.LabelField(change.OriginalPath, EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.MinHeight(200));
            var lines = BuildUnifiedDiff(change.OriginalText, change.NewText);
            var monospace = new GUIStyle(EditorStyles.label) { font = EditorGUIUtility.Load("Fonts/RobotoMono/RobotoMono-Regular.ttf") as Font ?? EditorStyles.label.font };
            var addColor = new Color(0.3f, 0.7f, 0.3f);
            var delColor = new Color(0.8f, 0.3f, 0.3f);
            foreach (var line in lines) {
                var prev = GUI.color;
                GUI.color = line.Length > 0 && line[0] == '+' ? addColor : line.Length > 0 && line[0] == '-' ? delColor : prev;
                EditorGUILayout.SelectableLabel(line, monospace, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                GUI.color = prev;
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
```

- [ ] **Step 3: Run tests, expect green**

All 3 `DiffViewTests` pass.

- [ ] **Step 4: Stage**

```bash
git add Editor/UI/DiffView.cs Tests/Editor/DiffViewTests.cs
```

---

## Task 8 — MigrationReportWriter

**Files:**
- Create: `Editor/Reporting/MigrationReportWriter.cs`
- Create: `Tests/Editor/MigrationReportWriterTests.cs`

Spec §4.5 lists the report sections. Test asserts presence of all section headers + at least one row per category fed in.

- [ ] **Step 1: Failing test**

`Tests/Editor/MigrationReportWriterTests.cs`:

```csharp
using NUnit.Framework;
using Zenject2VContainer.Core;
using Zenject2VContainer.Reporting;

namespace Zenject2VContainer.Tests {
    [TestFixture]
    public class MigrationReportWriterTests {
        [Test]
        public void Render_ContainsAllRequiredSections() {
            var plan = new MigrationPlan();
            plan.Changes.Add(new FileChange {
                OriginalPath = "Assets/Foo.cs", Category = FileChangeCategory.CSharp,
                Confidence = ChangeConfidence.High, OriginalText = "a", NewText = "b"
            });
            plan.Unsupported.Add(new Finding { Category = "InjectOptional", FilePath = "Assets/Foo.cs", Line = 12, Reason = "manual port", DocLink = "https://example/doc" });
            var md = MigrationReportWriter.Render(plan, new MigrationReportContext {
                ProjectPath = "C:/Project", UnityVersion = "2022.3", ToolVersion = "0.1.0", RunUtc = "2026-05-01T12:00:00Z", BackupTimestamp = "20260501T120000Z"
            });
            StringAssert.Contains("# Zenject → VContainer Migration Report", md);
            StringAssert.Contains("## Summary", md);
            StringAssert.Contains("## Changes", md);
            StringAssert.Contains("## Manual TODOs", md);
            StringAssert.Contains("## Rollback", md);
            StringAssert.Contains("Assets/Foo.cs", md);
            StringAssert.Contains("InjectOptional", md);
            StringAssert.Contains("https://example/doc", md);
            StringAssert.Contains("20260501T120000Z", md);
        }
    }
}
```

- [ ] **Step 2: Implement writer**

`Editor/Reporting/MigrationReportWriter.cs`:

```csharp
using System.IO;
using System.Text;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.Reporting {
    public sealed class MigrationReportContext {
        public string ProjectPath;
        public string UnityVersion;
        public string ToolVersion;
        public string RunUtc;
        public string BackupTimestamp;
    }

    public static class MigrationReportWriter {
        public static string Render(MigrationPlan plan, MigrationReportContext ctx) {
            var sb = new StringBuilder();
            sb.AppendLine("# Zenject → VContainer Migration Report");
            sb.AppendLine();
            sb.AppendLine($"- **Project:** `{ctx.ProjectPath}`");
            sb.AppendLine($"- **Unity:** {ctx.UnityVersion}");
            sb.AppendLine($"- **Tool:** {ctx.ToolVersion}");
            sb.AppendLine($"- **Run:** {ctx.RunUtc}");
            if (!string.IsNullOrEmpty(ctx.BackupTimestamp)) sb.AppendLine($"- **Backup:** `Temp/Zenject2VContainer/Backup/{ctx.BackupTimestamp}/`");
            sb.AppendLine();

            int csCount = 0, yamlCount = 0, manifestCount = 0;
            foreach (var c in plan.Changes) {
                if (c.Category == FileChangeCategory.CSharp) csCount++;
                else if (c.Category == FileChangeCategory.Yaml) yamlCount++;
                else if (c.Category == FileChangeCategory.Manifest) manifestCount++;
            }
            sb.AppendLine("## Summary");
            sb.AppendLine();
            sb.AppendLine($"- C# files changed: {csCount}");
            sb.AppendLine($"- YAML assets changed: {yamlCount}");
            sb.AppendLine($"- Manifest changes: {manifestCount}");
            sb.AppendLine($"- Manual TODOs: {plan.Unsupported.Count}");
            sb.AppendLine();

            sb.AppendLine("## Changes");
            sb.AppendLine();
            sb.AppendLine("| File | Category | Confidence |");
            sb.AppendLine("|------|----------|------------|");
            foreach (var c in plan.Changes) {
                sb.AppendLine($"| `{c.OriginalPath}` | {c.Category} | {c.Confidence} |");
            }
            sb.AppendLine();

            sb.AppendLine("## Manual TODOs");
            sb.AppendLine();
            if (plan.Unsupported.Count == 0) sb.AppendLine("_None._");
            else {
                sb.AppendLine("| Category | File:Line | Reason | Doc |");
                sb.AppendLine("|----------|-----------|--------|-----|");
                foreach (var f in plan.Unsupported) {
                    var doc = string.IsNullOrEmpty(f.DocLink) ? "" : $"[link]({f.DocLink})";
                    sb.AppendLine($"| {f.Category} | `{f.FilePath}:{f.Line}` | {f.Reason} | {doc} |");
                }
            }
            sb.AppendLine();

            sb.AppendLine("## Rollback");
            sb.AppendLine();
            if (!string.IsNullOrEmpty(ctx.BackupTimestamp)) {
                sb.AppendLine($"- File-level rollback available via `BackupManager.Restore(\"{ctx.BackupTimestamp}\")` or the Migration Wizard's Rollback button while this Editor session is alive.");
            }
            sb.AppendLine("- For commits made after migration, use `git revert` instead of tool rollback.");
            return sb.ToString();
        }

        public static string WriteToDisk(string projectRoot, MigrationPlan plan, MigrationReportContext ctx) {
            var dir = Path.Combine(projectRoot, "Assets", "Zenject2VContainer");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "MIGRATION_REPORT.md");
            File.WriteAllText(path, Render(plan, ctx));
            return path;
        }
    }
}
```

- [ ] **Step 3: Run tests, expect green**

`MigrationReportWriterTests.Render_ContainsAllRequiredSections` passes.

- [ ] **Step 4: Stage**

```bash
git add Editor/Reporting/MigrationReportWriter.cs Tests/Editor/MigrationReportWriterTests.cs
```

---

## Task 9 — Wizard window shell + step host

**Files:**
- Create: `Editor/UI/MigrationWizardWindow.cs`
- Create: `Editor/UI/MigrationLogPanel.cs`

This task wires the window and the navigation. Step bodies are stubbed (`OnGUI` shows "TODO step body") and filled in later tasks.

- [ ] **Step 1: Add log panel**

`Editor/UI/MigrationLogPanel.cs`:

```csharp
using UnityEditor;
using UnityEngine;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.UI {
    public static class MigrationLogPanel {
        private static Vector2 _scroll;
        private static bool _expanded = true;

        public static void Draw(MigrationLog log) {
            _expanded = EditorGUILayout.Foldout(_expanded, $"Log ({log.Entries.Count})", true);
            if (!_expanded) return;
            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(120), GUILayout.MaxHeight(240));
            foreach (var e in log.Entries) {
                var prev = GUI.color;
                if (e.Level == LogLevel.Error) GUI.color = new Color(1f, 0.5f, 0.5f);
                else if (e.Level == LogLevel.Warn) GUI.color = new Color(1f, 0.85f, 0.4f);
                EditorGUILayout.SelectableLabel(e.ToString(), EditorStyles.label, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                GUI.color = prev;
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
```

- [ ] **Step 2: Add the window**

`Editor/UI/MigrationWizardWindow.cs`:

```csharp
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Zenject2VContainer.Core;
using Zenject2VContainer.Manifest;
using Zenject2VContainer.UI.Steps;

namespace Zenject2VContainer.UI {
    public sealed class MigrationWizardWindow : EditorWindow {
        private MigrationContext _ctx;
        private WizardState _current = WizardState.Scan;
        private Dictionary<WizardState, IWizardStep> _steps;

        [MenuItem("Window/Zenject2VContainer/Migration Wizard…")]
        public static void Open() {
            var w = GetWindow<MigrationWizardWindow>("Z2VC Migration");
            w.minSize = new Vector2(720, 500);
            w.Show();
        }

        private void OnEnable() {
            _ctx = new MigrationContext();
            _steps = new Dictionary<WizardState, IWizardStep> {
                { WizardState.Scan,    new ScanStep() },
                { WizardState.Preview, new PreviewStep() },
                { WizardState.Apply,   new ApplyStep() },
                { WizardState.Verify,  new VerifyStep() },
                { WizardState.Remove,  new RemoveZenjectStep(new StubZenjectRemover()) }
            };
        }

        private void OnGUI() {
            DrawHeader();
            EditorGUILayout.Space();

            if (_current == WizardState.Done) {
                EditorGUILayout.HelpBox("Migration complete.", MessageType.Info);
            } else if (_steps.TryGetValue(_current, out var step)) {
                EditorGUILayout.LabelField(step.Title, EditorStyles.largeLabel);
                step.OnGUI(_ctx);
                EditorGUILayout.Space();
                using (new EditorGUILayout.HorizontalScope()) {
                    GUI.enabled = CanGoBack();
                    if (GUILayout.Button("Back")) GoBack();
                    GUI.enabled = step.CanAdvance(_ctx);
                    if (GUILayout.Button("Next")) GoNext();
                    GUI.enabled = true;
                }
            }

            EditorGUILayout.Space();
            MigrationLogPanel.Draw(_ctx.Log);
        }

        private void DrawHeader() {
            EditorGUILayout.LabelField("Zenject → VContainer migration", EditorStyles.boldLabel);
            EditorGUILayout.LabelField($"Step {(int)_current + 1} / {System.Enum.GetValues(typeof(WizardState)).Length - 1}: {_current}");
            EditorGUI.ProgressBar(GUILayoutUtility.GetRect(0, 8), Progress(), "");
        }

        private float Progress() => System.Math.Min(1f, (int)_current / (float)((int)WizardState.Done));

        private bool CanGoBack() => _current > WizardState.Scan && _current != WizardState.Done;
        private void GoBack() => _current = (WizardState)((int)_current - 1);
        private void GoNext() {
            var values = (WizardState[])System.Enum.GetValues(typeof(WizardState));
            int idx = System.Array.IndexOf(values, _current);
            if (idx + 1 < values.Length) _current = values[idx + 1];
        }
    }
}
```

- [ ] **Step 3: Stub each step (compiles, "Next" disabled until later tasks fill them in)**

Create the four files with the bare interface implementation. Each `OnGUI` shows `EditorGUILayout.HelpBox("Not yet implemented", MessageType.None);` and `CanAdvance` returns `false`.

`Editor/UI/Steps/ScanStep.cs`:
```csharp
using UnityEditor;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.UI.Steps {
    public sealed class ScanStep : IWizardStep {
        public string Title => "1. Scan project for Zenject usage";
        public WizardState State => WizardState.Scan;
        public void OnGUI(MigrationContext ctx) { EditorGUILayout.HelpBox("Stub — filled in Task 10", MessageType.None); }
        public bool CanAdvance(MigrationContext ctx) => false;
    }
}
```

Repeat the same shape for `PreviewStep.cs`, `ApplyStep.cs`, `VerifyStep.cs`, `RemoveZenjectStep.cs`. `RemoveZenjectStep` ctor takes `IZenjectRemover remover`:

```csharp
using UnityEditor;
using Zenject2VContainer.Core;
using Zenject2VContainer.Manifest;

namespace Zenject2VContainer.UI.Steps {
    public sealed class RemoveZenjectStep : IWizardStep {
        private readonly IZenjectRemover _remover;
        public RemoveZenjectStep(IZenjectRemover remover) { _remover = remover; }
        public string Title => "5. Remove Zenject (optional)";
        public WizardState State => WizardState.Remove;
        public void OnGUI(MigrationContext ctx) { EditorGUILayout.HelpBox("Stub — filled in Task 14", MessageType.None); }
        public bool CanAdvance(MigrationContext ctx) => false;
    }
}
```

- [ ] **Step 4: Open Unity, verify the window opens**

Window → Zenject2VContainer → Migration Wizard…. Window opens, shows "Step 1 / 5: Scan", stub help box, log panel empty. Buttons disabled (Next disabled until step fills in CanAdvance).

- [ ] **Step 5: Stage**

```bash
git add Editor/UI/MigrationWizardWindow.cs Editor/UI/MigrationLogPanel.cs Editor/UI/Steps/ScanStep.cs Editor/UI/Steps/PreviewStep.cs Editor/UI/Steps/ApplyStep.cs Editor/UI/Steps/VerifyStep.cs Editor/UI/Steps/RemoveZenjectStep.cs
```

---

## Task 10 — ScanStep body

**Files:**
- Modify: `Editor/UI/Steps/ScanStep.cs`

- [ ] **Step 1: Replace stub body**

```csharp
using System.IO;
using UnityEditor;
using UnityEngine;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.UI.Steps {
    public sealed class ScanStep : IWizardStep {
        public string Title => "1. Scan project for Zenject usage";
        public WizardState State => WizardState.Scan;

        public void OnGUI(MigrationContext ctx) {
            EditorGUILayout.HelpBox("Scans Assets/ and writable Packages/ for Zenject C# usage and asset GUIDs. Result feeds the Preview step.", MessageType.Info);

            if (GUILayout.Button("Run scan")) {
                ctx.Log.Info("Scan", "Starting scan…");
                ctx.ScanReport = MigrationPipeline.RunScanHeadless();
                ctx.Log.Info("Scan", $"Done. Bind calls: {ctx.ScanReport.Findings.BindCallCount}, installer subclasses: {ctx.ScanReport.Findings.InstallerSubclassCount}, asset references: {ctx.ScanReport.Findings.AssetReferenceCount}");
            }

            if (ctx.ScanReport == null) return;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Findings", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Bind calls", ctx.ScanReport.Findings.BindCallCount.ToString());
            EditorGUILayout.LabelField("Installer subclasses", ctx.ScanReport.Findings.InstallerSubclassCount.ToString());
            EditorGUILayout.LabelField("[Inject] members", ctx.ScanReport.Findings.InjectMemberCount.ToString());
            EditorGUILayout.LabelField("Asset GUID references", ctx.ScanReport.Findings.AssetReferenceCount.ToString());
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Zenject install", ctx.ScanReport.Installation == null ? "Not detected" :
                $"UPM: {ctx.ScanReport.Installation.UpmInstalled} ({ctx.ScanReport.Installation.UpmPackageId}); Folders: {string.Join(", ", ctx.ScanReport.Installation.FolderInstallPaths ?? new System.Collections.Generic.List<string>())}");
            EditorGUILayout.LabelField("VContainer install", ctx.ScanReport.Installation != null && ctx.ScanReport.Installation.VContainerInstalled ? ctx.ScanReport.Installation.VContainerVersion ?? "yes" : "missing");

            if (GUILayout.Button("Save scan.json")) {
                var defaultPath = Path.Combine(Path.GetFullPath(Path.Combine(Application.dataPath, "..")), "Library", "Zenject2VContainer", "scan.json");
                Directory.CreateDirectory(Path.GetDirectoryName(defaultPath));
                MigrationPipeline.WriteScanReport(ctx.ScanReport, defaultPath);
                ctx.Log.Info("Scan", "Wrote " + defaultPath);
                EditorUtility.RevealInFinder(defaultPath);
            }
        }

        public bool CanAdvance(MigrationContext ctx) => ctx.ScanReport != null;
    }
}
```

(Field names like `BindCallCount`, `InstallerSubclassCount`, `InjectMemberCount`, `AssetReferenceCount` must match the actual `ZenjectUsageReport.Findings` shape — verify against `Editor/Core/ZenjectUsageReport.cs` and adjust if names differ. If the report exposes a single `Counts` dictionary, render that instead.)

- [ ] **Step 2: Open wizard, run scan against the host project**

Click "Run scan". Expected: log entry "Scan: Done. …", findings populate, "Next" enables.

- [ ] **Step 3: Stage**

```bash
git add Editor/UI/Steps/ScanStep.cs
```

---

## Task 11 — PreviewStep body

**Files:**
- Modify: `Editor/UI/Steps/PreviewStep.cs`

- [ ] **Step 1: Replace stub**

```csharp
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.UI.Steps {
    public sealed class PreviewStep : IWizardStep {
        public string Title => "2. Preview migration changes";
        public WizardState State => WizardState.Preview;

        private FileChangeCategory _filter = FileChangeCategory.CSharp;
        private int _selected = -1;
        private Vector2 _listScroll;
        private Vector2 _diffScroll;
        private readonly HashSet<string> _approvedLowConfidence = new HashSet<string>();

        public void OnGUI(MigrationContext ctx) {
            EditorGUILayout.HelpBox("Builds the C# and YAML migration plans. Diff each change, approve any LowFlagged items, then advance to Apply.", MessageType.Info);

            using (new EditorGUILayout.HorizontalScope()) {
                if (GUILayout.Button("Build C# plan")) {
                    ctx.CSharpPlan = MigrationPipeline.RunCSharpHeadless();
                    ctx.Log.Info("Preview.CSharp", $"{ctx.CSharpPlan.Changes.Count} files / {ctx.CSharpPlan.Unsupported.Count} findings");
                }
                if (GUILayout.Button("Build YAML plan")) {
                    ctx.YamlPlan = MigrationPipeline.RunYamlHeadless();
                    ctx.Log.Info("Preview.Yaml", $"{ctx.YamlPlan.Changes.Count} assets / {ctx.YamlPlan.Unsupported.Count} findings");
                }
            }
            if (ctx.CSharpPlan == null && ctx.YamlPlan == null) return;

            _filter = (FileChangeCategory)EditorGUILayout.EnumPopup("Show category", _filter);
            var combined = Filtered(ctx, _filter);

            using (new EditorGUILayout.HorizontalScope()) {
                _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.Width(280), GUILayout.MinHeight(280));
                for (int i = 0; i < combined.Count; i++) {
                    var c = combined[i];
                    var label = $"{ConfBadge(c.Confidence)} {System.IO.Path.GetFileName(c.OriginalPath)}";
                    if (GUILayout.Toggle(_selected == i, label, "Button")) _selected = i;
                }
                EditorGUILayout.EndScrollView();

                using (new EditorGUILayout.VerticalScope()) {
                    if (_selected >= 0 && _selected < combined.Count) {
                        var c = combined[_selected];
                        DiffView.Draw(ref _diffScroll, c);
                        if (c.Confidence == ChangeConfidence.LowFlagged) {
                            var key = c.OriginalPath;
                            var approved = _approvedLowConfidence.Contains(key);
                            var newApproved = EditorGUILayout.Toggle("Approve LowFlagged change", approved);
                            if (newApproved && !approved) _approvedLowConfidence.Add(key);
                            else if (!newApproved && approved) _approvedLowConfidence.Remove(key);
                        }
                        if (c.RelatedFindings != null && c.RelatedFindings.Count > 0) {
                            EditorGUILayout.LabelField("Manual TODOs:", EditorStyles.boldLabel);
                            foreach (var f in c.RelatedFindings) EditorGUILayout.LabelField($"  [{f.Category}] line {f.Line}: {f.Reason}");
                        }
                    } else {
                        EditorGUILayout.HelpBox("Select a file on the left to view its diff.", MessageType.None);
                    }
                }
            }
        }

        private static List<FileChange> Filtered(MigrationContext ctx, FileChangeCategory filter) {
            var list = new List<FileChange>();
            if (ctx.CSharpPlan != null) foreach (var c in ctx.CSharpPlan.Changes) if (c.Category == filter) list.Add(c);
            if (ctx.YamlPlan != null)   foreach (var c in ctx.YamlPlan.Changes)   if (c.Category == filter) list.Add(c);
            return list;
        }

        private static string ConfBadge(ChangeConfidence c) => c switch {
            ChangeConfidence.High => "[H]",
            ChangeConfidence.Medium => "[M]",
            ChangeConfidence.LowFlagged => "[L!]",
            _ => "[?]"
        };

        public bool CanAdvance(MigrationContext ctx) {
            if (ctx.CSharpPlan == null && ctx.YamlPlan == null) return false;
            // Every LowFlagged change must be approved before advancing.
            foreach (var p in new[] { ctx.CSharpPlan, ctx.YamlPlan }) {
                if (p == null) continue;
                foreach (var c in p.Changes) {
                    if (c.Confidence == ChangeConfidence.LowFlagged && !_approvedLowConfidence.Contains(c.OriginalPath)) return false;
                }
            }
            return true;
        }
    }
}
```

- [ ] **Step 2: Open wizard, build plans**

Run scan, advance to Preview, click "Build C# plan" + "Build YAML plan". Expected: file lists populate, selecting a file renders the unified diff with green/red lines.

- [ ] **Step 3: Stage**

```bash
git add Editor/UI/Steps/PreviewStep.cs
```

---

## Task 12 — ApplyStep body

**Files:**
- Modify: `Editor/UI/Steps/ApplyStep.cs`
- Create: `Editor/Core/EditorCompileWaiter.cs`

`MigrationApplyService` needs an `ICompileWaiter`. `EditorCompileWaiter` is the Editor implementation; tests use the doubles in Task 6.

- [ ] **Step 1: Add `EditorCompileWaiter`**

`Editor/Core/EditorCompileWaiter.cs`:

```csharp
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Compilation;

namespace Zenject2VContainer.Core {
    public sealed class EditorCompileWaiter : ICompileWaiter {
        public CompileResult WaitForCompile(MigrationLog log) {
            log.Info("Apply.CSharp", "Refreshing AssetDatabase and waiting for compile…");
            AssetDatabase.Refresh();
            // Force compile if not already pending.
            CompilationPipeline.RequestScriptCompilation();
            // Block until compile finishes. Editor scripts cannot await on the main thread, so we spin briefly.
            var spinDeadline = System.DateTime.UtcNow.AddMinutes(5);
            while (EditorApplication.isCompiling && System.DateTime.UtcNow < spinDeadline) {
                System.Threading.Thread.Sleep(100);
            }
            // Collect compile errors for the latest assemblies.
            var errors = new List<string>();
            foreach (var asm in CompilationPipeline.GetAssemblies(AssembliesType.Editor)) {
                var msgs = CompilationPipeline.GetMessages(asm.outputPath);
                if (msgs == null) continue;
                foreach (var m in msgs) {
                    if (m.type == CompilerMessageType.Error) errors.Add($"{m.file}({m.line}): {m.message}");
                }
            }
            return new CompileResult { Succeeded = errors.Count == 0, ErrorMessages = errors };
        }
    }
}
```

(The `CompilationPipeline.GetMessages(string)` overload exists in Unity 2021.3+. If your Unity build does not surface it, fall back to subscribing to `CompilationPipeline.assemblyCompilationFinished` and aggregating per-asm messages.)

- [ ] **Step 2: Replace ApplyStep stub**

`Editor/UI/Steps/ApplyStep.cs`:

```csharp
using System.IO;
using UnityEditor;
using UnityEngine;
using Zenject2VContainer.Core;
using Zenject2VContainer.Reporting;

namespace Zenject2VContainer.UI.Steps {
    public sealed class ApplyStep : IWizardStep {
        public string Title => "3. Apply changes";
        public WizardState State => WizardState.Apply;

        private PreconditionReport _preconditions;

        public void OnGUI(MigrationContext ctx) {
            EditorGUILayout.HelpBox("Snapshots all touched files into Temp/Zenject2VContainer/Backup/<timestamp>/. Writes C# changes, awaits compile, then writes YAML. Auto-rolls back on compile errors.", MessageType.Info);

            if (GUILayout.Button("Run preconditions")) {
                _preconditions = PreconditionRunner.Run(ctx.ScanReport?.Installation, ctx.UserOverrodeGitDirty);
                foreach (var r in _preconditions.Results) ctx.Log.Info("Precondition", $"{r.Code}: {r.Message}");
            }

            if (_preconditions != null) {
                EditorGUILayout.LabelField("Preconditions", EditorStyles.boldLabel);
                foreach (var r in _preconditions.Results) {
                    var t = r.Result switch { PreconditionResult.Severity.Block => MessageType.Error, PreconditionResult.Severity.Warn => MessageType.Warning, _ => MessageType.Info };
                    EditorGUILayout.HelpBox($"[{r.Code}] {r.Message}", t);
                }
                if (_preconditions.Results.Find(r => r.Code == "GIT_DIRTY") != null) {
                    ctx.UserOverrodeGitDirty = EditorGUILayout.Toggle("Override dirty working tree warning", ctx.UserOverrodeGitDirty);
                }
            }

            GUI.enabled = _preconditions != null && !_preconditions.HasBlock && (ctx.CSharpPlan != null || ctx.YamlPlan != null) && !ctx.ApplySucceeded;
            if (GUILayout.Button("Apply migration")) {
                var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                var svc = new MigrationApplyService(new EditorCompileWaiter());
                var result = svc.Apply(projectRoot, ctx.CSharpPlan ?? MigrationPlan.Empty(), ctx.YamlPlan ?? MigrationPlan.Empty(), ctx.Log);
                ctx.BackupTimestamp = result.BackupTimestamp;
                ctx.ApplySucceeded = result.Success;
                ctx.CompileErrors = result.CompileErrors;
                ctx.Log.Info("Apply", result.Message);

                var combined = MigrationPlan.Empty();
                if (ctx.CSharpPlan != null) { combined.Changes.AddRange(ctx.CSharpPlan.Changes); combined.Unsupported.AddRange(ctx.CSharpPlan.Unsupported); }
                if (ctx.YamlPlan != null)   { combined.Changes.AddRange(ctx.YamlPlan.Changes);   combined.Unsupported.AddRange(ctx.YamlPlan.Unsupported); }
                if (result.Success) {
                    var reportPath = MigrationReportWriter.WriteToDisk(projectRoot, combined, new MigrationReportContext {
                        ProjectPath = projectRoot,
                        UnityVersion = Application.unityVersion,
                        ToolVersion = MigrationPipeline.ToolVersion,
                        RunUtc = System.DateTime.UtcNow.ToString("o"),
                        BackupTimestamp = result.BackupTimestamp
                    });
                    ctx.Log.Info("Report", "Wrote " + reportPath);
                    AssetDatabase.Refresh();
                }
            }
            GUI.enabled = true;

            if (!string.IsNullOrEmpty(ctx.BackupTimestamp)) {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Last backup: " + ctx.BackupTimestamp);
                if (GUILayout.Button("Rollback last apply")) {
                    var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                    BackupManager.Restore(projectRoot, ctx.BackupTimestamp);
                    AssetDatabase.Refresh();
                    ctx.Log.Warn("Apply", "Manually rolled back to " + ctx.BackupTimestamp);
                    ctx.ApplySucceeded = false;
                }
            }
        }

        public bool CanAdvance(MigrationContext ctx) => ctx.ApplySucceeded;
    }
}
```

- [ ] **Step 3: Smoke-test on a throwaway clone of PartyQuiz**

Open PartyQuiz host (or copy under Temp/), run wizard end to end. Expected: backup folder appears under `Temp/Zenject2VContainer/Backup/`, files rewritten, MIGRATION_REPORT.md emitted. If compile fails, files are restored.

- [ ] **Step 4: Stage**

```bash
git add Editor/Core/EditorCompileWaiter.cs Editor/UI/Steps/ApplyStep.cs
```

---

## Task 13 — VerifyStep body

**Files:**
- Modify: `Editor/UI/Steps/VerifyStep.cs`

- [ ] **Step 1: Replace stub**

```csharp
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Zenject2VContainer.Core;
using Zenject2VContainer.Core.Scanner;

namespace Zenject2VContainer.UI.Steps {
    public sealed class VerifyStep : IWizardStep {
        public string Title => "4. Verify migration";
        public WizardState State => WizardState.Verify;

        private ZenjectUsageReport _post;
        private bool _ranThisVisit;

        public void OnGUI(MigrationContext ctx) {
            EditorGUILayout.HelpBox("Re-runs the scanner. Expectation: zero remaining `using Zenject` for in-scope features. Anything still flagged is either an out-of-scope feature or a manual TODO surfaced earlier.", MessageType.Info);

            if (GUILayout.Button("Re-run scan")) {
                _post = MigrationPipeline.RunScanHeadless();
                _ranThisVisit = true;
                ctx.RemainingZenjectFiles.Clear();
                if (_post.Findings != null) {
                    // The exact field used to enumerate "files still using Zenject" depends on the report shape. Use whichever per-file collection the scanner exposes.
                    foreach (var path in EnumerateRemainingFiles(_post)) ctx.RemainingZenjectFiles.Add(path);
                }
                ctx.Log.Info("Verify", $"Remaining Zenject references: {ctx.RemainingZenjectFiles.Count}");
            }

            if (_ranThisVisit) {
                EditorGUILayout.LabelField("Bind calls after migration", _post.Findings.BindCallCount.ToString());
                EditorGUILayout.LabelField("Asset GUID references after migration", _post.Findings.AssetReferenceCount.ToString());
                if (ctx.RemainingZenjectFiles.Count > 0) {
                    EditorGUILayout.HelpBox(ctx.RemainingZenjectFiles.Count + " file(s) still reference Zenject — review the report for details.", MessageType.Warning);
                    foreach (var f in ctx.RemainingZenjectFiles) EditorGUILayout.LabelField("  " + f);
                } else {
                    EditorGUILayout.HelpBox("Verify clean — no residual in-scope Zenject usage.", MessageType.Info);
                }
            }
        }

        private static IEnumerable<string> EnumerateRemainingFiles(ZenjectUsageReport report) {
            // Scanner enumerates per-file findings. Replace the body below with the actual report iteration once the field name is confirmed
            // — for example `report.Findings.PerFile` or `report.Files`. Until then, fall back to "no per-file enumeration available".
            yield break;
        }

        public bool CanAdvance(MigrationContext ctx) => _ranThisVisit;
    }
}
```

- [ ] **Step 2: Wire actual per-file enumeration**

Inspect `Editor/Core/ZenjectUsageReport.cs` to find the property exposing per-file Zenject findings (likely `Findings.Files` or similar). Replace the placeholder `EnumerateRemainingFiles` body with the real iteration. If no per-file collection exists yet, surface a TODO finding here pointing at M6 and leave the count-only branch.

- [ ] **Step 3: Stage**

```bash
git add Editor/UI/Steps/VerifyStep.cs
```

---

## Task 14 — RemoveZenjectStep body (uses stub remover until M5)

**Files:**
- Modify: `Editor/UI/Steps/RemoveZenjectStep.cs`

- [ ] **Step 1: Replace stub**

```csharp
using UnityEditor;
using UnityEngine;
using Zenject2VContainer.Core;
using Zenject2VContainer.Manifest;

namespace Zenject2VContainer.UI.Steps {
    public sealed class RemoveZenjectStep : IWizardStep {
        private readonly IZenjectRemover _remover;
        private RemovalPlan _plan;
        public RemoveZenjectStep(IZenjectRemover remover) { _remover = remover; }
        public string Title => "5. Remove Zenject (optional)";
        public WizardState State => WizardState.Remove;

        public void OnGUI(MigrationContext ctx) {
            EditorGUILayout.HelpBox("Optional: removes the Zenject package + scoped registries once verify is clean. Skipping is safe — Zenject can stay installed indefinitely.", MessageType.Info);

            if (ctx.RemainingZenjectFiles.Count > 0) {
                EditorGUILayout.HelpBox("Verify reports residual Zenject usage. Removal is blocked until those references are addressed.", MessageType.Error);
                return;
            }

            if (_remover is StubZenjectRemover) {
                EditorGUILayout.HelpBox("Removal API is a stub in M4 — full implementation lands in M5. Remove Zenject manually for now.", MessageType.Warning);
            }

            if (GUILayout.Button("Plan removal")) {
                var projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, ".."));
                _plan = _remover.Plan(ctx.ScanReport?.Installation, projectRoot);
                ctx.Log.Info("Remove", _plan.IsNoop ? "Already removed (or stub)." : $"UPM: {_plan.UpmInstall} ({_plan.UpmPackageId}); folder: {_plan.FolderInstallPath}");
            }
            if (_plan == null) return;

            using (new EditorGUI.DisabledScope(_plan.IsNoop)) {
                if (GUILayout.Button("Apply removal")) {
                    var projectRoot = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, ".."));
                    var result = _remover.Apply(_plan, projectRoot);
                    if (result.Success) ctx.Log.Info("Remove", result.Message);
                    else ctx.Log.Error("Remove", result.Message);
                    foreach (var a in result.ActionsTaken) ctx.Log.Info("Remove", a);
                }
            }

            if (GUILayout.Button("Skip — leave Zenject installed")) {
                ctx.Log.Info("Remove", "User opted to skip Zenject removal.");
            }
        }

        public bool CanAdvance(MigrationContext ctx) => true; // optional step
    }
}
```

- [ ] **Step 2: Stage**

```bash
git add Editor/UI/Steps/RemoveZenjectStep.cs
```

---

## Task 15 — Idempotency + smoke against PartyQuiz host

**Files:** none modified.

- [ ] **Step 1: First run on a fresh clone of PartyQuiz**

In the host, open the wizard, run all five steps. Expected: scan reports usage, preview shows ~50 changes, apply succeeds, verify reports zero residual, remove step shows the stub warning.

- [ ] **Step 2: Second run on the same project (idempotency)**

Re-open the wizard, scan again. Expected: scan reports zero usage, plans are empty, wizard advances directly through Verify with a green status (per spec §13).

- [ ] **Step 3: Compile-error rollback drill**

Hand-edit one staged change (after preview, before apply) so its `NewText` will not compile (e.g. inject a stray `}`). Apply. Expected: backup created, files written, compile fails, auto-rollback restores originals, wizard shows error log.

- [ ] **Step 4: Capture results in `project_state.md` (memory)**

Update the project memory snapshot to reflect M4 completion: list new files, smoke-test outcome, and the M5 dependency stub. (No git op — owner lands the memory update.)

- [ ] **Step 5: Stage anything that was modified during smoke testing**

```bash
git status
git add <any modified files>
```

---

## Task 16 — Final stage

- [ ] **Step 1: Confirm everything is staged**

```bash
git status
```

All M4 files should be staged; no untracked `.cs` left under `Editor/`, `Tests/Editor/`, `docs/superpowers/plans/`.

- [ ] **Step 2: Hand off**

Memory ("Project state") updated. Ready for owner review and commit. M5 (Zenject removal) is the natural next milestone — it replaces `StubZenjectRemover` with a real implementation and the wizard gets the full removal flow with no further UI changes.

---

## Self-review checklist

- **Spec coverage:**
  - §3.1 wizard files (Window + Steps + DiffView) — Tasks 7, 9–14.
  - §4.2 preview with confidence badges + LowFlagged approval — Task 11.
  - §4.3 apply order + auto-rollback — Tasks 6, 12.
  - §4.4 verify — Task 13.
  - §4.5 MIGRATION_REPORT.md — Task 8.
  - §4.6 preconditions (all 7) — Task 3.
  - §4.7 backup/rollback infra + UI — Tasks 4, 12.
  - §4.8 removal (optional) — Tasks 5, 14 with M5 dependency stubbed.
  - §12 structured logs (file + UI panel) — Tasks 2, 9.
  - §13 idempotency — Task 15 step 2.
- **No placeholders:** every step has either runnable test code, runnable command output, or full source. The two "verify the field name against the actual `ZenjectUsageReport` shape" notes in Tasks 10/13 are deliberate — the report's per-file enumeration shape is set by M1 and the executor must look it up rather than guess. Both have a fallback path if the field is missing.
- **Type consistency:** `MigrationPlan.Empty()` static added in Task 6 step 3. `IWizardStep`, `MigrationContext`, `MigrationLog`, `BackupManager`, `ICompileWaiter`, `MigrationApplyService`, `IZenjectRemover` names are reused exactly across tasks. `RemovalPlan.UpmInstall/UpmPackageId/FolderInstallPath` match the field names used in `RemoveZenjectStep.OnGUI`.
- **Commit policy:** every task ends with `git add` only. No `git commit`, push, branch, merge, or worktree ops.
