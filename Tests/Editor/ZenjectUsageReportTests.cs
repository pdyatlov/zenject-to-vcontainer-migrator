using NUnit.Framework;
using UnityEngine;
using Zenject2VContainer.Core;

namespace Zenject2VContainer.Tests {
    public class ZenjectUsageReportTests {
        [Test]
        public void Report_Serialises_And_Deserialises_To_Equivalent_Object() {
            var original = new ZenjectUsageReport {
                ToolVersion = "0.1.0",
                UnityVersion = "2022.3.10f1",
                ScannedAtUtc = "2026-05-01T10:00:00Z"
            };
            original.CSharpFindings.Add(new CSharpFinding {
                FilePath = "Assets/Foo.cs",
                Line = 12,
                Column = 4,
                Kind = CSharpFindingKind.InjectAttribute,
                SymbolName = "Zenject.InjectAttribute",
                Snippet = "[Inject] private IBar _bar;",
                Confidence = "High",
                Notes = ""
            });
            original.Install.ZenjectViaUpm = true;
            original.Install.UpmPackageId = "com.svermeulen.extenject";

            var json = ZenjectUsageReport.ToJson(original);
            var roundTripped = ZenjectUsageReport.FromJson(json);

            Assert.AreEqual(original.ToolVersion, roundTripped.ToolVersion);
            Assert.AreEqual(1, roundTripped.CSharpFindings.Count);
            Assert.AreEqual(CSharpFindingKind.InjectAttribute, roundTripped.CSharpFindings[0].Kind);
            Assert.AreEqual("com.svermeulen.extenject", roundTripped.Install.UpmPackageId);
        }
    }
}
