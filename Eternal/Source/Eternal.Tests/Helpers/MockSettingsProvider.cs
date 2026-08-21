// Relative Path: Eternal/Source/Eternal.Tests/Helpers/MockSettingsProvider.cs
// Creation Date: 24-02-2026
// Last Edit: 16-07-2026
// Author: 0Shard
// Description: NSubstitute-based ISettingsProvider factory. The active provider surface
//              is populated from the production SettingsDefaults catalog so tests cannot
//              drift from persisted defaults and bounds.

using NSubstitute;
using Eternal;
using Eternal.Interfaces;

namespace Eternal.Tests.Helpers
{
    /// <summary>
    /// Factory for isolated ISettingsProvider substitutes.
    /// </summary>
    public static class MockSettingsProvider
    {
        /// <summary>
        /// Returns a provider configured with the catalog defaults.
        /// </summary>
        public static ISettingsProvider Default()
        {
            var settings = Substitute.For<ISettingsProvider>();

            settings.DebugMode.Returns(SettingsDefaults.DebugMode);
            settings.LoggingLevel.Returns(SettingsDefaults.LoggingLevel);
            settings.BaseHealingRate.Returns(SettingsDefaults.BaseHealingRate);
            settings.ShowEternalPowerLabel.Returns(SettingsDefaults.ShowEternalPowerLabel);
            settings.NutritionCostMultiplier.Returns(SettingsDefaults.NutritionCostMultiplier);
            settings.MaxDebtMultiplier.Returns(SettingsDefaults.MaxDebtMultiplier);
            settings.FoodDrainThreshold.Returns(SettingsDefaults.FoodDrainThreshold);
            settings.DebtRepaymentDays.Returns(SettingsDefaults.DebtRepaymentDays);
            settings.NormalTickRate.Returns(SettingsDefaults.NormalTickRate);
            settings.RareTickRate.Returns(SettingsDefaults.RareTickRate);
            settings.TraitCheckInterval.Returns(SettingsDefaults.TraitCheckInterval);
            settings.CorpseCheckInterval.Returns(SettingsDefaults.CorpseCheckInterval);
            settings.MapCheckInterval.Returns(SettingsDefaults.MapCheckInterval);
            settings.HealingHistorySweepInterval.Returns(SettingsDefaults.HealingHistorySweepInterval);
            settings.EnableMapAnchors.Returns(SettingsDefaults.EnableMapAnchors);
            settings.AnchorGracePeriodTicks.Returns(SettingsDefaults.AnchorGracePeriodTicks);

            return settings;
        }
    }
}
