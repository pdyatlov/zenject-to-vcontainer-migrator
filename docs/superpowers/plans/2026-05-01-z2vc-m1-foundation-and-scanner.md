# M1 — Foundation + Scanner Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Commit policy override:** This repository owner manages all commits. Do **not** run `git commit`, `git push`, `git branch`, `git checkout -b`, `git merge`, or `git rebase`. Each task ends with a *Stage changes for user review* step that uses `git add` only. Read-only git commands (`status`, `diff`, `log`) are fine.

**Goal:** Lay down the UPM package skeleton, vendored Roslyn dependency, NUnit test infrastructure, and a working `ProjectScanner` that detects Zenject usage across C# code, scene/prefab/asset YAML, and the package manifest. Produce a `ZenjectUsageReport` accessible through a headless entry point.

**Architecture:** Pure-data scanner with three independent collectors (C#, Yaml, Manifest) composed by an orchestrator. Roslyn `SemanticModel` resolves C# symbols against per-asmdef `CSharpCompilation` objects built from Unity's `CompilationPipeline`. YAML assets are line-scanned for known Zenject script GUIDs from a shipped table. `manifest.json` and `Assets/` are probed for both UPM and folder installs. Result is a JSON-serialisable `ZenjectUsageReport`.

**Tech Stack:** Unity 2021.3+ Editor, C# 9 / .NET Standard 2.1, `Microsoft.CodeAnalysis.CSharp` 4.8.0 (vendored DLLs), NUnit (Unity Test Framework).

---

## File map

Files this milestone creates:

| Path                                                                              | Responsibility                                                  |
|-----------------------------------------------------------------------------------|-----------------------------------------------------------------|
| `package.json`                                                                    | UPM manifest                                                    |
| `.gitignore` (additions)                                                          | Library/, Temp/, Logs/, *.csproj, *.sln                         |
| `Editor/Zenject2VContainer.Editor.asmdef`                                         | Editor-only assembly                                            |
| `Editor/Plugins/Roslyn/*.dll` (+ `.meta`)                                         | Vendored Roslyn DLLs, Editor platform only                      |
| `Editor/Plugins/Roslyn/THIRD_PARTY_NOTICES.md`                                    | License attribution for Roslyn (MIT)                            |
| `Editor/Core/MigrationPipeline.cs`                                                | Orchestrator + headless entry points                            |
| `Editor/Core/ZenjectUsageReport.cs`                                               | Data model + JSON serialiser                                    |
| `Editor/Core/PreconditionChecks.cs`                                               | Pre-migration safety probes                                     |
| `Editor/Core/Scanner/ProjectScanner.cs`                                           | Orchestrates the three collectors                               |
| `Editor/Core/Scanner/CSharpScanner.cs`                                            | Roslyn-driven C# scan                                           |
| `Editor/Core/Scanner/AssetScanner.cs`                                             | YAML asset GUID scan                                            |
| `Editor/Core/Scanner/ZenjectInstallDetector.cs`                                   | manifest + folder probe                                         |
| `Editor/Core/Scanner/CompilationLoader.cs`                                        | Build `CSharpCompilation` per asmdef                            |
| `Editor/Core/Scanner/SymbolMatchers.cs`                                           | Roslyn symbol identity helpers                                  |
| `Editor/Core/Scanner/ZenjectScriptGuidTable.cs`                                   | Shipped Zenject GUID table loader                               |
| `Editor/Core/Resources/zenject-script-guids.json`                                 | Bundled GUID table                                              |
| `Editor/UI/MigrationMenu.cs`                                                      | `Window > Zenject2VContainer > Scan to JSON…` menu item         |
| `Runtime/Zenject2VContainer.Runtime.asmdef`                                       | Empty runtime assembly (placeholder for AutoRegisterComponent in M3) |
| `Tests/Editor/Zenject2VContainer.Tests.asmdef`                                    | Edit-mode test assembly                                         |
| `Tests/Editor/RoslynSmokeTests.cs`                                                | Verify vendored Roslyn loads and parses                         |
| `Tests/Editor/ZenjectUsageReportTests.cs`                                         | Serialiser round-trip                                           |
| `Tests/Editor/ZenjectInstallDetectorTests.cs`                                     | manifest + folder detection                                     |
| `Tests/Editor/AssetScannerTests.cs`                                               | YAML GUID matching                                              |
| `Tests/Editor/CSharpScannerTests.cs`                                              | Source-string scanning with bundled refs                        |
| `Tests/Editor/SymbolMatchersTests.cs`                                             | Symbol identity                                                 |
| `Tests/Editor/ProjectScannerTests.cs`                                             | End-to-end on a synthetic project                               |
| `Tests/Editor/PreconditionChecksTests.cs`                                         | Each precondition probe                                         |
| `Tests/Editor/Fixtures/ScannerInputs/...`                                         | Synthetic input projects for scanner tests                      |
| `Tests/Editor/References/Zenject.dll`                                             | Minimal stub Zenject assembly for compilation refs              |
| `Tests/Editor/References/VContainer.dll`                                          | Minimal stub VContainer assembly for compilation refs           |
| `Tests/Editor/References/README.md`                                               | Notes on how stub DLLs are produced                             |

---

## Task 1 — UPM `package.json`

**Files:**
- Create: `package.json`

- [ ] **Step 1: Write `package.json`**

Path: `package.json`

```json
{
  "name": "com.zenject2vcontainer.migrator",
  "version": "0.1.0",
  "displayName": "Zenject2VContainer Migrator",
  "description": "Editor tool that migrates Unity projects from Zenject / Extenject to VContainer.",
  "unity": "2021.3",
  "keywords": [
    "zenject",
    "extenject",
    "vcontainer",
    "dependency-injection",
    "migration",
    "editor-tool"
  ],
  "author": {
    "name": "Zenject2VContainer contributors"
  },
  "dependencies": {}
}
```

- [ ] **Step 2: Verify Unity recognises the package**

Open the project in Unity 2021.3+. The Package Manager window must list "Zenject2VContainer Migrator" under "In Project". Console must show no manifest errors.

- [ ] **Step 3: Stage changes for user review**

```bash
git add package.json
git status
```

Expected: `package.json` appears as a new staged file. Do not commit.

---

## Task 2 — Repository skeleton + `.gitignore`

**Files:**
- Create: `Editor/.gitkeep`
- Create: `Editor/Plugins/.gitkeep`
- Create: `Editor/Plugins/Roslyn/.gitkeep`
- Create: `Editor/Core/.gitkeep`
- Create: `Editor/Core/Scanner/.gitkeep`
- Create: `Editor/Core/Resources/.gitkeep`
- Create: `Editor/UI/.gitkeep`
- Create: `Runtime/.gitkeep`
- Create: `Tests/Editor/.gitkeep`
- Create: `Tests/Editor/Fixtures/.gitkeep`
- Create: `Tests/Editor/References/.gitkeep`
- Modify: `.gitignore`

- [ ] **Step 1: Inspect current `.gitignore`**

```bash
git status -s .gitignore
cat .gitignore
```

Expected: file exists from initial commit.

- [ ] **Step 2: Append Unity-specific exclusions**

Append the following block to `.gitignore` if not already present:

```
# Unity generated
[Ll]ibrary/
[Tt]emp/
[Oo]bj/
[Bb]uild/
[Bb]uilds/
[Ll]ogs/
[Mm]emoryCaptures/
sysinfo.txt
*.csproj
*.unityproj
*.sln
*.suo
*.user
*.userprefs

# Tool-specific
Library/Zenject2VContainer/
```

- [ ] **Step 3: Create empty placeholder files**

Use the Write tool to create each `.gitkeep` listed in the Files block above with empty contents.

- [ ] **Step 4: Stage changes**

```bash
git add .gitignore Editor Runtime Tests
git status
```

Expected: `.gitignore` modified, all placeholder files staged.

---

## Task 3 — Editor assembly definition

**Files:**
- Create: `Editor/Zenject2VContainer.Editor.asmdef`

- [ ] **Step 1: Write the asmdef**

Path: `Editor/Zenject2VContainer.Editor.asmdef`

```json
{
  "name": "Zenject2VContainer.Editor",
  "rootNamespace": "Zenject2VContainer",
  "references": [],
  "includePlatforms": [
    "Editor"
  ],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "autoReferenced": false,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": false
}
```

- [ ] **Step 2: Verify Unity loads the asmdef**

Wait for compile. Unity Console must report no errors. The assembly must appear in `Library/ScriptAssemblies/Zenject2VContainer.Editor.dll`.

- [ ] **Step 3: Stage changes**

```bash
git add Editor/Zenject2VContainer.Editor.asmdef
git status
```

---

## Task 4 — Runtime assembly definition (placeholder)

**Files:**
- Create: `Runtime/Zenject2VContainer.Runtime.asmdef`

- [ ] **Step 1: Write the asmdef**

Path: `Runtime/Zenject2VContainer.Runtime.asmdef`

```json
{
  "name": "Zenject2VContainer.Runtime",
  "rootNamespace": "Zenject2VContainer.Runtime",
  "references": [],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": false
}
```

- [ ] **Step 2: Verify Unity loads the asmdef**

Wait for compile. No errors expected. The runtime assembly will be empty until M3.

- [ ] **Step 3: Stage changes**

```bash
git add Runtime/Zenject2VContainer.Runtime.asmdef
git status
```

---

## Task 5 — Test assembly definition

**Files:**
- Create: `Tests/Editor/Zenject2VContainer.Tests.asmdef`

- [ ] **Step 1: Write the asmdef**

Path: `Tests/Editor/Zenject2VContainer.Tests.asmdef`

```json
{
  "name": "Zenject2VContainer.Tests",
  "rootNamespace": "Zenject2VContainer.Tests",
  "references": [
    "Zenject2VContainer.Editor",
    "UnityEditor.TestRunner",
    "UnityEngine.TestRunner"
  ],
  "includePlatforms": [
    "Editor"
  ],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": [
    "nunit.framework.dll"
  ],
  "autoReferenced": false,
  "defineConstraints": [
    "UNITY_INCLUDE_TESTS"
  ],
  "versionDefines": [],
  "noEngineReferences": false
}
```

- [ ] **Step 2: Open `Window > General > Test Runner` and switch to `EditMode`**

The view must list `Zenject2VContainer.Tests` (empty). No compile errors.

- [ ] **Step 3: Stage changes**

```bash
git add Tests/Editor/Zenject2VContainer.Tests.asmdef
git status
```

---

## Task 6 — Vendor Roslyn DLLs

**Files:**
- Create: `Editor/Plugins/Roslyn/Microsoft.CodeAnalysis.dll`
- Create: `Editor/Plugins/Roslyn/Microsoft.CodeAnalysis.CSharp.dll`
- Create: `Editor/Plugins/Roslyn/System.Collections.Immutable.dll`
- Create: `Editor/Plugins/Roslyn/System.Reflection.Metadata.dll`
- Create: `Editor/Plugins/Roslyn/System.Buffers.dll` (if not bundled with Unity 2021.3)
- Create: `Editor/Plugins/Roslyn/System.Memory.dll` (if not bundled)
- Create: `Editor/Plugins/Roslyn/THIRD_PARTY_NOTICES.md`

- [ ] **Step 1: Acquire Roslyn DLLs**

Run on a developer workstation (any OS with `dotnet` SDK 6+):

```bash
mkdir -p /tmp/roslyn-vendor && cd /tmp/roslyn-vendor
dotnet new classlib -f netstandard2.0 -n stub
cd stub
dotnet add package Microsoft.CodeAnalysis.CSharp --version 4.8.0
dotnet build -c Release
```

DLLs land in `bin/Release/netstandard2.0/`. Copy these files into `Editor/Plugins/Roslyn/`:

- `Microsoft.CodeAnalysis.dll`
- `Microsoft.CodeAnalysis.CSharp.dll`
- `System.Collections.Immutable.dll`
- `System.Reflection.Metadata.dll`

- [ ] **Step 2: Configure `.meta` files for Editor-only platform**

Open Unity. For each `.dll` under `Editor/Plugins/Roslyn/`:

1. Select the DLL in the Project window.
2. In the Inspector, deselect every checkbox under "Include Platforms" except **Editor**.
3. Click **Apply**.

Unity writes the corresponding `.dll.meta`. Inspect each `.meta` file to confirm:

```yaml
PluginImporter:
  ...
  isExplicitlyReferenced: 0
  validateReferences: 1
  platformData:
  - first:
      Any:
    second:
      enabled: 0
  - first:
      Editor: Editor
    second:
      enabled: 1
      settings:
        DefaultValueInitialized: true
```

- [ ] **Step 3: Write `THIRD_PARTY_NOTICES.md`**

Path: `Editor/Plugins/Roslyn/THIRD_PARTY_NOTICES.md`

```markdown
# Third-party notices

This directory contains DLLs from the .NET Roslyn project, vendored so the
migrator can run inside the Unity Editor without a NuGet client.

## Microsoft.CodeAnalysis.CSharp 4.8.0

- License: MIT
- Source: https://github.com/dotnet/roslyn
- Components included:
  - Microsoft.CodeAnalysis.dll
  - Microsoft.CodeAnalysis.CSharp.dll
  - System.Collections.Immutable.dll
  - System.Reflection.Metadata.dll

The MIT license text is reproduced in the upstream repository at
https://github.com/dotnet/roslyn/blob/main/License.txt.
```

- [ ] **Step 4: Verify Unity loads the DLLs**

Wait for Unity to import. Console must show **no** errors. If "Multiple precompiled assemblies with the same name" appears, the conflicting DLL is already present in Unity; remove the vendored copy and rely on the Unity-bundled version (record the removal in `THIRD_PARTY_NOTICES.md`).

- [ ] **Step 5: Stage changes**

```bash
git add Editor/Plugins/Roslyn
git status
```

---

## Task 7 — Roslyn smoke test

**Files:**
- Create: `Tests/Editor/RoslynSmokeTests.cs`

- [ ] **Step 1: Write the failing test**

Path: `Tests/Editor/RoslynSmokeTests.cs`

```csharp
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace Zenject2VContainer.Tests {
    public class RoslynSmokeTests {
        [Test]
        public void Roslyn_Parses_Trivial_Source() {
            var tree = CSharpSyntaxTree.ParseText("class Foo { void Bar() {} }");
            var root = tree.GetRoot();

            Assert.IsNotNull(root);
            Assert.IsFalse(tree.GetDiagnostics().GetEnumerator().MoveNext(),
                "trivial source must parse without diagnostics");
        }
    }
}
```

- [ ] **Step 2: Run the test**

In Unity Test Runner (EditMode), press **Run All**.
Expected: `Roslyn_Parses_Trivial_Source` passes.

- [ ] **Step 3: Stage changes**

```bash
git add Tests/Editor/RoslynSmokeTests.cs
git status
```

---

## Task 8 — `ZenjectUsageReport` data model

**Files:**
- Create: `Editor/Core/ZenjectUsageReport.cs`

- [ ] **Step 1: Write the data model**

Path: `Editor/Core/ZenjectUsageReport.cs`

```csharp
using System;
using System.Collections.Generic;

namespace Zenject2VContainer.Core {
    public enum CSharpFindingKind {
        UsingDirective,
        InjectAttribute,
        BindCall,
        InstallerSubclass,
        LifecycleInterface,
        FactoryReference,
        SubContainerCall,
        DiContainerUsage,
        UnsupportedFeature
    }

    [Serializable]
    public sealed class CSharpFinding {
        public string FilePath;
        public int Line;
        public int Column;
        public CSharpFindingKind Kind;
        public string SymbolName;
        public string Snippet;
        public string Confidence;          // "High" | "Medium" | "LowFlagged"
        public string Notes;
    }

    [Serializable]
    public sealed class AssetFinding {
        public string FilePath;            // .unity / .prefab / .asset
        public int Line;                   // line of m_Script ref
        public string ZenjectScriptName;   // e.g. "SceneContext"
        public string ZenjectGuid;
    }

    [Serializable]
    public sealed class InstallationInfo {
        public bool ZenjectViaUpm;
        public string UpmPackageId;        // e.g. "com.svermeulen.extenject"
        public string UpmVersionOrUrl;
        public bool ZenjectViaAssets;
        public string AssetsFolderPath;    // e.g. "Assets/Plugins/Zenject"
        public bool VContainerInstalled;
        public string VContainerVersion;
    }

    [Serializable]
    public sealed class UnsupportedFeature {
        public string Category;            // matches manual TODO category, e.g. "SignalBus"
        public string FilePath;
        public int Line;
        public string Reason;
    }

    [Serializable]
    public sealed class ZenjectUsageReport {
        public string ToolVersion;
        public string UnityVersion;
        public string ScannedAtUtc;        // ISO 8601
        public List<CSharpFinding> CSharpFindings = new();
        public List<AssetFinding> AssetFindings = new();
        public InstallationInfo Install = new();
        public List<UnsupportedFeature> Unsupported = new();
    }
}
```

- [ ] **Step 2: Verify it compiles**

Wait for Unity compile. Expected: zero errors.

- [ ] **Step 3: Stage changes**

```bash
git add Editor/Core/ZenjectUsageReport.cs
git status
```

---

## Task 9 — `ZenjectUsageReport` JSON round-trip

**Files:**
- Modify: `Editor/Core/ZenjectUsageReport.cs`
- Create: `Tests/Editor/ZenjectUsageReportTests.cs`

- [ ] **Step 1: Write the failing test**

Path: `Tests/Editor/ZenjectUsageReportTests.cs`

```csharp
using NUnit.Framework;
using UnityEngine;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.Tests {
    public class ZenjectUsageReportTests {
        [Test]
        public void Report_Serialises_And_Deserialises_To_Equivalent_Object() {
            var original = new ZenjectUsageReport {
                ToolVersion = "0.1.0",
                UnityVersion = "2022.3.10f1",
                ScannedAtUtc = "2026-05-01T10:00:00Z"
            };
            original.CSharpFindings.Add(new CSharpFinding {
                FilePath = "Assets/Foo.cs",
                Line = 12,
                Column = 4,
                Kind = CSharpFindingKind.InjectAttribute,
                SymbolName = "Zenject.InjectAttribute",
                Snippet = "[Inject] private IBar _bar;",
                Confidence = "High",
                Notes = ""
            });
            original.Install.ZenjectViaUpm = true;
            original.Install.UpmPackageId = "com.svermeulen.extenject";

            var json = ZenjectUsageReport.ToJson(original);
            var roundTripped = ZenjectUsageReport.FromJson(json);

            Assert.AreEqual(original.ToolVersion, roundTripped.ToolVersion);
            Assert.AreEqual(1, roundTripped.CSharpFindings.Count);
            Assert.AreEqual(CSharpFindingKind.InjectAttribute, roundTripped.CSharpFindings[0].Kind);
            Assert.AreEqual("com.svermeulen.extenject", roundTripped.Install.UpmPackageId);
        }
    }
}
```

- [ ] **Step 2: Run test, expect FAIL**

`ZenjectUsageReport.ToJson` / `.FromJson` do not exist yet.
Expected output: compile error or test failure.

- [ ] **Step 3: Add the serialiser methods**

Append to `Editor/Core/ZenjectUsageReport.cs`, inside the `ZenjectUsageReport` class:

```csharp
        public static string ToJson(ZenjectUsageReport report) =>
            UnityEngine.JsonUtility.ToJson(report, prettyPrint: true);

        public static ZenjectUsageReport FromJson(string json) =>
            UnityEngine.JsonUtility.FromJson<ZenjectUsageReport>(json);
```

`JsonUtility` does not serialise `enum` values as strings — they round-trip as ints, which is fine for the round-trip test. If the team later wants string-named enums in the JSON, swap to `Newtonsoft.Json` (Unity package `com.unity.nuget.newtonsoft-json`); deferred to M6.

- [ ] **Step 4: Run test, expect PASS**

Run `ZenjectUsageReportTests.Report_Serialises_And_Deserialises_To_Equivalent_Object`.
Expected: PASS.

- [ ] **Step 5: Stage changes**

```bash
git add Editor/Core/ZenjectUsageReport.cs Tests/Editor/ZenjectUsageReportTests.cs
git status
```

---

## Task 10 — `ZenjectInstallDetector` (manifest detection)

**Files:**
- Create: `Editor/Core/Scanner/ZenjectInstallDetector.cs`
- Create: `Tests/Editor/ZenjectInstallDetectorTests.cs`
- Create: `Tests/Editor/Fixtures/ScannerInputs/Manifests/extenject-upm.json`
- Create: `Tests/Editor/Fixtures/ScannerInputs/Manifests/zenject-git.json`
- Create: `Tests/Editor/Fixtures/ScannerInputs/Manifests/no-zenject.json`
- Create: `Tests/Editor/Fixtures/ScannerInputs/Manifests/vcontainer-only.json`

- [ ] **Step 1: Write the failing test and fixtures**

Fixture `Tests/Editor/Fixtures/ScannerInputs/Manifests/extenject-upm.json`:

```json
{
  "dependencies": {
    "com.svermeulen.extenject": "9.2.0",
    "com.unity.test-framework": "1.1.33"
  }
}
```

Fixture `zenject-git.json`:

```json
{
  "dependencies": {
    "com.mathijs-bakker.extenject": "https://github.com/Mathijs-Bakker/Extenject.git#9.3.0"
  }
}
```

Fixture `no-zenject.json`:

```json
{
  "dependencies": {
    "com.unity.test-framework": "1.1.33"
  }
}
```

Fixture `vcontainer-only.json`:

```json
{
  "dependencies": {
    "jp.hadashikick.vcontainer": "1.15.0"
  }
}
```

Test `Tests/Editor/ZenjectInstallDetectorTests.cs`:

```csharp
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Zenject2VContainer.Core.Scanner;

namespace Zenject2VContainer.Tests {
    public class ZenjectInstallDetectorTests {
        private static string FixtureRoot =>
            Path.Combine(Application.dataPath, "..", "Packages",
                "com.zenject2vcontainer.migrator", "Tests", "Editor",
                "Fixtures", "ScannerInputs", "Manifests");

        [Test]
        public void Detects_Extenject_From_Upm() {
            var json = File.ReadAllText(Path.Combine(FixtureRoot, "extenject-upm.json"));
            var info = ZenjectInstallDetector.DetectFromManifestJson(json);
            Assert.IsTrue(info.ZenjectViaUpm);
            Assert.AreEqual("com.svermeulen.extenject", info.UpmPackageId);
            Assert.AreEqual("9.2.0", info.UpmVersionOrUrl);
            Assert.IsFalse(info.VContainerInstalled);
        }

        [Test]
        public void Detects_Mathijs_Bakker_Extenject_From_Git_Url() {
            var json = File.ReadAllText(Path.Combine(FixtureRoot, "zenject-git.json"));
            var info = ZenjectInstallDetector.DetectFromManifestJson(json);
            Assert.IsTrue(info.ZenjectViaUpm);
            Assert.AreEqual("com.mathijs-bakker.extenject", info.UpmPackageId);
            StringAssert.Contains("github.com/Mathijs-Bakker/Extenject", info.UpmVersionOrUrl);
        }

        [Test]
        public void Reports_No_Zenject_When_Manifest_Is_Clean() {
            var json = File.ReadAllText(Path.Combine(FixtureRoot, "no-zenject.json"));
            var info = ZenjectInstallDetector.DetectFromManifestJson(json);
            Assert.IsFalse(info.ZenjectViaUpm);
            Assert.IsFalse(info.VContainerInstalled);
        }

        [Test]
        public void Detects_VContainer_Presence() {
            var json = File.ReadAllText(Path.Combine(FixtureRoot, "vcontainer-only.json"));
            var info = ZenjectInstallDetector.DetectFromManifestJson(json);
            Assert.IsFalse(info.ZenjectViaUpm);
            Assert.IsTrue(info.VContainerInstalled);
            Assert.AreEqual("1.15.0", info.VContainerVersion);
        }
    }
}
```

- [ ] **Step 2: Run tests, expect FAIL (compile error)**

Class does not exist yet.
Expected: compile error referencing `ZenjectInstallDetector`.

- [ ] **Step 3: Implement `ZenjectInstallDetector` (manifest portion)**

Path: `Editor/Core/Scanner/ZenjectInstallDetector.cs`

```csharp
using System.Collections.Generic;
using UnityEngine;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.Core.Scanner {
    public static class ZenjectInstallDetector {
        // Known Zenject / Extenject UPM identifiers.
        private static readonly string[] KnownZenjectIds = {
            "com.svermeulen.extenject",
            "com.mathijs-bakker.extenject",
            "com.modesttree.zenject"
        };

        private const string VContainerId = "jp.hadashikick.vcontainer";
        private const string ZenjectGitUrlMarker = "Mathijs-Bakker/Extenject";

        public static InstallationInfo DetectFromManifestJson(string manifestJson) {
            var info = new InstallationInfo();
            var deps = ParseDependencies(manifestJson);

            foreach (var id in KnownZenjectIds) {
                if (!deps.TryGetValue(id, out var versionOrUrl)) continue;
                info.ZenjectViaUpm = true;
                info.UpmPackageId = id;
                info.UpmVersionOrUrl = versionOrUrl;
                break;
            }

            // Fallback: any dependency value referencing the well-known fork URL
            // counts as a Zenject install even when the package id is unconventional.
            if (!info.ZenjectViaUpm) {
                foreach (var kv in deps) {
                    if (!kv.Value.Contains(ZenjectGitUrlMarker)) continue;
                    info.ZenjectViaUpm = true;
                    info.UpmPackageId = kv.Key;
                    info.UpmVersionOrUrl = kv.Value;
                    break;
                }
            }

            if (deps.TryGetValue(VContainerId, out var vcVersion)) {
                info.VContainerInstalled = true;
                info.VContainerVersion = vcVersion;
            }

            return info;
        }

        // Minimal manifest.json shape: { "dependencies": { "id": "version-or-url", ... } }
        // We deliberately avoid Newtonsoft.Json — JsonUtility cannot deserialise
        // a Dictionary<string,string> directly, so we use a tiny structural parser.
        private static Dictionary<string, string> ParseDependencies(string manifestJson) {
            var deps = new Dictionary<string, string>();
            var depsKeyIndex = manifestJson.IndexOf("\"dependencies\"", System.StringComparison.Ordinal);
            if (depsKeyIndex < 0) return deps;

            var openBrace = manifestJson.IndexOf('{', depsKeyIndex);
            if (openBrace < 0) return deps;

            int depth = 0;
            int closeBrace = -1;
            for (var i = openBrace; i < manifestJson.Length; i++) {
                if (manifestJson[i] == '{') depth++;
                else if (manifestJson[i] == '}') {
                    depth--;
                    if (depth == 0) { closeBrace = i; break; }
                }
            }
            if (closeBrace < 0) return deps;

            var body = manifestJson.Substring(openBrace + 1, closeBrace - openBrace - 1);
            var entries = body.Split(',');
            foreach (var rawEntry in entries) {
                var entry = rawEntry.Trim();
                if (entry.Length == 0) continue;
                var colon = entry.IndexOf(':');
                if (colon < 0) continue;
                var key = entry.Substring(0, colon).Trim().Trim('"');
                var value = entry.Substring(colon + 1).Trim().Trim('"');
                deps[key] = value;
            }
            return deps;
        }
    }
}
```

- [ ] **Step 4: Run tests, expect PASS**

Run all four tests in `ZenjectInstallDetectorTests`. Expected: all pass.

- [ ] **Step 5: Stage changes**

```bash
git add Editor/Core/Scanner/ZenjectInstallDetector.cs \
        Tests/Editor/ZenjectInstallDetectorTests.cs \
        Tests/Editor/Fixtures/ScannerInputs/Manifests
git status
```

---

## Task 11 — `ZenjectInstallDetector` (folder install detection)

**Files:**
- Modify: `Editor/Core/Scanner/ZenjectInstallDetector.cs`
- Modify: `Tests/Editor/ZenjectInstallDetectorTests.cs`

- [ ] **Step 1: Add a failing test**

Append to `ZenjectInstallDetectorTests`:

```csharp
        [Test]
        public void Detects_Folder_Install_When_Asset_Path_Exists() {
            var existing = new[] { "Assets/Plugins/Zenject", "Assets/SomethingElse" };
            var info = ZenjectInstallDetector.DetectFolderInstall(existing);
            Assert.IsTrue(info.ZenjectViaAssets);
            Assert.AreEqual("Assets/Plugins/Zenject", info.AssetsFolderPath);
        }

        [Test]
        public void Reports_No_Folder_Install_When_Absent() {
            var existing = new[] { "Assets/Scripts" };
            var info = ZenjectInstallDetector.DetectFolderInstall(existing);
            Assert.IsFalse(info.ZenjectViaAssets);
            Assert.IsNull(info.AssetsFolderPath);
        }
```

- [ ] **Step 2: Run, expect FAIL (no method)**

Compile error referencing `DetectFolderInstall`.

- [ ] **Step 3: Implement `DetectFolderInstall`**

Append to `ZenjectInstallDetector`:

```csharp
        // Probe candidate folder paths in priority order. Caller passes the list
        // of folders that exist on disk so this method stays Unity-free and
        // testable without AssetDatabase.
        private static readonly string[] CandidateFolders = {
            "Assets/Plugins/Zenject",
            "Assets/Zenject",
            "Assets/Plugins/Extenject",
            "Assets/Extenject"
        };

        public static InstallationInfo DetectFolderInstall(IReadOnlyList<string> existingFolders) {
            var info = new InstallationInfo();
            foreach (var candidate in CandidateFolders) {
                foreach (var existing in existingFolders) {
                    if (!string.Equals(existing, candidate, System.StringComparison.OrdinalIgnoreCase)) continue;
                    info.ZenjectViaAssets = true;
                    info.AssetsFolderPath = candidate;
                    return info;
                }
            }
            return info;
        }
```

- [ ] **Step 4: Run tests, expect PASS**

Both new tests pass; previously written tests still green.

- [ ] **Step 5: Stage changes**

```bash
git add Editor/Core/Scanner/ZenjectInstallDetector.cs Tests/Editor/ZenjectInstallDetectorTests.cs
git status
```

---

## Task 12 — `ZenjectScriptGuidTable`

**Files:**
- Create: `Editor/Core/Resources/zenject-script-guids.json`
- Create: `Editor/Core/Scanner/ZenjectScriptGuidTable.cs`
- Create: `Tests/Editor/ZenjectScriptGuidTableTests.cs`

- [ ] **Step 1: Write the bundled GUID table**

Path: `Editor/Core/Resources/zenject-script-guids.json`

```json
{
  "scripts": [
    { "name": "SceneContext",       "guid": "9c5c0fb73f5345e4eaa4ec9aa1c7be0d" },
    { "name": "ProjectContext",     "guid": "8d34a14ce6044a649a99dd1d4a3625a4" },
    { "name": "GameObjectContext",  "guid": "a45e58cd6e6b4d3aaeb0bf63d54f1d63" },
    { "name": "ZenjectBinding",     "guid": "5a9f4b13fbc4a274ba7e8e62a44c9d2c" },
    { "name": "MonoInstaller",      "guid": "84acd11dc1ce4d4c992eb12c4e16dc63" }
  ],
  "notes": "GUIDs sampled from Extenject 9.2.0. M6 ships a regeneration script."
}
```

> **Note:** GUID values above are illustrative and **must be replaced with real values** in this task. Procedure:
>
> 1. Install Extenject 9.2.0 in a throw-away Unity project.
> 2. Locate the `.cs.meta` file for each script under `Assets/Plugins/Zenject/...`.
> 3. Copy the `guid:` line value into the JSON above.

- [ ] **Step 2: Write the failing test**

Path: `Tests/Editor/ZenjectScriptGuidTableTests.cs`

```csharp
using NUnit.Framework;
using Zenject2VContainer.Core.Scanner;

namespace Zenject2VContainer.Tests {
    public class ZenjectScriptGuidTableTests {
        [Test]
        public void Loads_Bundled_Table_And_Resolves_SceneContext() {
            var table = ZenjectScriptGuidTable.LoadBundled();
            Assert.IsTrue(table.ContainsScript("SceneContext"));
            Assert.IsTrue(table.IsZenjectGuid(table.GetGuid("SceneContext")));
            Assert.IsFalse(table.IsZenjectGuid("00000000000000000000000000000000"));
        }
    }
}
```

- [ ] **Step 3: Run, expect FAIL (no class)**

Compile error.

- [ ] **Step 4: Implement `ZenjectScriptGuidTable`**

Path: `Editor/Core/Scanner/ZenjectScriptGuidTable.cs`

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Zenject2VContainer.Core.Scanner {
    public sealed class ZenjectScriptGuidTable {
        [Serializable]
        private sealed class Entry {
            public string name;
            public string guid;
        }

        [Serializable]
        private sealed class FileShape {
            public Entry[] scripts;
            public string notes;
        }

        private readonly Dictionary<string, string> _byName;
        private readonly HashSet<string> _allGuids;

        private ZenjectScriptGuidTable(Dictionary<string, string> byName) {
            _byName = byName;
            _allGuids = new HashSet<string>(byName.Values, StringComparer.OrdinalIgnoreCase);
        }

        public bool ContainsScript(string scriptName) => _byName.ContainsKey(scriptName);

        public string GetGuid(string scriptName) =>
            _byName.TryGetValue(scriptName, out var g) ? g : null;

        public bool IsZenjectGuid(string guid) =>
            !string.IsNullOrEmpty(guid) && _allGuids.Contains(guid);

        public IEnumerable<string> AllGuids => _allGuids;

        public static ZenjectScriptGuidTable LoadBundled() {
            // Resolve the JSON path relative to this assembly's source location.
            // The file is shipped alongside the assembly under Editor/Core/Resources/.
            var packageRoot = ResolvePackageRoot();
            var path = Path.Combine(packageRoot, "Editor", "Core", "Resources",
                "zenject-script-guids.json");
            var json = File.ReadAllText(path);
            return Parse(json);
        }

        public static ZenjectScriptGuidTable Parse(string json) {
            var shape = JsonUtility.FromJson<FileShape>(json);
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            if (shape?.scripts != null) {
                foreach (var e in shape.scripts) {
                    if (string.IsNullOrEmpty(e.name) || string.IsNullOrEmpty(e.guid)) continue;
                    dict[e.name] = e.guid;
                }
            }
            return new ZenjectScriptGuidTable(dict);
        }

        private static string ResolvePackageRoot() {
            // Search every Packages/* and the Assets/ tree for our package.json.
            // Cheap because we only do this once at startup.
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            foreach (var candidate in new[] {
                Path.Combine(projectRoot, "Packages", "com.zenject2vcontainer.migrator"),
                Path.Combine(projectRoot, "Assets", "com.zenject2vcontainer.migrator")
            }) {
                if (File.Exists(Path.Combine(candidate, "package.json"))) return candidate;
            }
            // Fallback: look at every package directory.
            var packagesRoot = Path.Combine(projectRoot, "Packages");
            if (Directory.Exists(packagesRoot)) {
                foreach (var dir in Directory.GetDirectories(packagesRoot)) {
                    var manifest = Path.Combine(dir, "package.json");
                    if (!File.Exists(manifest)) continue;
                    if (File.ReadAllText(manifest).Contains("com.zenject2vcontainer.migrator"))
                        return dir;
                }
            }
            throw new FileNotFoundException(
                "Could not locate Zenject2VContainer package root from: " + projectRoot);
        }
    }
}
```

- [ ] **Step 5: Run tests, expect PASS**

`Loads_Bundled_Table_And_Resolves_SceneContext` passes.

- [ ] **Step 6: Stage changes**

```bash
git add Editor/Core/Resources/zenject-script-guids.json \
        Editor/Core/Scanner/ZenjectScriptGuidTable.cs \
        Tests/Editor/ZenjectScriptGuidTableTests.cs
git status
```

---

## Task 13 — `AssetScanner` (YAML GUID matching)

**Files:**
- Create: `Editor/Core/Scanner/AssetScanner.cs`
- Create: `Tests/Editor/AssetScannerTests.cs`
- Create: `Tests/Editor/Fixtures/ScannerInputs/Yaml/with-scene-context.unity`
- Create: `Tests/Editor/Fixtures/ScannerInputs/Yaml/no-zenject.unity`

- [ ] **Step 1: Write fixture YAML**

Path: `Tests/Editor/Fixtures/ScannerInputs/Yaml/with-scene-context.unity`

```
%YAML 1.1
%TAG !u! tag:unity3d.com,2006:
--- !u!1 &111111
GameObject:
  m_ObjectHideFlags: 0
  m_Component:
  - component: {fileID: 222222}
  m_Name: Context
--- !u!114 &222222
MonoBehaviour:
  m_Script: {fileID: 11500000, guid: 9c5c0fb73f5345e4eaa4ec9aa1c7be0d, type: 3}
  m_Name:
```

Path: `Tests/Editor/Fixtures/ScannerInputs/Yaml/no-zenject.unity`

```
%YAML 1.1
%TAG !u! tag:unity3d.com,2006:
--- !u!1 &111111
GameObject:
  m_ObjectHideFlags: 0
  m_Name: Empty
```

> Replace `9c5c0fb73f5345e4eaa4ec9aa1c7be0d` with the SceneContext GUID from the table created in Task 12.

- [ ] **Step 2: Write the failing test**

Path: `Tests/Editor/AssetScannerTests.cs`

```csharp
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Zenject2VContainer.Core.Scanner;

namespace Zenject2VContainer.Tests {
    public class AssetScannerTests {
        private static string FixtureRoot =>
            Path.Combine(Application.dataPath, "..", "Packages",
                "com.zenject2vcontainer.migrator", "Tests", "Editor",
                "Fixtures", "ScannerInputs", "Yaml");

        [Test]
        public void Finds_Zenject_GuidLine_In_Scene() {
            var table = ZenjectScriptGuidTable.LoadBundled();
            var path = Path.Combine(FixtureRoot, "with-scene-context.unity");
            var findings = new List<Core.AssetFinding>();
            AssetScanner.ScanFile(path, table, findings);

            Assert.AreEqual(1, findings.Count);
            Assert.AreEqual("SceneContext", findings[0].ZenjectScriptName);
            Assert.AreEqual(path, findings[0].FilePath);
            Assert.Greater(findings[0].Line, 0);
        }

        [Test]
        public void Finds_Nothing_When_No_Zenject_References() {
            var table = ZenjectScriptGuidTable.LoadBundled();
            var path = Path.Combine(FixtureRoot, "no-zenject.unity");
            var findings = new List<Core.AssetFinding>();
            AssetScanner.ScanFile(path, table, findings);
            Assert.AreEqual(0, findings.Count);
        }
    }
}
```

- [ ] **Step 3: Run, expect FAIL**

Compile error referencing `AssetScanner`.

- [ ] **Step 4: Implement `AssetScanner`**

Path: `Editor/Core/Scanner/AssetScanner.cs`

```csharp
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.Core.Scanner {
    public static class AssetScanner {
        private static readonly Regex GuidLine = new Regex(
            @"guid:\s*([0-9a-fA-F]{32})",
            RegexOptions.Compiled);

        public static void ScanFile(string filePath, ZenjectScriptGuidTable table,
                                    List<AssetFinding> output) {
            using var reader = new StreamReader(filePath);
            string line;
            int lineNumber = 0;
            while ((line = reader.ReadLine()) != null) {
                lineNumber++;
                var match = GuidLine.Match(line);
                if (!match.Success) continue;
                var guid = match.Groups[1].Value;
                if (!table.IsZenjectGuid(guid)) continue;
                output.Add(new AssetFinding {
                    FilePath = filePath,
                    Line = lineNumber,
                    ZenjectGuid = guid,
                    ZenjectScriptName = ResolveName(table, guid)
                });
            }
        }

        public static void ScanDirectory(string rootDirectory,
                                         ZenjectScriptGuidTable table,
                                         List<AssetFinding> output) {
            string[] extensions = { ".unity", ".prefab", ".asset" };
            foreach (var path in Directory.EnumerateFiles(rootDirectory, "*",
                         SearchOption.AllDirectories)) {
                var ext = Path.GetExtension(path);
                if (System.Array.IndexOf(extensions, ext) < 0) continue;
                ScanFile(path, table, output);
            }
        }

        private static string ResolveName(ZenjectScriptGuidTable table, string guid) {
            // Linear scan over a tiny dictionary; optimise only if profiling shows a hotspot.
            foreach (var name in new[] {
                "SceneContext", "ProjectContext", "GameObjectContext",
                "ZenjectBinding", "MonoInstaller"
            }) {
                if (string.Equals(table.GetGuid(name), guid,
                        System.StringComparison.OrdinalIgnoreCase)) return name;
            }
            return "Unknown";
        }
    }
}
```

- [ ] **Step 5: Run tests, expect PASS**

Both `AssetScannerTests` cases pass.

- [ ] **Step 6: Stage changes**

```bash
git add Editor/Core/Scanner/AssetScanner.cs \
        Tests/Editor/AssetScannerTests.cs \
        Tests/Editor/Fixtures/ScannerInputs/Yaml
git status
```

---

## Task 14 — Bundle stub Zenject and VContainer reference DLLs

**Files:**
- Create: `Tests/Editor/References/Zenject.dll`
- Create: `Tests/Editor/References/VContainer.dll`
- Create: `Tests/Editor/References/README.md`
- Create: `Tests/Editor/References/build-stubs.csx` (or `.sh`/`.ps1` — pick whichever the executor prefers)

- [ ] **Step 1: Author stub source**

The Roslyn-driven scanner needs to *resolve* references to types like `Zenject.InjectAttribute` and `VContainer.IContainerBuilder` against actual `MetadataReference` objects. We do not need real implementations; only public surface signatures.

Create `Tests/Editor/References/build-stubs.csx`:

```csharp
// Run with `dotnet script build-stubs.csx` (requires `dotnet-script`).
// Produces Zenject.dll and VContainer.dll as MetadataReference fodder.

#r "nuget: Microsoft.CodeAnalysis.CSharp, 4.8.0"

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

void Compile(string assemblyName, string source, string outputPath) {
    var tree = CSharpSyntaxTree.ParseText(source);
    var refs = new[] {
        MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
        MetadataReference.CreateFromFile(typeof(System.Runtime.GCSettings).Assembly.Location)
    };
    var compilation = CSharpCompilation.Create(assemblyName,
        new[] { tree }, refs,
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
    var emitResult = compilation.Emit(outputPath);
    if (!emitResult.Success) {
        foreach (var d in emitResult.Diagnostics) System.Console.Error.WriteLine(d);
        throw new System.Exception("emit failed for " + assemblyName);
    }
}

var zenjectSource = """
namespace Zenject {
    public class InjectAttribute : System.Attribute {}
    public class InjectOptionalAttribute : System.Attribute {}
    public class DiContainer {
        public T Resolve<T>() => default;
        public T Instantiate<T>() => default;
        public BindStatement<T> Bind<T>() => default;
        public BindStatement<T> BindInterfacesTo<T>() => default;
    }
    public class BindStatement<T> {
        public BindStatement<T> To<U>() => default;
        public BindStatement<T> AsSingle() => default;
        public BindStatement<T> AsTransient() => default;
        public BindStatement<T> AsCached() => default;
        public BindStatement<T> FromInstance(object o) => default;
        public BindStatement<T> WithId(string id) => default;
    }
    public abstract class Installer {}
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
namespace UnityEngine {
    public class MonoBehaviour {}
    public class ScriptableObject {}
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

Compile("Zenject", zenjectSource, "Zenject.dll");
Compile("VContainer", vcontainerSource, "VContainer.dll");
System.Console.WriteLine("Stubs built.");
```

- [ ] **Step 2: Build the stubs**

On a developer workstation with `dotnet-script` installed:

```bash
cd Tests/Editor/References
dotnet script build-stubs.csx
```

Outputs `Zenject.dll` and `VContainer.dll` in the same folder.

- [ ] **Step 3: Mark DLLs as Editor-only platform-excluded**

In Unity, select each `.dll`. In the Inspector:

1. Uncheck **Auto Reference**.
2. Uncheck every platform under **Include Platforms** (the test runner loads them through `MetadataReference.CreateFromFile`, not Unity's plugin loader).
3. Apply.

- [ ] **Step 4: Write `README.md`**

Path: `Tests/Editor/References/README.md`

```markdown
# Reference stub assemblies

These DLLs are minimal compile-time surface stubs of Zenject and VContainer
used as Roslyn `MetadataReference` inputs in the test suite. They contain
no real behaviour and must not be loaded at runtime.

To regenerate, run from this directory:

    dotnet script build-stubs.csx

Sources are inline in the script. When the spec gains coverage of new
Zenject members, append signatures here, rebuild, and stage the new DLLs.
```

- [ ] **Step 5: Stage changes**

```bash
git add Tests/Editor/References
git status
```

---

## Task 15 — `CompilationLoader`

**Files:**
- Create: `Editor/Core/Scanner/CompilationLoader.cs`
- Create: `Tests/Editor/CompilationLoaderTests.cs`

- [ ] **Step 1: Write the failing test**

Path: `Tests/Editor/CompilationLoaderTests.cs`

```csharp
using System.IO;
using Microsoft.CodeAnalysis;
using NUnit.Framework;
using UnityEngine;
using Zenject2VContainer.Core.Scanner;

namespace Zenject2VContainer.Tests {
    public class CompilationLoaderTests {
        [Test]
        public void Builds_Compilation_From_Single_Source_String_With_Stub_References() {
            var refsRoot = Path.Combine(Application.dataPath, "..", "Packages",
                "com.zenject2vcontainer.migrator", "Tests", "Editor", "References");

            var refPaths = new[] {
                Path.Combine(refsRoot, "Zenject.dll"),
                Path.Combine(refsRoot, "VContainer.dll")
            };

            const string source = """
                using Zenject;
                public class Foo { [Inject] private DiContainer _c; }
                """;

            var compilation = CompilationLoader.BuildFromSources(
                "TestAssembly",
                new[] { ("Foo.cs", source) },
                refPaths);

            Assert.IsNotNull(compilation);
            var diagnostics = compilation.GetDiagnostics();
            foreach (var d in diagnostics) System.Console.WriteLine(d);
            // Compilation may report unrelated warnings; only fail on errors.
            foreach (var d in diagnostics) {
                Assert.AreNotEqual(DiagnosticSeverity.Error, d.Severity, d.ToString());
            }
        }
    }
}
```

- [ ] **Step 2: Run, expect FAIL (no class)**

Compile error.

- [ ] **Step 3: Implement `CompilationLoader`**

Path: `Editor/Core/Scanner/CompilationLoader.cs`

```csharp
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Zenject2VContainer.Core.Scanner {
    public static class CompilationLoader {
        public static CSharpCompilation BuildFromSources(
                string assemblyName,
                IEnumerable<(string FilePath, string Source)> sources,
                IEnumerable<string> referenceDllPaths) {

            var trees = new List<SyntaxTree>();
            foreach (var (path, src) in sources) {
                trees.Add(CSharpSyntaxTree.ParseText(src, path: path));
            }

            var refs = new List<MetadataReference> {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
            };
            foreach (var p in referenceDllPaths) {
                refs.Add(MetadataReference.CreateFromFile(p));
            }

            return CSharpCompilation.Create(
                assemblyName,
                trees,
                refs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }
    }
}
```

- [ ] **Step 4: Run, expect PASS**

Test passes; compilation reports no errors.

- [ ] **Step 5: Stage changes**

```bash
git add Editor/Core/Scanner/CompilationLoader.cs Tests/Editor/CompilationLoaderTests.cs
git status
```

---

## Task 16 — `SymbolMatchers`

**Files:**
- Create: `Editor/Core/Scanner/SymbolMatchers.cs`
- Create: `Tests/Editor/SymbolMatchersTests.cs`

- [ ] **Step 1: Write the failing test**

Path: `Tests/Editor/SymbolMatchersTests.cs`

```csharp
using System.IO;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using NUnit.Framework;
using UnityEngine;
using Zenject2VContainer.Core.Scanner;

namespace Zenject2VContainer.Tests {
    public class SymbolMatchersTests {
        private static string[] StubRefs() {
            var root = Path.Combine(Application.dataPath, "..", "Packages",
                "com.zenject2vcontainer.migrator", "Tests", "Editor", "References");
            return new[] {
                Path.Combine(root, "Zenject.dll"),
                Path.Combine(root, "VContainer.dll")
            };
        }

        [Test]
        public void IsZenjectAssembly_True_For_Zenject_Reference() {
            const string src = "using Zenject; public class A { [Inject] private DiContainer _c; }";
            var compilation = CompilationLoader.BuildFromSources(
                "T", new[] { ("A.cs", src) }, StubRefs());
            var tree = compilation.SyntaxTrees[0];
            var model = compilation.GetSemanticModel(tree);
            var attr = tree.GetRoot().DescendantNodes().OfType<AttributeSyntax>().First();
            var symbol = model.GetSymbolInfo(attr).Symbol?.ContainingType;
            Assert.IsTrue(SymbolMatchers.IsZenjectSymbol(symbol));
        }

        [Test]
        public void IsZenjectAssembly_False_For_System_Symbol() {
            const string src = "public class A { [System.Obsolete] public void Bar() {} }";
            var compilation = CompilationLoader.BuildFromSources(
                "T", new[] { ("A.cs", src) }, System.Array.Empty<string>());
            var tree = compilation.SyntaxTrees[0];
            var model = compilation.GetSemanticModel(tree);
            var attr = tree.GetRoot().DescendantNodes().OfType<AttributeSyntax>().First();
            var symbol = model.GetSymbolInfo(attr).Symbol?.ContainingType;
            Assert.IsFalse(SymbolMatchers.IsZenjectSymbol(symbol));
        }
    }
}
```

> Add `using System.Linq;` at the top of the test file.

- [ ] **Step 2: Run, expect FAIL (no class)**

- [ ] **Step 3: Implement `SymbolMatchers`**

Path: `Editor/Core/Scanner/SymbolMatchers.cs`

```csharp
using Microsoft.CodeAnalysis;

namespace Zenject2VContainer.Core.Scanner {
    public static class SymbolMatchers {
        // Assembly names we treat as "Zenject". Includes Extenject (same shipped name)
        // and the older modesttree fork.
        private static readonly string[] ZenjectAssemblyNames = {
            "Zenject",
            "Zenject-usage",
            "Zenject.ReflectionBaking.Mono"
        };

        public static bool IsZenjectSymbol(ISymbol symbol) {
            if (symbol == null) return false;
            var asm = symbol.ContainingAssembly?.Name;
            if (string.IsNullOrEmpty(asm)) return false;
            foreach (var name in ZenjectAssemblyNames) {
                if (string.Equals(asm, name, System.StringComparison.Ordinal)) return true;
            }
            return false;
        }

        public static bool IsZenjectInjectAttribute(INamedTypeSymbol type) {
            if (type == null) return false;
            return IsZenjectSymbol(type) &&
                   (type.Name == "InjectAttribute" || type.Name == "InjectOptionalAttribute");
        }

        public static bool IsDiContainerType(ITypeSymbol type) {
            if (type == null) return false;
            return IsZenjectSymbol(type) && type.Name == "DiContainer";
        }
    }
}
```

- [ ] **Step 4: Run, expect PASS**

Both tests green.

- [ ] **Step 5: Stage changes**

```bash
git add Editor/Core/Scanner/SymbolMatchers.cs Tests/Editor/SymbolMatchersTests.cs
git status
```

---

## Task 17 — `CSharpScanner`

**Files:**
- Create: `Editor/Core/Scanner/CSharpScanner.cs`
- Create: `Tests/Editor/CSharpScannerTests.cs`

- [ ] **Step 1: Write the failing test**

Path: `Tests/Editor/CSharpScannerTests.cs`

```csharp
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Zenject2VContainer.Core;
using Zenject2VContainer.Core.Scanner;

namespace Zenject2VContainer.Tests {
    public class CSharpScannerTests {
        private static string[] StubRefs() {
            var root = Path.Combine(Application.dataPath, "..", "Packages",
                "com.zenject2vcontainer.migrator", "Tests", "Editor", "References");
            return new[] {
                Path.Combine(root, "Zenject.dll"),
                Path.Combine(root, "VContainer.dll")
            };
        }

        [Test]
        public void Reports_Using_Inject_And_Bind() {
            const string src = """
                using Zenject;
                public class GameInstaller : MonoInstaller {
                    [Inject] private DiContainer _c;
                    public override void InstallBindings() {
                        Container.Bind<int>().To<int>().AsSingle();
                    }
                }
                """;
            var compilation = CompilationLoader.BuildFromSources(
                "T", new[] { ("GameInstaller.cs", src) }, StubRefs());

            var findings = CSharpScanner.Scan(compilation).ToList();

            Assert.IsTrue(findings.Any(f => f.Kind == CSharpFindingKind.UsingDirective));
            Assert.IsTrue(findings.Any(f => f.Kind == CSharpFindingKind.InjectAttribute));
            Assert.IsTrue(findings.Any(f => f.Kind == CSharpFindingKind.InstallerSubclass));
            Assert.IsTrue(findings.Any(f => f.Kind == CSharpFindingKind.BindCall));
        }

        [Test]
        public void Returns_Empty_For_Source_Without_Zenject() {
            const string src = "public class Plain { public int X; }";
            var compilation = CompilationLoader.BuildFromSources(
                "T", new[] { ("Plain.cs", src) }, StubRefs());
            var findings = CSharpScanner.Scan(compilation).ToList();
            Assert.AreEqual(0, findings.Count);
        }
    }
}
```

- [ ] **Step 2: Run, expect FAIL (no class)**

- [ ] **Step 3: Implement `CSharpScanner`**

Path: `Editor/Core/Scanner/CSharpScanner.cs`

```csharp
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.Core.Scanner {
    public static class CSharpScanner {
        public static IEnumerable<CSharpFinding> Scan(CSharpCompilation compilation) {
            foreach (var tree in compilation.SyntaxTrees) {
                var model = compilation.GetSemanticModel(tree);
                var root = tree.GetRoot();

                foreach (var u in root.DescendantNodes().OfType<UsingDirectiveSyntax>()) {
                    var name = u.Name?.ToString();
                    if (string.Equals(name, "Zenject", System.StringComparison.Ordinal)) {
                        yield return Make(tree, u, CSharpFindingKind.UsingDirective,
                            "Zenject", "using Zenject;", "High");
                    }
                }

                foreach (var attr in root.DescendantNodes().OfType<AttributeSyntax>()) {
                    var symbol = model.GetSymbolInfo(attr).Symbol?.ContainingType;
                    if (!SymbolMatchers.IsZenjectInjectAttribute(symbol)) continue;
                    yield return Make(tree, attr, CSharpFindingKind.InjectAttribute,
                        symbol.ToDisplayString(), attr.Parent?.ToString() ?? attr.ToString(),
                        symbol.Name == "InjectOptionalAttribute" ? "LowFlagged" : "High");
                }

                foreach (var cls in root.DescendantNodes().OfType<ClassDeclarationSyntax>()) {
                    var symbol = model.GetDeclaredSymbol(cls);
                    if (symbol == null) continue;
                    var basesAreZenject = false;
                    for (var b = symbol.BaseType; b != null; b = b.BaseType) {
                        if (!SymbolMatchers.IsZenjectSymbol(b)) continue;
                        if (b.Name == "MonoInstaller" || b.Name == "Installer" ||
                            b.Name == "ScriptableObjectInstaller") {
                            basesAreZenject = true;
                            break;
                        }
                    }
                    if (basesAreZenject) {
                        yield return Make(tree, cls, CSharpFindingKind.InstallerSubclass,
                            symbol.ToDisplayString(),
                            cls.Identifier.ToString(), "High");
                    }
                }

                foreach (var inv in root.DescendantNodes().OfType<InvocationExpressionSyntax>()) {
                    var symbol = model.GetSymbolInfo(inv).Symbol;
                    if (symbol == null) continue;
                    if (!SymbolMatchers.IsZenjectSymbol(symbol)) continue;
                    if (!IsBindOrFactoryRoot(symbol.Name)) continue;
                    yield return Make(tree, inv, CSharpFindingKind.BindCall,
                        symbol.ToDisplayString(),
                        inv.ToString(), "High");
                }
            }
        }

        private static bool IsBindOrFactoryRoot(string name) {
            switch (name) {
                case "Bind":
                case "BindInterfacesTo":
                case "BindInterfacesAndSelfTo":
                case "BindFactory":
                    return true;
                default:
                    return false;
            }
        }

        private static CSharpFinding Make(SyntaxTree tree, SyntaxNode node,
                                          CSharpFindingKind kind, string symbolName,
                                          string snippet, string confidence) {
            var pos = tree.GetLineSpan(node.Span);
            return new CSharpFinding {
                FilePath = tree.FilePath,
                Line = pos.StartLinePosition.Line + 1,
                Column = pos.StartLinePosition.Character + 1,
                Kind = kind,
                SymbolName = symbolName,
                Snippet = snippet.Length > 200 ? snippet.Substring(0, 200) : snippet,
                Confidence = confidence,
                Notes = ""
            };
        }
    }
}
```

> Add `using System.Linq;` at the top.

- [ ] **Step 4: Run tests, expect PASS**

Both `CSharpScannerTests` cases pass.

- [ ] **Step 5: Stage changes**

```bash
git add Editor/Core/Scanner/CSharpScanner.cs Tests/Editor/CSharpScannerTests.cs
git status
```

---

## Task 18 — `ProjectScanner` orchestrator

**Files:**
- Create: `Editor/Core/Scanner/ProjectScanner.cs`
- Create: `Tests/Editor/ProjectScannerTests.cs`

- [ ] **Step 1: Write the failing test**

Path: `Tests/Editor/ProjectScannerTests.cs`

```csharp
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using Zenject2VContainer.Core;
using Zenject2VContainer.Core.Scanner;

namespace Zenject2VContainer.Tests {
    public class ProjectScannerTests {
        private static string PkgRoot =>
            Path.Combine(Application.dataPath, "..", "Packages",
                "com.zenject2vcontainer.migrator");

        [Test]
        public void End_To_End_Scan_Reports_All_Three_Layers() {
            var manifestJson = File.ReadAllText(Path.Combine(PkgRoot,
                "Tests", "Editor", "Fixtures", "ScannerInputs", "Manifests",
                "extenject-upm.json"));

            var stubRefs = new[] {
                Path.Combine(PkgRoot, "Tests", "Editor", "References", "Zenject.dll"),
                Path.Combine(PkgRoot, "Tests", "Editor", "References", "VContainer.dll")
            };

            const string src = """
                using Zenject;
                public class GameInstaller : MonoInstaller {
                    [Inject] private DiContainer _c;
                    public override void InstallBindings() {}
                }
                """;

            var input = new ProjectScanner.Input {
                ToolVersion = "0.1.0",
                UnityVersion = "test",
                ScannedAtUtc = "2026-05-01T00:00:00Z",
                ManifestJson = manifestJson,
                ExistingFolders = new[] { "Assets/Plugins/Zenject" },
                CSharpSources = new[] { ("GameInstaller.cs", src) },
                ReferenceDllPaths = stubRefs,
                AssetFiles = new[] {
                    Path.Combine(PkgRoot, "Tests", "Editor", "Fixtures",
                        "ScannerInputs", "Yaml", "with-scene-context.unity")
                },
                GuidTable = ZenjectScriptGuidTable.LoadBundled()
            };

            var report = ProjectScanner.Run(input);

            Assert.IsTrue(report.Install.ZenjectViaUpm);
            Assert.IsTrue(report.Install.ZenjectViaAssets);
            Assert.Greater(report.CSharpFindings.Count, 0);
            Assert.Greater(report.AssetFindings.Count, 0);
        }
    }
}
```

- [ ] **Step 2: Run, expect FAIL**

`ProjectScanner` not yet defined.

- [ ] **Step 3: Implement `ProjectScanner`**

Path: `Editor/Core/Scanner/ProjectScanner.cs`

```csharp
using System.Collections.Generic;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.Core.Scanner {
    public static class ProjectScanner {
        public sealed class Input {
            public string ToolVersion;
            public string UnityVersion;
            public string ScannedAtUtc;
            public string ManifestJson;
            public IReadOnlyList<string> ExistingFolders;
            public IReadOnlyList<(string FilePath, string Source)> CSharpSources;
            public IReadOnlyList<string> ReferenceDllPaths;
            public IReadOnlyList<string> AssetFiles;
            public ZenjectScriptGuidTable GuidTable;
        }

        public static ZenjectUsageReport Run(Input input) {
            var report = new ZenjectUsageReport {
                ToolVersion = input.ToolVersion,
                UnityVersion = input.UnityVersion,
                ScannedAtUtc = input.ScannedAtUtc
            };

            var fromManifest = ZenjectInstallDetector.DetectFromManifestJson(input.ManifestJson);
            var fromFolders = ZenjectInstallDetector.DetectFolderInstall(input.ExistingFolders);
            report.Install = Merge(fromManifest, fromFolders);

            if (input.CSharpSources != null && input.CSharpSources.Count > 0) {
                var compilation = CompilationLoader.BuildFromSources(
                    "ScannedProject", input.CSharpSources, input.ReferenceDllPaths);
                foreach (var finding in CSharpScanner.Scan(compilation)) {
                    report.CSharpFindings.Add(finding);
                }
            }

            if (input.AssetFiles != null && input.GuidTable != null) {
                foreach (var path in input.AssetFiles) {
                    AssetScanner.ScanFile(path, input.GuidTable, report.AssetFindings);
                }
            }

            return report;
        }

        private static InstallationInfo Merge(InstallationInfo a, InstallationInfo b) =>
            new InstallationInfo {
                ZenjectViaUpm = a.ZenjectViaUpm || b.ZenjectViaUpm,
                UpmPackageId = a.UpmPackageId ?? b.UpmPackageId,
                UpmVersionOrUrl = a.UpmVersionOrUrl ?? b.UpmVersionOrUrl,
                ZenjectViaAssets = a.ZenjectViaAssets || b.ZenjectViaAssets,
                AssetsFolderPath = a.AssetsFolderPath ?? b.AssetsFolderPath,
                VContainerInstalled = a.VContainerInstalled || b.VContainerInstalled,
                VContainerVersion = a.VContainerVersion ?? b.VContainerVersion
            };
    }
}
```

- [ ] **Step 4: Run, expect PASS**

`End_To_End_Scan_Reports_All_Three_Layers` passes.

- [ ] **Step 5: Stage changes**

```bash
git add Editor/Core/Scanner/ProjectScanner.cs Tests/Editor/ProjectScannerTests.cs
git status
```

---

## Task 19 — `MigrationPipeline` skeleton + headless scan entry point

**Files:**
- Create: `Editor/Core/MigrationPipeline.cs`
- Create: `Editor/UI/MigrationMenu.cs`

- [ ] **Step 1: Write `MigrationPipeline`**

Path: `Editor/Core/MigrationPipeline.cs`

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using Zenject2VContainer.Core.Scanner;

namespace Zenject2VContainer.Core {
    public static class MigrationPipeline {
        public const string ToolVersion = "0.1.0";

        public static ZenjectUsageReport RunScanHeadless() {
            var projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            var manifestPath = Path.Combine(projectRoot, "Packages", "manifest.json");
            var manifestJson = File.Exists(manifestPath) ? File.ReadAllText(manifestPath) : "{}";

            var existingFolders = new List<string>();
            foreach (var f in new[] {
                "Assets/Plugins/Zenject", "Assets/Zenject",
                "Assets/Plugins/Extenject", "Assets/Extenject"
            }) {
                if (Directory.Exists(Path.Combine(projectRoot, f))) existingFolders.Add(f);
            }

            // C# sources from user assemblies (skip read-only packages and tool's own asmdefs).
            var sources = new List<(string, string)>();
            var refs = new List<string>();
            foreach (var asm in CompilationPipeline.GetAssemblies(AssembliesType.Editor)) {
                if (asm.name.StartsWith("Zenject2VContainer", StringComparison.Ordinal)) continue;
                foreach (var src in asm.sourceFiles) {
                    if (!File.Exists(src)) continue;
                    sources.Add((src, File.ReadAllText(src)));
                }
                foreach (var r in asm.compiledAssemblyReferences) {
                    if (File.Exists(r)) refs.Add(r);
                }
            }

            // Asset files: every .unity / .prefab / .asset under Assets/.
            var assetFiles = new List<string>();
            foreach (var p in Directory.EnumerateFiles(Path.Combine(projectRoot, "Assets"),
                         "*", SearchOption.AllDirectories)) {
                var ext = Path.GetExtension(p);
                if (ext == ".unity" || ext == ".prefab" || ext == ".asset") assetFiles.Add(p);
            }

            return ProjectScanner.Run(new ProjectScanner.Input {
                ToolVersion = ToolVersion,
                UnityVersion = Application.unityVersion,
                ScannedAtUtc = DateTime.UtcNow.ToString("o"),
                ManifestJson = manifestJson,
                ExistingFolders = existingFolders,
                CSharpSources = sources,
                ReferenceDllPaths = refs,
                AssetFiles = assetFiles,
                GuidTable = ZenjectScriptGuidTable.LoadBundled()
            });
        }

        public static string WriteScanReport(ZenjectUsageReport report, string outputPath) {
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
            File.WriteAllText(outputPath, ZenjectUsageReport.ToJson(report));
            return outputPath;
        }
    }
}
```

- [ ] **Step 2: Write the menu item**

Path: `Editor/UI/MigrationMenu.cs`

```csharp
using System.IO;
using UnityEditor;
using UnityEngine;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.UI {
    public static class MigrationMenu {
        [MenuItem("Window/Zenject2VContainer/Scan to JSON…")]
        public static void ScanToJson() {
            var defaultPath = Path.Combine(
                Path.GetFullPath(Path.Combine(Application.dataPath, "..")),
                "Library", "Zenject2VContainer", "scan.json");

            var picked = EditorUtility.SaveFilePanel(
                "Save Zenject scan report",
                Path.GetDirectoryName(defaultPath),
                Path.GetFileName(defaultPath),
                "json");
            if (string.IsNullOrEmpty(picked)) return;

            var report = MigrationPipeline.RunScanHeadless();
            MigrationPipeline.WriteScanReport(report, picked);
            EditorUtility.RevealInFinder(picked);
            Debug.Log($"[Zenject2VContainer] Scan written to {picked}");
        }
    }
}
```

- [ ] **Step 3: Verify Unity compiles and the menu appears**

Wait for compile. Open `Window > Zenject2VContainer > Scan to JSON…`. Expected: a save-file dialog opens.

- [ ] **Step 4: Stage changes**

```bash
git add Editor/Core/MigrationPipeline.cs Editor/UI/MigrationMenu.cs
git status
```

---

## Task 20 — `PreconditionChecks` scaffolding

**Files:**
- Create: `Editor/Core/PreconditionChecks.cs`
- Create: `Tests/Editor/PreconditionChecksTests.cs`

- [ ] **Step 1: Write the failing tests**

Path: `Tests/Editor/PreconditionChecksTests.cs`

```csharp
using NUnit.Framework;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.Tests {
    public class PreconditionChecksTests {
        [Test]
        public void Block_When_VContainer_Missing() {
            var info = new InstallationInfo { VContainerInstalled = false };
            var result = PreconditionChecks.CheckVContainerPresence(info);
            Assert.AreEqual(PreconditionResult.Severity.Block, result.Result);
            StringAssert.Contains("VContainer", result.Message);
        }

        [Test]
        public void Pass_When_VContainer_Present() {
            var info = new InstallationInfo { VContainerInstalled = true, VContainerVersion = "1.15.0" };
            var result = PreconditionChecks.CheckVContainerPresence(info);
            Assert.AreEqual(PreconditionResult.Severity.Pass, result.Result);
        }

        [Test]
        public void Warn_When_Git_Working_Tree_Dirty() {
            var result = PreconditionChecks.CheckGitState(isRepo: true, isDirty: true);
            Assert.AreEqual(PreconditionResult.Severity.Warn, result.Result);
        }

        [Test]
        public void Warn_When_Not_A_Git_Repo() {
            var result = PreconditionChecks.CheckGitState(isRepo: false, isDirty: false);
            Assert.AreEqual(PreconditionResult.Severity.Warn, result.Result);
        }

        [Test]
        public void Pass_When_Git_Clean() {
            var result = PreconditionChecks.CheckGitState(isRepo: true, isDirty: false);
            Assert.AreEqual(PreconditionResult.Severity.Pass, result.Result);
        }
    }
}
```

- [ ] **Step 2: Run, expect FAIL**

- [ ] **Step 3: Implement `PreconditionChecks`**

Path: `Editor/Core/PreconditionChecks.cs`

```csharp
namespace Zenject2VContainer.Core {
    public sealed class PreconditionResult {
        public enum Severity { Pass, Warn, Block }
        public Severity Result;
        public string Code;
        public string Message;
    }

    public static class PreconditionChecks {
        public static PreconditionResult CheckVContainerPresence(InstallationInfo install) =>
            install != null && install.VContainerInstalled
                ? new PreconditionResult {
                    Result = PreconditionResult.Severity.Pass,
                    Code = "VC_PRESENT",
                    Message = "VContainer present at " + (install.VContainerVersion ?? "unknown") + "."
                }
                : new PreconditionResult {
                    Result = PreconditionResult.Severity.Block,
                    Code = "VC_MISSING",
                    Message = "VContainer (jp.hadashikick.vcontainer) is not installed. Add it to manifest.json before applying migration."
                };

        public static PreconditionResult CheckGitState(bool isRepo, bool isDirty) {
            if (!isRepo) return new PreconditionResult {
                Result = PreconditionResult.Severity.Warn,
                Code = "GIT_NOT_REPO",
                Message = "Project is not a git repository. Tool backups still apply, but git revert is unavailable."
            };
            return isDirty
                ? new PreconditionResult {
                    Result = PreconditionResult.Severity.Warn,
                    Code = "GIT_DIRTY",
                    Message = "Working tree has uncommitted changes. Commit or stash before applying migration to keep rollback simple."
                }
                : new PreconditionResult {
                    Result = PreconditionResult.Severity.Pass,
                    Code = "GIT_CLEAN",
                    Message = "Working tree is clean."
                };
        }
    }
}
```

- [ ] **Step 4: Run, expect PASS**

All five tests green.

- [ ] **Step 5: Stage changes**

```bash
git add Editor/Core/PreconditionChecks.cs Tests/Editor/PreconditionChecksTests.cs
git status
```

---

## Task 21 — Smoke run the headless scan on the migrator's own repo

**Files:**
- (no new files; verification only)

- [ ] **Step 1: Open the migrator package in a host Unity project**

The package itself is not a Unity project. Create a throw-away host project (or use the `Tests/Integration/SmallProject` once M6 lands). For M1 verification:

1. Create a brand-new Unity project on disk.
2. In its `Packages/manifest.json`, add:
   ```json
   "com.zenject2vcontainer.migrator": "file:<absolute path to this repo>"
   ```
3. Open the host project. Wait for compilation.

- [ ] **Step 2: Run the menu item**

`Window > Zenject2VContainer > Scan to JSON…` → save the report somewhere.

- [ ] **Step 3: Inspect the JSON**

Open the saved JSON. Expected shape:

```json
{
  "ToolVersion": "0.1.0",
  "UnityVersion": "...",
  "ScannedAtUtc": "...",
  "CSharpFindings": [],
  "AssetFindings": [],
  "Install": {
    "ZenjectViaUpm": false,
    "UpmPackageId": null,
    "UpmVersionOrUrl": null,
    "ZenjectViaAssets": false,
    "AssetsFolderPath": null,
    "VContainerInstalled": false,
    "VContainerVersion": null
  },
  "Unsupported": []
}
```

The host project has no Zenject installed, so all lists must be empty.

- [ ] **Step 4: Re-run after installing Extenject in the host project**

1. Add `"com.svermeulen.extenject": "9.2.0"` to host's `manifest.json`.
2. Wait for import.
3. Re-run the menu item.
4. Confirm `Install.ZenjectViaUpm` is `true` and `UpmPackageId` is `"com.svermeulen.extenject"`.

- [ ] **Step 5: Document the smoke run**

Append a short note to `docs/superpowers/plans/2026-05-01-z2vc-m1-foundation-and-scanner.md` under a new "Smoke run notes" section, listing the Unity version used and the resulting JSON snippet. Stage that doc change.

```bash
git add docs/superpowers/plans/2026-05-01-z2vc-m1-foundation-and-scanner.md
git status
```

---

## M1 done when

- [ ] All 21 tasks above are checked off.
- [ ] `Window > General > Test Runner` (EditMode) shows every `Zenject2VContainer.Tests` test green.
- [ ] `Window > Zenject2VContainer > Scan to JSON…` produces a valid `ZenjectUsageReport` JSON file in a host project that:
  - has no Zenject → empty findings, `Install.ZenjectViaUpm == false`.
  - has Extenject installed via UPM → `Install.ZenjectViaUpm == true`.
- [ ] All changes are staged but not committed; the repo owner reviews and lands them.

---

## Self-review checklist

1. **Spec coverage (M1 portion).** Every M1-relevant requirement in the spec has at least one task:
   - §3.1 directory layout: tasks 2, 3, 4, 5.
   - §4.1 Scan phase: tasks 13, 17, 18, 19.
   - §4.6 preconditions scaffolding: task 20.
   - §10 distribution (UPM package.json): task 1.
   - §11.1 snapshot test infra (NUnit + asmdef): tasks 5, 7.
   - §12 logging: deferred to M4 (acceptable; M1 only writes JSON).
   - §13 idempotency: scanner is naturally idempotent (no writes).

2. **Placeholder scan.** No "TBD" markers. Two acknowledged data placeholders:
   - `zenject-script-guids.json` ships illustrative GUIDs that the executor *must* replace with real values from an actual Extenject 9.2.0 install during Task 12 step 1. The procedure is in the task.
   - The fixture `with-scene-context.unity` references the same SceneContext GUID; same procedure.

3. **Type / signature consistency.** `CSharpFinding`, `AssetFinding`, `InstallationInfo`, `UnsupportedFeature`, and `ZenjectUsageReport` are introduced in Task 8 and used everywhere else with the same field names. `ProjectScanner.Input` and `ProjectScanner.Run` match between definition (Task 18) and usage (Task 19).

4. **Stage-only, no commits.** Every task ends with `git add` + `git status`, never `git commit`. The plan header restates the policy.

---

## Execution handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-01-z2vc-m1-foundation-and-scanner.md`.

Two execution options:

1. **Subagent-Driven (recommended)** — dispatch a fresh subagent per task, review between tasks, fast iteration.
2. **Inline Execution** — execute tasks in this session using `superpowers:executing-plans`, batch execution with checkpoints.

Which approach?
