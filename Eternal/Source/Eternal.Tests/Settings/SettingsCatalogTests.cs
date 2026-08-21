// Relative Path: Eternal/Source/Eternal.Tests/Settings/SettingsCatalogTests.cs
// Creation Date: 16-07-2026
// Last Edit: 16-07-2026
// Author: 0Shard
// Description: Contracts for the Gate 3 settings catalog, shared bounds,
//              logging choices, and the internal severity-to-nutrition conversion.

using System.Linq;
using Xunit;
using Eternal;

namespace Eternal.Tests.Settings
{
    public class SettingsCatalogTests
    {
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
            Assert.Equal(0.004f, SettingsDefaults.SeverityToNutritionRatio, 6);
            Assert.Equal("250 : 1", SettingsDefaults.GetSeverityToNutritionRatioDisplay());
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
