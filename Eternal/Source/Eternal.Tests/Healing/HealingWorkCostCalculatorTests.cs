using Eternal.Healing;
using Xunit;

namespace Eternal.Tests.Healing
{
    public class HealingWorkCostCalculatorTests
    {
        private static HealingCostProfile Profile(bool chronic = false, float floor = 0f, float maximum = 1f,
            float range = 0f, float multiplier = 1f, float global = 1f, float[] stages = null)
            => HealingWorkCostCalculator.CreateProfile(false, chronic, floor, maximum, -1f, range, multiplier, global, stages);

        [Fact]
        public void Categories_HaveSeparateFullRangePricesAndInjuryUnits()
        {
            Assert.Equal(1f, HealingWorkCostCalculator.Cost(Profile(), 1f, 0f));
            Assert.Equal(3f, HealingWorkCostCalculator.Cost(Profile(chronic: true), 1f, 0f));
            var injury = HealingWorkCostCalculator.CreateProfile(true, false, 0f, float.MaxValue, -1f, 0f, 1f, 1f);
            Assert.Equal(1f, HealingWorkCostCalculator.Cost(injury, 1200f, 0f), 6);
        }

        [Fact]
        public void StageIntegral_IsAdditiveAcrossBatchCadenceAndResumeBoundaries()
        {
            var profile = Profile(stages: new[] { 0.75f, 0f, 0.5f, 0.25f, 0.5f, float.NaN, float.PositiveInfinity });
            Assert.Equal(4, profile.Stages.Count);
            Assert.Equal(1.375f, HealingWorkCostCalculator.Cost(profile, 1f, 0f), 6);
            float sum = 0f;
            for (int i = 100; i > 0; i--)
            {
                sum += HealingWorkCostCalculator.Cost(profile, i / 100f, (i - 1) / 100f);
            }
            Assert.Equal(1.375f, sum, 5);
            Assert.Equal(1.375f, HealingWorkCostCalculator.Cost(profile, 1f, 0.5f) +
                HealingWorkCostCalculator.Cost(profile, 0.5f, 0f), 6);
        }

        [Fact]
        public void StagePremium_CapsAtTwoAndDoesNotInventAFullRange()
        {
            var profile = Profile(maximum: float.MaxValue, stages: new[] { 0f, 1f, 2f, 3f, 4f, 5f, 6f });
            Assert.Contains("fallback", profile.RangeBasis);
            Assert.Equal(0f, profile.Range);
            Assert.Equal(2f, profile.StagePremium(100f));
            Assert.Equal(2f, HealingWorkCostCalculator.Cost(profile, 7f, 6f));
            Assert.Equal(1f, HealingWorkCostCalculator.Cost(Profile(maximum: float.MaxValue, range: 5f), 5f, 0f));
        }

        [Fact]
        public void FloorAndMultipliers_AreAppliedExactlyOnce()
        {
            var profile = Profile(chronic: true, floor: 0.2f, maximum: 1.2f, multiplier: 2f, global: 3f);
            Assert.Equal(18f, HealingWorkCostCalculator.Cost(profile, 1.2f, 0.2f), 5);
            Assert.Equal(0f, HealingWorkCostCalculator.Cost(profile, 0.2f, 0f));
            var step = CorpseHealingCostCalculator.CalculateProfileStep(0.205f, 1f, profile);
            Assert.True(step.IsComplete);
            Assert.Equal(0.2f, step.RemainingSeverity);
            Assert.Equal(0.09f, step.NutritionCharge, 5);
            var stagedFloor = Profile(floor: 0.2f, maximum: 1.2f, stages: new[] { 0f, 0.5f });
            Assert.Equal(1.25f, stagedFloor.StagePremium(0.5f));
        }

        [Fact]
        public void AffordableWork_UsesTheSameIntegralForTinyPartialAndWaivedBudgets()
        {
            var profile = Profile(stages: new[] { 0f, 0.25f, 0.5f, 0.75f });
            foreach (float budget in new[] { 0f, 0.000001f, 0.03f, 0.7f, 5f })
            {
                float work = HealingWorkCostCalculator.AffordableSeverity(profile, 1f, 1f, budget);
                Assert.InRange(work, 0f, 1f);
                Assert.True(HealingWorkCostCalculator.Cost(profile, 1f, 1f - work) <= budget + 0.0000001f);
                if (budget > 0f)
                {
                    Assert.True(work > 0f);
                }
            }
            Assert.Equal(1f, HealingWorkCostCalculator.AffordableSeverity(Profile(global: 0f), 1f, 1f, 0f));
            Assert.Equal(0f, HealingWorkCostCalculator.AffordableSeverity(profile, 1f, float.NaN, 1f));
            Assert.True(float.IsNaN(HealingWorkCostCalculator.Cost(profile, float.PositiveInfinity, 0f)));
        }

        [Fact]
        public void Regrowth_OnlyActualProgressConsumesPartWork()
        {
            Assert.Equal(300f, RegrowthWorkAccounting.RemainingEffort(0f, 30f));
            Assert.Equal(3f, RegrowthWorkAccounting.ConsumedEffort(0.99f, 1f, 30f), 4);
            Assert.Equal(0f, RegrowthWorkAccounting.ConsumedEffort(1f, 1f, 30f));
            Assert.Equal(0f, RegrowthWorkAccounting.ConsumedEffort(0f, 0f, 30f));
        }

        [Fact]
        public void OverflowingCoefficient_IsUnavailableAndCannotAuthorizeFreeWork()
        {
            var invalid = Profile(range: float.Epsilon);
            Assert.False(invalid.IsValid);
            Assert.True(float.IsNaN(HealingWorkCostCalculator.Cost(invalid, 1f, 0f)));
            Assert.Equal(0f, HealingWorkCostCalculator.AffordableSeverity(invalid, 1f, 1f, 100f));
        }
    }
}
