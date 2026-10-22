// Relative Path: Eternal/Source/Eternal/UI/EternalHediffSetting.cs
// Creation Date: 09-11-2025
// Last Edit: 16-07-2026
// Author: 0Shard
// Description: Individual hediff configuration setting for the Eternal mod healing system.
//              Conservative defaults: Only injuries/scars/regrowth heal by default.
//              Active per-hediff controls are canHeal, healingRate, and noThreshold;
//              retired legacy fields are ignored when loading older ModSettings records.

using Eternal.Compatibility;
using System;
using Verse;

namespace Eternal
{
    /// <summary>
    /// Represents individual configuration settings for a specific hediff in the Eternal healing system.
    /// Allows fine-grained control over healing behavior, speed, and conditions.
    /// </summary>
    public class EternalHediffSetting : IExposable
    {
        /// <summary>
        /// Sentinel value indicating "use global baseHealingRate".
        /// </summary>
        public const float USE_GLOBAL_RATE = SettingsDefaults.HediffHealingRateUseGlobal;

        /// <summary>
        /// The defName of the hediff this setting is for. Used for lookup during reset.
        /// </summary>
        public string defName = "";

        /// <summary>
        /// The default value of canHeal computed from hediff properties.
        /// Used by HasCustomSettings() to detect customization.
        /// </summary>
        public bool defaultCanHeal = SettingsDefaults.HediffCanHeal;

        public bool canHeal = SettingsDefaults.HediffCanHeal;

        /// <summary>
        /// Healing rate for this hediff.
        /// If set to USE_GLOBAL_RATE (-1), uses the global baseHealingRate.
        /// Otherwise, uses this custom value (overrides global).
        /// Range: 0.001 - 0.1
        /// </summary>
        public float healingRate = USE_GLOBAL_RATE;

        /// <summary>
        /// When true, this hediff heals immediately without waiting for a severity threshold.
        /// Overrides the default threshold behavior for debuff hediffs.
        /// Note: Bloodloss, injuries, scars, and regrowth ALWAYS bypass threshold regardless of this setting.
        /// </summary>
        public bool noThreshold = SettingsDefaults.HediffNoThreshold;

        public float nutritionCostMultiplier = SettingsDefaults.HediffNutritionCostMultiplier;
        public float nutritionSeverityRangeOverride = SettingsDefaults.HediffNutritionSeverityRangeOverride;

        /// <summary>
        /// Returns true if this hediff uses a custom healing rate (overrides global).
        /// </summary>
        public bool HasCustomHealingRate => healingRate != USE_GLOBAL_RATE;

        /// <summary>
        /// Gets the effective healing rate for this hediff.
        /// Returns custom rate if set, otherwise returns the global baseHealingRate.
        /// Uses GetSettings() for guaranteed non-null access (SAFE-08).
        /// </summary>
        public float GetEffectiveHealingRate()
        {
            if (HasCustomHealingRate)
            {
                return healingRate;
            }

            return Eternal_Mod.GetSettings().baseHealingRate;
        }

        /// <summary>
        /// Resets the healing rate to use the global value.
        /// </summary>
        public void ResetToGlobalRate()
        {
            healingRate = USE_GLOBAL_RATE;
        }

        // Category-based filtering
        public HediffCategory allowedCategories = HediffCategory.All;
        public bool isInjuryFilter = false;
        public bool isDiseaseFilter = false;
        public bool isConditionFilter = false;

        // Source tracking (set during initialization)
        public string modSource = "Unknown";        // Mod that added this hediff
        public bool isFromBaseGame = false;         // Is from RimWorld base game
        public bool isFromDLC = false;              // Is from official DLC

        // Hediff properties (cached for filtering)
        public bool isBad = false;                  // Is harmful hediff
        public bool isLethal = false;               // Can kill the pawn
        public bool isTendable = false;             // Can be tended by doctors
        public bool isChronic = false;              // Is chronic condition
        public bool isBeneficial = false;           // Is beneficial/positive hediff (not isBad)

        /// <summary>
        /// Default constructor.
        /// </summary>
        public EternalHediffSetting()
        {
        }

