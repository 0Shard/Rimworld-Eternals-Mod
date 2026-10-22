using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using Eternal.Tests.Infrastructure;
using Xunit;

namespace Eternal.Tests.Healing
{
    public class ScarHealingRecordContractTests
    {
        private const string Applier = "Eternal.Healing.Scars.ScarHealingApplier";
        private const string Coordinator = "Eternal.EternalScarHealing";

        [Fact]
        public void RecordStorageAndKeyUseExistingInstanceIdentity()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            var field = Assert.Single(assembly.Reader.GetTypeDefinition(assembly.Type(Applier)).GetFields(),
                handle => assembly.Reader.GetString(assembly.Reader.GetFieldDefinition(handle).Name) == "activeHealingRecords");
            Assert.Equal("System.Collections.Generic.Dictionary`2<Eternal.Infrastructure.HealingDictionaryKey,Eternal.ScarHealingRecord>",
                assembly.Reader.GetFieldDefinition(field).DecodeSignature(assembly.Names, (object)null));
            var key = assembly.Instructions(assembly.Method(Applier, "GetRecordKey"));
            Assert.Contains(key, i => i.Code == OpCodes.Newobj
                && Equals(i.Operand, "Eternal.Infrastructure.HealingDictionaryKey..ctor"));
        }

        [Fact]
        public void GetterRequiresLiveInstanceAndNeverCreatesOrMutatesRecords()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            var getter = assembly.Instructions(assembly.Method(Applier, "GetHealingRecord"));
            Assert.Contains(getter, i => Equals(i.Operand, Applier + ".IsLiveScar"));
            AssertReferenceComparison(getter, "Eternal.ScarHealingRecord.get_Scar", OpCodes.Ldarg_1);
            AssertReferenceComparison(getter, "Eternal.ScarHealingRecord.get_Pawn", OpCodes.Ldarg_2);
            Assert.DoesNotContain(getter, i => i.Code == OpCodes.Newobj || i.Code == OpCodes.Stfld);
            Assert.DoesNotContain(getter, i => (i.Operand as string)?.EndsWith(".set_Item", StringComparison.Ordinal) == true);
            var live = assembly.Instructions(assembly.Method(Applier, "IsLiveScar"));
            AssertReferenceComparison(live, "Verse.Hediff.pawn", OpCodes.Ldarg_1);
            Assert.Contains(live, i => (i.Operand as string)?.EndsWith(".Contains", StringComparison.Ordinal) == true);
        }

        [Fact]
        public void PreviewReadsExistingRecordAndUsesPureKernelWithoutProcessingOrRegistration()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            var preview = assembly.Instructions(assembly.Method(Coordinator, "GetHealingTiming"));
            Assert.Contains(preview, i => Equals(i.Operand, Applier + ".GetHealingRecord"));
            Assert.Contains(preview, i => Equals(i.Operand, "Eternal.Healing.Scars.ScarHealingTiming.Calculate"));
            Assert.Contains(preview, i => Equals(i.Operand, "Eternal.EternalModState.get_IsDisabled"));
            Assert.DoesNotContain(preview, i => Equals(i.Operand, "Eternal.Eternal_Settings.CreateSnapshot"));
            Assert.DoesNotContain(preview, i => i.Code == OpCodes.Stfld);
            foreach (var forbidden in new[] { "UpdateHealingRecords", "HealScar", "ProcessScarHealing", "ProcessPawnScarHealing" })
            {
                Assert.DoesNotContain(preview, i => (i.Operand as string)?.EndsWith("." + forbidden, StringComparison.Ordinal) == true);
            }
        }

        [Fact]
        public void ProductionHealingAndProjectionUseOneFloatHealingAmountHelper()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            foreach (string type in new[] { "Eternal.Healing.Scars.ScarCostCalculator", "Eternal.Healing.Scars.ScarHealingTiming" })
            {
                string method = type.EndsWith("ScarCostCalculator", StringComparison.Ordinal)
                    ? "CalculateHealingAmount" : "Calculate";
                var instructions = assembly.Instructions(assembly.Method(type, method));
                Assert.Contains(instructions,
                    i => Equals(i.Operand, "Eternal.Healing.Scars.ScarHealingTiming.CalculateHealingAmount"));
            }
        }

        [Fact]
        public void EmptyScarListStillReconcilesDetachedRecords()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            var processing = assembly.Instructions(assembly.Method(Coordinator, "ProcessPawnScarHealing"));
            int update = processing.FindIndex(i => Equals(i.Operand, Applier + ".UpdateHealingRecords"));
            int scarCount = processing.FindIndex(i => (i.Operand as string)?.EndsWith(".get_Count", StringComparison.Ordinal) == true);
            Assert.True(update >= 0 && scarCount > update);
        }

        private static void AssertReferenceComparison(List<CompiledInstruction> instructions,
            string instanceMember, OpCode argument)
        {
            int read = Assert.Single(Enumerable.Range(0, instructions.Count),
                index => Equals(instructions[index].Operand, instanceMember));
            Assert.Equal(argument, instructions[read + 1].Code);
            var comparison = instructions[read + 2];
            // Roslyn can lower ReferenceEquals to a direct reference branch or ceq.
            Assert.True(Equals(comparison.Operand, "System.Object.ReferenceEquals")
                || comparison.Code == OpCodes.Ceq
                || comparison.Code == OpCodes.Beq || comparison.Code == OpCodes.Beq_S
                || comparison.Code == OpCodes.Bne_Un || comparison.Code == OpCodes.Bne_Un_S,
                "Expected an exact instance comparison for " + instanceMember);
        }
    }
}
