using System;
using Eternal.Resources;
using Eternal.Healing;
using Eternal.Tests.UI;
using Xunit;

namespace Eternal.Tests.Resources
{
    public class ExactNutritionDebtTests
    {
        [Fact]
        public void OrdinaryEpisodeClosesOnlyItsProvenUnpayableExactTail()
        {
            var original = NutritionAmount.FromFloat(0.001f);
            var debt = original;
            float afterFood = FoodDebtPolicy.FoodAfterPayment(1f, debt.ToFloat(), debt.ToFloat(), 0.15f);
            float paid = FoodDebtPolicy.RepayExact(ref debt, 1f - afterFood);
            var tail = debt;
            Assert.False(tail.IsZero);
            Assert.Equal(original, NutritionAmount.FromFloat(paid) + tail);
            Assert.True(FoodDebtPolicy.TryCloseTerminalDust(ref debt, 1f, 0.15f));
            Assert.True(debt.IsZero);
            Assert.Equal(1f - afterFood, paid);
        }

        [Theory]
        [InlineData(1f, 0.15f, 1.4901161193847656e-8f)]
        [InlineData(1f, 0f, float.Epsilon)]
        [InlineData(1f, 0.15f, 0.0000001f)]
        [InlineData(float.MaxValue, 0.15f, 0.001f)]
        [InlineData(float.MaxValue, 0.15f, 0.000001f)]
        [InlineData(float.NaN, 0.15f, 1e-9f)]
        [InlineData(float.PositiveInfinity, 0.15f, 1e-9f)]
        [InlineData(1f, float.NaN, 1e-9f)]
        [InlineData(1f, -0.1f, 1e-9f)]
        [InlineData(1f, 1f, 1e-9f)]
        public void PayableBoundaryOrdinaryBalancesAndInvalidRangesNeverClose(float maximum, float floor, float amount)
        {
            var debt = NutritionAmount.FromFloat(amount);
            var original = debt;
            Assert.False(FoodDebtPolicy.TryCloseTerminalDust(ref debt, maximum, floor));
            Assert.Equal(original, debt);
        }

        [Fact]
        public void DustClosureDoesNotDependOnCurrentFoodShortageOrRoundedRequest()
        {
            var debt = NutritionAmount.FromFloat(1e-9f);
            Assert.True(FoodDebtPolicy.TryCloseTerminalDust(ref debt, 1f, 0.15f));
            Assert.True(debt.IsZero);
            Assert.False(FoodDebtPolicy.TryCloseTerminalDust(ref debt, 1f, 0.15f));
        }

        [Fact]
        public void SupportedTinyRangePriceKeepsOrdinaryPayments()
        {
            var profile = HealingWorkCostCalculator.CreateProfile(false, false, 0f, 1f, 0f, 0.000001f, 100f, 1f);
            float quote = HealingWorkCostCalculator.Cost(profile, 1f, 0f);
            Assert.Equal(100000000f, quote);
            var debt = NutritionAmount.FromFloat(quote);
            var before = debt;
            float repaid = FoodDebtPolicy.RepayExact(ref debt, 0.85f);
            Assert.Equal(NutritionAmount.FromFloat(repaid), NutritionAmount.Difference(before, debt));
        }

        [Theory]
        [InlineData(100000000f)]
        [InlineData(1e25f)]
        [InlineData(float.MaxValue)]
        public void OrdinaryPaymentAtLargeDebtMustReduceTheAuthoritativeLedger(float initial)
        {
            var debt = NutritionAmount.FromFloat(initial);
            var baseline = debt;
            var peak = debt;
            var starting = debt;
            var totalPaid = default(NutritionAmount);
            for (int pass = 0; pass < 1000; pass++)
            {
                float food = 1f;
                float drain = FoodDebtPolicy.Drain(debt.ToFloat(),
                    FoodDebtPolicy.DrainRate(debt.ToFloat(), peak.ToFloat(), 1f), 250, food, 1f, 0.15f);
                float afterFood = FoodDebtPolicy.FoodAfterPayment(food, drain, debt.ToFloat(), 0.15f);
                float observedFood = food - afterFood;
                var beforeDebt = debt;
                float repaid = FoodDebtPolicy.RepayExact(ref debt, observedFood);
                Assert.True(repaid > 0f);
                Assert.Equal(observedFood, repaid);
                Assert.Equal(NutritionAmount.FromFloat(observedFood), NutritionAmount.Difference(beforeDebt, debt));
                Assert.True(afterFood >= 0.15f);
                totalPaid += NutritionAmount.FromFloat(repaid);
                baseline = NutritionAmount.Min(baseline, debt);
                Assert.Equal(5f, NutritionAmount.Difference(baseline + NutritionAmount.FromFloat(5f), debt).ToFloat());
                if (pass == 499)
                {
                    Assert.True(NutritionAmount.TryParse(debt.Serialize(), out debt));
                    Assert.True(NutritionAmount.TryParse(baseline.Serialize(), out baseline));
                    Assert.True(NutritionAmount.TryParse(peak.Serialize(), out peak));
                }
            }
            Assert.Equal(totalPaid, NutritionAmount.Difference(starting, debt));
            Assert.NotEqual(starting, debt);
        }

