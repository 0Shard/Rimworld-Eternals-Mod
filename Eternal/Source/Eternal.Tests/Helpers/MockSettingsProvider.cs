// Relative Path: Eternal/Source/Eternal.Tests/Helpers/MockSettingsProvider.cs
// Creation Date: 24-02-2026
// Last Edit: 24-02-2026
// Author: 0Shard
// Description: NSubstitute-based ISettingsProvider factory for tests.
//              Default() returns a substitute with all properties matching SettingsDefaults.
//              Individual properties can be overridden per test via Returns().

using NSubstitute;
using Eternal.Interfaces;

namespace Eternal.Tests.Helpers
{
    /// <summary>
    /// Factory for NSubstitute-based <see cref="ISettingsProvider"/> stubs.
    /// Each call to <see cref="Default"/> returns a fresh substitute so tests remain isolated.
    /// </summary>
    public static class MockSettingsProvider
    {
        /// <summary>
        /// Returns an <see cref="ISettingsProvider"/> substitute configured with values
        /// matching <see cref="SettingsDefaults"/>. Override per-test:
        /// <c>settings.BaseHealingRate.Returns(0.05f);</c>
        /// </summary>
        public static ISettingsProvider Default()
        {
            var settings = Substitute.For<ISettingsProvider>();

            // General
            settings.ModEnabled.Returns(TestData.DefaultModEnabled);
            settings.DebugMode.Returns(TestData.DefaultDebugMode);
            settings.LoggingLevel.Returns(TestData.DefaultLoggingLevel);

            // Healing
            settings.BaseHealingRate.Returns(TestData.DefaultBaseHealingRate);
            settings.ShowRegrowthEffects.Returns(TestData.DefaultShowRegrowthEffects);
            settings.ShowRegrowthProgress.Returns(TestData.DefaultShowRegrowthProgress);

            // Resources
            settings.NutritionCostMultiplier.Returns(TestData.DefaultNutritionCostMultiplier);
            settings.PauseOnResourceDepletion.Returns(TestData.DefaultPauseOnResourceDepletion);
            settings.MinimumNutritionThreshold.Returns(TestData.DefaultMinimumNutritionThreshold);
            settings.AllowResourceBorrowing.Returns(TestData.DefaultAllowResourceBorrowing);

            // Food Debt
            settings.MaxDebtMultiplier.Returns(TestData.DefaultMaxDebtMultiplier);
            settings.FoodDrainThreshold.Returns(TestData.DefaultFoodDrainThreshold);
            settings.MinDebtDrainRate.Returns(TestData.DefaultMinDebtDrainRate);
            settings.MaxDebtDrainRate.Returns(TestData.DefaultMaxDebtDrainRate);
            settings.SeverityToNutritionRatio.Returns(TestData.DefaultSeverityToNutritionRatio);

            // Performance
            settings.NormalTickRate.Returns(TestData.DefaultNormalTickRate);
            settings.RareTickRate.Returns(TestData.DefaultRareTickRate);
            settings.TraitCheckInterval.Returns(TestData.DefaultTraitCheckInterval);
            settings.CorpseCheckInterval.Returns(TestData.DefaultCorpseCheckInterval);
            settings.MapCheckInterval.Returns(TestData.DefaultMapCheckInterval);

            // Advanced
            settings.EnableIndividualHediffControl.Returns(TestData.DefaultEnableIndividualHediffControl);
            settings.AutoHealEnabled.Returns(TestData.DefaultAutoHealEnabled);
            settings.HealingOrder.Returns(default(HealingOrder)); // CheapestFirst = 0

            // Map Protection
            settings.EnableMapAnchors.Returns(TestData.DefaultEnableMapAnchors);
            settings.AnchorGracePeriodTicks.Returns(TestData.DefaultAnchorGracePeriodTicks);

            return settings;
        }
    }
}
