using System.IO;
using System.Linq;
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
                Path.Combine(root, "UnityEngine.dll"),
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
