// Relative Path: Eternal/Source/Eternal/Settings/SettingsAdapter.cs
// Creation Date: 29-12-2025
// Last Edit: 04-03-2026
// Author: 0Shard
// Description: Adapter that wraps Eternal_Mod.settings to provide ISettingsProvider interface.
//              Eliminates direct static dependencies on Eternal_Mod.settings throughout codebase.

using Eternal.Interfaces;

namespace Eternal.Settings
{
    /// <summary>
    /// Adapter that provides ISettingsProvider access to Eternal_Settings.
    /// Wraps the static Eternal_Mod.settings to enable constructor injection.
    /// </summary>
    public class SettingsAdapter : ISettingsProvider
    {
        private Eternal_Settings Settings => Eternal_Mod.settings;

        #region General Settings

        public bool ModEnabled => Settings?.modEnabled ?? true;
        public bool DebugMode => Settings?.debugMode ?? false;
        public int LoggingLevel => Settings?.loggingLevel ?? 1;

        #endregion

        #region Healing Settings

        public float BaseHealingRate => Settings?.baseHealingRate ?? 1.8f;
        public bool ShowRegrowthEffects => Settings?.showRegrowthEffects ?? true;
        public bool ShowRegrowthProgress => Settings?.showRegrowthProgress ?? true;

        #endregion

        #region Resource Settings

        public float NutritionCostMultiplier => Settings?.nutritionCostMultiplier ?? 1.0f;
        public bool PauseOnResourceDepletion => Settings?.pauseOnResourceDepletion ?? true;
        public float MinimumNutritionThreshold => Settings?.minimumNutritionThreshold ?? 0.1f;
        public bool AllowResourceBorrowing => Settings?.allowResourceBorrowing ?? false;

        #endregion

        #region Food Debt Settings

        public float MaxDebtMultiplier => Settings?.maxDebtMultiplier ?? 5.0f;
        public float FoodDrainThreshold => Settings?.foodDrainThreshold ?? 0.15f;
        public float MinDebtDrainRate => Settings?.minDebtDrainRate ?? 0.0001f;
        public float MaxDebtDrainRate => Settings?.maxDebtDrainRate ?? 0.001f;
        public float SeverityToNutritionRatio => Settings?.severityToNutritionRatio ?? 0.004f;
        public float HungerMultiplierCap => Settings?.hungerMultiplierCap ?? SettingsDefaults.HungerMultiplierCap;
        public bool HungerBoostEnabled => Settings?.hungerBoostEnabled ?? SettingsDefaults.HungerBoostEnabled;

        #endregion

        #region Performance Settings

        public int NormalTickRate => Settings?.normalTickRate ?? 60;
        public int RareTickRate => Settings?.rareTickRate ?? 250;
        public int TraitCheckInterval => Settings?.traitCheckInterval ?? 5000;
        public int CorpseCheckInterval => Settings?.corpseCheckInterval ?? 1000;
        public int MapCheckInterval => Settings?.mapCheckInterval ?? 500;

        #endregion

        #region Advanced Settings

        public bool EnableIndividualHediffControl => Settings?.enableIndividualHediffControl ?? true;
        public bool AutoHealEnabled => Settings?.autoHealEnabled ?? true;
        public HealingOrder HealingOrder => Settings?.healingOrder ?? HealingOrder.CheapestFirst;

        #endregion

        #region Map Protection Settings

        public bool EnableMapAnchors => Settings?.enableMapAnchors ?? true;
        public int AnchorGracePeriodTicks => Settings?.anchorGracePeriodTicks ?? 300;

        #endregion
    }
}
