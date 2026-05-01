using System;
using System.Collections.Generic;
using System.IO;
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

            var refs = new List<MetadataReference>();
            var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Dedupe by simple assembly name too, so a stub DLL with the same simple
            // name as a real loaded assembly (e.g. our Zenject.dll stub vs. the real
            // Zenject loaded by Unity) does not produce duplicate type definitions.
            var seenSimpleNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string path) {
                if (string.IsNullOrEmpty(path)) return;
                if (!File.Exists(path)) return;
                if (!seenPaths.Add(path)) return;
                var simpleName = Path.GetFileNameWithoutExtension(path);
                if (!seenSimpleNames.Add(simpleName)) return;
                refs.Add(MetadataReference.CreateFromFile(path));
            }

            // AppDomain assemblies are added first so they win the simple-name dedupe.
            // This means: if Unity has already loaded the real Zenject, our stub gets
            // skipped — and the test compilation resolves Zenject types against the
            // real assembly, which is what tests assert against.
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies()) {
                if (asm.IsDynamic) continue;
                try { Add(asm.Location); } catch { /* dynamic / in-memory assemblies have no Location */ }
            }
            foreach (var p in referenceDllPaths) Add(p);

            return CSharpCompilation.Create(
                assemblyName,
                trees,
                refs,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        }
    }
}
