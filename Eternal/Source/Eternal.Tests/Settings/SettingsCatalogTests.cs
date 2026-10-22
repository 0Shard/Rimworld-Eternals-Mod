// Relative Path: Eternal/Source/Eternal.Tests/Settings/SettingsCatalogTests.cs
// Creation Date: 16-07-2026
// Last Edit: 16-07-2026
// Author: 0Shard
// Description: Contracts for the Gate 3 settings catalog, shared bounds,
//              logging choices, and the internal severity-to-nutrition conversion.

using System.Linq;
using Xunit;

namespace Eternal.Tests.Settings
{
    public class SettingsCatalogTests
    {
        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)]
        public void EveryGlobalFloatUsesItsCatalogDefaultForNonfiniteInput(float invalid)
        {
            foreach (string name in new[] { "BaseHealingRate", "NutritionCostMultiplier", "MaxDebtMultiplier",
                "FoodDrainThreshold", "DebtRepaymentDays", "ConsciousnessMultiplier" })
            {
                var catalog = typeof(SettingsDefaults);
                float fallback = (float)catalog.GetField(name).GetRawConstantValue();
                float min = (float)catalog.GetField(name + "Min").GetRawConstantValue();
                float max = (float)catalog.GetField(name + "Max").GetRawConstantValue();
                Assert.Equal(fallback, SettingsDefaults.NormalizeGlobalFloat(invalid, fallback, min, max));
                Assert.Equal(min, SettingsDefaults.NormalizeGlobalFloat(min - 1f, fallback, min, max));
                Assert.Equal(max, SettingsDefaults.NormalizeGlobalFloat(max + 1f, fallback, min, max));
                float ordinary = (min + max) * 0.5f;
                Assert.Equal(ordinary, SettingsDefaults.NormalizeGlobalFloat(ordinary, fallback, min, max));
            }
        }

        [Theory]
        [InlineData(0.001f)]
        [InlineData(0.005f)]
        [InlineData(1.2f)]
        [InlineData(3f)]
        [InlineData(-1f)]
        public void PersistedOverrides_PreserveLowHighAndInheritedRates(float value)
        {
            Assert.Equal(value, SettingsDefaults.NormalizePersistedHediffRate(value));
            Assert.Equal(SettingsDefaults.BaseHealingRateMin, SettingsDefaults.HediffHealingRateMin);
            Assert.Equal(SettingsDefaults.BaseHealingRateMax, SettingsDefaults.HediffHealingRateMax);
        }

        [Fact]
        public void InvalidPersistedRate_UsesGlobalSentinel()
        {
            Assert.Equal(-1f, SettingsDefaults.NormalizePersistedHediffRate(float.NaN));
            Assert.Equal(-1f, SettingsDefaults.NormalizePersistedHediffRate(float.PositiveInfinity));
        }
        [Fact]
        public void MapCheckInterval_DefaultIsInsideTheCanonicalRange()
        {
            Assert.Equal(5000, SettingsDefaults.MapCheckInterval);
            Assert.InRange(
                SettingsDefaults.MapCheckInterval,
                SettingsDefaults.MapCheckIntervalMin,
                SettingsDefaults.MapCheckIntervalMax);
        }

        [Fact]
        public void LoggingLevelCatalog_ContainsOneChoicePerValidLevel()
        {
            int choiceCount = SettingsDefaults.LoggingLevelMax - SettingsDefaults.LoggingLevelMin + 1;

            Assert.Equal(choiceCount, SettingsDefaults.LoggingLevelLabels.Length);
            Assert.Equal("Errors only", SettingsDefaults.LoggingLevelLabels[SettingsDefaults.LoggingLevelError]);
            Assert.Equal("Debug", SettingsDefaults.LoggingLevelLabels[SettingsDefaults.LoggingLevelDebug]);
        }

        [Fact]
        public void LoggingLevelCatalog_FormatsEveryChoiceWithoutFallbackText()
        {
            string[] labels = Enumerable.Range(
                    SettingsDefaults.LoggingLevelMin,
                    SettingsDefaults.LoggingLevelMax - SettingsDefaults.LoggingLevelMin + 1)
                .Select(SettingsDefaults.GetLoggingLevelLabel)
                .ToArray();

            Assert.Equal(SettingsDefaults.LoggingLevelLabels, labels);
            Assert.DoesNotContain(labels, label => string.IsNullOrWhiteSpace(label));
        }

        [Fact]
        public void CanonicalBounds_ContainEveryPersistedDefault()
        {
            Assert.InRange(SettingsDefaults.BaseHealingRate,
                SettingsDefaults.BaseHealingRateMin, SettingsDefaults.BaseHealingRateMax);
            Assert.InRange(SettingsDefaults.NutritionCostMultiplier,
                SettingsDefaults.NutritionCostMultiplierMin, SettingsDefaults.NutritionCostMultiplierMax);
            Assert.InRange(SettingsDefaults.MaxDebtMultiplier,
                SettingsDefaults.MaxDebtMultiplierMin, SettingsDefaults.MaxDebtMultiplierMax);
            Assert.InRange(SettingsDefaults.FoodDrainThreshold,
                SettingsDefaults.FoodDrainThresholdMin, SettingsDefaults.FoodDrainThresholdMax);
            Assert.InRange(SettingsDefaults.DebtRepaymentDays,
                SettingsDefaults.DebtRepaymentDaysMin, SettingsDefaults.DebtRepaymentDaysMax);
            Assert.InRange(SettingsDefaults.NormalTickRate,
                SettingsDefaults.NormalTickRateMin, SettingsDefaults.NormalTickRateMax);
            Assert.InRange(SettingsDefaults.RareTickRate,
                SettingsDefaults.RareTickRateMin, SettingsDefaults.RareTickRateMax);
            Assert.InRange(SettingsDefaults.TraitCheckInterval,
                SettingsDefaults.TraitCheckIntervalMin, SettingsDefaults.TraitCheckIntervalMax);
            Assert.InRange(SettingsDefaults.CorpseCheckInterval,
                SettingsDefaults.CorpseCheckIntervalMin, SettingsDefaults.CorpseCheckIntervalMax);
            Assert.InRange(SettingsDefaults.MapCheckInterval,
                SettingsDefaults.MapCheckIntervalMin, SettingsDefaults.MapCheckIntervalMax);
            Assert.InRange(SettingsDefaults.HealingHistorySweepInterval,
                SettingsDefaults.HealingHistorySweepIntervalMin,
                SettingsDefaults.HealingHistorySweepIntervalMax);
        }

        [Fact]
        public void SeverityConversion_IsAnInternalNamedConstant()
        {
            Assert.Equal(1f / 1200f, SettingsDefaults.SeverityToNutritionRatio, 9);
            Assert.Equal("1200 : 1", SettingsDefaults.GetSeverityToNutritionRatioDisplay());
        }

        [Fact]
        public void LegacyDebugMode_MigratesToDebugLevelWithoutLoweringExplicitLevel()
        {
            Assert.Equal(
                SettingsDefaults.LoggingLevelDebug,
                SettingsDefaults.MigrateLegacyDebugMode(true, SettingsDefaults.LoggingLevelWarning));
            Assert.Equal(
                SettingsDefaults.LoggingLevelDebug,
                SettingsDefaults.MigrateLegacyDebugMode(true, SettingsDefaults.LoggingLevelDebug));
            Assert.Equal(
                SettingsDefaults.LoggingLevelError,
                SettingsDefaults.MigrateLegacyDebugMode(false, SettingsDefaults.LoggingLevelError));
        }

        [Fact]
        public void HealingLabelDefault_UsesTheRenamedMeaning()
        {
            Assert.True(SettingsDefaults.ShowEternalPowerLabel);
        }
    }
}
