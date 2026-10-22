using System.Linq;
using Eternal.Tests.UI;
using Eternal.UI;
using Eternal.Healing;
using Xunit;

namespace Eternal.Tests.Healing
{
    public class HealingPaymentContractTests
    {
        [Fact]
        public void LivingPositiveFloor_RemovesBeforePaidWorkAndClearsTracking()
        {
            var profile = new HealingCostProfile(1f, 0.001f);
            Assert.Equal(0f, HealingWorkCostCalculator.AffordableSeverity(profile, 0.001f, 0.012f, 0f));
            Assert.Equal(0f, HealingWorkCostCalculator.Cost(profile, 0.001f, 0.001f));
            Assert.True(HealingWorkCostCalculator.IsAtHealingFloor(profile, 0.001f));
            Assert.False(HealingWorkCostCalculator.IsAtHealingFloor(profile, 0.002f));
            Assert.False(HealingWorkCostCalculator.IsAtHealingFloor(new HealingCostProfile(float.NaN, 0.001f), 0.001f));
            Assert.False(HealingWorkCostCalculator.IsAtHealingFloor(profile, float.NaN));
            var calls = MenuAssemblyContract.Calls("Eternal.EternalHediffHealer", "HealHediff").ToList();
            int eligibility = calls.FindIndex(c => c != null && c.EndsWith(".EffectiveCanHeal"));
            int remove = calls.FindIndex(c => c == "Verse.Pawn_HealthTracker.RemoveHediff");
            int bound = calls.FindIndex(c => c != null && c.EndsWith(".AffordableSeverity"));
            Assert.True(eligibility >= 0 && remove > eligibility && remove < bound,
                "An opted-in condition already at its positive floor must complete before the positive-work affordability gate.");
            Assert.Contains(calls.Take(bound), c => c != null && c.EndsWith(".ClearHediffTracking"));
        }

        [Fact]
        public void InjuryTracer_120HpRequestWith005BudgetBuys60HpAndLeaves005FutureCost()
        {
            float coefficient = SettingsDefaults.SeverityToNutritionRatio;
            var profile = new HealingCostProfile(coefficient);
            float affordable = HealingWorkCostCalculator.AffordableSeverity(profile, 120f, 120f, 0.03f + 0.02f);
            Assert.Equal(60f, affordable, 4);
            Assert.Equal(0.05f, MenuValueCalculator.NutritionCost(affordable, coefficient, 1f), 6);
            Assert.Equal(0.05f, CorpseHealingCostCalculator.FutureNutrition(120f - affordable, coefficient, 1f, false), 6);
        }

        [Fact]
        public void LivingPath_BoundsWorkBeforeMutationAndSettlesMeasuredCost()
        {
            var calls = MenuAssemblyContract.Calls("Eternal.EternalHediffHealer", "HealHediff").ToList();
            int budget = calls.FindIndex(c => c != null && c.EndsWith(".GetAvailableNutrition"));
            int mutate = calls.FindIndex(c => c != null && c.EndsWith(".ApplyHealingWithTracking"));
            int settle = calls.FindIndex(c => c != null && c.EndsWith(".SettleNutrition"));
            Assert.True(budget >= 0 && budget < mutate && mutate < settle,
                "Actual nutrition budget must bound mutation before actual-work settlement.");
        }

        [Fact]
        public void Settlement_ReturnsObservedFoodAndLedgerDeltasWithoutReapplyingMultipliers()
        {
            var calls = MenuAssemblyContract.Calls("Eternal.Resources.FoodCostProcessor", "SettleNutrition").ToList();
            Assert.DoesNotContain("Eternal.Interfaces.IFoodDebtReader.GetDebt", calls);
            Assert.Contains(calls, c => c != null && c.EndsWith(".AddDebtAndGetAccepted"));
            Assert.DoesNotContain(calls, c => c != null && c.EndsWith(".get_NutritionCostMultiplier"));
            Assert.DoesNotContain(MenuAssemblyContract.Calls("Eternal.Resources.FoodCostProcessor", "GetAvailableNutrition"),
                c => c != null && c.Contains("RegisterPawn"));
        }
    }
}
