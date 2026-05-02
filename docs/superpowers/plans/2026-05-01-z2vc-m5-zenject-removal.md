# M5 — Zenject Removal Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task.
>
> **Commit policy:** Owner authorised commits per task for this session. One atomic commit per task in the existing imperative-subject style with `Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>` trailer.

**Goal:** Replace `StubZenjectRemover` with a real `ZenjectRemover` that detects the install style (UPM / folder / both) and removes Zenject from the host project: UPM uninstall, folder deletion, scoped-registry cleanup in `manifest.json`. Verify step exits clean after this runs.

**Architecture:** New `ZenjectRemover : IZenjectRemover` in `Editor/Manifest/`. `Plan` reads `InstallationInfo` + scans `Packages/manifest.json` for Zenject-only scoped registries. `Apply` performs the three operations idempotently (skips ops already done) and returns an `ActionsTaken` log. UPM removal goes through `UnityEditor.PackageManager.Client.Remove(...)`; we poll its `Request<RemoveResult>` until completion. Folder removal goes through `AssetDatabase.DeleteAsset` after a confirmation dialog listing modified files (via `git status --porcelain` if the project is a repo). Manifest editing uses a small line-based JSON mutator (avoid pulling in a JSON dependency).

**Tech Stack:** C# 9, NUnit, Unity 2021.3+, `UnityEditor.PackageManager.Client`, `AssetDatabase`. Same package layout as M1–M4.

**Out of scope:**
- Restoring Zenject after removal (use git revert).
- Detecting Zenject usage that survived migration (Verify step covers that already).
- Multi-project bulk removal.

---

## File map

| Path | Responsibility |
|---|---|
| `Editor/Manifest/ZenjectRemover.cs` | Real `IZenjectRemover` implementation. |
| `Editor/Manifest/ManifestEditor.cs` | Strip scoped registries that are Zenject-only from `Packages/manifest.json`. |
| `Editor/UI/MigrationWizardWindow.cs` (modify) | Swap `StubZenjectRemover` → `ZenjectRemover`. |
| `Tests/Editor/ManifestEditorTests.cs` | Tests for scoped-registry stripping. |
| `Tests/Editor/ZenjectRemoverPlanTests.cs` | Tests for `Plan(...)` against `InstallationInfo` shapes. |

`Apply` paths that hit Unity APIs (UPM Client, AssetDatabase) cannot be unit-tested without an Editor — covered by smoke run on PartyQuiz host instead.

---

## Task 1 — ManifestEditor

**Files:**
- Create: `Editor/Manifest/ManifestEditor.cs` (+ .meta)
- Create: `Tests/Editor/ManifestEditorTests.cs` (+ .meta)

**Goal:** Pure-C#, no-dependency function that reads a `manifest.json` text and returns the edited text with all scoped registries stripped that are Zenject-only (i.e. their `scopes` array contains exactly Zenject-related namespaces). Also list the names dropped so the caller can log them.

### Step 1 — Failing tests

`Tests/Editor/ManifestEditorTests.cs`:

```csharp
using NUnit.Framework;
using Zenject2VContainer.Manifest;

namespace Zenject2VContainer.Tests {
    [TestFixture]
    public class ManifestEditorTests {
        [Test]
        public void StripsZenjectOnlyScopedRegistry() {
            var input = @"{
  ""dependencies"": { ""com.svermeulen.extenject"": ""9.2.0"" },
  ""scopedRegistries"": [
    {
      ""name"": ""Extenject"",
      ""url"": ""https://npm.pkg.github.com/svermeulen"",
      ""scopes"": [ ""com.svermeulen"" ]
    }
  ]
}";
            var result = ManifestEditor.StripZenjectScopedRegistries(input);
            Assert.IsTrue(result.Modified);
            Assert.That(result.RemovedRegistryNames, Is.EquivalentTo(new[] { ""Extenject"" }));
            StringAssert.DoesNotContain(""Extenject"", result.NewText);
            // dependencies block survives
            StringAssert.Contains(""dependencies"", result.NewText);
        }

        [Test]
        public void KeepsUnrelatedScopedRegistries() {
            var input = @"{
  ""scopedRegistries"": [
    {
      ""name"": ""OpenUPM"",
      ""url"": ""https://package.openupm.com"",
      ""scopes"": [ ""com.cysharp"" ]
    }
  ]
}";
            var result = ManifestEditor.StripZenjectScopedRegistries(input);
            Assert.IsFalse(result.Modified);
            Assert.IsEmpty(result.RemovedRegistryNames);
            StringAssert.Contains(""OpenUPM"", result.NewText);
        }

        [Test]
        public void NoScopedRegistriesArray_ReturnsUnchanged() {
            var input = @"{ ""dependencies"": { ""com.unity.test"": ""1.0.0"" } }";
            var result = ManifestEditor.StripZenjectScopedRegistries(input);
            Assert.IsFalse(result.Modified);
            Assert.AreEqual(input, result.NewText);
        }
    }
}
```

