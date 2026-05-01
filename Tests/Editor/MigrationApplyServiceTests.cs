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
