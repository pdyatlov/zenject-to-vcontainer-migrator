# M2 — C# Migration Engine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Commit policy:** Repository owner manages commits. Tasks may end with either `git add` only (default) or include explicit `git commit` if the owner has authorised committing during this milestone. Default is stage-only.

**Goal:** Implement Roslyn-driven C# rewriters that translate Zenject source files to VContainer equivalents per spec §5.1–5.4, 5.7–5.9. Produce a `MigrationPipeline.RunCSharpHeadless()` entry point that, given a project, emits a `MigrationPlan` whose `NewText` for each `.cs` file is the migrated output. Cover ~30 snapshot fixtures.

**Architecture:** Per-feature `CSharpSyntaxRewriter` subclasses. Each rewriter walks one syntactic shape, queries `SemanticModel` to confirm Zenject identity, emits replacement syntax, and records `// TODO: MIGRATE-MANUAL` markers for unsupported patterns. A pipeline class composes rewriters in dependency order. Snapshot tests under `Tests/Editor/Fixtures/CSharpRewriters/<name>/` exercise each rule.

**Tech Stack:** C# 9, `Microsoft.CodeAnalysis.CSharp` 4.8.0, NUnit, vendored stubs from M1 (`Tests/Editor/References~/`).

**Out of scope (deferred to later milestones):**
- Scene/prefab/asset YAML edits (M3).
- ZenjectBinding component replacement (M3).
- Editor wizard UI (M4).
- Zenject removal (M5).

---

## File map

Files this milestone creates or substantially modifies.

| Path                                                                              | Responsibility                                                            |
|-----------------------------------------------------------------------------------|---------------------------------------------------------------------------|
| `Editor/CSharp/Rewriters/UsingDirectiveRewriter.cs`                               | `using Zenject;` → `using VContainer;` (+ `VContainer.Unity` when needed) |
| `Editor/CSharp/Rewriters/InjectAttributeRewriter.cs`                              | Preserves `[Inject]`; emits manual TODO for `[InjectOptional]`            |
| `Editor/CSharp/Rewriters/BindToAsRewriter.cs`                                     | `Bind/To/As*/From*/WithId` chain → VContainer `Register*` chain           |
| `Editor/CSharp/Rewriters/InstallerRewriter.cs`                                    | `MonoInstaller` / `Installer<T>` / `ScriptableObjectInstaller` → `IInstaller` |
| `Editor/CSharp/Rewriters/TickableInitializableRewriter.cs`                        | `IInitializable` → `IStartable`; tickables namespace migration            |
| `Editor/CSharp/Rewriters/FactoryRewriter.cs`                                      | `PlaceholderFactory` + `BindFactory` → `Func<>` + `RegisterFactory`       |
| `Editor/CSharp/Rewriters/SubContainerRewriter.cs`                                 | Simple `FromSubContainerResolve.ByInstaller<>` → child LifetimeScope      |
| `Editor/CSharp/Rewriters/DiContainerUsageRewriter.cs`                             | `DiContainer` member → `IObjectResolver`; Resolve / Instantiate adjustments |
| `Editor/CSharp/RewriterBase.cs`                                                   | Shared base: SemanticModel access, manual TODO emission, line tracking    |
| `Editor/CSharp/ManualTodoEmitter.cs`                                              | Builds `// TODO: MIGRATE-MANUAL [Category]` comments with consistent shape |
| `Editor/CSharp/RewritePipeline.cs`                                                | Composes all rewriters; runs in order; collects findings                  |
| `Editor/Core/MigrationPlan.cs`                                                    | Plan data model (FileChange, Category, Confidence, RelatedFindings)       |
| `Editor/Core/MigrationPipeline.cs` (extended)                                     | New entry point `RunCSharpHeadless`                                       |
| `Editor/UI/MigrationMenu.cs` (extended)                                           | New menu item `Preview C# Migration…`                                     |
| `Tests/Editor/SnapshotRunner.cs`                                                  | Generic NUnit harness that walks `Tests/Editor/Fixtures/CSharpRewriters/` |
| `Tests/Editor/Fixtures/CSharpRewriters/<feature>/`                                | One folder per snapshot fixture (input / expected / meta.json)            |
| `Tests/Editor/RewritePipelineTests.cs`                                            | End-to-end test of the composed pipeline                                  |

Note: per spec §3.1 the rewriter classes live under `Editor/CSharp/Rewriters/`; M1 created the parent `Editor/Core/Scanner/` folder. M2 introduces `Editor/CSharp/`.

---

## Task 1 — Snapshot test infrastructure