        /// <summary>
        /// Constructor with initial values.
        /// </summary>
        public EternalHediffSetting(HediffDef hediffDef)
        {
            // Auto-configure based on hediff properties
            if (hediffDef != null)
            {
                // Store defName for later lookup during reset
                defName = hediffDef.defName;

                CacheDefinitionMetadata(hediffDef, preserveCategoryFilters: false);

                allowedCategories = GetDefaultCategory(hediffDef);

                // Apply Immortals-inspired default behaviour for healing flags
                ConfigureDefaultFlags(hediffDef);

                // Store the computed default for HasCustomSettings() comparison
                defaultCanHeal = canHeal;
                PopulateSourceInfo(hediffDef);
            }
        }

        /// <summary>
        /// Configures default healing-related flags based on hediff characteristics.
        /// Conservative defaults: Only injuries, scars, and regrowth heal by default.
        /// Everything else (diseases, conditions) requires user opt-in.
        /// Public so it can be called during reset to re-apply context-aware defaults.
        /// </summary>
        public void ConfigureDefaultFlags(HediffDef hediffDef)
        {
            bool isInjury = hediffDef.injuryProps != null && !string.Equals(hediffDef.defName, "MissingBodyPart", StringComparison.OrdinalIgnoreCase);
            isBeneficial = !hediffDef.isBad;

            // RJW overrides apply before the conservative injury-only default, including scars.
            canHeal = RjwHealthPolicy.GetDefaultCanHeal(hediffDef) ?? (hediffDef.isBad && isInjury);
        }

        /// <summary>
        /// Populates source information from the hediff definition.
        /// </summary>
        private void PopulateSourceInfo(HediffDef hediffDef)
        {
            // Get mod content pack
            var modContentPack = hediffDef.modContentPack;

            if (modContentPack != null)
            {
                modSource = modContentPack.Name;
                isFromBaseGame = modContentPack.IsCoreMod;

                // Check if from official DLC
                if (modContentPack.IsOfficialMod && !isFromBaseGame)
                {
                    isFromDLC = true;
                }
            }
            else
            {
                modSource = "Unknown";
                isFromBaseGame = false;
            }
        }

        /// <summary>
        /// Initializes source information from a hediff definition.
        /// Public method to allow re-initialization after loading.
        /// </summary>
        public void InitializeFromHediffDef(HediffDef hediffDef)
        {
            if (hediffDef == null)
            {
                return;
            }

            PopulateSourceInfo(hediffDef);
            CacheDefinitionMetadata(hediffDef, preserveCategoryFilters: true);
        }

        private void CacheDefinitionMetadata(HediffDef hediffDef, bool preserveCategoryFilters)
        {
            isBad = hediffDef.isBad;
            isLethal = hediffDef.lethalSeverity > 0;
            isTendable = hediffDef.tendable;
            isChronic = hediffDef.chronic;
            isBeneficial = !hediffDef.isBad;

            // Construction always classifies; reload preserves previously configured flags.
            if (!preserveCategoryFilters || (!isInjuryFilter && !isDiseaseFilter && !isConditionFilter))
            {
                isInjuryFilter = hediffDef.injuryProps != null;
                isDiseaseFilter = hediffDef.lethalSeverity > 0;
                isConditionFilter = hediffDef.isBad && !isInjuryFilter && !isDiseaseFilter;
            }
        }

        /// <summary>
        /// Ensures source information is initialized by checking if it's unknown.
        /// </summary>
        public void EnsureSourceInitialized(string hediffDefName)
        {
            if (modSource == "Unknown" && !string.IsNullOrEmpty(hediffDefName))
            {
                var hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(hediffDefName);
                if (hediffDef != null)
                {
                    InitializeFromHediffDef(hediffDef);
                }
            }
        }

        /// <summary>
        /// Gets the default category for a hediff.
        /// </summary>
        private HediffCategory GetDefaultCategory(HediffDef hediffDef)
        {
            if (hediffDef.injuryProps != null)
            {
                return HediffCategory.Injury;
            }

            if (hediffDef.lethalSeverity > 0)
            {
                return HediffCategory.Disease;
            }

            if (hediffDef.isBad)
            {
                return HediffCategory.Condition;
            }

            return HediffCategory.Other;
        }

