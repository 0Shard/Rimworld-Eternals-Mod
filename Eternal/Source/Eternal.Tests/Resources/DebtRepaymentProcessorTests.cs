// Relative Path: Eternal/Source/Eternal.Tests/Resources/DebtRepaymentProcessorTests.cs
// Creation Date: 13-07-2026
// Last Edit: 13-07-2026
// Author: 0Shard
// Description: Drain-rate formula tests for DebtRepaymentProcessor. The processor itself
//              cannot be instantiated here (Pawn in its method signatures triggers
//              Assembly-CSharp loading), so tests verify the CalculateDrainRate math:
//              rate = max(peakDebt / (60000 × debtRepaymentDays), MinDrainNutritionPerDay / 60000)
//              and the ProcessPawnDebtRepayment clamps (drainable above floor, remaining debt).

using System;
using Xunit;
using Eternal.Resources;
using Eternal.Tests.Helpers;

namespace Eternal.Tests.Resources
{
    public class DebtRepaymentProcessorTests
    {
        private static float DrainRate(float peakDebt)
        {
            float repaymentTicks = 60000f * Math.Max(TestData.DefaultDebtRepaymentDays, 0.01f);
            return Math.Max(peakDebt / repaymentTicks, DebtRepaymentProcessor.MinDrainNutritionPerDay / 60000f);
        }

        // -----------------------------------------------------------------
        // Minimum drain rate floor
        // A residual crumb of debt is its own episode; without the floor it
        // would take the FULL repayment window ("stuck at 0% debt" bug).
        // -----------------------------------------------------------------

        [Fact]
        public void DrainRate_TinyEpisode_UsesFloorNotEpisodeRate()
        {
            float episodeRate = 0.05f / (60000f * TestData.DefaultDebtRepaymentDays);
            float floorRate = DebtRepaymentProcessor.MinDrainNutritionPerDay / 60000f;
            Assert.True(floorRate > episodeRate);
            Assert.Equal(floorRate, DrainRate(0.05f), TestData.FloatPrecision);
        }

        [Fact]
        public void DrainRate_TinyEpisode_ClearsInFractionOfWindow()
        {
            // 0.05 nutrition at the floor rate: 0.05 / (1/60000) = 3000 ticks = 5% of a day
            float ticksToRepay = 0.05f / DrainRate(0.05f);
            Assert.True(ticksToRepay <= 3000f + 1f);
        }

        [Fact]
        public void DrainRate_LargeEpisode_KeepsEpisodeRate()
        {
            // 50 nutrition: episode rate 50/60000 per tick dwarfs the 1/60000 floor
            float episodeRate = 50f / (60000f * TestData.DefaultDebtRepaymentDays);
            Assert.Equal(episodeRate, DrainRate(50f), TestData.FloatPrecision);
        }

        [Fact]
        public void DrainRate_FloorIsBelowNaturalHunger()
        {
            // ~1.6 nutrition/day natural hunger — the floor must stay imperceptible
            Assert.True(DebtRepaymentProcessor.MinDrainNutritionPerDay < 1.6f);
        }

        // -----------------------------------------------------------------
        // ProcessPawnDebtRepayment clamps (mirrored math)
        // toDrain = min(scaledRate, drainable above 15% floor, remaining debt)
        // -----------------------------------------------------------------

        [Fact]
        public void Drain_ClampedByRemainingDebt_NeverOverdrains()
        {
            float scaledRate = DrainRate(50f) * 250f; // rare tick scaling
            float drainable = 0.85f;                  // full bar above 15% floor
            float debt = 0.02f;
            float toDrain = Math.Min(Math.Min(scaledRate, drainable), debt);
            Assert.Equal(debt, toDrain, TestData.FloatPrecision);
        }

        [Fact]
        public void Drain_ClampedByFoodFloor_NeverDipsBelowThreshold()
        {
            float scaledRate = DrainRate(50f) * 250f;
            float curLevel = 0.30f;
            float threshold = 1.0f * TestData.DefaultFoodDrainThreshold;
            float drainable = curLevel - threshold;
            float toDrain = Math.Min(Math.Min(scaledRate, drainable), 50f);
            Assert.Equal(drainable, toDrain, TestData.FloatPrecision);
            Assert.Equal(threshold, curLevel - toDrain, TestData.FloatPrecision);
        }
    }
}