**Files:**
- Create: `Tests/Editor/SnapshotRunner.cs`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/Sample_Passthrough/input/Foo.cs`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/Sample_Passthrough/expected/Foo.cs`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/Sample_Passthrough/meta.json`

- [ ] **Step 1: Define fixture meta shape**

`meta.json` schema (one per fixture):

```json
{
  "description": "Short human-readable description.",
  "rewriters": ["UsingDirectiveRewriter"],
  "expectedManualTodoCategories": [],
  "extraReferences": []
}
```

- `rewriters` lists which rewriters to apply (in declared order). `["*"]` means full pipeline.
- `expectedManualTodoCategories` is the list of `[Category]` strings the snapshot test asserts must appear in the output (e.g. `["InjectOptional"]`).
- `extraReferences` are extra DLL paths beyond the bundled stubs.

- [ ] **Step 2: Write a no-op `Sample_Passthrough` fixture**

`Tests/Editor/Fixtures/CSharpRewriters/Sample_Passthrough/input/Foo.cs`:

```csharp
public class Foo
{
    public int Bar() { return 0; }
}
```

`Tests/Editor/Fixtures/CSharpRewriters/Sample_Passthrough/expected/Foo.cs`: identical content.

`Tests/Editor/Fixtures/CSharpRewriters/Sample_Passthrough/meta.json`:

```json
{
  "description": "Source with no Zenject usage must pass through unchanged.",
  "rewriters": ["*"],
  "expectedManualTodoCategories": [],
  "extraReferences": []
}
```

- [ ] **Step 3: Implement `SnapshotRunner`**

Path: `Tests/Editor/SnapshotRunner.cs`

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Zenject2VContainer.CSharp;
using Zenject2VContainer.Core;
using Zenject2VContainer.Core.Scanner;

namespace Zenject2VContainer.Tests {
    public sealed class SnapshotRunner {
        [Serializable]
        private sealed class FixtureMeta {
            public string description;
            public string[] rewriters;
            public string[] expectedManualTodoCategories;
            public string[] extraReferences;
        }

        public static IEnumerable<TestCaseData> Fixtures() {
            var root = Path.Combine(Application.dataPath, "..", "Packages",
                "com.zenject2vcontainer.migrator", "Tests", "Editor",
                "Fixtures", "CSharpRewriters");
            if (!Directory.Exists(root)) yield break;
            foreach (var dir in Directory.GetDirectories(root)) {
                yield return new TestCaseData(dir).SetName(Path.GetFileName(dir));
            }
        }

        [TestCaseSource(nameof(Fixtures))]
        public void Snapshot_Roundtrip(string fixtureDir) {
            var meta = LoadMeta(fixtureDir);
            var inputDir = Path.Combine(fixtureDir, "input");
            var expectedDir = Path.Combine(fixtureDir, "expected");

            var inputs = LoadSources(inputDir);
            var expected = LoadSources(expectedDir);

            var stubsRoot = Path.Combine(Application.dataPath, "..", "Packages",
                "com.zenject2vcontainer.migrator", "Tests", "Editor", "References~");
            var refs = new List<string> {
                Path.Combine(stubsRoot, "UnityEngine.dll"),
                Path.Combine(stubsRoot, "Zenject.dll"),
                Path.Combine(stubsRoot, "VContainer.dll")
            };
            if (meta.extraReferences != null) refs.AddRange(meta.extraReferences);

            var compilation = CompilationLoader.BuildFromSources(
                "FixtureCompilation",
                inputs.Select(kv => (kv.Key, kv.Value)),
                refs);

            var pipeline = new RewritePipeline(meta.rewriters ?? new[] { "*" });
            var changes = pipeline.Run(compilation);

            // Build actual output map.
            var actual = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in inputs) actual[kv.Key] = kv.Value; // start from inputs
            foreach (var change in changes) {
                actual[Path.GetFileName(change.OriginalPath)] = change.NewText;
            }

            foreach (var fname in expected.Keys) {
                Assert.IsTrue(actual.ContainsKey(fname),
                    "expected file " + fname + " missing from migration output");
                AssertTextEqual(expected[fname], actual[fname], fname);
            }

            // Verify expected manual TODO categories.
            var emittedCats = new HashSet<string>(
                changes.SelectMany(c => c.RelatedFindings)
                    .Where(f => f.Category != null)
                    .Select(f => f.Category));
            foreach (var expectedCat in meta.expectedManualTodoCategories ?? new string[0]) {
                Assert.IsTrue(emittedCats.Contains(expectedCat),
                    "expected manual TODO [" + expectedCat + "] not found");
            }
        }

        private static Dictionary<string, string> LoadSources(string dir) {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(dir)) return map;
            foreach (var file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)) {
                map[Path.GetFileName(file)] = File.ReadAllText(file);
            }
            return map;
        }

        private static FixtureMeta LoadMeta(string fixtureDir) {
            var path = Path.Combine(fixtureDir, "meta.json");
            var json = File.Exists(path) ? File.ReadAllText(path) : "{}";
            return JsonUtility.FromJson<FixtureMeta>(json) ?? new FixtureMeta();
        }

        private static void AssertTextEqual(string expected, string actual, string name) {
            // Normalise line endings: comparing to expected with \n only.
            var e = expected.Replace("\r\n", "\n");
            var a = actual.Replace("\r\n", "\n");
            if (e == a) return;
            Assert.Fail("snapshot mismatch in " + name + "\nEXPECTED:\n" + e + "\nACTUAL:\n" + a);
        }
    }
}
```

- [ ] **Step 4: Add stub `RewritePipeline` so tests compile**

Path: `Editor/CSharp/RewritePipeline.cs`

```csharp
using System.Collections.Generic;
using Microsoft.CodeAnalysis.CSharp;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.CSharp {
    public sealed class RewritePipeline {
        private readonly string[] _rewriterFilter;
        public RewritePipeline(string[] rewriterFilter) {
            _rewriterFilter = rewriterFilter ?? new[] { "*" };
        }

        public IReadOnlyList<FileChange> Run(CSharpCompilation compilation) {
            // Stubbed: returns no changes. Real rewriters added in Tasks 4–15.
            return new List<FileChange>();
        }
    }
}
```

(`FileChange` is created in Task 3; for now the stub returns an empty list of an internal placeholder type. If the placeholder type does not yet exist, define a temporary `internal sealed class FileChange { public string OriginalPath; public string NewText; public List<Finding> RelatedFindings = new(); }` in the same file — Task 3 replaces it.)

