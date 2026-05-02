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
                EditorUtility.DisplayProgressBar("Compiling", "Refreshing AssetDatabase…", 0.05f);
                AssetDatabase.Refresh();
                CompilationPipeline.RequestScriptCompilation();
                // Brief wait for compile to start (Refresh + RequestScriptCompilation are async).
                var startDeadline = DateTime.UtcNow.AddSeconds(5);
                while (!EditorApplication.isCompiling && DateTime.UtcNow < startDeadline) {
                    EditorUtility.DisplayProgressBar("Compiling", "Waiting for compile to start…", 0.1f);
                    Thread.Sleep(50);
                }
                // Block until compile finishes. Editor scripts cannot await on the main thread, so we spin.
                var spinDeadline = DateTime.UtcNow.AddMinutes(5);
                int spins = 0;
                while (EditorApplication.isCompiling && DateTime.UtcNow < spinDeadline) {
                    spins++;
                    EditorUtility.DisplayProgressBar("Compiling", $"Compile in progress… ({errors.Count} error(s) so far)", System.Math.Min(0.95f, 0.2f + spins * 0.005f));
                    Thread.Sleep(100);
                }
            } finally {
                CompilationPipeline.assemblyCompilationFinished -= OnAssemblyFinished;
                EditorUtility.ClearProgressBar();
            }

            return new CompileResult { Succeeded = errors.Count == 0, ErrorMessages = errors };
        }
    }
}
