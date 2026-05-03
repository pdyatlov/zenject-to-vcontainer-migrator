using NUnit.Framework;
using Zenject2VContainer.Core;
using Zenject2VContainer.Reporting;

namespace Zenject2VContainer.Tests {
    [TestFixture]
    public class MigrationReportWriterTests {
        // ── baseline ──────────────────────────────────────────────────────────
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

        // ── polish item 1: project-relative paths ─────────────────────────────
        [Test]
        public void Render_ProjectRelativePath_StripsProjectRoot() {
            var plan = new MigrationPlan();
            plan.Changes.Add(new FileChange {
                OriginalPath = "C:/Project/Assets/Foo.cs",
                Category = FileChangeCategory.CSharp,
                Confidence = ChangeConfidence.High
            });
            // Path outside project root rendered as-is
            plan.Changes.Add(new FileChange {
                OriginalPath = "D:/OtherRepo/Bar.cs",
                Category = FileChangeCategory.CSharp,
                Confidence = ChangeConfidence.Medium
            });
            var md = MigrationReportWriter.Render(plan, new MigrationReportContext {
                ProjectPath = "C:/Project", UnityVersion = "2022.3", ToolVersion = "0.1.0",
                RunUtc = "2026-05-01T12:00:00Z"
            });
            StringAssert.DoesNotContain("C:/Project/Assets/Foo.cs", md);
            StringAssert.Contains("Assets/Foo.cs", md);
            StringAssert.Contains("D:/OtherRepo/Bar.cs", md);
        }

        // ── polish item 5: pipe escaping ──────────────────────────────────────
        [Test]
        public void Render_PipeInReason_IsEscaped() {
            var plan = new MigrationPlan();
            plan.Unsupported.Add(new Finding {
                Category = "SignalBus",
                FilePath = "Assets/A.cs",
                Line = 1,
                Reason = "foo | bar",
                DocLink = ""
            });
            var md = MigrationReportWriter.Render(plan, new MigrationReportContext {
                ProjectPath = "C:/Project", UnityVersion = "2022.3", ToolVersion = "0.1.0",
                RunUtc = "2026-05-01T12:00:00Z"
            });
            // The table cell must not contain a bare | that would break the markdown table
            // The reason value should be escaped as \|
            StringAssert.Contains(@"foo \| bar", md);
        }

        // ── polish item 6: Doc column hidden when no DocLinks ─────────────────
        [Test]
        public void Render_NoDocLinks_HidesDocColumn() {
            var plan = new MigrationPlan();
            plan.Unsupported.Add(new Finding {
                Category = "SignalBus", FilePath = "Assets/A.cs", Line = 1,
                Reason = "no doc", DocLink = ""
            });
            var md = MigrationReportWriter.Render(plan, new MigrationReportContext {
                ProjectPath = "C:/Project", UnityVersion = "2022.3", ToolVersion = "0.1.0",
                RunUtc = "2026-05-01T12:00:00Z"
            });
            StringAssert.DoesNotContain("| Doc |", md);
        }

        [Test]
        public void Render_WithDocLinks_ShowsDocColumn() {
            var plan = new MigrationPlan();
            plan.Unsupported.Add(new Finding {
                Category = "SignalBus", FilePath = "Assets/A.cs", Line = 1,
                Reason = "has doc", DocLink = "https://example.com/doc"
            });
            var md = MigrationReportWriter.Render(plan, new MigrationReportContext {
                ProjectPath = "C:/Project", UnityVersion = "2022.3", ToolVersion = "0.1.0",
                RunUtc = "2026-05-01T12:00:00Z"
            });
            StringAssert.Contains("| Doc |", md);
        }