- [ ] **Step 5: Run snapshot tests, expect `Sample_Passthrough` PASS**

In Unity Test Runner. Other tests added in later tasks. No commits.

- [ ] **Step 6: Stage**

```bash
git add Editor/CSharp/RewritePipeline.cs \
        Tests/Editor/SnapshotRunner.cs \
        Tests/Editor/Fixtures/CSharpRewriters/Sample_Passthrough
```

---

## Task 2 — `MigrationPlan` data model + `ManualTodoEmitter`

**Files:**
- Create: `Editor/Core/MigrationPlan.cs`
- Create: `Editor/CSharp/ManualTodoEmitter.cs`
- Create: `Tests/Editor/ManualTodoEmitterTests.cs`

- [ ] **Step 1: Write `MigrationPlan.cs`**

Path: `Editor/Core/MigrationPlan.cs`

```csharp
using System;
using System.Collections.Generic;

namespace Zenject2VContainer.Core {
    public enum FileChangeCategory { CSharp, Yaml, Manifest }
    public enum ChangeConfidence { High, Medium, LowFlagged }

    [Serializable]
    public sealed class Finding {
        public string Category;       // e.g. "SignalBus", "InjectOptional"
        public string FilePath;
        public int Line;
        public string Reason;
        public string DocLink;
    }

    [Serializable]
    public sealed class FileChange {
        public string OriginalPath;
        public string OriginalText;
        public string NewText;
        public FileChangeCategory Category;
        public ChangeConfidence Confidence;
        public List<Finding> RelatedFindings = new List<Finding>();
    }

    [Serializable]
    public sealed class MigrationPlan {
        public List<FileChange> Changes = new List<FileChange>();
        public List<Finding> Unsupported = new List<Finding>();
    }
}
```

- [ ] **Step 2: Write `ManualTodoEmitter.cs`**

Path: `Editor/CSharp/ManualTodoEmitter.cs`

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.CSharp {
    public static class ManualTodoEmitter {
        // Categories live as constants so rewriters reference them through one place.
        public const string SignalBus = "SignalBus";
        public const string MemoryPool = "MemoryPool";
        public const string ConditionalBind = "ConditionalBind";
        public const string InjectOptional = "InjectOptional";
        public const string ComplexSubContainer = "ComplexSubContainer";
        public const string InstantiateUnregistered = "InstantiateUnregistered";
        public const string Decorator = "Decorator";
        public const string CustomFactory = "CustomFactory";
        public const string CustomDiContainerExtension = "CustomDiContainerExtension";

        public static SyntaxTriviaList Build(string category, string reason, string docLink) {
            var lines = new[] {
                "// TODO: MIGRATE-MANUAL [" + category + "]",
                "// Reason: " + reason,
                "// Suggested: see " + docLink,
                "// Original code preserved below — review and rewrite."
            };
            var triviaList = new System.Collections.Generic.List<SyntaxTrivia>();
            foreach (var line in lines) {
                triviaList.Add(SyntaxFactory.Comment(line));
                triviaList.Add(SyntaxFactory.EndOfLine("\n"));
            }
            return SyntaxFactory.TriviaList(triviaList);
        }

        public static Finding ToFinding(string category, string filePath, int line, string reason) {
            return new Finding {
                Category = category,
                FilePath = filePath,
                Line = line,
                Reason = reason,
                DocLink = "https://github.com/<owner>/<repo>/blob/main/docs/manual-todos/" + category + ".md"
            };
        }
    }
}
```

- [ ] **Step 3: Write the failing test**

Path: `Tests/Editor/ManualTodoEmitterTests.cs`

```csharp
using NUnit.Framework;
using Zenject2VContainer.CSharp;

namespace Zenject2VContainer.Tests {
    public class ManualTodoEmitterTests {
        [Test]
        public void Emits_Triple_Comment_Trivia_For_Category() {
            var trivia = ManualTodoEmitter.Build(ManualTodoEmitter.SignalBus,
                "no equivalent", "docs.md");
            var text = trivia.ToFullString();
            StringAssert.Contains("MIGRATE-MANUAL [SignalBus]", text);
            StringAssert.Contains("no equivalent", text);
            StringAssert.Contains("docs.md", text);
        }

        [Test]
        public void Builds_Finding_With_Required_Fields() {
            var finding = ManualTodoEmitter.ToFinding(ManualTodoEmitter.MemoryPool,
                "Foo.cs", 12, "needs IObjectPool");
            Assert.AreEqual("MemoryPool", finding.Category);
            Assert.AreEqual("Foo.cs", finding.FilePath);
            Assert.AreEqual(12, finding.Line);
            Assert.IsNotNull(finding.DocLink);
        }
    }
}
```

- [ ] **Step 4: Run, expect PASS**

- [ ] **Step 5: Stage**

```bash
git add Editor/Core/MigrationPlan.cs \
        Editor/CSharp/ManualTodoEmitter.cs \
        Tests/Editor/ManualTodoEmitterTests.cs
