using NUnit.Framework;
using Zenject2VContainer.Manifest;

namespace Zenject2VContainer.Tests {
    [TestFixture]
    public class ManifestEditorTests {
        [Test]
        public void StripsZenjectOnlyScopedRegistry() {
            var input = @"{
  ""dependencies"": { ""com.svermeulen.extenject"": ""9.2.0"" },
  ""scopedRegistries"": [
    {
      ""name"": ""Extenject"",
      ""url"": ""https://npm.pkg.github.com/svermeulen"",
      ""scopes"": [ ""com.svermeulen"" ]
    }
  ]
}";
            var result = ManifestEditor.StripZenjectScopedRegistries(input);
            Assert.IsTrue(result.Modified);
            Assert.That(result.RemovedRegistryNames, Is.EquivalentTo(new[] { "Extenject" }));
            StringAssert.DoesNotContain("Extenject", result.NewText);
            StringAssert.Contains("dependencies", result.NewText);
        }

        [Test]
        public void KeepsUnrelatedScopedRegistries() {
            var input = @"{
  ""scopedRegistries"": [
    {
      ""name"": ""OpenUPM"",
      ""url"": ""https://package.openupm.com"",
      ""scopes"": [ ""com.cysharp"" ]
    }
  ]
}";
            var result = ManifestEditor.StripZenjectScopedRegistries(input);
            Assert.IsFalse(result.Modified);
            Assert.IsEmpty(result.RemovedRegistryNames);
            StringAssert.Contains("OpenUPM", result.NewText);
        }

        [Test]
        public void NoScopedRegistriesArray_ReturnsUnchanged() {
            var input = @"{ ""dependencies"": { ""com.unity.test"": ""1.0.0"" } }";
            var result = ManifestEditor.StripZenjectScopedRegistries(input);
            Assert.IsFalse(result.Modified);
            Assert.AreEqual(input, result.NewText);
        }
    }
}
