// Relative Path: Eternal/Source/Eternal.Tests/UI/MenuValueCalculatorTests.cs
// Creation Date: 12-10-2026
// Author: 0Shard
// Description: Scalar menu calculations exercised without a game host.
using Eternal.UI;
using Eternal.UI.HediffSettings;
using Eternal.UI.Management;
using Eternal.Healing;
using Xunit;

namespace Eternal.Tests.UI
{
    public class MenuValueCalculatorTests
    {
        [Fact]
        public void FocusedRateEditorPreservesClearAndPartialTextAcrossFrames()
        {
            var state = new RateEditBuffer();
            Assert.Equal("1.2000", state.Prepare(1.2f, false));
            state.Update("");
            Assert.Equal("", state.Prepare(1.2f, true));
            state.Update("0.");
            Assert.Equal("0.", state.Prepare(1.2f, true));
        }

        [Fact]
        public void RateEditorCommitHonorsEnabledStateResetAndDefinitionIdentity()
        {
            var first = new RateEditBuffer();
            var second = new RateEditBuffer();
            first.Prepare(1.2f, false);
            first.Update("2.5");
            Assert.Equal("0.5000", second.Prepare(0.5f, false));
            Assert.Equal(1.2f, first.Commit(1.2f, false));
            first.Prepare(1.2f, false);
            first.Update("0.25");
            Assert.Equal(0.25f, first.Commit(1.2f, true));
            first.Prepare(0.25f, false);
            first.Update("-");
            Assert.Equal(0.25f, first.Commit(0.25f, true));
            first.Prepare(0.25f, false);
            first.Update("2");
            Assert.Equal("1.2000", first.Prepare(1.2f, true));
            Assert.False(first.HasEdit);
            first.Update("2");
            first.Reset();
            Assert.Equal("1.2000", first.Prepare(1.2f, true));
        }

        [Fact]
        public void FreeConditionProgressesBesidePaidConditionWithNoBudget()
        {
            Assert.True(LivingHealingProjection.CanAdvance(new HealingCostProfile(0f), 2f, 1.2f, 0f, false));
            Assert.False(LivingHealingProjection.CanAdvance(new HealingCostProfile(1f), 2f, 1.2f, 0f, false));
            Assert.True(LivingHealingProjection.CanAdvance(new HealingCostProfile(1f, 0.1f), 0.1f, 1.2f, 0f, false));
            Assert.False(LivingHealingProjection.CanAdvance(new HealingCostProfile(0f), 2f, 0f, 0f, false));
        }

        [Fact]
        public void LivingEtaUsesRuntimeFactorFloorAndNextActualPass()
        {
            Assert.Equal(470f, LivingHealingProjection.Ticks(new HealingCostProfile(0f), 0.5f, 0.125f, 0.5f, 60, 50));
            Assert.Equal(290f, LivingHealingProjection.Ticks(new HealingCostProfile(0f, 0.001f), 0.0015f, 0.0001f, 1f, 60, 50));
            Assert.Equal(50f, LivingHealingProjection.Ticks(new HealingCostProfile(0f, 0.001f), 0.001f, 0.0001f, 1f, 60, 50));
            Assert.Equal(50, LivingHealingProjection.NextPass(110, 100, 60));
            Assert.Equal(1, LivingHealingProjection.NextPass(170, 100, 60));
            Assert.Equal(0, LivingHealingProjection.NextPass(110, 120, 60));
        }

        [Fact]
        public void RateBudgetAndRecovery_KeepTheirOwnUnitsAndCurrentInputs()
        {
            float coefficient = 1f / 1200f;
            Assert.Equal(1200f, MenuValueCalculator.ProcessingHpPerDay(1.2f, 60), 3);
            Assert.Equal(2400f, MenuValueCalculator.ProcessingHpPerDay(2.4f, 60), 3);
            Assert.Equal(600f, MenuValueCalculator.InjuryHpBudget(0.5f, coefficient), 3);
            Assert.Equal(12000f, MenuValueCalculator.RecoveryEquivalentHpPerDay(1f, 5f, 0.5f, coefficient), 2);
            Assert.Equal(1200f, MenuValueCalculator.RecoveryEquivalentHpPerDay(0.01f, 0.01f, 5f, coefficient), 3);
            Assert.Equal(0f, MenuValueCalculator.RecoveryEquivalentHpPerDay(0f, 5f, 1f, coefficient));
            Assert.True(float.IsNaN(MenuValueCalculator.InjuryHpBudget(1f, 0f)));
        }
        [Fact]
        public void TrackedThresholdEligibility_DoesNotInventMissingStateOrRequireLatchingToDisplayCrossing()
        {
            Assert.Null(MenuValueCalculator.TrackedThresholdEligibility(false, false, 1f, 0f));
            Assert.False(MenuValueCalculator.TrackedThresholdEligibility(true, false, 0.2f, 0.5f));
            Assert.True(MenuValueCalculator.TrackedThresholdEligibility(true, false, 0.6f, 0.5f));
            Assert.True(MenuValueCalculator.TrackedThresholdEligibility(true, true, 0.2f, 0.5f));
            Assert.Null(MenuValueCalculator.TrackedThresholdEligibility(true, false, float.NaN, 0.5f));
        }
        [Fact]
        public void NonPermanentScarWithNormalHealingEnabled_QualifiesPossibleChargeAndCombinedTime()
        {
            var display = MenuValueCalculator.DescribeLivingScar(false, true, true, true, false);
            Assert.Contains("normal healing may consume nutrition", display.Status);
            Assert.DoesNotContain("no nutrition charge", display.Status);
            Assert.True(float.IsNaN(display.RemainingTicks));
        }