        // ── polish item 3: Confidence breakdown ───────────────────────────────
        [Test]
        public void Render_ConfidenceBreakdown_InSummary() {
            var plan = new MigrationPlan();
            plan.Changes.Add(new FileChange { OriginalPath = "A.cs", Category = FileChangeCategory.CSharp, Confidence = ChangeConfidence.High });
            plan.Changes.Add(new FileChange { OriginalPath = "B.cs", Category = FileChangeCategory.CSharp, Confidence = ChangeConfidence.Medium });
            plan.Changes.Add(new FileChange { OriginalPath = "C.cs", Category = FileChangeCategory.CSharp, Confidence = ChangeConfidence.LowFlagged });
            plan.Changes.Add(new FileChange { OriginalPath = "D.cs", Category = FileChangeCategory.CSharp, Confidence = ChangeConfidence.High });
            var md = MigrationReportWriter.Render(plan, new MigrationReportContext {
                ProjectPath = "C:/Project", UnityVersion = "2022.3", ToolVersion = "0.1.0",
                RunUtc = "2026-05-01T12:00:00Z"
            });
            StringAssert.Contains("High: 2", md);
            StringAssert.Contains("Medium: 1", md);
            StringAssert.Contains("LowFlagged: 1", md);
        }

        // ── polish item 2: grouped Changes subsections ────────────────────────
        [Test]
        public void Render_GroupedChanges_AllThreeCategories() {
            var plan = new MigrationPlan();
            plan.Changes.Add(new FileChange { OriginalPath = "A.cs", Category = FileChangeCategory.CSharp, Confidence = ChangeConfidence.High });
            plan.Changes.Add(new FileChange { OriginalPath = "B.unity", Category = FileChangeCategory.Yaml, Confidence = ChangeConfidence.High });
            plan.Changes.Add(new FileChange { OriginalPath = "Packages/manifest.json", Category = FileChangeCategory.Manifest, Confidence = ChangeConfidence.High });
            var md = MigrationReportWriter.Render(plan, new MigrationReportContext {
                ProjectPath = "C:/Project", UnityVersion = "2022.3", ToolVersion = "0.1.0",
                RunUtc = "2026-05-01T12:00:00Z"
            });
            StringAssert.Contains("### C#", md);
            StringAssert.Contains("### YAML", md);
            StringAssert.Contains("### Manifest", md);
        }

        [Test]
        public void Render_GroupedChanges_OnlyCSharp_NoEmptySubsections() {
            var plan = new MigrationPlan();
            plan.Changes.Add(new FileChange { OriginalPath = "A.cs", Category = FileChangeCategory.CSharp, Confidence = ChangeConfidence.High });
            var md = MigrationReportWriter.Render(plan, new MigrationReportContext {
                ProjectPath = "C:/Project", UnityVersion = "2022.3", ToolVersion = "0.1.0",
                RunUtc = "2026-05-01T12:00:00Z"
            });
            StringAssert.Contains("### C#", md);
            StringAssert.DoesNotContain("### YAML", md);
            StringAssert.DoesNotContain("### Manifest", md);
        }

        // ── polish item 7: Table of Contents ──────────────────────────────────
        [Test]
        public void Render_ContainsTableOfContents() {
            var plan = new MigrationPlan();
            var md = MigrationReportWriter.Render(plan, new MigrationReportContext {
                ProjectPath = "C:/Project", UnityVersion = "2022.3", ToolVersion = "0.1.0",
                RunUtc = "2026-05-01T12:00:00Z"
            });
            StringAssert.Contains("## Table of contents", md);
        }

        // ── polish item 12: Post-Apply Verification block ─────────────────────
        [Test]
        public void Render_PostApplyVerification_ShowsCounters() {
            var plan = new MigrationPlan();
            var md = MigrationReportWriter.Render(plan, new MigrationReportContext {
                ProjectPath = "C:/Project", UnityVersion = "2022.3", ToolVersion = "0.1.0",
                RunUtc = "2026-05-01T12:00:00Z",
                RemainingZenjectFiles = 3,
                CompileErrorCount = 1
            });
            StringAssert.Contains("## Post-Apply Verification", md);
            StringAssert.Contains("Remaining Zenject references (files): 3", md);
            StringAssert.Contains("Compile errors after apply: 1", md);
        }