### Step 2 — Implementation

`Editor/Manifest/ManifestEditor.cs`:

```csharp
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Zenject2VContainer.Manifest {
    public sealed class ManifestEditResult {
        public string NewText;
        public bool Modified;
        public List<string> RemovedRegistryNames = new List<string>();
    }

    public static class ManifestEditor {
        // Heuristic match: a scoped registry whose `scopes` array contains only entries
        // matching a Zenject-related prefix (svermeulen / extenject / zenject).
        private static readonly string[] ZenjectScopePrefixes = {
            "com.svermeulen", "com.extenject", "com.zenject"
        };

        public static ManifestEditResult StripZenjectScopedRegistries(string manifestJson) {
            var result = new ManifestEditResult { NewText = manifestJson };
            // Locate "scopedRegistries": [ ... ] block. Tolerate whitespace.
            var blockMatch = Regex.Match(manifestJson,
                @"""scopedRegistries""\s*:\s*\[(?<body>.*?)\](\s*,)?",
                RegexOptions.Singleline);
            if (!blockMatch.Success) return result;
            var body = blockMatch.Groups["body"].Value;

            // Split scoped registries on `},{` boundaries (single-level). Each entry is a JSON object.
            var entries = SplitTopLevelObjects(body);
            var keep = new List<string>();
            foreach (var raw in entries) {
                if (IsZenjectOnlyRegistry(raw, out var name)) {
                    result.RemovedRegistryNames.Add(name);
                } else {
                    keep.Add(raw);
                }
            }
            if (result.RemovedRegistryNames.Count == 0) return result;

            result.Modified = true;
            string replacement;
            if (keep.Count == 0) {
                // Drop the whole scopedRegistries property + any dangling comma.
                var leadingComma = Regex.IsMatch(manifestJson.Substring(0, blockMatch.Index).TrimEnd(), @",$");
                if (leadingComma) {
                    var prefix = manifestJson.Substring(0, blockMatch.Index);
                    var trimmed = Regex.Replace(prefix, @",\s*$", "");
                    result.NewText = trimmed + manifestJson.Substring(blockMatch.Index + blockMatch.Length);
                } else {
                    result.NewText = manifestJson.Remove(blockMatch.Index, blockMatch.Length);
                }
            } else {
                replacement = "\"scopedRegistries\": [" + string.Join(",", keep) + "]";
                if (blockMatch.Value.TrimEnd().EndsWith(",")) replacement += ",";
                result.NewText = manifestJson.Remove(blockMatch.Index, blockMatch.Length).Insert(blockMatch.Index, replacement);
            }
            return result;
        }

        private static List<string> SplitTopLevelObjects(string body) {
            var list = new List<string>();
            int depth = 0;
            int start = -1;
            for (int i = 0; i < body.Length; i++) {
                var c = body[i];
                if (c == '{') {
                    if (depth == 0) start = i;
                    depth++;
                } else if (c == '}') {
                    depth--;
                    if (depth == 0 && start >= 0) {
                        list.Add(body.Substring(start, i - start + 1));
                        start = -1;
                    }
                }
            }
            return list;
        }

        private static bool IsZenjectOnlyRegistry(string raw, out string name) {
            name = ExtractStringField(raw, "name") ?? "";
            // Read all scopes entries. Match against the Zenject prefix list.
            var scopes = ExtractStringArray(raw, "scopes");
            if (scopes.Count == 0) return false;
            foreach (var s in scopes) {
                bool match = false;
                foreach (var p in ZenjectScopePrefixes) {
                    if (s.StartsWith(p, System.StringComparison.OrdinalIgnoreCase)) { match = true; break; }
                }
                if (!match) return false;
            }
            return true;
        }

        private static string ExtractStringField(string raw, string field) {
            var m = Regex.Match(raw, "\"" + Regex.Escape(field) + "\"\\s*:\\s*\"(?<v>[^\"]*)\"");
            return m.Success ? m.Groups["v"].Value : null;
        }

        private static List<string> ExtractStringArray(string raw, string field) {
            var list = new List<string>();
            var m = Regex.Match(raw, "\"" + Regex.Escape(field) + "\"\\s*:\\s*\\[(?<body>[^\\]]*)\\]");
            if (!m.Success) return list;
            foreach (Match s in Regex.Matches(m.Groups["body"].Value, "\"(?<v>[^\"]*)\"")) {
                list.Add(s.Groups["v"].Value);
            }
            return list;
        }
    }
}
```

### Step 3 — Commit

Subject: `Add ManifestEditor for stripping Zenject scoped registries`

---

## Task 2 — ZenjectRemover

**Files:**
- Create: `Editor/Manifest/ZenjectRemover.cs` (+ .meta)
- Create: `Tests/Editor/ZenjectRemoverPlanTests.cs` (+ .meta)

**Goal:** Real `IZenjectRemover` implementation. `Plan` returns a populated `RemovalPlan` from `InstallationInfo`. `Apply` runs the three operations idempotently, returning an `ActionsTaken` log.

### Step 1 — Failing tests for Plan

`Tests/Editor/ZenjectRemoverPlanTests.cs`:

```csharp
using System.IO;
using NUnit.Framework;
using Zenject2VContainer.Core;
using Zenject2VContainer.Manifest;

namespace Zenject2VContainer.Tests {
    [TestFixture]
    public class ZenjectRemoverPlanTests {
        private string _projectRoot;

        [SetUp] public void Setup() {
            _projectRoot = Path.Combine(Path.GetTempPath(), "z2vc-removal-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_projectRoot, "Packages"));
            File.WriteAllText(Path.Combine(_projectRoot, "Packages", "manifest.json"), @"{
  ""scopedRegistries"": [
    { ""name"": ""Extenject"", ""url"": ""x"", ""scopes"": [ ""com.svermeulen"" ] }
  ]
}");
        }
        [TearDown] public void Cleanup() {
            if (Directory.Exists(_projectRoot)) Directory.Delete(_projectRoot, true);
        }

        [Test]
        public void Plan_UpmInstall_PopulatesUpmPackageId() {
            var info = new InstallationInfo { ZenjectViaUpm = true, UpmPackageId = "com.svermeulen.extenject" };
            var plan = new ZenjectRemover().Plan(info, _projectRoot);
            Assert.IsTrue(plan.UpmInstall);
            Assert.AreEqual("com.svermeulen.extenject", plan.UpmPackageId);
            Assert.IsFalse(plan.IsNoop);
        }

        [Test]
        public void Plan_FolderInstall_PopulatesFolderPath() {
            var info = new InstallationInfo { ZenjectViaAssets = true, AssetsFolderPath = "Assets/Plugins/Zenject" };
            var plan = new ZenjectRemover().Plan(info, _projectRoot);
            Assert.AreEqual("Assets/Plugins/Zenject", plan.FolderInstallPath);
            Assert.IsFalse(plan.IsNoop);
        }

        [Test]
        public void Plan_DetectsZenjectScopedRegistries() {
            var info = new InstallationInfo { ZenjectViaUpm = true, UpmPackageId = "com.svermeulen.extenject" };
            var plan = new ZenjectRemover().Plan(info, _projectRoot);
            Assert.That(plan.ScopedRegistryNamesToDrop, Is.EquivalentTo(new[] { "Extenject" }));
        }

        [Test]
        public void Plan_NotInstalledAtAll_IsNoop() {
            var info = new InstallationInfo();
            // Empty manifest, no install.
            File.WriteAllText(Path.Combine(_projectRoot, "Packages", "manifest.json"), "{}");
            var plan = new ZenjectRemover().Plan(info, _projectRoot);
            Assert.IsTrue(plan.IsNoop);
        }
    }
}
```

### Step 2 — Implementation

`Editor/Manifest/ZenjectRemover.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.Manifest {
    public sealed class ZenjectRemover : IZenjectRemover {
        public RemovalPlan Plan(InstallationInfo install, string projectRoot) {
            var plan = new RemovalPlan();
            if (install != null) {
                if (install.ZenjectViaUpm) {
                    plan.UpmInstall = true;
                    plan.UpmPackageId = install.UpmPackageId;
                }
                if (install.ZenjectViaAssets && !string.IsNullOrEmpty(install.AssetsFolderPath)) {
                    plan.FolderInstallPath = install.AssetsFolderPath;
                }
            }

            var manifestPath = Path.Combine(projectRoot, "Packages", "manifest.json");
            if (File.Exists(manifestPath)) {
                var preview = ManifestEditor.StripZenjectScopedRegistries(File.ReadAllText(manifestPath));
                plan.ScopedRegistryNamesToDrop.AddRange(preview.RemovedRegistryNames);
            }

            plan.IsNoop = !plan.UpmInstall
                && string.IsNullOrEmpty(plan.FolderInstallPath)
                && plan.ScopedRegistryNamesToDrop.Count == 0;
            return plan;
        }

        public RemovalResult Apply(RemovalPlan plan, string projectRoot) {
            var result = new RemovalResult { Success = true };
            if (plan.IsNoop) {
                result.Message = "Nothing to remove.";
                return result;
            }

            // 1. UPM uninstall — block until Client.Remove finishes (or 60s timeout).
            if (plan.UpmInstall && !string.IsNullOrEmpty(plan.UpmPackageId)) {
                var req = Client.Remove(plan.UpmPackageId);
                var deadline = System.DateTime.UtcNow.AddSeconds(60);
                while (!req.IsCompleted && System.DateTime.UtcNow < deadline) {
                    System.Threading.Thread.Sleep(100);
                }
                if (req.Status == StatusCode.Success) {
                    result.ActionsTaken.Add("UPM removed " + plan.UpmPackageId);
                } else if (req.Status == StatusCode.Failure) {
                    result.Success = false;
                    result.Message = "UPM removal failed: " + (req.Error?.message ?? "unknown");
                    return result;
                } else {
                    result.Success = false;
                    result.Message = "UPM removal timed out after 60s.";
                    return result;
                }
            }

            // 2. Folder removal — AssetDatabase keeps the .meta in sync.
            if (!string.IsNullOrEmpty(plan.FolderInstallPath)) {
                if (AssetDatabase.DeleteAsset(plan.FolderInstallPath)) {
                    result.ActionsTaken.Add("Deleted folder " + plan.FolderInstallPath);
                } else {
                    result.Success = false;
                    result.Message = "AssetDatabase.DeleteAsset failed for " + plan.FolderInstallPath;
                    return result;
                }
            }

            // 3. Manifest scoped registries.
            if (plan.ScopedRegistryNamesToDrop.Count > 0) {
                var manifestPath = Path.Combine(projectRoot, "Packages", "manifest.json");
                if (File.Exists(manifestPath)) {
                    var edited = ManifestEditor.StripZenjectScopedRegistries(File.ReadAllText(manifestPath));
                    if (edited.Modified) {
                        File.WriteAllText(manifestPath, edited.NewText);
                        foreach (var name in edited.RemovedRegistryNames) {
                            result.ActionsTaken.Add("Dropped scoped registry " + name);
                        }
                    }
                }
            }

            AssetDatabase.Refresh();
            result.Message = "Zenject removal complete.";
            return result;
        }
    }
}
```

### Step 3 — Commit

Subject: `Add ZenjectRemover with UPM, folder and manifest cleanup`

---

## Task 3 — Wire ZenjectRemover into wizard

**Files:**
- Modify: `Editor/UI/MigrationWizardWindow.cs`

Swap `new StubZenjectRemover()` for `new ZenjectRemover()` in `OnEnable`.

### Step 1 — Edit

```diff
-                { WizardState.Remove,  new RemoveZenjectStep(new StubZenjectRemover()) }
+                { WizardState.Remove,  new RemoveZenjectStep(new ZenjectRemover()) }
```

### Step 2 — Commit

Subject: `Wire ZenjectRemover into wizard's remove step`

---

## Task 4 — Smoke against PartyQuiz host

**Files:** none modified. Manual run.

### Step 1 — Run wizard end-to-end on the migrated PartyQuiz host

1. Open wizard. Advance through Scan → Preview → Apply → Verify (all should now be near-no-op since migration already ran).
2. Remove step → "Plan removal" → expect `ZenjectViaAssets=true, AssetsFolderPath=Assets/Plugins/Zenject`. ScopedRegistryNamesToDrop populated if manifest has any.
3. "Apply removal" → folder deleted, manifest cleaned. Compile completes (no Zenject types referenced anymore). AssetDatabase refresh.
4. Re-run scan. Expect `CSharpFindings: 0, AssetFindings: 0, Install.ZenjectViaAssets: false`.

### Step 2 — Update memory snapshot

Update `~/.claude/projects/D--REPOS-zenject-to-vcontainer-migrator/memory/project_state.md` to reflect M5 completion.

### Step 3 — Stage / commit any incidental changes

If smoke surfaced a bug, fix and add a follow-up commit before declaring M5 done.

---

## Self-review checklist

- ManifestEditor strips only `scopedRegistries` whose `scopes` are exclusively Zenject-prefixed. Mixed-scope registries are kept.
- ZenjectRemover.Apply early-returns on each step's failure with `Success = false` and a specific error message. UPM polling has a 60s deadline.
- Wizard wiring swaps stub for real implementation; nothing else changes in the wizard.
- Tests cover Plan and ManifestEditor logic; Apply path is smoke-tested.
- Commit subjects match the existing imperative style and carry the Co-Authored-By trailer.