        [Fact]
        public void ScarWithNormalHealingDisabled_DescribesOnlyTheUnchargedScarPath()
        {
            var display = MenuValueCalculator.DescribeLivingScar(false, false, false, true, false);
            Assert.Contains("scar healing is uncharged", display.Status);
            Assert.Contains("normal healing is disabled", display.Status);
            Assert.DoesNotContain("may consume nutrition", display.Status);
            Assert.True(float.IsNaN(display.RemainingTicks));
        }

        [Fact]
        public void PermanentInjury_DescribesExclusiveUnchargedPathWithoutInventingRemainingTime()
        {
            var display = MenuValueCalculator.DescribeLivingScar(true, true, false, true, false);
            Assert.Contains("Permanent injury", display.Status);
            Assert.Contains("scar healing is uncharged", display.Status);
            Assert.DoesNotContain("may consume nutrition", display.Status);
            Assert.True(float.IsNaN(display.RemainingTicks));
        }

        [Fact]
        public void ScarUnknownNormalConfiguration_CannotPromiseZeroCost()
        {
            var display = MenuValueCalculator.DescribeLivingScar(false, null, null, true, false);
            Assert.Contains("eligibility is unavailable", display.Status);
            Assert.DoesNotContain("no nutrition charge", display.Status);
        }

        [Fact]
        public void ScarNormalRouteWaitingOrWaived_IsQualifiedWithoutClaimingActiveCharges()
        {
            Assert.Contains("when eligible", MenuValueCalculator.DescribeLivingScar(false, true, false, true, false).Status);
            Assert.Contains("waiting for food or debt capacity", MenuValueCalculator.DescribeLivingScar(false, true, true, false, false).Status);
            Assert.Contains("nutrition is waived", MenuValueCalculator.DescribeLivingScar(false, true, true, true, true).Status);
        }
        [Fact]
        public void CompletedChildWithMissingParent_IsWaitingForRestoration()
        {
            Assert.Equal("Progress complete; waiting for parent restoration",
                MenuValueCalculator.PartStatus(true, true, true, false, false));
        }

        [Theory]
        [InlineData(true, false, false, true, false, "Restored")]
        [InlineData(true, false, true, true, false, "Progress complete; awaiting restoration")]
        [InlineData(true, true, true, true, false, "Progress complete; awaiting restoration")]
        [InlineData(false, true, true, false, true, "Regrowing; parent restoration pending")]
        [InlineData(false, true, true, true, true, "Regrowing")]
        public void PartStatus_DistinguishesProgressAndHealthRestoration(bool complete, bool missing,
            bool pending, bool parentComplete, bool canUpdate, string expected)
        {
            Assert.Equal(expected, MenuValueCalculator.PartStatus(complete, missing, pending, parentComplete, canUpdate));
        }

        [Fact]
        public void ExplicitlyHealedNonBadCondition_RemainsInLivingOverview()
        {
            Assert.True(MenuValueCalculator.IncludeLivingCondition(false, true, false, false, false));
        }

        [Theory]
        [InlineData(false, false, false, false, false, false)]
        [InlineData(false, false, true, false, false, true)]
        [InlineData(true, false, false, false, false, true)]
        [InlineData(true, true, false, true, false, false)]
        [InlineData(true, true, true, true, false, false)]
        [InlineData(true, true, true, false, true, false)]
        public void LivingConditionSelection_HonorsScarRouteAndModExclusions(bool bad, bool configured,
            bool scar, bool modOwned, bool missing, bool expected)
        {
            Assert.Equal(expected, MenuValueCalculator.IncludeLivingCondition(bad, configured, scar, modOwned, missing));
        }
        [Theory]
        [InlineData(false, true, false, true)]
        [InlineData(false, false, true, true)]
        [InlineData(true, false, false, true)]
        [InlineData(false, false, false, false)]
        public void LivingResources_UseFoodOrDebtOrWaiver(bool waiver, bool food, bool debt, bool expected)
        {
            Assert.Equal(expected, MenuValueCalculator.ResourcesPermitHealing(waiver, food, debt));
        }
        [Fact]
        public void PositiveWork_TakesAtLeastOnePass()
        {
            Assert.Equal(250f, MenuValueCalculator.ConditionTicks(1f, 3f, 1f, 250));
            Assert.Equal(120f, MenuValueCalculator.ConditionTicks(4f, 1f, 2f, 60));
        }

