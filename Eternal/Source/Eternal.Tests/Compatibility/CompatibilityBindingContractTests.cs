// Relative Path: Eternal/Source/Eternal.Tests/Compatibility/CompatibilityBindingContractTests.cs
// Creation Date: 13-10-2026
// Author: 0Shard
// Description: Compiled ISF/RI/CTR binding identity, overload, fallback and helper contracts.

using System;
using System.Linq;
using System.Reflection.Emit;
using Eternal.Compatibility;
using Eternal.Tests.Infrastructure;
using Xunit;

namespace Eternal.Tests.Compatibility
{
    public class CompatibilityBindingContractTests
    {
        private const string Binding = "Eternal.Compatibility.CompatibilityBinding";

        [Fact]
        public void CachedReadSeesTheMemberPopulatedByInitialization()
        {
            object member = null;
            var resolved = new object();
            Action initialize = () => member = resolved;

            Assert.Same(resolved, CompatibilityBinding.Cached(initialize, ref member));
            Assert.Same(resolved, member);
        }

        [Theory]
        [InlineData("ItsSorceryDetection", "ItsSorceryActive", "zomuro.itssorcery")]
        [InlineData("CultivatorOfTheRimDetection", "CultivatorOfTheRimActive", "Aranmaho.Xianxia")]
        public void ExactPackageIdsReachTheGameActiveCheck(string detector, string active, string packageId)
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            var il = assembly.Instructions(assembly.Method("Eternal.Compatibility." + detector, "get_" + active));
            Assert.Contains(il, i => i.Code == OpCodes.Ldstr && Equals(i.Operand, packageId));
            Assert.Contains(il, i => Equals(i.Operand, "Verse.ModsConfig.IsActive"));
        }