```

After this task, replace the placeholder `FileChange` / `Finding` types in `RewritePipeline.cs` with the real ones from `Zenject2VContainer.Core`.

---

## Task 3 — `RewriterBase`

**Files:**
- Create: `Editor/CSharp/RewriterBase.cs`

- [ ] **Step 1: Write `RewriterBase`**

Path: `Editor/CSharp/RewriterBase.cs`

```csharp
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.CSharp {
    public abstract class RewriterBase : CSharpSyntaxRewriter {
        protected SemanticModel Model { get; private set; }
        protected SyntaxTree CurrentTree { get; private set; }
        public List<Finding> Findings { get; } = new List<Finding>();

        public abstract string Name { get; }

        public SyntaxNode Apply(SyntaxNode root, SemanticModel model, SyntaxTree tree) {
            Model = model;
            CurrentTree = tree;
            return Visit(root);
        }

        protected void EmitManualTodo(string category, SyntaxNode anchor, string reason) {
            var span = CurrentTree.GetLineSpan(anchor.Span);
            Findings.Add(ManualTodoEmitter.ToFinding(category, CurrentTree.FilePath,
                span.StartLinePosition.Line + 1, reason));
        }

        protected static MemberAccessExpressionSyntax MemberAccess(string lhs, string rhs) {
            return SyntaxFactory.MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                SyntaxFactory.IdentifierName(lhs),
                SyntaxFactory.IdentifierName(rhs));
        }
    }
}
```

- [ ] **Step 2: Verify compile.** No tests added here; covered indirectly through later rewriter tests.

- [ ] **Step 3: Stage**

```bash
git add Editor/CSharp/RewriterBase.cs
```

---

## Task 4 — `UsingDirectiveRewriter`

**Files:**
- Create: `Editor/CSharp/Rewriters/UsingDirectiveRewriter.cs`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/UsingDirective_Basic/{input,expected}/Foo.cs`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/UsingDirective_Basic/meta.json`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/UsingDirective_AliasedNamespace/{input,expected}/Foo.cs`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/UsingDirective_AliasedNamespace/meta.json`

- [ ] **Step 1: Define fixtures**

`UsingDirective_Basic/input/Foo.cs`:

```csharp
using Zenject;

public class Foo
{
    public int Bar() { return 0; }
}
```

`UsingDirective_Basic/expected/Foo.cs`:

```csharp
using VContainer;

public class Foo
{
    public int Bar() { return 0; }
}
```

`UsingDirective_Basic/meta.json`:

```json
{
  "description": "Plain using Zenject; replaced by using VContainer;.",
  "rewriters": ["UsingDirectiveRewriter"],
  "expectedManualTodoCategories": [],
  "extraReferences": []
}
```

`UsingDirective_AliasedNamespace/input/Foo.cs`:

```csharp
using Z = Zenject;

public class Foo
{
    public Z.DiContainer Container;
}
```

`UsingDirective_AliasedNamespace/expected/Foo.cs`:

```csharp
using Z = VContainer;

public class Foo
{
    public Z.DiContainer Container;
}
```

(`Z.DiContainer` body is deliberately not yet rewritten — only the using is touched in this rewriter; later rewriters handle `DiContainer` references.)

`UsingDirective_AliasedNamespace/meta.json`:

```json
{
  "description": "Aliased using updates the right-hand side; alias preserved.",
  "rewriters": ["UsingDirectiveRewriter"],
  "expectedManualTodoCategories": [],
  "extraReferences": []
}
```

- [ ] **Step 2: Implement the rewriter**

Path: `Editor/CSharp/Rewriters/UsingDirectiveRewriter.cs`

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Zenject2VContainer.CSharp.Rewriters {
    public sealed class UsingDirectiveRewriter : RewriterBase {
        public override string Name => nameof(UsingDirectiveRewriter);

        public bool RequiresVContainerUnity { get; private set; }

        public override SyntaxNode VisitUsingDirective(UsingDirectiveSyntax node) {
            var name = node.Name?.ToString();
            if (string.Equals(name, "Zenject", System.StringComparison.Ordinal)) {
                return node.WithName(SyntaxFactory.ParseName("VContainer")
                    .WithTriviaFrom(node.Name));
            }
            return base.VisitUsingDirective(node);
        }

        // Caller may also call this to add `using VContainer.Unity;` after another
        // rewriter has signalled that Unity-specific surface is needed.
        public static CompilationUnitSyntax EnsureVContainerUnityUsing(CompilationUnitSyntax root) {
            foreach (var u in root.Usings) {
                if (u.Name?.ToString() == "VContainer.Unity") return root;
            }
            var newUsing = SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("VContainer.Unity"))
                .WithTrailingTrivia(SyntaxFactory.EndOfLine("\n"));
            return root.AddUsings(newUsing);
        }
    }
}
```

- [ ] **Step 3: Wire into pipeline**

Edit `Editor/CSharp/RewritePipeline.cs` `Run` method. Pseudocode of the structural change:

```csharp
public IReadOnlyList<FileChange> Run(CSharpCompilation compilation) {
    var changes = new List<FileChange>();
    foreach (var tree in compilation.SyntaxTrees) {
        var model = compilation.GetSemanticModel(tree);
        var root = tree.GetRoot();
        var current = root;

        if (Includes(nameof(UsingDirectiveRewriter))) {
            var r = new Rewriters.UsingDirectiveRewriter();
            current = r.Apply(current, model, tree);
            // findings collected if any
        }

        if (current != root) {
            changes.Add(new FileChange {
                OriginalPath = tree.FilePath,
                OriginalText = root.ToFullString(),
                NewText = current.ToFullString(),
                Category = FileChangeCategory.CSharp,
                Confidence = ChangeConfidence.High
            });
        }
    }
    return changes;
}

private bool Includes(string name) =>
    System.Array.IndexOf(_rewriterFilter, "*") >= 0 ||
    System.Array.IndexOf(_rewriterFilter, name) >= 0;
```

- [ ] **Step 4: Run snapshot tests**

Both `UsingDirective_Basic` and `UsingDirective_AliasedNamespace` PASS.

- [ ] **Step 5: Stage**