        [Fact]
        public void InvalidZeroAndPausedWork_AreDistinct()
        {
            Assert.Equal("No work", MenuValueCalculator.Time(MenuValueCalculator.ProcessingTicks(0f, 1f, 60)));
            Assert.Equal("Paused", MenuValueCalculator.Time(MenuValueCalculator.ProcessingTicks(1f, 0f, 60)));
            Assert.Equal("Unavailable", MenuValueCalculator.Time(MenuValueCalculator.ProcessingTicks(float.NaN, 1f, 60)));
            Assert.True(float.IsNaN(MenuValueCalculator.ProcessingTicks(1f, float.PositiveInfinity, 60)));
        }

        [Fact]
        public void CurrentMultiplier_AppliesExactlyOnce()
        {
            Assert.Equal(0.4f, MenuValueCalculator.NutritionCost(50f, 0.004f, 2f), 6);
            Assert.Equal("125 severity : 1 nutrition", MenuValueCalculator.EffectiveConversion(0.004f, 2f));
            Assert.Equal("No nutrition cost", MenuValueCalculator.EffectiveConversion(0.004f, 0f));
        }

        [Fact]
        public void PositiveTinyNutrition_CannotDisplayZero()
        {
            Assert.Equal("1E-6 nutrition", MenuValueCalculator.Nutrition(0.000001f));
            Assert.Equal("None", MenuValueCalculator.Nutrition(0f));
            Assert.Equal("Unavailable", MenuValueCalculator.Nutrition(null));
        }

        [Fact]
        public void CompactNumbers_RemainReadableWithoutHidingPositiveDebt()
        {
            Assert.Equal("<0.001", MenuValueCalculator.CompactNumber(0.00000004993f));
            Assert.Equal("0", MenuValueCalculator.CompactNumber(0f));
            Assert.Equal("11.5", MenuValueCalculator.CompactNumber(11.5f));
            Assert.Equal("13,800", MenuValueCalculator.CompactNumber(13800f));
            Assert.Equal("Unavailable", MenuValueCalculator.CompactNumber(null));
            Assert.Equal("Unavailable", MenuValueCalculator.CompactNumber(float.PositiveInfinity));
        }

        [Fact]
        public void KnownScarTiming_IsDisplayedWithoutChangingChargeQualifications()
        {
            var permanent = MenuValueCalculator.DescribeLivingScar(true, true, false, false, false, 1080f);
            Assert.Equal(1080f, permanent.RemainingTicks);
            Assert.Contains("uncharged", permanent.Status);
            Assert.DoesNotContain("unavailable", permanent.Status);

            var combined = MenuValueCalculator.DescribeLivingScar(false, true, true, true, false, 1080f);
            Assert.Equal(1080f, combined.RemainingTicks);
            Assert.Contains("normal healing may consume nutrition", combined.Status);
            Assert.Contains("normal healing may finish sooner", combined.Status);
        }

        [Theory]
        [InlineData(0.001f)]
        [InlineData(0.005f)]
        [InlineData(-1f)]
        public void UntouchedEditor_PreservesSavedRate(float existing)
        {
            Assert.Equal(existing, MenuValueCalculator.EditedRate(existing, 0.01f, false));
            Assert.Equal(existing, MenuValueCalculator.EditedRate(existing, float.NaN, true));
            Assert.Equal(3f, MenuValueCalculator.EditedRate(existing, 3f, true));
        }

        [Fact]
        public void DebuffExample_UsesProductionStageAndBodyFactors()
        {
            Assert.Equal(3000f, MenuValueCalculator.ConditionTicks(1f, 1f, 2f, 60, 0));
            Assert.Equal(7500f, MenuValueCalculator.ConditionTicks(1f, 1f, 2f, 60, 3));
            Assert.Equal(0.2f, MenuValueCalculator.StageFactor(-1));
            Assert.Equal(0.2f, MenuValueCalculator.StageFactor(9));
        }

        [Fact]
        public void ScarExample_UsesGlobalRateAndRareCadenceWithoutBodyScaling()
        {
            Assert.Equal(500f, MenuValueCalculator.ProcessingTicks(2f, 1.2f, 250));
        }
    }
}