        // ── polish item 9: Skipped / Unchanged section ────────────────────────
        [Test]
        public void Render_SkippedFiles_ShowsSection() {
            var plan = new MigrationPlan();
            var md = MigrationReportWriter.Render(plan, new MigrationReportContext {
                ProjectPath = "C:/Project", UnityVersion = "2022.3", ToolVersion = "0.1.0",
                RunUtc = "2026-05-01T12:00:00Z",
                SkippedFiles = new[] { "Assets/Foo.cs" }
            });
            StringAssert.Contains("## Skipped / Unchanged", md);
            StringAssert.Contains("Assets/Foo.cs", md);
        }

        [Test]
        public void Render_SkippedFiles_Empty_OmitsSection() {
            var plan = new MigrationPlan();
            var md = MigrationReportWriter.Render(plan, new MigrationReportContext {
                ProjectPath = "C:/Project", UnityVersion = "2022.3", ToolVersion = "0.1.0",
                RunUtc = "2026-05-01T12:00:00Z",
                SkippedFiles = new string[0]
            });
            StringAssert.DoesNotContain("## Skipped / Unchanged", md);
        }

        // ── polish item 10: Backup dual form ──────────────────────────────────
        [Test]
        public void Render_BackupTimestamp_RendersBothForms() {
            var plan = new MigrationPlan();
            var md = MigrationReportWriter.Render(plan, new MigrationReportContext {
                ProjectPath = "C:/Project", UnityVersion = "2022.3", ToolVersion = "0.1.0",
                RunUtc = "2026-05-01T12:00:00Z",
                BackupTimestamp = "20260501T120000Z"
            });
            StringAssert.Contains("20260501T120000Z", md);
            StringAssert.Contains("2026-05-01 12:00 UTC", md);
        }

        // ── polish item 8: Rollback Temp/ wipe warning ────────────────────────
        [Test]
        public void Render_Rollback_ContainsTempWipeWarning() {
            var plan = new MigrationPlan();
            var md = MigrationReportWriter.Render(plan, new MigrationReportContext {
                ProjectPath = "C:/Project", UnityVersion = "2022.3", ToolVersion = "0.1.0",
                RunUtc = "2026-05-01T12:00:00Z",
                BackupTimestamp = "20260501T120000Z"
            });
            // Must mention Temp/ wipe and Editor reimport
            StringAssert.Contains("Temp/", md);
            StringAssert.Contains("Editor reimport", md);
        }

        // ── polish item 4: Manual TODOs grouped by category ───────────────────
        [Test]
        public void Render_ManualTodos_GroupedByCategory() {
            var plan = new MigrationPlan();
            plan.Unsupported.Add(new Finding { Category = "SignalBus", FilePath = "A.cs", Line = 1, Reason = "sig1" });
            plan.Unsupported.Add(new Finding { Category = "SignalBus", FilePath = "B.cs", Line = 2, Reason = "sig2" });
            plan.Unsupported.Add(new Finding { Category = "InjectOptional", FilePath = "C.cs", Line = 3, Reason = "opt1" });
            var md = MigrationReportWriter.Render(plan, new MigrationReportContext {
                ProjectPath = "C:/Project", UnityVersion = "2022.3", ToolVersion = "0.1.0",
                RunUtc = "2026-05-01T12:00:00Z"
            });
            // Group headers with counts
            StringAssert.Contains("SignalBus", md);
            StringAssert.Contains("InjectOptional", md);
            // Per-category count
            StringAssert.Contains("(2)", md);
            StringAssert.Contains("(1)", md);
            // Link to docs anchor
            StringAssert.Contains("Docs~/manual-todos.md#signalbus", md);
            StringAssert.Contains("Docs~/manual-todos.md#injectoptional", md);
        }
    }
}
