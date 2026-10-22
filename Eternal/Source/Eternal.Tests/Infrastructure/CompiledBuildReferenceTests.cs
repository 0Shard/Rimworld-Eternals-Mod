// Author: 0Shard
// Description: Current captured provenance and controlled missing/mismatched/optional setup failures.
using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Eternal.Tests.Compatibility;
using Xunit;

namespace Eternal.Tests.Infrastructure
{
    public class CompiledBuildReferenceTests
    {
        [Theory]
        [InlineData("schema", "Malformed")]
        [InlineData("assets", "assets entry")]
        [InlineData("duplicate-artifact", "artifact entry")]
        [InlineData("configuration", "configuration")]
        [InlineData("reference-hash", "hash mismatch")]
        [InlineData("reference-identity", "identity missing")]
        [InlineData("reference-missing", "missing")]
        [InlineData("game-pin", "pinned")]
        [InlineData("game-missing", "game reference")]
        [InlineData("reference-removed", "canonical compiler reference")]
        [InlineData("reference-duplicate", "duplicate compiler reference")]
        [InlineData("joint-game-pin", "pinned")]
        [InlineData("optional-policy", "optional_checks")]
        public void CapturedManifestRejectsDamagedProvenance(string mutation, string reason)
        {
            var manifest = CompiledBuildReferences.CapturedManifest;
            string artifact = CompiledBuildReferences.ProductionPath;
            string configuration = (string)manifest.Attribute("configuration");
            var reference = manifest.Elements("reference").First();
            switch (mutation)
            {
                case "schema": manifest.SetAttributeValue("schema", "unknown"); break;
                case "assets": manifest.Element("assets").Remove(); break;
                case "duplicate-artifact": manifest.Add(new XElement(manifest.Element("artifact"))); break;
                case "configuration": manifest.SetAttributeValue("configuration", "other"); break;
                case "reference-hash": reference.SetAttributeValue("sha256", "incorrect"); break;
                case "reference-identity": reference.Attribute("identity").Remove(); break;
                case "reference-missing": reference.SetAttributeValue("path", Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dll")); break;
                case "game-pin": manifest.SetAttributeValue("rimworldPackage", "Krafs.Rimworld.Ref/wrong"); break;
                case "game-missing": manifest.Elements("reference").Single(r => Path.GetFileName((string)r.Attribute("path")) == "Assembly-CSharp.dll").Remove(); break;
                case "reference-removed": manifest.Elements("reference").First(r => Path.GetFileName((string)r.Attribute("path")) != "Assembly-CSharp.dll").Remove(); break;
                case "reference-duplicate": manifest.Add(new XElement(reference)); break;
                case "joint-game-pin":
                    manifest.SetAttributeValue("rimworldPackage", "Krafs.Rimworld.Ref/wrong");
                    manifest.Elements("reference").Single(r => Path.GetFileName((string)r.Attribute("path")) == "Assembly-CSharp.dll").SetAttributeValue("package", "Krafs.Rimworld.Ref/wrong");
                    break;
                case "optional-policy": manifest.SetAttributeValue("optionalPolicy", "absence-is-pass"); break;
                default: throw new InvalidOperationException("Unknown fixture mutation");
            }
            Assert.Contains(reason, Assert.Throws<InvalidOperationException>(() =>
                CompiledBuildReferences.ValidateManifest(manifest, artifact, configuration)).Message);
        }

        [Fact]
        public void DifferentLoadedArtifactIsRejectedEvenWithValidDeclaredInputs()
        {
            var manifest = CompiledBuildReferences.CapturedManifest;
            string tests = new Uri(typeof(CompiledBuildReferenceTests).Assembly.CodeBase).LocalPath;
            Assert.Contains("hash mismatch", Assert.Throws<InvalidOperationException>(() =>
                CompiledBuildReferences.ValidateManifest(manifest, tests, (string)manifest.Attribute("configuration"))).Message);
        }

        [Fact]
        public void OptionalAbsenceIsSkippedNormallyAndFailsWhenRequired()
        {
            string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".dll");
            var normal = new OptionalModFactAttribute("ControlledOptional", missing, false);
            Assert.Contains("ControlledOptional unavailable", normal.Skip);
            Assert.Contains(missing, normal.Skip);
            Assert.Contains("no integration verified", normal.Skip);
            var strict = new OptionalModFactAttribute("ControlledOptional", missing, true);
            Assert.Null(strict.Skip);
            Assert.Contains("required optional assembly missing", Assert.Throws<Xunit.Sdk.TrueException>(() =>
                SpaceModSeamPresenceTests.OpenRequiredPath("ControlledOptional", missing)).Message);
        }

        [Fact]
        public void PresentMalformedOptionalAssemblyCannotPassAsAnInstalledIntegration()
        {
            string path = Path.GetTempFileName();
            try
            {
                File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
                Assert.Null(new OptionalModFactAttribute("ControlledOptional", path, false).Skip);
                Assert.Throws<BadImageFormatException>(() => SpaceModSeamPresenceTests.OpenRequiredPath("ControlledOptional", path));
            }
            finally { File.Delete(path); }
        }
    }

    // Run this class separately with ETERNAL_COMPILED_MANIFEST absent to exercise
    // the actual C# fallback. Ordinary suite execution uses the captured manifest.
    public class CompiledBuildStandaloneProbeTests
    {
        [Fact]
        public void ExactLoadedArtifactAndPinnedGameReferenceAreValidated()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            Assert.Equal("Eternal.Eternal_Mod", assembly.MemberName(assembly.Type("Eternal.Eternal_Mod")));
            var manifest = CompiledBuildReferences.CapturedManifest;
            Assert.StartsWith("Krafs.Rimworld.Ref/", (string)manifest.Attribute("rimworldPackage"));
            Assert.All(manifest.Elements("reference"), r => Assert.False(string.IsNullOrEmpty((string)r.Attribute("identity"))));
            Assert.Equal("Assembly-CSharp.dll", Path.GetFileName(CompiledBuildReferences.GameReferencePath));
        }
    }
}
