// Relative Path: Eternal/Source/Eternal.Tests/UI/ResurrectionPreviewTests.cs
// Creation Date: 12-10-2026
// Author: 0Shard
// Description: Executes authoritative source selection and corpse snapshot scalars without game objects.
using System.Collections.Generic;
using System.Linq;
using Eternal.UI;
using Xunit;

namespace Eternal.Tests.UI
{
    public class ResurrectionPreviewTests
    {
        [Fact]
        public void Confirmation_FormatsTheCurrentKnownNutritionThroughFiniteAwareFormatter()
        {
            var il = MenuAssemblyContract.Instructions("Eternal.UI.ResurrectionGizmo", "Summary");
            int nutrition = il.FindIndex(i => Equals(i.Operand, "KnownNutrition"));
            int debt = il.FindIndex(i => Equals(i.Operand, "IncurredDebt"));
            Assert.True(nutrition >= 0 && debt > nutrition);
            Assert.Contains(il.Skip(nutrition).Take(debt - nutrition),
                i => Equals(i.Operand, "Eternal.UI.MenuValueCalculator.Nutrition"));
        }
        [Fact]
        public void KnownRegrowthWithInvalidMultiplier_IsUnavailableRatherThanFree()
        {
            var snapshot = new ResurrectionPreviewSnapshot();
            snapshot.Work.Add(new CorpsePreviewWork { IsRegrowth = true, RegrowthEffort = 300f });
            ResurrectionPreviewValues.Estimate(snapshot, 1f, 1f, 60, 0.004f, float.NaN);
            Assert.Equal("Unavailable", MenuValueCalculator.Nutrition(snapshot.KnownNutrition));
            Assert.Contains("nutrition is unavailable", snapshot.Qualification);
        }

        [Theory]
        [InlineData(300f, float.PositiveInfinity)]
        [InlineData(-1f, 1f)]
        [InlineData(float.NaN, 1f)]
        [InlineData(float.PositiveInfinity, 1f)]
        [InlineData(float.MaxValue, float.MaxValue)]
        public void InvalidKnownRegrowthCost_CannotFormatAsNone(float effort, float multiplier)
        {
            var snapshot = new ResurrectionPreviewSnapshot();
            snapshot.Work.Add(new CorpsePreviewWork { IsRegrowth = true, RegrowthEffort = effort });
            ResurrectionPreviewValues.Estimate(snapshot, 1f, 1f, 60, 0.004f, multiplier);
            Assert.Equal("Unavailable", MenuValueCalculator.Nutrition(snapshot.KnownNutrition));
            Assert.True(snapshot.Qualified);
        }

        [Fact]
        public void InvalidKnownRegrowth_MakesMixedTotalUnavailable()
        {
            var snapshot = new ResurrectionPreviewSnapshot();
            snapshot.Work.Add(new CorpsePreviewWork { Severity = 2f });
            snapshot.Work.Add(new CorpsePreviewWork { IsRegrowth = true, RegrowthEffort = -1f });
            ResurrectionPreviewValues.Estimate(snapshot, 1f, 1f, 60, 0.004f, 1f);
            Assert.Equal("Unavailable", MenuValueCalculator.Nutrition(snapshot.KnownNutrition));
            Assert.Equal(2f, snapshot.Work[0].Severity);
        }

        [Fact]
        public void RealFoodWaiver_RemainsZeroWithInvalidRegrowthInputs()
        {
            var snapshot = new ResurrectionPreviewSnapshot { Waived = true };
            snapshot.Work.Add(new CorpsePreviewWork { IsRegrowth = true, RegrowthEffort = float.NaN });
            ResurrectionPreviewValues.Estimate(snapshot, 1f, 1f, 60, 0.004f, float.NaN);
            Assert.Equal(0f, snapshot.KnownNutrition);
            Assert.True(snapshot.Waived);
            Assert.DoesNotContain("nutrition is unavailable", snapshot.Qualification);
        }

        [Fact]
        public void IntentionallyUnknownRegrowth_RetainsQualifiedKnownConditionCost()
        {
            var snapshot = new ResurrectionPreviewSnapshot();
            snapshot.Work.Add(new CorpsePreviewWork { Severity = 2f });
            snapshot.Work.Add(new CorpsePreviewWork { IsRegrowth = true, RegrowthEffort = null });
            ResurrectionPreviewValues.Estimate(snapshot, 1f, 1f, 60, 0.004f, 1f);
            Assert.Equal(0.008f, snapshot.KnownNutrition, 6);
            Assert.Contains("only known condition work", snapshot.Qualification);
        }
        [Fact]
        public void InvalidCurrentCostCoefficient_IsUnavailableInsteadOfZeroCost()
        {
            var snapshot = new ResurrectionPreviewSnapshot();
            snapshot.Work.Add(new CorpsePreviewWork { Severity = 4f });
            ResurrectionPreviewValues.Estimate(snapshot, 1f, 1f, 60, 0.004f, float.NaN);
            Assert.False(MenuValueCalculator.IsFinite(snapshot.KnownNutrition));
            Assert.True(snapshot.Qualified);
        }
        [Theory]
        [InlineData(false, true, false, ResurrectionStartOutcome.Rejected)]
        [InlineData(true, true, true, ResurrectionStartOutcome.Active)]
        [InlineData(true, false, false, ResurrectionStartOutcome.Completed)]
        [InlineData(true, true, false, ResurrectionStartOutcome.Rejected)]
        public void StartOutcome_HonorsProcessorResultAndFreshPawnState(bool started, bool dead, bool active, ResurrectionStartOutcome expected)
        {
            Assert.Equal(expected, ResurrectionPreviewValues.StartOutcome(started, dead, active));
        }