        /// <summary>
        /// Checks if this setting has non-default values.
        /// Simplified: Only checks the 3 user-configurable fields.
        /// </summary>
        public bool HasCustomSettings()
        {
            // Only check the three active v2 fields:
            // 1. canHeal differs from its context-aware default
            // 2. Custom healing rate (not using global)
            // 3. Explicit threshold bypass
            return canHeal != defaultCanHeal ||
                   HasCustomHealingRate ||
                   noThreshold ||
                   nutritionCostMultiplier != SettingsDefaults.HediffNutritionCostMultiplier ||
                   nutritionSeverityRangeOverride != SettingsDefaults.HediffNutritionSeverityRangeOverride;
        }

        /// <summary>
        /// Resets this setting to default values.
        /// Context-aware: re-applies ConfigureDefaultFlags to get correct canHeal default.
        /// </summary>
        public void ResetToDefaults()
        {
            // Reset the user-configurable fields to defaults
            healingRate = USE_GLOBAL_RATE;  // Reset to use global rate

            // Re-apply context-aware defaults for canHeal
            HediffDef hediffDef = null;
            if (!string.IsNullOrEmpty(defName))
            {
                hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
            }

            if (hediffDef != null)
            {
                // Re-apply context-aware defaults (beneficial hediffs get canHeal=false, etc.)
                ConfigureDefaultFlags(hediffDef);
                defaultCanHeal = canHeal;  // Update stored default
            }
            else
            {
                // Fallback: use the stored default if we can't look up the def
                canHeal = defaultCanHeal;
            }

            noThreshold = SettingsDefaults.HediffNoThreshold;
            nutritionCostMultiplier = SettingsDefaults.HediffNutritionCostMultiplier;
            nutritionSeverityRangeOverride = SettingsDefaults.HediffNutritionSeverityRangeOverride;
            allowedCategories = HediffCategory.All;
        }

        /// <summary>
        /// Serializes and deserializes the setting data.
        /// </summary>
        public void ExposeData()
        {
            // Core identifiers (new in simplified version)
            Scribe_Values.Look(ref defName, "defName", "");
            Scribe_Values.Look(ref defaultCanHeal, "defaultCanHeal", SettingsDefaults.HediffCanHeal);

            // Unknown legacy keys are ignored while the active values retain their original names.
            Scribe_Values.Look(ref canHeal, "canHeal", SettingsDefaults.HediffCanHeal);
            Scribe_Values.Look(ref healingRate, "healingRate", SettingsDefaults.HediffHealingRateUseGlobal);
            Scribe_Values.Look(ref noThreshold, "noThreshold", SettingsDefaults.HediffNoThreshold);
            Scribe_Values.Look(ref nutritionCostMultiplier, "nutritionCostMultiplier", SettingsDefaults.HediffNutritionCostMultiplier);
            Scribe_Values.Look(ref nutritionSeverityRangeOverride, "nutritionSeverityRangeOverride", SettingsDefaults.HediffNutritionSeverityRangeOverride);

            Scribe_Values.Look(ref allowedCategories, "allowedCategories", HediffCategory.All);
            Scribe_Values.Look(ref isInjuryFilter, "isInjuryFilter", false);
            Scribe_Values.Look(ref isDiseaseFilter, "isDiseaseFilter", false);
            Scribe_Values.Look(ref isConditionFilter, "isConditionFilter", false);

            // Re-compute context-aware default after loading
            if (Scribe.mode == LoadSaveMode.PostLoadInit && !string.IsNullOrEmpty(defName))
            {
                var hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
                if (hediffDef != null)
                {
                    // Create temp setting to compute correct default
                    var tempSetting = new EternalHediffSetting();
                    tempSetting.ConfigureDefaultFlags(hediffDef);
                    defaultCanHeal = tempSetting.canHeal;
                }
            }
        }

        /// <summary>
        /// Returns a string representation of this setting.
        /// </summary>
        public override string ToString()
        {
            return $"EternalHediffSetting: CanHeal={canHeal}";
        }
    }

    /// <summary>
    /// Categories of hediffs for filtering and organization.
    /// </summary>
    public enum HediffCategory
    {
        All,
        Injury,
        Disease,
        Condition,
        Other,
        Permanent
    }

}
