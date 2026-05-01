using NUnit.Framework;
using Zenject2VContainer.Core.Scanner;

namespace Zenject2VContainer.Tests {
    public class ZenjectScriptGuidTableTests {
        [Test]
        public void Loads_Bundled_Table_And_Resolves_SceneContext() {
            var table = ZenjectScriptGuidTable.LoadBundled();
            Assert.IsTrue(table.ContainsScript("SceneContext"));
            Assert.IsTrue(table.IsZenjectGuid(table.GetGuid("SceneContext")));
            Assert.IsFalse(table.IsZenjectGuid("00000000000000000000000000000000"));
        }
    }
}
