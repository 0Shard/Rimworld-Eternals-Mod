// Author: 0Shard
// Description: Actual test-assembly fixtures for exact metadata, malformed inputs and ownership.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;
using Eternal.Tests.Compatibility;
using HarmonyLib;
using Xunit;

namespace Eternal.Tests.Infrastructure
{
    public class CompiledAssemblyContractTests
    {
        private const string Fixture = "Eternal.Tests.Infrastructure.MetadataFixture";
        private static string FixturePath => new Uri(typeof(CompiledAssemblyContractTests).Assembly.CodeBase).LocalPath;
        private static CompiledAssemblyContract Open() => new CompiledAssemblyContract(FixturePath);

        [Fact]
        public void ExactOverload_ReturnGenericAndStaticShapeAreRequired()
        {
            using var assembly = Open();
            var integer = assembly.Method(Fixture, "Choose", new[] { "System.Int32" }, "System.Int32", 0, true);
            Assert.Equal("System.Int32", assembly.Signature(integer).ReturnType);
            var generic = assembly.Method(Fixture, "Echo", new[] { "!!0" }, "!!0", 1, true);
            Assert.Equal(1, assembly.Signature(generic).GenericParameterCount);
            foreach (var mismatch in new Action[]
            {
                () => assembly.Method(Fixture, "Choose", new[] { "System.Int32" }, "System.String"),
                () => assembly.Method(Fixture, "Choose", new[] { "System.Int32" }, isStatic: false),
                () => assembly.Method(Fixture, "Echo", new[] { "!!0" }, genericArity: 0)
            })
            {
                Assert.Contains(Fixture, Assert.Throws<InvalidOperationException>(mismatch).Message);
            }
        }

        [Fact]
        public void AmbiguousAndMissingMethodsFailWithCandidatesAndContext()
        {
            using var assembly = Open();
            string ambiguous = Assert.Throws<InvalidOperationException>(() => assembly.Method(Fixture, "Choose")).Message;
            Assert.Contains("ambiguous", ambiguous);
            Assert.Contains("System.Int32", ambiguous);
            Assert.Contains("System.String", ambiguous);
            string missing = Assert.Throws<InvalidOperationException>(() => assembly.Method(Fixture, "Absent", Array.Empty<string>())).Message;
            Assert.Contains(Fixture + ".Absent", missing);
            Assert.Contains("missing", missing);
        }

        [Fact]
        public void NestedGenericByrefAndArraySignaturesRemainDistinct()
        {
            using var assembly = Open();
            string nested = Fixture + "+Inner`1+Leaf";
            Assert.Equal(nested, assembly.MemberName(assembly.Type(nested)));
            var method = assembly.Method(nested, "Shape", new[] { "!0&", "System.Int32[,]", "System.String[]" }, "!0", 0, false);
            Assert.Equal("!0", assembly.Signature(method).ReturnType);
            var call = assembly.Instructions(assembly.Method(Fixture, "GenericCall"));
            Assert.Contains(call, i => i.Code == OpCodes.Call && Equals(i.Operand, Fixture + ".Echo") && i.CallSignature.HasValue);
        }

        [Fact]
        public void InheritedGetterSetterAndConstructorsResolveExactly()
        {
            using var assembly = Open();
            Assert.Equal("System.Int32", assembly.Signature(assembly.Method(Fixture, "get_Value", Array.Empty<string>(), inherited: true)).ReturnType);
            assembly.Method(Fixture, "set_Value", new[] { "System.Int32" }, "System.Void", inherited: true);
            assembly.Method(Fixture, ".ctor", Array.Empty<string>(), "System.Void", 0, false);
            var dynamic = new List<string>();
            Assert.Equal(4, HarmonyPatchSignatureTests.Validate(assembly, assembly, dynamic, name => name == "Eternal.Tests.Infrastructure.ExactFixturePatch"));
            Assert.Empty(dynamic);
        }

        [Fact]
        public void DecodedOperandsPreserveSignedConstantsWideValuesAndAbsoluteBranches()
        {
            using var assembly = Open();
            var il = assembly.Decode(new byte[] { 0x1f, 0xff, 0x26, 0x21, 1, 2, 3, 4, 5, 6, 7, 8, 0x26, 0x22, 0, 0, 0x80, 0x3f, 0x26, 0x2b, 0, 0x2a }, "operand fixture");
            Assert.Equal(-1, il[0].Operand);
            Assert.Equal(0x0807060504030201L, il[2].Operand);
            Assert.Equal(1f, il[4].Operand);
            Assert.Equal(21, il[6].Operand);
            Assert.Equal(21, il.Last().Offset);
        }

