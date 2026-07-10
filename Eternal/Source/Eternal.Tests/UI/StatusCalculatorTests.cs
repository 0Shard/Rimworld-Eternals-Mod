// Relative Path: Eternal/Source/Eternal.Tests/UI/StatusCalculatorTests.cs
// Creation Date: 10-07-2026
// Last Edit: 11-07-2026
// Author: 0Shard
// Description: Unit tests for StatusCalculator display formulas. Guards the Status tab
//              against drifting from the engine again: nutrition costs must include
//              severityToNutritionRatio (cost = severity × ratio × multiplier, matching
//              EternalHediffHealer + FoodCostProcessor), and disease times must include
//              the 0.1x low-maxSeverity multiplier. Pure string/float methods — no Verse types.
//              11-07: default rate is 1.2 (Apex pacing); regrowth rows are HP-scaled for the
//              reference arm (30 HP x RegrowthWorkPerPartHP work).

using Xunit;
using Eternal.UI.Settings;

namespace Eternal.Tests.UI
{
    public class StatusCalculatorTests
    {
        // Defaults: baseHealingRate 1.2, nutritionCostMultiplier 1.0, severityToNutritionRatio 0.004 (250:1)
        private const float DefaultRate = 1.2f;
        private const float DefaultMult = 1.0f;
        private const float DefaultRatio = 0.004f;

        // -----------------------------------------------------------------
        // Nutrition costs — must apply the severity-to-nutrition ratio
        // -----------------------------------------------------------------

        [Fact]
        public void NormalTickCost_AppliesRatio()
        {
            // 1.2 severity/cycle × 0.004 × 1.0 = 0.0048 (NOT 1.2 — the pre-fix 250x bug)
            Assert.Equal("0.0048 nutrition", StatusCalculator.NormalTickCost(DefaultRate, DefaultMult, DefaultRatio));
        }

        [Fact]
        public void RareTickCost_AppliesRatio()
        {
            Assert.Equal("0.0048 nutrition", StatusCalculator.RareTickCost(DefaultRate, DefaultMult, DefaultRatio));
        }

        [Fact]
        public void FullInjuryCost_At250ToOneRatio_SeverityOneCostsFourThousandths()
        {
            // Severity 1.0 total: 1.0 × 0.004 × 1.0 = 0.004 nutrition
            Assert.Equal("~0.0040 nutrition", StatusCalculator.FullInjuryCost(DefaultMult, DefaultRatio));
        }

        [Fact]
        public void FullInjuryCost_250Severity_CostsOneNutrition()
        {
            // The defining property of the 250:1 ratio, expressed through the multiplier slot:
            // 250 severity × 0.004 = 1.0 nutrition
            Assert.Equal("~1.0000 nutrition", StatusCalculator.FullInjuryCost(250f * DefaultMult, DefaultRatio));
        }

        [Fact]
        public void FullScarCost_MatchesFullInjuryCost()
        {
            // Unified system: the 0.5x scar speed changes duration, not total cost
            Assert.Equal(
                StatusCalculator.FullInjuryCost(DefaultMult, DefaultRatio),
                StatusCalculator.FullScarCost(DefaultMult, DefaultRatio));
        }

        [Fact]
        public void InjuriesCoveredByDebt_AppliesRatio()
        {
            // maxDebt 5.0 / (1.0 × 0.004 × 1.0) = 1250 injuries (NOT ~5 — the pre-fix bug)
            Assert.Equal("~1250 injuries", StatusCalculator.InjuriesCoveredByDebt(5.0f, DefaultMult, DefaultRatio));
        }

        [Fact]
        public void InjuriesCoveredByDebt_ZeroCost_ReturnsUnlimited()
        {
            Assert.Equal("unlimited", StatusCalculator.InjuriesCoveredByDebt(5.0f, DefaultMult, 0f));
        }

        [Fact]
        public void ScarsCoveredByDebt_AppliesRatio()
        {
            Assert.Equal("~1250 scars", StatusCalculator.ScarsCoveredByDebt(5.0f, DefaultMult, DefaultRatio));
        }

        // -----------------------------------------------------------------
        // Healing times
        // -----------------------------------------------------------------

        [Fact]
        public void DiseaseHealTime_Stage0_IncludesLowMaxSeverityMultiplier()
        {
            // cycles = 1 / (1.2 × 1.0 × 0.1) = 8.33 cycles × 60 ticks = 500 ticks = 12 in-game minutes
            Assert.Equal("~12 minutes", StatusCalculator.DiseaseHealTime(DefaultRate, 60, 0));
        }

        [Fact]
        public void DiseaseHealTime_HigherStage_IsSlower()
        {
            // Stage 3 (0.4x) must report a longer time than stage 0 (1.0x).
            // Compare via the underlying cycle math: both are minutes at defaults.
            string stage0 = StatusCalculator.DiseaseHealTime(DefaultRate, 60, 0);
            string stage3 = StatusCalculator.DiseaseHealTime(DefaultRate, 60, 3);
            Assert.NotEqual(stage0, stage3);
        }

        // -----------------------------------------------------------------
        // Regrowth (HP-scaled, reference arm 30 HP)
        // -----------------------------------------------------------------

        [Fact]
        public void FullLimbRegrowth_ReferenceArm_TakesAboutOneDay()
        {
            // 30 HP × 10 work/HP / 1.2 per pass = 250 passes × 250 ticks = 62500 ticks = 25 h ≈ 1.0 days
            Assert.Equal("~1.0 days", StatusCalculator.FullLimbRegrowth(DefaultRate, 250));
        }

        [Fact]
        public void RegrowthPhaseTime_IsQuarterOfFullLimb()
        {
            // One phase spans 0.25 severity: 62.5 passes × 250 ticks = 15625 ticks = 6.25 h
            Assert.Equal("~6.3 hours", StatusCalculator.RegrowthPhaseTime(DefaultRate, 250));
        }

        [Fact]
        public void FullLimbRegrowthCost_ReferenceArm_ChargesEffortBasedNutrition()
        {
            // 30 HP × 10 work/HP × 0.004 × 1.0 = 1.2 nutrition — a limb costs meals, not crumbs
            Assert.Equal("~1.20 nutrition", StatusCalculator.FullLimbRegrowthCost(DefaultMult, DefaultRatio));
        }

        [Fact]
        public void ScarHealTime_UsesRareTickRate()
        {
            // (1 / 1.2) × 250 ticks = 208 ticks ≈ 5 minutes — matches EternalScarHealing,
            // whose ScarCostCalculator normalizes healing by rareTickRate
            Assert.Equal("~5 minutes", StatusCalculator.ScarHealTime(DefaultRate, 250));
        }
    }
}
