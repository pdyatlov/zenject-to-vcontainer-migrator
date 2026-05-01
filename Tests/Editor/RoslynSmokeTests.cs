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
