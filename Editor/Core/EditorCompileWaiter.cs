using System;
using System.Collections.Generic;
using System.Threading;
using UnityEditor;
using UnityEditor.Compilation;

namespace Zenject2VContainer.Core {
    public sealed class EditorCompileWaiter : ICompileWaiter {
        public CompileResult WaitForCompile(MigrationLog log) {
            log.Info("Apply.CSharp", "Refreshing AssetDatabase and waiting for compile…");
            var errors = new List<string>();

            void OnAssemblyFinished(string outputPath, CompilerMessage[] messages) {
                if (messages == null) return;
                foreach (var m in messages) {
                    if (m.type == CompilerMessageType.Error) {
                        errors.Add($"{m.file}({m.line}): {m.message}");
                    }
                }
            }

            CompilationPipeline.assemblyCompilationFinished += OnAssemblyFinished;
            try {
                AssetDatabase.Refresh();
                CompilationPipeline.RequestScriptCompilation();
                // Brief wait for compile to start (Refresh + RequestScriptCompilation are async).
                var startDeadline = DateTime.UtcNow.AddSeconds(5);
                while (!EditorApplication.isCompiling && DateTime.UtcNow < startDeadline) {
                    Thread.Sleep(50);
                }
                // Block until compile finishes. Editor scripts cannot await on the main thread, so we spin.
                var spinDeadline = DateTime.UtcNow.AddMinutes(5);
                while (EditorApplication.isCompiling && DateTime.UtcNow < spinDeadline) {
                    Thread.Sleep(100);
                }
            } finally {
                CompilationPipeline.assemblyCompilationFinished -= OnAssemblyFinished;
            }

            return new CompileResult { Succeeded = errors.Count == 0, ErrorMessages = errors };
        }
    }
}