```bash
git add Editor/CSharp/RewritePipeline.cs \
        Editor/CSharp/Rewriters/UsingDirectiveRewriter.cs \
        Tests/Editor/Fixtures/CSharpRewriters/UsingDirective_Basic \
        Tests/Editor/Fixtures/CSharpRewriters/UsingDirective_AliasedNamespace
```

---

## Task 5 — `InjectAttributeRewriter`

**Files:**
- Create: `Editor/CSharp/Rewriters/InjectAttributeRewriter.cs`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/Inject_FieldAndMethod/{input,expected}/Foo.cs`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/Inject_FieldAndMethod/meta.json`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/Inject_Optional_Manual/{input,expected}/Foo.cs`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/Inject_Optional_Manual/meta.json`

- [ ] **Step 1: Fixture `Inject_FieldAndMethod`**

`input/Foo.cs`:

```csharp
using Zenject;

public class Foo
{
    [Inject] private IBar _bar;
    [Inject] public IBaz Baz { get; private set; }

    [Inject]
    public void Construct(IQux qux) { }
}
```

`expected/Foo.cs`:

```csharp
using VContainer;

public class Foo
{
    [Inject] private IBar _bar;
    [Inject] public IBaz Baz { get; private set; }

    [Inject]
    public void Construct(IQux qux) { }
}
```

`meta.json`:

```json
{
  "description": "[Inject] preserved verbatim; only the using namespace changes.",
  "rewriters": ["UsingDirectiveRewriter", "InjectAttributeRewriter"],
  "expectedManualTodoCategories": [],
  "extraReferences": []
}
```

- [ ] **Step 2: Fixture `Inject_Optional_Manual`**

`input/Foo.cs`:

```csharp
using Zenject;

public class Foo
{
    [InjectOptional] private IBar _maybeBar;
}
```

`expected/Foo.cs`:

```csharp
using VContainer;

public class Foo
{
    // TODO: MIGRATE-MANUAL [InjectOptional]
    // Reason: VContainer has no direct field/method [InjectOptional]; constructor defaults work.
    // Suggested: see https://github.com/<owner>/<repo>/blob/main/docs/manual-todos/InjectOptional.md
    // Original code preserved below — review and rewrite.
    [InjectOptional] private IBar _maybeBar;
}
```

`meta.json`:

```json
{
  "description": "[InjectOptional] flagged as manual TODO with original code preserved.",
  "rewriters": ["UsingDirectiveRewriter", "InjectAttributeRewriter"],
  "expectedManualTodoCategories": ["InjectOptional"],
  "extraReferences": []
}
```

- [ ] **Step 3: Implement**

Path: `Editor/CSharp/Rewriters/InjectAttributeRewriter.cs`

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Zenject2VContainer.Core.Scanner;

namespace Zenject2VContainer.CSharp.Rewriters {
    public sealed class InjectAttributeRewriter : RewriterBase {
        public override string Name => nameof(InjectAttributeRewriter);

        public override SyntaxNode VisitFieldDeclaration(FieldDeclarationSyntax node) {
            return TagOptionalIfNeeded(node, base.VisitFieldDeclaration(node));
        }

        public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node) {
            return TagOptionalIfNeeded(node, base.VisitMethodDeclaration(node));
        }

        public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node) {
            return TagOptionalIfNeeded(node, base.VisitPropertyDeclaration(node));
        }

        private SyntaxNode TagOptionalIfNeeded(SyntaxNode original, SyntaxNode visited) {
            foreach (var attrList in (visited as MemberDeclarationSyntax)?.AttributeLists ?? default) {
                foreach (var attr in attrList.Attributes) {
                    var symbol = Model.GetSymbolInfo(attr).Symbol?.ContainingType;
                    if (symbol == null) continue;
                    if (!SymbolMatchers.IsZenjectSymbol(symbol)) continue;
                    if (symbol.Name != "InjectOptionalAttribute") continue;
                    EmitManualTodo(ManualTodoEmitter.InjectOptional, original,
                        "VContainer has no direct field/method [InjectOptional]");
                    var trivia = ManualTodoEmitter.Build(
                        ManualTodoEmitter.InjectOptional,
                        "VContainer has no direct field/method [InjectOptional]; constructor defaults work.",
                        "https://github.com/<owner>/<repo>/blob/main/docs/manual-todos/InjectOptional.md");
                    return visited.WithLeadingTrivia(visited.GetLeadingTrivia().AddRange(trivia));
                }
            }
            return visited;
        }
    }
}
```

- [ ] **Step 4: Wire into pipeline**

Add to `RewritePipeline.Run`:

```csharp
if (Includes(nameof(InjectAttributeRewriter))) {
    var r = new Rewriters.InjectAttributeRewriter();
    current = r.Apply(current, model, tree);
    findings.AddRange(r.Findings);
}
```

`findings` is a per-file list collected and attached to the `FileChange.RelatedFindings` produced for that file.

- [ ] **Step 5: Run, both fixtures PASS**

- [ ] **Step 6: Stage**

```bash
git add Editor/CSharp/RewritePipeline.cs \
        Editor/CSharp/Rewriters/InjectAttributeRewriter.cs \
        Tests/Editor/Fixtures/CSharpRewriters/Inject_FieldAndMethod \
        Tests/Editor/Fixtures/CSharpRewriters/Inject_Optional_Manual
```

---

## Task 6 — `BindToAsRewriter` (basic lifetimes)

**Goal:** translate `Container.Bind<T>().To<U>().AsSingle()` and lifetime variants.

**Files:**
- Create: `Editor/CSharp/Rewriters/BindToAsRewriter.cs`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/Bind_AsSingle/{input,expected}/Foo.cs` + `meta.json`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/Bind_AsTransient/...`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/Bind_AsCached/...`