        [Fact]
        public void StartValidation_RejectsRemovedUnavailableDuplicateOrDisabledState()
        {
            Assert.True(ResurrectionPreviewValues.CanStart(true, true, false, false));
            Assert.False(ResurrectionPreviewValues.CanStart(false, true, false, false));
            Assert.False(ResurrectionPreviewValues.CanStart(true, false, false, false));
            Assert.False(ResurrectionPreviewValues.CanStart(true, true, true, false));
            Assert.False(ResurrectionPreviewValues.CanStart(true, true, false, true));
        }

        [Fact]
        public void DrawCapture_HasNoRegistrationHydrationOrDebtMutations()
        {
            foreach (var calls in new[] { MenuAssemblyContract.Calls("Eternal.UI.ResurrectionPreview", "Capture"),
                MenuAssemblyContract.Calls("Eternal.UI.ResurrectionPreview", "SelectQueue"),
                MenuAssemblyContract.Calls("Eternal.UI.ResurrectionGizmo", "Refresh") })
            {
                Assert.DoesNotContain(calls, c => c != null && (c.Contains("RegisterCorpse") || c.Contains("TryHydrate") || c.Contains("AddResurrectionDebt")));
            }

            var action = MenuAssemblyContract.Calls("Eternal.UI.ResurrectionGizmo", "StartResurrection");
            Assert.Contains("Eternal.Corpse.EternalCorpseManager.RegisterCorpse", action);
            Assert.Contains("Eternal.Healing.EternalCorpseHealingProcessor.StartCorpseHealing", action);
            Assert.Contains("Eternal.UI.ResurrectionPreviewValues.StartOutcome", action);
        }

        [Fact]
        public void CorpseInspection_UsesCurrentPreviewAndTinyDebtFormatterWithoutHistoricalProgress()
        {
            var calls = MenuAssemblyContract.Calls("Eternal.Corpse.EternalCorpseComponent", "GetStatusDescription");
            Assert.Contains("Eternal.UI.ResurrectionPreview.Capture", calls);
            Assert.Contains("Eternal.UI.ResurrectionGizmo.Summary", calls);
            Assert.DoesNotContain(calls, c => c != null && (c.Contains("RegisterCorpse") || c.Contains("TryHydrate")));
            Assert.DoesNotContain(MenuAssemblyContract.Instructions("Eternal.Corpse.EternalCorpseComponent", "GetStatusDescription"),
                i => Equals(i.Operand, "HealingProgress"));
            Assert.Contains("Eternal.UI.MenuValueCalculator.Nutrition", MenuAssemblyContract.Calls("Eternal.UI.ResurrectionGizmo", "Summary"));
            Assert.Equal("1E-6 nutrition", MenuValueCalculator.Nutrition(0.000001f));
        }

        [Fact]
        public void RegrowthEffort_IsQualifiedAndPreservesProductionEffortChargePath()
        {
            var snapshot = new ResurrectionPreviewSnapshot();
            snapshot.Work.Add(new CorpsePreviewWork { IsRegrowth = true, RegrowthEffort = 300f, RegrowthTicks = 60000f });
            ResurrectionPreviewValues.Estimate(snapshot, 1.2f, 1f, 60, 0.004f, 2f);
            Assert.Equal(2.4f, snapshot.KnownNutrition, 6);
            Assert.True(snapshot.Qualified);
            Assert.True(float.IsNaN(snapshot.EstimatedTicks));
            var calls = MenuAssemblyContract.Calls("Eternal.Healing.EternalCorpseHealingProcessor", "ProcessCorpseRegrowthTick");
            Assert.Contains(calls, c => c != null && c.EndsWith(".ApplyHealing"));
            Assert.Contains("Eternal.Healing.RegrowthBatchResult.get_NutritionCharge", calls);
            Assert.Contains(calls, c => c != null && c.EndsWith(".IsRegrowthComplete"));
            Assert.Contains(calls, c => c != null && c.EndsWith(".AddResurrectionDebt"));
            Assert.DoesNotContain("Eternal.Healing.CorpseHealingCostCalculator.CalculateStep", calls);
        }

