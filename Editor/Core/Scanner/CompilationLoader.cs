using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Zenject2VContainer.Core.Scanner {
    public static class CompilationLoader {
        public static CSharpCompilation BuildFromSources(
                string assemblyName,
                IEnumerable<(string FilePath, string Source)> sources,
                IEnumerable<string> referenceDllPaths) {

            var trees = new List<SyntaxTree>();
            foreach (var (path, src) in sources) {
                trees.Add(CSharpSyntaxTree.ParseText(src, path: path));
            }

            var refs = new List<MetadataReference> {
                MetadataReference.CreateFromFile(typeof(object).Assembly.Location)
            };
            foreach (var p in referenceDllPaths) {
                refs.Add(MetadataReference.CreateFromFile(p));
            }

            return CSharpCompilation.Create(
                assemblyName,
                trees,
                refs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }
    }
}
