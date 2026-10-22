// Relative Path: Eternal/Source/Eternal/Settings/SettingsCatalog.cs
// Creation Date: 16-07-2026
// Last Edit: 16-07-2026
// Author: 0Shard
// Description: Canonical defaults, bounds, choices, and internal balance constants for
//              Eternal settings. Every persisted field, validator, UI control, adapter,
//              warning, preview, and test derives its setting metadata from this catalog.

using System;

namespace Eternal
{
    /// <summary>
    /// Single source of truth for Eternal setting metadata.
    /// The mutable persisted root remains <see cref="Eternal_Settings"/>; this class only
    /// defines values and bounds so those surfaces cannot silently drift apart.
    /// </summary>
    public static class SettingsDefaults
    {
        internal static float NormalizeGlobalFloat(float value, float defaultValue, float min, float max)
            => float.IsNaN(value) || float.IsInfinity(value) ? defaultValue : Math.Max(min, Math.Min(max, value));

        #region General

        public const int LoggingLevelError = 0;
        public const int LoggingLevelWarning = 1;
        public const int LoggingLevelInfo = 2;
        public const int LoggingLevelDebug = 3;
        public const int LoggingLevelMin = LoggingLevelError;
        public const int LoggingLevelMax = LoggingLevelDebug;
        public const int LoggingLevel = LoggingLevelWarning;
        public const bool DebugMode = false;

        public static readonly string[] LoggingLevelLabels =
        {
            "Errors only",
            "Warnings",
            "Info",
            "Debug"
        };

        public static string GetLoggingLevelLabel(int loggingLevel)
        {
            int clampedLevel = Math.Max(LoggingLevelMin, Math.Min(LoggingLevelMax, loggingLevel));
            return LoggingLevelLabels[clampedLevel - LoggingLevelMin];
        }

        /// <summary>
        /// Converts the old persisted debug flag once, without lowering an explicit level.
        /// </summary>
        internal static int MigrateLegacyDebugMode(bool legacyDebugMode, int loggingLevel)
        {
            return legacyDebugMode
                ? Math.Max(loggingLevel, LoggingLevelDebug)
                : loggingLevel;
        }

        #endregion

        #region Healing

        /// <summary>
        /// Severity reduction per healing pass. The default matches the Apex Immortal rate.
        /// </summary>
        public const float BaseHealingRate = 1.2f;
        public const float BaseHealingRateMin = 0.01f;
        public const float BaseHealingRateMax = 3.0f;
        public const float BaseHealingRateWarningThreshold = 2.5f;
        public const float HediffHealingRateUseGlobal = -1f;
        public const bool HediffCanHeal = true;
        public const bool HediffNoThreshold = false;
        public const float HediffHealingRateCompatibilityMin = 0.001f;
        public const float HediffHealingRateMin = BaseHealingRateMin;
        public const float HediffHealingRateMax = BaseHealingRateMax;

        internal static float NormalizePersistedHediffRate(float value)
        {
            if (value == HediffHealingRateUseGlobal)
            {
                return value;
            }

            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                return HediffHealingRateUseGlobal;
            }

            return Math.Max(HediffHealingRateCompatibilityMin, Math.Min(HediffHealingRateMax, value));
        }

        /// <summary>
        /// Severity-equivalent work required per body-part hit point during regrowth.
        /// </summary>
        public const float RegrowthWorkPerPartHP = 10f;
        public const float RegrowthInitialProgress = 0.01f;

        /// <summary>
        /// Regrowth severity at which a part's children may start in parallel.
        /// </summary>
        public const float RegrowthChildStartThreshold = 0.625f;

        /// <summary>
        /// Controls the Eternal Power label shown by the Essence hediff. The persisted key
        /// remains showRegrowthProgress for compatibility with existing saves.
        /// </summary>
        public const bool ShowEternalPowerLabel = true;

        #endregion

        #region Nutrition and Food Debt

        public const float NutritionCostMultiplier = 1.0f;
        public const float NutritionCostMultiplierMin = 0.1f;
        public const float NutritionCostMultiplierMax = 5.0f;
        public const float IllnessFullRangeNutrition = 1f;
        public const float ChronicFullRangeNutrition = 3f;
        public const float HediffNutritionCostMultiplier = 1f;
        public const float HediffNutritionCostMultiplierMax = 100f;
        public const float HediffNutritionSeverityRangeOverride = 0f;
        public const float HediffNutritionSeverityRangeMax = 1000000f;
        public const float CostStageIncrement = 0.25f;
        public const float CostStageMaximum = 2f;