The rewriter must walk fluent invocation chains rooted at `Container.Bind<T>()` and produce the equivalent VContainer chain rooted at `builder.Register<U>(Lifetime.X).As<T>()`. Use `SemanticModel.GetSymbolInfo` to confirm each method belongs to Zenject.

- [ ] **Step 1: Fixtures (one per lifetime)**

Example `Bind_AsSingle/input/GameInstaller.cs`:

```csharp
using Zenject;

public class GameInstaller : MonoInstaller
{
    public override void InstallBindings()
    {
        Container.Bind<IFoo>().To<Foo>().AsSingle();
    }
}
```

`Bind_AsSingle/expected/GameInstaller.cs`:

```csharp
using VContainer;

public class GameInstaller : MonoInstaller
{
    public override void InstallBindings()
    {
        builder.Register<Foo>(Lifetime.Singleton).As<IFoo>();
    }
}
```

> Note: `MonoInstaller` is left as-is in this task; Task 10 (`InstallerRewriter`) handles the installer transformation. The resulting expected file represents the partial state after running only `UsingDirectiveRewriter` and `BindToAsRewriter`. The rewriter substitutes the receiver `Container.` with `builder.` only inside `InstallBindings` method bodies; outside that scope it emits a manual TODO `[ConditionalBind]`.

(Repeat for `AsTransient` → `Lifetime.Transient` and `AsCached` → `Lifetime.Scoped`.)

- [ ] **Step 2: Implement `BindToAsRewriter`**

The implementation walks invocations recursively. Pseudocode (full code in this step):

```csharp
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Zenject2VContainer.Core.Scanner;

namespace Zenject2VContainer.CSharp.Rewriters {
    public sealed class BindToAsRewriter : RewriterBase {
        public override string Name => nameof(BindToAsRewriter);

        public override SyntaxNode VisitInvocationExpression(InvocationExpressionSyntax node) {
            // Only root-level bind chains are entry points; descend through children first.
            var visited = (InvocationExpressionSyntax)base.VisitInvocationExpression(node);
            var chain = ChainHelper.Collect(visited);
            if (chain.Count == 0) return visited;

            var head = chain[0];
            if (!IsZenjectBindHead(head)) return visited;

            // Build the new chain: Register<U>(lifetime).As<T>()...
            // Implementation detail: walk the chain to extract:
            //   T (from Bind<T>), U (from To<U>), Lifetime (AsSingle/Transient/Cached),
            //   id (WithId), instance (FromInstance), lambda (FromMethod), etc.
            return BuildVContainerChain(chain, visited);
        }

        private bool IsZenjectBindHead(InvocationExpressionSyntax inv) {
            var symbol = Model.GetSymbolInfo(inv).Symbol as IMethodSymbol;
            if (symbol == null) return false;
            if (!SymbolMatchers.IsZenjectSymbol(symbol)) return false;
            return symbol.Name == "Bind"
                || symbol.Name == "BindInterfacesTo"
                || symbol.Name == "BindInterfacesAndSelfTo";
        }

        // Full chain decomposition + reconstruction is implementation detail.
        // Output `builder.Register<U>(Lifetime.X).As<T>()` per spec §5.3.
        private SyntaxNode BuildVContainerChain(IReadOnlyList<InvocationExpressionSyntax> chain,
                                                InvocationExpressionSyntax fallback) {
            // <... omitted; see implementation skeleton in plan note below ...>
            return fallback;
        }
    }

    internal static class ChainHelper {
        // Returns invocations in source order from the head of the chain.
        public static IReadOnlyList<InvocationExpressionSyntax> Collect(InvocationExpressionSyntax tail) {
            var list = new List<InvocationExpressionSyntax>();
            InvocationExpressionSyntax cur = tail;
            while (cur != null) {
                list.Insert(0, cur);
                if (cur.Expression is MemberAccessExpressionSyntax m
                    && m.Expression is InvocationExpressionSyntax inner) {
                    cur = inner;
                } else {
                    break;
                }
            }
            return list;
        }
    }
}
```

