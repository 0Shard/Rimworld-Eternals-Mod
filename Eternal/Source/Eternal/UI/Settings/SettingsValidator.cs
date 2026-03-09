// Relative Path: Eternal/Source/Eternal/UI/Settings/SettingsValidator.cs
// Creation Date: 01-01-2025
// Last Edit: 14-01-2026
// Author: 0Shard
// Description: Validation logic for Eternal mod settings. Clamps values to valid
//              ranges and logs warnings for potentially problematic configurations.

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
        /// Validates all settings in the given Eternal_Settings instance.
        /// Clamps values to valid ranges and logs warnings for potentially problematic settings.
        /// </summary>
        public static void ValidateSettings(Eternal_Settings settings)
        {
            // Validate base healing rate (range must match slider in SettingsDrawer: 0.01-3.0)
            settings.baseHealingRate = Mathf.Clamp(settings.baseHealingRate, 0.01f, 3.0f);

            // Validate resource settings
            settings.nutritionCostMultiplier = Mathf.Clamp(settings.nutritionCostMultiplier, 0.1f, 5.0f);
            settings.minimumNutritionThreshold = Mathf.Clamp(settings.minimumNutritionThreshold, 0.01f, 1.0f);

            // Validate food debt settings
            settings.maxDebtMultiplier = Mathf.Clamp(settings.maxDebtMultiplier, 1.0f, 20.0f);
            settings.foodDrainThreshold = Mathf.Clamp(settings.foodDrainThreshold, 0.01f, 0.5f);
            settings.minDebtDrainRate = Mathf.Clamp(settings.minDebtDrainRate, 0.00001f, 0.01f);
            settings.maxDebtDrainRate = Mathf.Clamp(settings.maxDebtDrainRate, 0.0001f, 0.1f);
            settings.severityToNutritionRatio = Mathf.Clamp(settings.severityToNutritionRatio, 0.001f, 0.1f);

            // Validate map protection settings
            settings.anchorGracePeriodTicks = Mathf.Clamp(settings.anchorGracePeriodTicks, 60, 3600);

            // Validate performance settings
            settings.normalTickRate = Mathf.Clamp(settings.normalTickRate, 30, 250);
            settings.rareTickRate = Mathf.Clamp(settings.rareTickRate, 100, 1000);
            settings.traitCheckInterval = Mathf.Clamp(settings.traitCheckInterval, 1000, 15000);
            settings.corpseCheckInterval = Mathf.Clamp(settings.corpseCheckInterval, 250, 5000);
            settings.mapCheckInterval = Mathf.Clamp(settings.mapCheckInterval, 100, 2000);

            // Validate logging level
            settings.loggingLevel = Mathf.Clamp(settings.loggingLevel, 0, 3);

            // Log warnings for potentially problematic settings
            CheckForWarnings(settings);
        }

        /// <summary>
        /// Checks for settings that may cause issues and logs warnings.
        /// </summary>
        private static void CheckForWarnings(Eternal_Settings settings)
        {
            if (settings.minimumNutritionThreshold > 0.5f)
            {
                EternalLogger.Warning("Minimum nutrition threshold is very high, regrowth may be frequently paused.");
            }

            if (settings.normalTickRate < 45)
            {
                EternalLogger.Warning("Normal tick rate is very low, this may impact game performance.");
            }

            if (settings.rareTickRate < 150)
            {
                EternalLogger.Warning("Rare tick rate is very low, this may impact game performance.");
            }

            if (settings.baseHealingRate > 2.5f)
            {
                EternalLogger.Warning("Base healing rate is very high, this may make the game too easy.");
            }
        }

        #region Individual Validation Methods

        /// <summary>
        /// Validates heal amount is within acceptable range.
        /// </summary>
        public static float ValidateHealAmount(float value)
        {
            return Mathf.Clamp(value, 0.1f, 5.0f);
        }

        /// <summary>
        /// Validates nutrition cost multiplier is within acceptable range.
        /// </summary>
        public static float ValidateNutritionCostMultiplier(float value)
        {
            return Mathf.Clamp(value, 0.1f, 5.0f);
        }

        /// <summary>
        /// Validates tick rate is within acceptable range.
        /// </summary>
        public static int ValidateTickRate(int value, int min, int max)
        {
            return Mathf.Clamp(value, min, max);
        }

        /// <summary>
        /// Validates logging level is within acceptable range.
        /// </summary>
        public static int ValidateLoggingLevel(int value)
        {
            return Mathf.Clamp(value, 0, 3);
        }

        #endregion
    }
}
