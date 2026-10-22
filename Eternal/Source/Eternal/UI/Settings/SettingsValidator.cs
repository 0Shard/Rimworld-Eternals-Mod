// Relative Path: Eternal/Source/Eternal/UI/Settings/SettingsValidator.cs
// Creation Date: 01-01-2025
// Last Edit: 16-07-2026
// Author: 0Shard
// Description: Validates persisted Eternal settings at load and write boundaries. Bounds
//              and warning thresholds come from the canonical SettingsDefaults catalog;
//              the settings window never validates during repaint.

using UnityEngine;
using Eternal.Utils;

namespace Eternal.UI.Settings
{
    /// <summary>
    /// Handles validation of Eternal mod settings to prevent extreme or invalid values.
    /// </summary>
    public static class SettingsValidator
    {
        /// <summary>
        /// Clamps every mutable setting to the catalog range and emits warnings for values
        /// that are valid but likely to surprise the player. Call only after load or before write.
        /// </summary>
        public static void ValidateSettings(Eternal_Settings settings)
        {
            if (settings == null)
            {
                return;
            }

            settings.baseHealingRate = SettingsDefaults.NormalizeGlobalFloat(
                settings.baseHealingRate, SettingsDefaults.BaseHealingRate,
                SettingsDefaults.BaseHealingRateMin,
                SettingsDefaults.BaseHealingRateMax);

            settings.nutritionCostMultiplier = SettingsDefaults.NormalizeGlobalFloat(
                settings.nutritionCostMultiplier, SettingsDefaults.NutritionCostMultiplier,
                SettingsDefaults.NutritionCostMultiplierMin,
                SettingsDefaults.NutritionCostMultiplierMax);

            settings.maxDebtMultiplier = SettingsDefaults.NormalizeGlobalFloat(
                settings.maxDebtMultiplier, SettingsDefaults.MaxDebtMultiplier,
                SettingsDefaults.MaxDebtMultiplierMin,
                SettingsDefaults.MaxDebtMultiplierMax);
            settings.foodDrainThreshold = SettingsDefaults.NormalizeGlobalFloat(
                settings.foodDrainThreshold, SettingsDefaults.FoodDrainThreshold,
                SettingsDefaults.FoodDrainThresholdMin,
                SettingsDefaults.FoodDrainThresholdMax);
            settings.debtRepaymentDays = SettingsDefaults.NormalizeGlobalFloat(
                settings.debtRepaymentDays, SettingsDefaults.DebtRepaymentDays,
                SettingsDefaults.DebtRepaymentDaysMin,
                SettingsDefaults.DebtRepaymentDaysMax);

            settings.anchorGracePeriodTicks = Mathf.Clamp(
                settings.anchorGracePeriodTicks,
                SettingsDefaults.AnchorGracePeriodTicksMin,
                SettingsDefaults.AnchorGracePeriodTicksMax);

            settings.normalTickRate = Mathf.Clamp(
                settings.normalTickRate,
                SettingsDefaults.NormalTickRateMin,
                SettingsDefaults.NormalTickRateMax);
            settings.rareTickRate = Mathf.Clamp(
                settings.rareTickRate,
                SettingsDefaults.RareTickRateMin,
                SettingsDefaults.RareTickRateMax);
            settings.traitCheckInterval = Mathf.Clamp(
                settings.traitCheckInterval,
                SettingsDefaults.TraitCheckIntervalMin,
                SettingsDefaults.TraitCheckIntervalMax);
            settings.corpseCheckInterval = Mathf.Clamp(
                settings.corpseCheckInterval,
                SettingsDefaults.CorpseCheckIntervalMin,
                SettingsDefaults.CorpseCheckIntervalMax);
            settings.mapCheckInterval = Mathf.Clamp(
                settings.mapCheckInterval,
                SettingsDefaults.MapCheckIntervalMin,
                SettingsDefaults.MapCheckIntervalMax);
            settings.healingHistorySweepInterval = Mathf.Clamp(
                settings.healingHistorySweepInterval,
                SettingsDefaults.HealingHistorySweepIntervalMin,
                SettingsDefaults.HealingHistorySweepIntervalMax);

            settings.loggingLevel = Mathf.Clamp(
                settings.loggingLevel,
                SettingsDefaults.LoggingLevelMin,
                SettingsDefaults.LoggingLevelMax);

            settings.consciousnessMultiplier = SettingsDefaults.NormalizeGlobalFloat(
                settings.consciousnessMultiplier, SettingsDefaults.ConsciousnessMultiplier,
                SettingsDefaults.ConsciousnessMultiplierMin,
                SettingsDefaults.ConsciousnessMultiplierMax);
            settings.moodBuffValue = Mathf.Clamp(
                settings.moodBuffValue,
                SettingsDefaults.MoodBuffValueMin,
                SettingsDefaults.MoodBuffValueMax);
            settings.populationCap = Mathf.Clamp(
                settings.populationCap,
                SettingsDefaults.PopulationCapMin,
                SettingsDefaults.PopulationCapMax);

            CheckForWarnings(settings);
        }

        /// <summary>
        /// Checks for values that are valid but may have a material gameplay or performance cost.
        /// </summary>
        private static void CheckForWarnings(Eternal_Settings settings)
        {
            if (settings.normalTickRate < SettingsDefaults.NormalTickRateWarningThreshold)
            {
                EternalLogger.Warning("Normal tick rate is very low, this may impact game performance.");
            }

            if (settings.rareTickRate < SettingsDefaults.RareTickRateWarningThreshold)
            {
                EternalLogger.Warning("Rare tick rate is very low, this may impact game performance.");
            }

            if (settings.baseHealingRate > SettingsDefaults.BaseHealingRateWarningThreshold)
            {
                EternalLogger.Warning("Base healing rate is very high, this may make the game too easy.");
            }
        }

        #region Individual Validation Methods

        #endregion
    }
}
