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
                Path.Combine(refsRoot, "UnityEngine.dll"),
                Path.Combine(refsRoot, "Zenject.dll"),
                Path.Combine(refsRoot, "VContainer.dll")
            };

            const string source =
                "using Zenject;\n" +
                "public class Foo { [Inject] private DiContainer _c; }\n";

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