> **Implementer note:** the `BuildVContainerChain` body is the most intricate piece of M2. It must:
> - Identify `Bind<T>` and capture `T`.
> - Identify subsequent `To<U>` (or absent — `Bind<T>` with no `To` means `T` is also the impl).
> - Identify the lifetime call (`AsSingle/Transient/Cached`) and map to `Lifetime.Singleton/Transient/Scoped`.
> - Identify `From*` calls and map to corresponding VContainer factory expressions.
> - Identify `.WithId(x)` and append `.Keyed<TKey>(x)` (TKey inferred from x's type via `Model.GetTypeInfo(x)`).
> - Identify `.WhenInjectedInto<X>()` etc. and emit manual TODO `[ConditionalBind]`.
> - Synthesise the new chain as `MemberAccessExpressionSyntax` / `InvocationExpressionSyntax` nodes.
>
> Implementation grows incrementally across Tasks 6–9; this task only covers the lifetime variants.

- [ ] **Step 3: Run all three lifetime fixtures, expect PASS**

- [ ] **Step 4: Stage**

```bash
git add Editor/CSharp/Rewriters/BindToAsRewriter.cs \
        Editor/CSharp/RewritePipeline.cs \
        Tests/Editor/Fixtures/CSharpRewriters/Bind_AsSingle \
        Tests/Editor/Fixtures/CSharpRewriters/Bind_AsTransient \
        Tests/Editor/Fixtures/CSharpRewriters/Bind_AsCached
```

---

## Task 7 — `BindToAsRewriter` (interface bindings)

**Files:**
- Modify: `Editor/CSharp/Rewriters/BindToAsRewriter.cs`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/Bind_BindInterfacesTo/...`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/Bind_BindInterfacesAndSelfTo/...`

`BindInterfacesTo<T>()` → `Register<T>(...).AsImplementedInterfaces()`.
`BindInterfacesAndSelfTo<T>()` → `Register<T>(...).AsImplementedInterfaces().AsSelf()`.

(Steps follow the same pattern as Task 6: fixture, extend `BuildVContainerChain` to handle these two head variants, run, stage.)

---

## Task 8 — `BindToAsRewriter` (instance / method / component sources)

**Files:**
- Modify: `Editor/CSharp/Rewriters/BindToAsRewriter.cs`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/Bind_FromInstance/...`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/Bind_FromMethod/...`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/Bind_FromComponentInHierarchy/...`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/Bind_FromComponentInNewPrefab/...`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/Bind_FromNewComponentOnNewGameObject/...`

Mappings per spec §5.3 table. Each is a separate snapshot fixture. Implementation extends the chain-rewriting branch.

---

## Task 9 — `BindToAsRewriter` (keyed binding + conditional flagging)

**Files:**
- Modify: `Editor/CSharp/Rewriters/BindToAsRewriter.cs`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/Bind_WithId_Keyed/...`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/Bind_WhenInjectedInto_Manual/...`

`.WithId(string)` → `.Keyed<string>(value)`. `.WhenInjectedInto<T>()` → manual TODO `[ConditionalBind]`, original chain preserved.

---

## Task 10 — `InstallerRewriter` (MonoInstaller)

**Files:**
- Create: `Editor/CSharp/Rewriters/InstallerRewriter.cs`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/MonoInstaller_Single/...`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/MonoInstaller_Multiple_AsIInstaller/...`

Detection logic for the "single MonoInstaller per SceneContext" pattern requires a hint from the YAML scan that lands in M3. For M2, default to the conservative `IInstaller` translation:

```csharp
public class GameInstaller : MonoInstaller, IInstaller
{
    public override void InstallBindings() { /* legacy entry */ }

    public void Install(IContainerBuilder builder)
    {
        // bindings translated by BindToAsRewriter
    }
}
```

…then have `MigrationPipeline` (M3) prune the legacy `InstallBindings` after wiring the YAML-side LifetimeScope. Document this clearly in the rewriter's class-level comment.

A `MonoInstaller_Multiple_AsIInstaller` fixture demonstrates the IInstaller-only transformation (no fold).

A second fixture (`MonoInstaller_Single_FoldedToLifetimeScope`, optional, marked `"foldHint": true` in `meta.json`) covers the folded variant for when M3 supplies the hint. M2 stub returns the unfolded form unless the hint is present.

---

## Task 11 — `InstallerRewriter` (ScriptableObjectInstaller + Installer<T>)

**Files:**
- Modify: `Editor/CSharp/Rewriters/InstallerRewriter.cs`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/ScriptableObjectInstaller/...`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/Installer_Generic_POCO/...`

`ScriptableObjectInstaller` → `ScriptableObject` implementing `IInstaller` (preserve asset GUID).
`Installer<T>` POCO → `IInstaller`.

---

## Task 12 — `TickableInitializableRewriter`

**Files:**
- Create: `Editor/CSharp/Rewriters/TickableInitializableRewriter.cs`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/Lifecycle_Initializable/...`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/Lifecycle_Tickables/...`

`IInitializable` → `IStartable` (`Initialize` → `Start`).
`ITickable` / `ILateTickable` / `IFixedTickable` retained but resolved against `VContainer.Unity` namespace.
`IDisposable` left as `System.IDisposable`; emit no change.

Adds `using VContainer.Unity;` directive when any of these interfaces are touched (calls `UsingDirectiveRewriter.EnsureVContainerUnityUsing` after rewrite).

Within installers, emit `builder.RegisterEntryPoint<T>(Lifetime.Singleton).AsImplementedInterfaces()` — but this lives in `BindToAsRewriter` because it's a binding rewrite. The two rewriters cooperate.

---

## Task 13 — `FactoryRewriter`

**Files:**
- Create: `Editor/CSharp/Rewriters/FactoryRewriter.cs`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/Factory_PlaceholderFactory_OneArg/...`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/Factory_PlaceholderFactory_NoArg/...`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/Factory_BindFactory/...`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/Factory_PreservedWrapper/...`

Rules per spec §5.7:
- `class FooFactory : PlaceholderFactory<TArg, TOut> { }` → if class is referenced externally, replace with thin wrapper around `Func<TArg, TOut>`. Otherwise delete and rewrite consumers to take `Func<>`.
- `Container.BindFactory<TArg, TOut, FooFactory>()` → `builder.RegisterFactory<TArg, TOut>(...)`.
- `[Inject] FooFactory _f;` → `[Inject] Func<TArg, TOut> _f;` when wrapper is dropped.

External-reference detection: walk the syntax trees and check if `FooFactory` appears outside its own declaration *and* outside the installer body. If yes → preserve wrapper; if no → drop.

---

## Task 14 — `SubContainerRewriter`

**Files:**
- Create: `Editor/CSharp/Rewriters/SubContainerRewriter.cs`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/SubContainer_ByInstaller_Simple/...`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/SubContainer_ByMethod_Manual/...`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/SubContainer_ByPrefab_Manual/...`

Simple `FromSubContainerResolve.ByInstaller<X>().AsSingle()` → child LifetimeScope factory pattern (per spec §5.8). All other forms (`ByMethod`, `ByPrefab`, dynamic args) → manual TODO `[ComplexSubContainer]`.

---

## Task 15 — `DiContainerUsageRewriter`

**Files:**
- Create: `Editor/CSharp/Rewriters/DiContainerUsageRewriter.cs`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/DiContainer_Resolve/...`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/DiContainer_Instantiate_Registered/...`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/DiContainer_Instantiate_Unregistered_Manual/...`
- Create: `Tests/Editor/Fixtures/CSharpRewriters/DiContainer_InstantiatePrefab/...`

Field/parameter type `DiContainer` → `IObjectResolver`. Method calls:
- `Resolve<T>()` → `Resolve<T>()` (same name, different receiver type).
- `Instantiate<T>()` for registered T → `Resolve<T>()`.
- `Instantiate<T>()` for unregistered → manual TODO `[InstantiateUnregistered]`.
- `InstantiatePrefab(prefab)` → `Instantiate(prefab)` (extension method in `VContainer.Unity`).

Registered detection: requires the rewriter to know the binding set produced by `BindToAsRewriter` for the same compilation. Pass a shared `BindingRegistry` populated by `BindToAsRewriter` first; `DiContainerUsageRewriter` queries it. The pipeline orchestrator passes the registry between rewriters.

---

## Task 16 — Compose pipeline + add `MigrationPipeline.RunCSharpHeadless`

**Files:**
- Modify: `Editor/CSharp/RewritePipeline.cs` (final form)
- Modify: `Editor/Core/MigrationPipeline.cs`
- Create: `Tests/Editor/RewritePipelineTests.cs`

Final pipeline order:
1. `UsingDirectiveRewriter`
2. `InjectAttributeRewriter`
3. `BindToAsRewriter` (also populates `BindingRegistry`)
4. `InstallerRewriter`
5. `TickableInitializableRewriter`
6. `FactoryRewriter`
7. `SubContainerRewriter`
8. `DiContainerUsageRewriter`

Add to `MigrationPipeline`:

```csharp
public static MigrationPlan RunCSharpHeadless() {
    var compilation = BuildCompilationFromHostProject(); // factor out from RunScanHeadless
    var pipeline = new RewritePipeline(new[] { "*" });
    var changes = pipeline.Run(compilation);
    return new MigrationPlan { Changes = new List<FileChange>(changes) };
}
```

Add an end-to-end fixture under `Tests/Editor/Fixtures/CSharpRewriters/Pipeline_FullProject/` exercising a small but realistic installer + consumer pair.

---

## Task 17 — Editor menu: `Preview C# Migration…`

**Files:**
- Modify: `Editor/UI/MigrationMenu.cs`

Add a new menu item:

```csharp
[MenuItem("Window/Zenject2VContainer/Preview C# Migration…")]
public static void PreviewCSharp() {
    var plan = MigrationPipeline.RunCSharpHeadless();
    var outDir = Path.Combine(/* Library/Zenject2VContainer/preview-csharp/ */);
    Directory.CreateDirectory(outDir);
    foreach (var change in plan.Changes) {
        var dest = Path.Combine(outDir, change.OriginalPath.Replace(":", "_"));
        Directory.CreateDirectory(Path.GetDirectoryName(dest));
        File.WriteAllText(dest, change.NewText);
    }
    EditorUtility.RevealInFinder(outDir);
    Debug.Log($"[Zenject2VContainer] {plan.Changes.Count} files rewritten under {outDir}");
}
```

This is preview-only; nothing in `Assets/` is touched. M4 wraps this in the Apply step of the wizard.

---

## Task 18 — Smoke run on host project

User-action verification: open Unity, run the new menu item against the host project, sanity-check the preview output for a handful of files. Document results in this plan file under a "Smoke run notes" section.

---

## M2 done when

- All snapshot fixtures (Tasks 4–15) green.
- `RewritePipelineTests.End_To_End_Pipeline_Composes_All_Rewriters` green.
- The new menu item produces a folder of rewritten `.cs` files for the host project without throwing.

---

## Self-review checklist

1. **Spec coverage:** §5.1 (Task 4), §5.2 (Task 5), §5.3 (Tasks 6–9), §5.4 (Tasks 10–11), §5.7 (Task 13), §5.8 (Task 14), §5.9 (Task 15). Lifecycle interfaces (within §5.4) covered in Task 12. §5.5 / §5.6 deferred to M3 as planned.
2. **Placeholder scan:** All tasks contain code blocks and concrete fixture paths. The "implementer note" in Task 6 acknowledges that `BuildVContainerChain` is the milestone's single complex method; subsequent tasks (7–9) extend it incrementally.
3. **Type consistency:** `FileChange`, `Finding`, `MigrationPlan` defined in Task 2; all later tasks reference the same types. `RewriterBase` introduced in Task 3 used by every rewriter. `BindingRegistry` (Task 15) introduced as the cross-rewriter state bus.
4. **Rewriter independence:** Each rewriter is testable in isolation via the `meta.json` `rewriters` array. The full pipeline is tested in Task 16.

---

## Execution handoff

Plan saved to `docs/superpowers/plans/2026-05-01-z2vc-m2-csharp-rewriters.md`.

Same two execution options as M1:

1. **Subagent-Driven (recommended)** — fresh subagent per task, review between, isolated.
2. **Inline Execution** — batched in current session.

Which approach?
