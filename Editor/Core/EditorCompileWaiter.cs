using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Compilation;

namespace Zenject2VContainer.Core {
    public sealed class EditorCompileWaiter : ICompileWaiter {
        public CompileResult WaitForCompile(MigrationLog log) {
            log.Info("Apply.CSharp", "Refreshing AssetDatabase and waiting for compile…");
            AssetDatabase.Refresh();
            // Force compile if not already pending.
            CompilationPipeline.RequestScriptCompilation();
            // Block until compile finishes. Editor scripts cannot await on the main thread, so we spin briefly.
            var spinDeadline = System.DateTime.UtcNow.AddMinutes(5);
            while (EditorApplication.isCompiling && System.DateTime.UtcNow < spinDeadline) {
                System.Threading.Thread.Sleep(100);
            }
            // Collect compile errors for the latest assemblies.
            var errors = new List<string>();
            foreach (var asm in CompilationPipeline.GetAssemblies(AssembliesType.Editor)) {
                var msgs = CompilationPipeline.GetMessages(asm.outputPath);
                if (msgs == null) continue;
                foreach (var m in msgs) {
                    if (m.type == CompilerMessageType.Error) errors.Add($"{m.file}({m.line}): {m.message}");
                }
            }
            return new CompileResult { Succeeded = errors.Count == 0, ErrorMessages = errors };
        }
    }
}
