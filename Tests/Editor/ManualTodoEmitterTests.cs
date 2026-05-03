using NUnit.Framework;
using Zenject2VContainer.CSharp;

namespace Zenject2VContainer.Tests {
    public class ManualTodoEmitterTests {
        [Test]
        public void Emits_Triple_Comment_Trivia_For_Category() {
            var trivia = ManualTodoEmitter.Build(ManualTodoEmitter.SignalBus,
                "no equivalent");
            var text = trivia.ToFullString();
            StringAssert.Contains("MIGRATE-MANUAL [SignalBus]", text);
            StringAssert.Contains("no equivalent", text);
            StringAssert.Contains("Docs~/manual-todos.md#signalbus", text);
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
