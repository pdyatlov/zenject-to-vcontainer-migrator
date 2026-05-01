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
                Path.Combine(root, "UnityEngine.dll"),
                Path.Combine(root, "Zenject.dll"),
                Path.Combine(root, "VContainer.dll")
            };
        }

        [Test]
        public void Reports_Using_Inject_And_Bind() {
            const string src =
                "using Zenject;\n" +
                "public class GameInstaller : MonoInstaller {\n" +
                "    [Inject] private DiContainer _c;\n" +
                "    public override void InstallBindings() {\n" +
                "        Container.Bind<int>().To<int>().AsSingle();\n" +
                "    }\n" +
                "}\n";
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
