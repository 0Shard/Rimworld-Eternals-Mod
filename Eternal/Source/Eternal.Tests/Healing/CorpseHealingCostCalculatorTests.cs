// Relative Path: Eternal/Source/Eternal.Tests/Healing/CorpseHealingCostCalculatorTests.cs
// Creation Date: 12-10-2026
// Author: 0Shard
// Description: Actual-work corpse charges and compiled result-to-debt data flow.
using Eternal.Tests.Infrastructure;
using System.Linq;
using System.Reflection.Emit;
using Eternal.Healing;
using Eternal.Tests.UI;
using Xunit;

namespace Eternal.Tests.Healing
{
    public class CorpseHealingCostCalculatorTests
    {
        [Fact]
        public void CutoffTail_025Severity_001PerPass_ChargesAllRemovedWork()
        {
            var first = CorpseHealingCostCalculator.CalculateStep(0.025f, 0.01f, 0.004f, 1f);
            Assert.False(first.IsComplete);
            Assert.Equal(0.015f, first.RemainingSeverity, 7);
            Assert.Equal(0.01f, first.ConsumedSeverity, 7);
            var second = CorpseHealingCostCalculator.CalculateStep(first.RemainingSeverity, 0.01f, 0.004f, 1f);
            Assert.True(second.IsComplete);
            Assert.Equal(0f, second.RemainingSeverity);
            Assert.Equal(0.015f, second.ConsumedSeverity, 7);
            Assert.Equal(0.025f, first.ConsumedSeverity + second.ConsumedSeverity, 7);
            Assert.Equal(CorpseHealingCostCalculator.FutureNutrition(0.025f, 0.004f, 1f, false),
                first.NutritionCharge + second.NutritionCharge, 7);
            Assert.Equal(2f, CorpseHealingCostCalculator.ProcessingPasses(0.025f, 0.01f));
        }

        [Theory]
        [InlineData(1f)]
        [InlineData(0.7f)]
        [InlineData(6f)]
        public void FourSeverity_TotalChargeIsIndependentOfPassCount(float perPass)
        {
            float severity = 4f;
            float total = 0f;
            int count = 0;
            while (severity > 0f && count++ < 20)
            {
                var step = CorpseHealingCostCalculator.CalculateStep(severity, perPass, 0.004f, 2f);
                Assert.True(step.IsValid);
                total += step.NutritionCharge;
                severity = step.RemainingSeverity;
            }
            Assert.Equal(0f, severity);
            Assert.Equal(0.032f, total, 6);
        }

        [Fact]
        public void ExactZeroCompletion_IsChargedOnceAndFinalPassIsBounded()
        {
            var step = CorpseHealingCostCalculator.CalculateStep(1f, 3f, 0.004f, 1f);
            Assert.Equal(1f, step.ConsumedSeverity);
            Assert.Equal(0.004f, step.NutritionCharge, 7);
            var again = CorpseHealingCostCalculator.CalculateStep(step.RemainingSeverity, 3f, 0.004f, 1f);
            Assert.Equal(0f, again.NutritionCharge);
            Assert.True(again.IsComplete);
        }

        [Fact]
        public void Resume_AddsOnlyRemainingTailToExistingDebtWithCurrentMultiplier()
        {
            float priorDebt = 7.125f;
            var resumed = CorpseHealingCostCalculator.CalculateStep(0.015f, 0.01f, 0.004f, 3f);
            Assert.Equal(0.015f, resumed.ConsumedSeverity, 7);
            Assert.Equal(7.12518f, priorDebt + resumed.NutritionCharge, 5);
            Assert.Equal(7.125f, priorDebt);
            Assert.Equal(0f, CorpseHealingCostCalculator.FutureNutrition(0.015f, 0.004f, 3f, true));
        }