        [Fact]
        public void RimImmortalKeepsPackageVariantsAndTheFloatSetExpOverload()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            const string detector = "Eternal.Compatibility.RimImmortalDetection";
            var setup = assembly.Instructions(assembly.Method(detector, ".cctor"));
            Assert.Equal(new[] { "RI.RimImmortal.Core", "Atrox.RimImmortal", "RimImmortal", "rimImmortal", "RI", "atrox.rimimmortal" },
                setup.Where(i => i.Code == OpCodes.Ldstr).Select(i => (string)i.Operand));
            var il = assembly.Instructions(assembly.Method(detector, "EnsureTypesInitialized"));
            int start = il.FindIndex(i => i.Code == OpCodes.Ldstr && Equals(i.Operand, "SetExp"));
            Assert.True(start >= 0);
            int call = il.FindIndex(start, i => Equals(i.Operand, "HarmonyLib.AccessTools.Method"));
            Assert.True(call > start);
            Assert.Contains(il.Skip(start).Take(call - start), i => i.Code == OpCodes.Ldtoken && Equals(i.Operand, "System.Single"));
            Assert.Contains(il, i => i.Code == OpCodes.Ldstr && Equals(i.Operand, "sucessRate"));
            Assert.DoesNotContain(il, i => i.Code == OpCodes.Ldstr && Equals(i.Operand, "successRate"));
        }

        [Fact]
        public void SorceryKeepsByRefLedgerAndTypedLevelOverloads()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            var il = assembly.Instructions(assembly.Method("Eternal.Compatibility.ItsSorceryDetection", "EnsureTypesInitialized"));
            foreach (string member in new[] { "AdjustModifiers", "AdjustAbilities", "AdjustHediffs", "ForceLevelUp", "AddExperience", "FindSorcerySchema" })
            {
                Assert.Contains(il, i => i.Code == OpCodes.Ldstr && Equals(i.Operand, member));
            }
            Assert.Equal(3, il.Count(i => Equals(i.Operand, "System.Type.MakeByRefType")));
            foreach (string parameter in new[] { "System.Int32", "System.Boolean", "System.Single", "Verse.Pawn" })
            {
                Assert.Contains(il, i => i.Code == OpCodes.Ldtoken && Equals(i.Operand, parameter));
            }
        }

        [Fact]
        public void LazyGettersAndPatchPreparationKeepDetectorSpecificRoutes()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            foreach (string detector in new[] { "ItsSorceryDetection", "RimImmortalDetection", "CultivatorOfTheRimDetection" })
            {
                string type = "Eternal.Compatibility." + detector;
                var getters = assembly.Reader.GetTypeDefinition(assembly.Type(type)).GetMethods().Where(h =>
                    assembly.Reader.GetString(assembly.Reader.GetMethodDefinition(h).Name).StartsWith("get_", StringComparison.Ordinal) &&
                    assembly.Signature(h).ReturnType.StartsWith("System.Reflection.", StringComparison.Ordinal)).ToArray();
                Assert.Equal(detector == "ItsSorceryDetection" ? 21 : 5, getters.Length);
                foreach (var getter in getters)
                {
                    var il = assembly.Instructions(getter);
                    Assert.Contains(il, i => i.Code == OpCodes.Ldsflda);
                    Assert.Contains(il, i => Equals(i.Operand, Binding + ".Cached"));
                    Assert.DoesNotContain(il, i => i.Code == OpCodes.Newobj && Equals(i.Operand, "System.Action..ctor"));
                }
            }

            foreach (var route in new[]
            {
                ("ItsSorcery.ISF_AddExperience_Patch", "ItsSorceryDetection.get_ItsSorceryActive"),
                ("ItsSorcery.ISF_ApplyOptions_Patch", "ItsSorceryDetection.get_ItsSorceryActive"),
                ("ItsSorcery.ISF_SchemaUse_Patch", "ItsSorceryDetection.get_ItsSorceryActive"),
                ("RimImmortal.RI_SetExp_Patch", "RimImmortalDetection.get_RimImmortalActive"),
                ("RimImmortal.RI_GetFloatUpgrade_Patch", "RimImmortalDetection.get_RimImmortalActive"),
                ("CultivatorOfTheRim.CTR_OnReadingTick_Patch", "CultivatorOfTheRimDetection.get_CultivatorOfTheRimActive"),
                ("CultivatorOfTheRim.CTR_BodyCultivationTick_Patch", "CultivatorOfTheRimDetection.get_CultivatorOfTheRimActive")
            })
            {
                var il = assembly.Instructions(assembly.Method("Eternal.Patches." + route.Item1, "Prepare"));
                Assert.Contains(il, i => Equals(i.Operand, "Eternal.Compatibility." + route.Item2));
                Assert.Contains(il, i => Equals(i.Operand, Binding + ".Prepare"));
            }
        }

        [Theory]
        [InlineData("ItsSorceryDetection", "IsMaxed", 1)]
        [InlineData("ItsSorceryDetection", "GetCurrLevel", 0)]
        public void SorceryFailurePolicyKeepsSafeNoOpAndZeroLevel(string detector, string method, int fallback)
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            var handle = assembly.Method("Eternal.Compatibility." + detector, method);
            var body = assembly.Body(handle);
            var il = assembly.Instructions(handle);
            var code = fallback == 1 ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0;
            Assert.Contains(il, i => i.Offset < body.ExceptionRegions.First().TryOffset && i.Code == code);
            Assert.Contains(il, i => body.ExceptionRegions.Any(r => i.Offset >= r.HandlerOffset &&
                i.Offset < r.HandlerOffset + r.HandlerLength) && i.Code == code);
        }

        [Theory]
        [InlineData("RimImmortalDetection", "GetSucessRate")]
        [InlineData("CultivatorOfTheRimDetection", "GetFinalChance")]
        [InlineData("CultivatorOfTheRimDetection", "GetBodyCultivationSpeed")]
        public void NumericBindingsKeepZeroFallbackBeforeReflectionAndOnFailure(string detector, string method)
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            var handle = assembly.Method("Eternal.Compatibility." + detector, method);
            var body = assembly.Body(handle);
            var il = assembly.Instructions(handle);
            Assert.Contains(il, i => i.Offset < body.ExceptionRegions.First().TryOffset &&
                i.Code == OpCodes.Ldc_R4 && Equals(i.Operand, 0f));
            Assert.Contains(il, i => body.ExceptionRegions.Any(r => i.Offset >= r.HandlerOffset &&
                i.Offset < r.HandlerOffset + r.HandlerLength) && i.Code == OpCodes.Ldc_R4 && Equals(i.Operand, 0f));
        }
    }
}