        [Theory]
        [InlineData(100000000f)]
        [InlineData(float.MaxValue)]
        public void AccumulationKeepsUncappedBaselineAndFiveUnitsOfLivingHeadroom(float initial)
        {
            var debt = NutritionAmount.FromFloat(initial);
            debt += NutritionAmount.FromFloat(initial);
            var baseline = debt;
            var capacity = baseline + NutritionAmount.Product(1f, 5f);
            Assert.Equal(5f, NutritionAmount.Difference(capacity, debt).ToFloat());
            var before = debt;
            for (int i = 0; i < 20; i++)
            {
                Assert.Equal(0.25f, FoodDebtPolicy.AddExact(ref debt, capacity, 0.25f));
            }
            Assert.Equal(NutritionAmount.FromFloat(5f), NutritionAmount.Difference(debt, before));
            Assert.Equal(0f, FoodDebtPolicy.AddExact(ref debt, capacity, 0.25f));
            Assert.Equal(0f, NutritionAmount.Difference(capacity, debt).ToFloat());
            Assert.True(debt.ToFloat() > 0f && !float.IsInfinity(debt.ToFloat()));
            Assert.True(NutritionAmount.TryParse(debt.Serialize(), out var restored));
            Assert.Equal(debt, restored);
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(0.001f)]
        [InlineData(5f)]
        [InlineData(float.Epsilon)]
        [InlineData(float.MaxValue)]
        public void LegacyFiniteFloatsAndExactSaveStringsRoundTrip(float value)
        {
            var amount = NutritionAmount.FromFloat(value);
            Assert.Equal(value, amount.ToFloat());
            Assert.True(NutritionAmount.TryParse(amount.Serialize(), out var restored));
            Assert.Equal(amount, restored);
        }

        [Fact]
        public void CorruptSerializedAndNonfiniteAmountsAreRejected()
        {
            foreach (string text in new[] { null, "", "-1", "NaN", "1e8", "1.5", new string('9', 513) })
            {
                Assert.False(NutritionAmount.TryParse(text, out _));
            }
            foreach (float value in new[] { -1f, float.NaN, float.PositiveInfinity })
            {
                Assert.False(NutritionAmount.IsSupported(value));
                Assert.Throws<ArgumentOutOfRangeException>(() => NutritionAmount.FromFloat(value));
            }
        }

        [Fact]
        public void FinalPaymentCannotChargeAnUnrepresentableFoodStep()
        {
            var debt = NutritionAmount.FromFloat(0.0000001f);
            float food = 1f;
            float afterFood = FoodDebtPolicy.FoodAfterPayment(food, debt.ToFloat(), debt.ToFloat(), 0.15f);
            float repaid = FoodDebtPolicy.RepayExact(ref debt, food - afterFood);
            Assert.Equal(food - afterFood, repaid);
            Assert.True(repaid <= 0.0000001f);
        }

        [Fact]
        public void SettlementUsesAuthoritativeAcceptedAmountInsteadOfSubtractingRoundedDebtViews()
        {
            var calls = MenuAssemblyContract.Calls("Eternal.Resources.FoodCostProcessor", "SettleNutrition");
            Assert.DoesNotContain("Eternal.Interfaces.IFoodDebtReader.GetDebt", calls);
            Assert.Contains(calls, c => c != null && c.EndsWith(".AddDebtAndGetAccepted"));
            const string manager = "Eternal.Resources.UnifiedFoodDebtManager";
            Assert.Contains("Eternal.Resources.FoodDebtPolicy.RepayExact", MenuAssemblyContract.Calls(manager, "RepayDebt"));
            Assert.Contains("Eternal.Resources.FoodDebtPolicy.AddExact", MenuAssemblyContract.Calls(manager, "AddDebtCore"));
            var remaining = MenuAssemblyContract.Calls(manager, "GetRemainingCapacity");
            Assert.True(Array.IndexOf(remaining, "Eternal.Resources.NutritionAmount.Difference") <
                Array.IndexOf(remaining, "Eternal.Resources.NutritionAmount.ToFloat"));
            var repayment = MenuAssemblyContract.Calls("Eternal.Resources.DebtRepaymentProcessor", "ProcessPawnDebtRepayment");
            Assert.Contains("Eternal.Resources.FoodDebtPolicy.FoodAfterPayment", repayment);
            Assert.Contains("Eternal.Interfaces.IFoodDebtWriter.RepayDebt", repayment);
        }
    }
}
