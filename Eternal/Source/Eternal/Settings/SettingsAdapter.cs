// Relative Path: Eternal/Source/Eternal/Settings/SettingsAdapter.cs
// Creation Date: 29-12-2025
// Last Edit: 16-07-2026
// Author: 0Shard
// Description: Adapter that wraps Eternal_Mod.settings at the active runtime boundary.
//              Every fallback comes from the canonical SettingsDefaults catalog.

using Eternal.Interfaces;

namespace Eternal.Settings
{
    /// <summary>
    /// Provides constructor-injectable access to the active Eternal settings.
    /// </summary>
    public class SettingsAdapter : ISettingsProvider
    {
        private Eternal_Settings Settings => Eternal_Mod.settings;

        #region General Settings

        public bool DebugMode => Settings?.DebugMode ?? SettingsDefaults.DebugMode;
        public int LoggingLevel => Settings?.loggingLevel ?? SettingsDefaults.LoggingLevel;

        #endregion

        #region Healing Settings

        public float BaseHealingRate => Settings?.baseHealingRate ?? SettingsDefaults.BaseHealingRate;
        public bool ShowEternalPowerLabel => Settings?.showEternalPowerLabel ?? SettingsDefaults.ShowEternalPowerLabel;

        #endregion

        #region Nutrition Settings

        public float NutritionCostMultiplier => Settings?.nutritionCostMultiplier ?? SettingsDefaults.NutritionCostMultiplier;

        #endregion

        #region Food Debt Settings

        public float MaxDebtMultiplier => Settings?.maxDebtMultiplier ?? SettingsDefaults.MaxDebtMultiplier;
        public float FoodDrainThreshold => Settings?.foodDrainThreshold ?? SettingsDefaults.FoodDrainThreshold;
        public float DebtRepaymentDays => Settings?.debtRepaymentDays ?? SettingsDefaults.DebtRepaymentDays;

        #endregion

        #region Performance Settings

        public int NormalTickRate => Settings?.normalTickRate ?? SettingsDefaults.NormalTickRate;
        public int RareTickRate => Settings?.rareTickRate ?? SettingsDefaults.RareTickRate;
        public int TraitCheckInterval => Settings?.traitCheckInterval ?? SettingsDefaults.TraitCheckInterval;
        public int CorpseCheckInterval => Settings?.corpseCheckInterval ?? SettingsDefaults.CorpseCheckInterval;
        public int MapCheckInterval => Settings?.mapCheckInterval ?? SettingsDefaults.MapCheckInterval;
        public int HealingHistorySweepInterval => Settings?.healingHistorySweepInterval
            ?? SettingsDefaults.HealingHistorySweepInterval;

        #endregion

        #region Map Protection Settings

        public bool EnableMapAnchors => Settings?.enableMapAnchors ?? SettingsDefaults.EnableMapAnchors;
        public int AnchorGracePeriodTicks => Settings?.anchorGracePeriodTicks ?? SettingsDefaults.AnchorGracePeriodTicks;

        #endregion
    }
}