        public static float NormalizeNutritionMultiplier(float value)
            => float.IsNaN(value) || float.IsInfinity(value) ? HediffNutritionCostMultiplier :
                Math.Max(0f, Math.Min(HediffNutritionCostMultiplierMax, value));

        public static float NormalizeNutritionRange(float value)
            => float.IsNaN(value) || float.IsInfinity(value) ? HediffNutritionSeverityRangeOverride :
                Math.Max(0f, Math.Min(HediffNutritionSeverityRangeMax, value));

        public const float MaxDebtMultiplier = 5.0f;
        public const float MaxDebtMultiplierMin = 1.0f;
        public const float MaxDebtMultiplierMax = 10.0f;

        public const float FoodDrainThreshold = 0.15f;
        public const float FoodDrainThresholdMin = 0.05f;
        public const float FoodDrainThresholdMax = 0.5f;

        public const float DebtRepaymentDays = 1.0f;
        public const float DebtRepaymentDaysMin = 0.25f;
        public const float DebtRepaymentDaysMax = 5.0f;

        /// <summary>
        /// Internal conversion from healed severity to nutrition before the single global
        /// nutritionCostMultiplier is applied. This is balance data, not a player control.
        /// </summary>
        // Immortals Apex entry: 0.002 × 10 × 60000 = 1200 HP/day at size 1,
        // default settings and no temporary buffs. This is a nominal benchmark.
        internal const float SeverityToNutritionRatio = 1f / 1200f;

        public static string GetSeverityToNutritionRatioDisplay()
        {
            int ratioValue = (int)Math.Round(1.0f / SeverityToNutritionRatio);
            return $"{ratioValue} : 1";
        }

        #endregion

        #region Performance

        public const int NormalTickRate = 60;
        public const int NormalTickRateMin = 30;
        public const int NormalTickRateMax = 250;
        public const int NormalTickRateWarningThreshold = 45;

        public const int RareTickRate = 250;
        public const int RareTickRateMin = 100;
        public const int RareTickRateMax = 1000;
        public const int RareTickRateWarningThreshold = 150;

        public const int TraitCheckInterval = 5000;
        public const int TraitCheckIntervalMin = 1000;
        public const int TraitCheckIntervalMax = 15000;

        public const int CorpseCheckInterval = 1000;
        public const int CorpseCheckIntervalMin = 250;
        public const int CorpseCheckIntervalMax = 5000;

        public const int MapCheckInterval = 5000;
        public const int MapCheckIntervalMin = 1000;
        public const int MapCheckIntervalMax = 15000;

        public const int HealingHistorySweepInterval = 300000;
        public const int HealingHistorySweepIntervalMin = 60000;
        public const int HealingHistorySweepIntervalMax = 900000;

        #endregion

        #region Map Protection

        public const bool EnableMapAnchors = true;
        public const int AnchorGracePeriodTicks = 300;
        public const int AnchorGracePeriodTicksMin = 60;
        public const int AnchorGracePeriodTicksMax = 3600;
        public const bool EnableRoofCollapseProtection = true;

        #endregion

        #region Effects

        public const bool ConsciousnessBuffEnabled = true;
        public const float ConsciousnessMultiplier = 3.0f;
        public const float ConsciousnessMultiplierMin = 1.0f;
        public const float ConsciousnessMultiplierMax = 10.0f;

        public const bool MoodBuffEnabled = true;
        public const int MoodBuffValue = 40;
        public const int MoodBuffValueMin = 1;
        public const int MoodBuffValueMax = 200;

        public const bool PopulationCapEnabled = true;
        public const int PopulationCap = 3;
        public const int PopulationCapMin = 1;
        public const int PopulationCapMax = 30;

        #endregion

        #region Legacy Snapshot Bridge

        // ProcessTick still consumes this compatibility guard. Other retired settings
        // have no snapshot consumers and do not need constant bridge fields.
        internal const bool LegacySnapshotModEnabled = true;

        #endregion
    }
}