        [Fact]
        public void RepeatedEstimate_UsesCurrentMultiplierWithoutChangingWorkOrDebt()
        {
            var snapshot = new ResurrectionPreviewSnapshot { IncurredDebt = 9f };
            snapshot.Work.Add(new CorpsePreviewWork { Severity = 0.025f });
            ResurrectionPreviewValues.Estimate(snapshot, 0.01f, 1f, 60, 0.004f, 1f);
            Assert.Equal(0.0001f, snapshot.KnownNutrition, 7);
            ResurrectionPreviewValues.Estimate(snapshot, 0.01f, 1f, 60, 0.004f, 2f);
            Assert.Equal(0.0002f, snapshot.KnownNutrition, 7);
            Assert.Equal(0.025f, snapshot.Work[0].Severity);
            Assert.Equal(9f, snapshot.IncurredDebt);
            Assert.Equal(120f, snapshot.EstimatedTicks);
        }
        [Fact]
        public void CutoffTailPreview_025Severity_001PerPass_TakesTwoPasses()
        {
            var snapshot = new ResurrectionPreviewSnapshot();
            snapshot.Work.Add(new CorpsePreviewWork { Severity = 0.025f });
            ResurrectionPreviewValues.Estimate(snapshot, 0.01f, 1f, 60, 0.004f, 1f);
            Assert.Equal(120f, snapshot.EstimatedTicks);
            Assert.Equal(0.0001f, snapshot.KnownNutrition, 7);
        }
        [Fact]
        public void HydratedActiveEmptyQueue_RemainsAuthoritative()
        {
            var active = new List<float>();
            var death = new List<float> { 4f };
            int fallbackCalls = 0;
            var result = HealingQueueSelector.Select(true, active, death, () => { fallbackCalls++; return new[] { 9f }; });
            Assert.Same(active, result.Items);
            Assert.Empty(result.Items);
            Assert.Equal(HealingQueueSource.Active, result.Source);
            Assert.Equal(0, fallbackCalls);
            Assert.Equal(new[] { 4f }, death);
        }

        [Fact]
        public void CapturedDeathQueue_WinsOverChangedLiveHealthWithoutMutation()
        {
            var active = new List<float> { 2f };
            var death = new List<float> { 4f, 5f };
            var result = HealingQueueSelector.Select(false, active, death, () => new[] { 99f });
            Assert.Same(death, result.Items);
            Assert.Equal(HealingQueueSource.CapturedAtDeath, result.Source);
            Assert.Equal(new[] { 2f }, active);
            Assert.Equal(new[] { 4f, 5f }, death);
        }

        [Fact]
        public void EmptyUnhydratedDeathQueue_UsesExplicitFallbackOrUnavailable()
        {
            Assert.Equal(HealingQueueSource.Calculated,
                HealingQueueSelector.Select(false, new float[0], new float[0], () => new[] { 7f }).Source);
            Assert.Equal(HealingQueueSource.Unavailable,
                HealingQueueSelector.Select<float>(false, null, null, null).Source);
        }

        [Theory]
        [InlineData(true, true, true, true, false, false, CorpsePreviewStatus.WaitingToStart)]
        [InlineData(true, true, true, true, true, false, CorpsePreviewStatus.Active)]
        [InlineData(true, true, false, true, false, false, CorpsePreviewStatus.Unavailable)]
        [InlineData(true, true, true, false, true, false, CorpsePreviewStatus.Unavailable)]
        [InlineData(false, false, true, true, false, false, CorpsePreviewStatus.Completed)]
        [InlineData(true, true, true, true, false, true, CorpsePreviewStatus.Disabled)]
        public void CorpseStatus_UsesTrackingAndProcessorState(bool dead, bool valid, bool tracking,
            bool processor, bool active, bool disabled, CorpsePreviewStatus expected)
        {
            Assert.Equal(expected, ResurrectionPreviewValues.Status(dead, valid, tracking, processor, active, disabled));
        }

        [Fact]
        public void ScalarOnlyCorpseConditions_UseCurrentGlobalBodyRateAndNormalCadenceInParallel()
        {
            var snapshot = new ResurrectionPreviewSnapshot();
            snapshot.Work.Add(new CorpsePreviewWork { Label = "Scar", Severity = 4f, ScalarOnly = true });
            snapshot.Work.Add(new CorpsePreviewWork { Label = "Injury", Severity = 2f });
            ResurrectionPreviewValues.Estimate(snapshot, 1f, 2f, 60, 0.004f, 2f);
            Assert.Equal(120f, snapshot.EstimatedTicks);
            Assert.Equal(0.048f, snapshot.KnownNutrition, 6);
            Assert.Equal(4f, snapshot.Work[0].Severity);
            Assert.Equal(2, snapshot.ConditionCount);
            Assert.False(snapshot.Qualified);
        }

        [Fact]
        public void UnknownRegrowthDependency_QualifiesEstimateInsteadOfInventingTime()
        {
            var snapshot = new ResurrectionPreviewSnapshot { Waived = true };
            snapshot.Work.Add(new CorpsePreviewWork { IsRegrowth = true, Severity = 2f });
            ResurrectionPreviewValues.Estimate(snapshot, 1f, 1f, 60, 0.004f, 1f);
            Assert.True(snapshot.Qualified);
            Assert.True(float.IsNaN(snapshot.EstimatedTicks));
            Assert.Equal(0f, snapshot.KnownNutrition);
        }
    }
}
