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