        [Fact]
        public void NonfiniteOrNegativeWork_IsRejectedWithoutCharge()
        {
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, -1f })
            {
                var step = CorpseHealingCostCalculator.CalculateStep(invalid, 1f, 0.004f, 1f);
                Assert.False(step.IsValid);
                Assert.Equal(0f, step.NutritionCharge);
            }
            Assert.False(CorpseHealingCostCalculator.CalculateStep(1f, 0f, 0.004f, 1f).IsValid);
            Assert.False(CorpseHealingCostCalculator.CalculateStep(1f, 1f, 0.004f, float.NaN).IsValid);
        }

        [Fact]
        public void Processor_UsesTheSameStepLocalForRemainingCompletionAndDebtBeforeCleanup()
        {
            var il = MenuAssemblyContract.Instructions("Eternal.Healing.EternalCorpseHealingProcessor", "ProcessCorpseInjuryTick");
            int calculate = il.FindIndex(i => Equals(i.Operand, "Eternal.Healing.CorpseHealingCostCalculator.CalculateProfileStep"));
            Assert.True(calculate >= 0);
            int store = calculate + 1;
            while (il[store].Code == OpCodes.Nop)
            {
                store++;
            }

            int local = CompiledAssemblyContract.Local(il[store]);
            Assert.True(local >= 0, "The calculated completion result must be stored in a local.");
            string[] fields = { "get_RemainingSeverity", "get_IsComplete", "get_NutritionCharge" };
            foreach (var field in fields)
            {
                var getters = il.Select((i, n) => new { i, n }).Where(v => Equals(v.i.Operand, "Eternal.Healing.CorpseHealingStep." + field)).ToArray();
                Assert.NotEmpty(getters);
                foreach (var getter in getters)
                {
                    Assert.Equal(local, CompiledAssemblyContract.Local(il[getter.n - 1]));
                }
            }
            int charge = il.FindIndex(i => Equals(i.Operand, "Eternal.Healing.CorpseHealingStep.get_NutritionCharge"));
            int addDebt = il.FindIndex(i => (i.Operand as string)?.EndsWith(".AddResurrectionDebt") == true);
            Assert.True(charge < addDebt && addDebt < il.FindIndex(i => Equals(i.Operand,
                "Eternal.Healing.EternalCorpseHealingProcessor.CompactCompletedQueue")));
            Assert.Equal(OpCodes.Callvirt, il[addDebt].Code);
            // The exact tested charge is the final argument, with no intervening arithmetic.
            Assert.All(il.Skip(charge + 1).Take(addDebt - charge - 1), i => Assert.Equal(OpCodes.Nop, i.Code));
            Assert.DoesNotContain(il, i => (i.Operand as string)?.EndsWith(".get_EnergyCost") == true);
            int remaining = il.FindIndex(i => Equals(i.Operand, "Eternal.Healing.CorpseHealingStep.get_RemainingSeverity"));
            Assert.Contains(il.Skip(remaining), i => Equals(i.Operand, "Eternal.HealingItem.set_Severity"));
            Assert.Contains(il.Skip(remaining), i => Equals(i.Operand, "Verse.Hediff.set_Severity"));
            int completed = il.FindIndex(i => Equals(i.Operand, "Eternal.Healing.CorpseHealingStep.get_IsComplete"));
            Assert.Contains(il.Skip(completed).TakeWhile(i => !Equals(i.Operand,
                "Eternal.Healing.EternalCorpseHealingProcessor.CompactCompletedQueue")), i => i.Code.FlowControl == FlowControl.Cond_Branch);
        }

        [Fact]
        public void Preview_UsesSharedRemainingWorkCostAndCutoffAwarePasses()
        {
            var calls = MenuAssemblyContract.Calls("Eternal.UI.ResurrectionPreviewValues", "Estimate");
            Assert.Contains("Eternal.Healing.CorpseHealingCostCalculator.FutureNutrition", calls);
            Assert.Contains("Eternal.Healing.CorpseHealingCostCalculator.ProcessingPasses", calls);
            Assert.Contains("Eternal.Healing.CorpseHealingCostCalculator.RemainingWork",
                MenuAssemblyContract.Calls("Eternal.Healing.CorpseHealingCostCalculator", "FutureNutrition"));
        }
    }
}