        [Theory]
        [InlineData(new byte[] { 0xfe })]
        [InlineData(new byte[] { 0x20, 1 })]
        [InlineData(new byte[] { 0x45, 0xff, 0xff, 0xff, 0xff })]
        [InlineData(new byte[] { 0x2b, 1, 0x2a })]
        public void MalformedInstructionsFailWithFixtureContext(byte[] bytes)
        {
            using var assembly = Open();
            Assert.Contains("broken fixture", Assert.Throws<BadImageFormatException>(() => assembly.Decode(bytes, "broken fixture")).Message);
        }

        [Fact]
        public void EveryReturnProofRejectsAFalseAndTrueBody()
        {
            using var assembly = Open();
            Assert.True(assembly.ReturnsConstantInt32(assembly.Method(Fixture, "AlwaysFalse"), 0));
            Assert.False(assembly.ReturnsConstantInt32(assembly.Method(Fixture, "Mixed", new[] { "System.Boolean" }), 0));
        }

        [Fact]
        public void OwnershipDisposesValidAndMalformedStreamsAndRejectsUseAfterDispose()
        {
            var stream = new TrackingStream(File.ReadAllBytes(FixturePath));
            var assembly = new CompiledAssemblyContract(stream, "owned fixture");
            assembly.Dispose();
            assembly.Dispose();
            Assert.True(stream.WasDisposed);
            Assert.Throws<ObjectDisposedException>(() => assembly.Type(Fixture));
            var malformed = new TrackingStream(new byte[] { 1, 2, 3 });
            Assert.Contains("malformed fixture", Assert.Throws<BadImageFormatException>(() => new CompiledAssemblyContract(malformed, "malformed fixture")).Message);
            Assert.True(malformed.WasDisposed);
        }

