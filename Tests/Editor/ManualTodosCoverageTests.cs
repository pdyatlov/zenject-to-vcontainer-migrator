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
