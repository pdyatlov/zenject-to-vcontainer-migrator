using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Zenject2VContainer.CSharp;
using Zenject2VContainer.Core;
using Zenject2VContainer.Core.Scanner;

namespace Zenject2VContainer.Tests {
    public sealed class SnapshotRunner {
        [Serializable]
        private sealed class FixtureMeta {
            public string description;
            public string[] rewriters;
            public string[] expectedManualTodoCategories;
            public string[] extraReferences;
        }

        public static IEnumerable<TestCaseData> Fixtures() {
            var root = Path.Combine(Application.dataPath, "..", "Packages",
                "com.zenject2vcontainer.migrator", "Tests", "Editor",
                "Fixtures", "CSharpRewriters~");
            if (!Directory.Exists(root)) yield break;
            foreach (var dir in Directory.GetDirectories(root)) {
                yield return new TestCaseData(dir).SetName(Path.GetFileName(dir));
            }
        }

        [TestCaseSource(nameof(Fixtures))]
        public void Snapshot_Roundtrip(string fixtureDir) {
            var meta = LoadMeta(fixtureDir);
            var inputDir = Path.Combine(fixtureDir, "input");
            var expectedDir = Path.Combine(fixtureDir, "expected");

            var inputs = LoadSources(inputDir);
            var expected = LoadSources(expectedDir);

            var stubsRoot = Path.Combine(Application.dataPath, "..", "Packages",
                "com.zenject2vcontainer.migrator", "Tests", "Editor", "References~");
            var refs = new List<string> {
                Path.Combine(stubsRoot, "UnityEngine.dll"),
                Path.Combine(stubsRoot, "Zenject.dll"),
                Path.Combine(stubsRoot, "VContainer.dll")
            };
            if (meta.extraReferences != null) refs.AddRange(meta.extraReferences);

            var compilation = CompilationLoader.BuildFromSources(
                "FixtureCompilation",
                inputs.Select(kv => (kv.Key, kv.Value)),
                refs);

            var pipeline = new RewritePipeline(meta.rewriters ?? new[] { "*" });
            var changes = pipeline.Run(compilation);

            // Build actual output map.
            var actual = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in inputs) actual[kv.Key] = kv.Value; // start from inputs
            foreach (var change in changes) {
                actual[Path.GetFileName(change.OriginalPath)] = change.NewText;
            }

            foreach (var fname in expected.Keys) {
                Assert.IsTrue(actual.ContainsKey(fname),
                    "expected file " + fname + " missing from migration output");
                AssertTextEqual(expected[fname], actual[fname], fname);
            }

            // Verify expected manual TODO categories.
            var emittedCats = new HashSet<string>(
                changes.SelectMany(c => c.RelatedFindings)
                    .Where(f => f.Category != null)
                    .Select(f => f.Category));
            foreach (var expectedCat in meta.expectedManualTodoCategories ?? new string[0]) {
                Assert.IsTrue(emittedCats.Contains(expectedCat),
                    "expected manual TODO [" + expectedCat + "] not found");
            }
        }

        private static Dictionary<string, string> LoadSources(string dir) {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(dir)) return map;
            foreach (var file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)) {
                map[Path.GetFileName(file)] = File.ReadAllText(file);
            }
            return map;
        }

        private static FixtureMeta LoadMeta(string fixtureDir) {
            var path = Path.Combine(fixtureDir, "meta.json");
            var json = File.Exists(path) ? File.ReadAllText(path) : "{}";
            return JsonUtility.FromJson<FixtureMeta>(json) ?? new FixtureMeta();
        }

        private static void AssertTextEqual(string expected, string actual, string name) {
            // Normalise line endings: comparing to expected with \n only.
            var e = expected.Replace("\r\n", "\n");
            var a = actual.Replace("\r\n", "\n");
            if (e == a) return;
            Assert.Fail("snapshot mismatch in " + name + "\nEXPECTED:\n" + e + "\nACTUAL:\n" + a);
        }
    }
}
