// Author: 0Shard
// Description: Executable production drain policy; host tests observe food and ledger mutations.
using Eternal.Resources;
using Xunit;

namespace Eternal.Tests.Resources
{
    public class DebtRepaymentProcessorTests
    {
        [Fact]
        public void TinyEpisode_UsesOneNutritionPerDayMinimum()
        {
            Assert.Equal(1f / 60000f, FoodDebtPolicy.DrainRate(0.05f, 0.05f, 2f), 9);
            Assert.Equal(0.05f, FoodDebtPolicy.Drain(0.05f,
                FoodDebtPolicy.DrainRate(0.05f, 0.05f, 2f), 3000, 1f, 1f, 0.15f), 7);
            Assert.True(FoodDebtPolicy.MinDrainNutritionPerDay < 1.6f);
        }

        [Theory]
        [InlineData(50f, 50f, 2f, 0.0004166667f)]
        [InlineData(50f, 3f, 2f, 0.0004166667f)]
        [InlineData(3f, 50f, 2f, 0.0004166667f)]
        [InlineData(6f, 6f, 1f, 0.0001f)]
        [InlineData(6f, 6f, 0f, 0.01f)]
        public void DrainRate_UsesCurrentOrPeakAndConfiguredWindow(float debt, float peak, float days, float expected)
            => Assert.Equal(expected, FoodDebtPolicy.DrainRate(debt, peak, days), 8);

        [Theory]
        [InlineData(50f, 0.001f, 300, 1.2f, 2f, 0.4f, 0.3f)]
        [InlineData(50f, 0.001f, 600, 1.2f, 2f, 0.4f, 0.4f)]
        [InlineData(0.02f, 0.001f, 600, 1.2f, 2f, 0.4f, 0.02f)]
        [InlineData(50f, 0.001f, 300, 0.8f, 2f, 0.4f, 0f)]
        [InlineData(50f, 0.001f, 300, 0.7f, 2f, 0.4f, 0f)]
        [InlineData(0f, 0.001f, 300, 1.2f, 2f, 0.4f, 0f)]
        public void Drain_UsesCadenceAndBoundsFoodAndDebt(float debt, float rate, int cadence,
            float food, float foodMax, float floor, float expected)
            => Assert.Equal(expected, FoodDebtPolicy.Drain(debt, rate, cadence, food, foodMax, floor), 6);
    }
}
