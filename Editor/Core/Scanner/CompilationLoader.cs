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
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Add(string path) {
                if (string.IsNullOrEmpty(path)) return;
                if (!File.Exists(path)) return;
                if (!seen.Add(path)) return;
                refs.Add(MetadataReference.CreateFromFile(path));
            }

            // Include every loaded runtime assembly so referenced types like System.Attribute,
            // type-forwarded BCL surface, and Unity APIs all resolve. AppDomain enumeration is
            // the canonical way to get the right set in the Unity Editor.
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