        [Fact]
        public void MissingMismatchedHashAndWrongAssemblyIdentityFailClosed()
        {
            Assert.Throws<InvalidOperationException>(() => CompiledBuildReferences.ValidatedPath(null));
            Assert.Throws<InvalidOperationException>(() => CompiledBuildReferences.ValidatedPath(new XElement("reference", new XAttribute("path", FixturePath), new XAttribute("sha256", "wrong"))));
            using var hash = System.Security.Cryptography.SHA256.Create();
            string digest = BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(FixturePath))).Replace("-", "").ToLowerInvariant();
            var row = new XElement("reference", new XAttribute("path", FixturePath), new XAttribute("sha256", digest), new XAttribute("identity", "Wrong, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"));
            Assert.Contains("identity mismatch", Assert.Throws<InvalidOperationException>(() => CompiledBuildReferences.ValidatedPath(row)).Message);
            row.Attribute("identity").Remove();
            Assert.Contains("identity missing", Assert.Throws<InvalidOperationException>(() => CompiledBuildReferences.ValidatedPath(row)).Message);
        }

        [Fact]
        public void SerializedGenericNamesDoNotDropNestedOrArrayArguments()
        {
            Assert.Equal("System.Nullable`1<Verse.DamageInfo>", MetadataTypeNames.SerializedTypeName("System.Nullable`1[[Verse.DamageInfo, Assembly-CSharp]], mscorlib"));
            Assert.Equal("Example.Outer+Inner[]", MetadataTypeNames.SerializedTypeName("Example.Outer+Inner[], Fixture"));
        }

        [Fact]
        public void RemovedPawnIndirectionAndObsoletePrivateHelpersAreAbsentFromCompiledProduction()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            Assert.False(assembly.TryType("Eternal.DI.PawnWrapper", out _));
            Assert.False(assembly.TryType("Eternal.Interfaces.IPawnData", out _));
            foreach (string method in new[] { "CalculateHediffHealing", "GetBodySizeScaling" })
            {
                var handles = assembly.Methods("Eternal.Healing.UnifiedHediffHealingCalculator", method).ToArray();
                Assert.Single(handles);
                Assert.Equal("Verse.Pawn", assembly.Signature(handles.Single()).ParameterTypes[0]);
            }
            foreach (var member in new[]
            {
                ("Eternal.EternalHediffSetting", "GetDefaultHealingSpeed"),
                ("Eternal.Extensions.NutritionExtensions", "GetNutritionCost"),
                ("Eternal.Extensions.NutritionExtensions", "GetStageBasedHealingSpeed"),
                ("Eternal.EternalHediffHealer", "get_DebtAccumulator"),
                ("Eternal.Healing.EternalCorpseHealingProcessor", "get_FoodCostProcessor")
            })
            {
                Assert.Empty(assembly.Methods(member.Item1, member.Item2));
            }

            Assert.DoesNotContain(assembly.Reader.TypeReferences, h => assembly.MemberName(h) == "Eternal.Interfaces.IPawnData" || assembly.MemberName(h) == "Eternal.DI.PawnWrapper");
            foreach (var field in new[]
            {
                ("Eternal.Caravan.EternalCaravanDeathHandler", "RECOVERY_DURATION"),
                ("Eternal.Compatibility.SpaceModDetection", "SOS2_PACKAGE_ID"),
                ("Eternal.Compatibility.SpaceModDetection", "ODYSSEY_PACKAGE_ID"),
                ("Eternal.Models.PawnAssignmentSnapshot", "setWorkGiverPriorityMethod")
            })
            {
                Assert.DoesNotContain(assembly.Reader.GetTypeDefinition(assembly.Type(field.Item1)).GetFields(), h =>
                assembly.Reader.GetString(assembly.Reader.GetFieldDefinition(h).Name) == field.Item2);
            }

            foreach (string method in new[] { "PostAdd", "PostRemoved" })
            {
                Assert.Contains(assembly.Instructions(assembly.Method("Eternal.Eternal_Hediff", method)), i => i.Code == OpCodes.Rethrow);
            }
        }

        [Fact]
        public void MalformedAttributeAndSignatureBlobsFailWithOwningContext()
        {
            using var assembly = Open();
            var attribute = assembly.Reader.GetTypeDefinition(assembly.Type("Eternal.Tests.Infrastructure.ExactFixturePatch")).GetCustomAttributes().First();
            CheckCorruptedBlob(assembly.Reader.GetCustomAttribute(attribute).Value, false, corrupted =>
                Assert.Contains("broken attribute", Assert.Throws<BadImageFormatException>(() => corrupted.DecodeAttribute(attribute, "broken attribute")).Message));
            var method = assembly.Method(Fixture, "Choose", new[] { "System.Int32" });
            CheckCorruptedBlob(assembly.Reader.GetMethodDefinition(method).Signature, true, corrupted =>
                Assert.Contains(Fixture + ".Choose", Assert.Throws<BadImageFormatException>(() => corrupted.Signature(method)).Message));
        }

        [Fact]
        public void MalformedHarmonyTargetAttributeFailsTheCompleteCheckerWithPatchClassContext()
        {
            using var assembly = Open();
            const string patch = "Eternal.Tests.Infrastructure.ExactFixturePatch";
            var attribute = assembly.Reader.GetTypeDefinition(assembly.Type(patch)).GetCustomAttributes().First();
            CheckCorruptedBlob(assembly.Reader.GetCustomAttribute(attribute).Value, false, corrupted =>
                Assert.Contains(patch, Assert.Throws<BadImageFormatException>(() =>
                    HarmonyPatchSignatureTests.Validate(corrupted, corrupted, new List<string>(), name => name == patch)).Message));
        }

        internal static void CheckCorruptedBlob(BlobHandle blob, bool lastByte, Action<CompiledAssemblyContract> check)
        {
            byte[] bytes = File.ReadAllBytes(FixturePath);
            using (var pe = new PEReader(new MemoryStream(bytes)))
            {
                var reader = pe.GetMetadataReader();
                int offset = pe.PEHeaders.MetadataStartOffset + reader.GetHeapMetadataOffset(HeapIndex.Blob) + MetadataTokens.GetHeapOffset(blob);
                int prefix = bytes[offset] < 0x80 ? 1 : bytes[offset] < 0xc0 ? 2 : 4;
                bytes[offset + prefix + (lastByte ? reader.GetBlobBytes(blob).Length - 1 : 0)] = 0xff;
            }
            using var corrupted = new CompiledAssemblyContract(new MemoryStream(bytes), "corrupted fixture");
            check(corrupted);
        }

        private sealed class TrackingStream : MemoryStream
        {
            public bool WasDisposed;
            public TrackingStream(byte[] bytes) : base(bytes) { }
            protected override void Dispose(bool disposing) { WasDisposed = true; base.Dispose(disposing); }
        }
    }

    internal class MetadataBase
    {
        public int Value { get; set; }
    }
    internal class MetadataFixture : MetadataBase
    {
        public static int Choose(int left) => left;
        public static string Choose(string right) => right;
        public static T Echo<T>(T value) => value;
        public static int GenericCall() => Echo(17);
        public static bool AlwaysFalse() => false;
        public static bool Mixed(bool flag) { if (flag) { return false; } return true; }
        public class Inner<T>
        {
            public class Leaf { public T Shape(ref T value, int[,] matrix, string[] vector) => value; }
        }
    }
    [HarmonyPatch(typeof(MetadataFixture))]
    internal static class ExactFixturePatch
    {
        [HarmonyPatch("Choose", new[] { typeof(int) })]
        [HarmonyPostfix]
        public static void Integer(int left, ref int __result) { }
        [HarmonyPatch("Value", MethodType.Getter)]
        [HarmonyPostfix]
        public static void Getter(MetadataBase __instance, int __result) { }
        [HarmonyPatch("Value", MethodType.Setter)]
        [HarmonyPrefix]
        public static bool Setter(int value) => true;
        [HarmonyPatch(MethodType.Constructor)]
        [HarmonyPrefix]
        public static void Constructor(MetadataFixture __instance) { }
    }
}
