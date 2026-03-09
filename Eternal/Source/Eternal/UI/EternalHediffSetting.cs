// Relative Path: Eternal/Source/Eternal/UI/EternalHediffSetting.cs
// Creation Date: 09-11-2025
// Last Edit: 19-02-2026
// Author: 0Shard
// Description: Individual hediff configuration setting for the Eternal mod healing system.
//              Contains HealingOrder enum for healing order configuration.
//              Conservative defaults: Only injuries/scars/regrowth heal by default.
//              Simplified: Only canHeal, healingRate, nutritionCostMultiplier are user-configurable.

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
        public const float USE_GLOBAL_RATE = -1f;

        /// <summary>
        /// The defName of the hediff this setting is for. Used for lookup during reset.
        /// </summary>
        public string defName = "";

        /// <summary>
        /// The default value of canHeal computed from hediff properties.
        /// Used by HasCustomSettings() to detect customization.
        /// </summary>
        public bool defaultCanHeal = true;

        // Core healing properties
        public bool enabled = true;
        public bool canHeal = true;
        public bool requireCureToResurrect = false;

        // Auto-healing properties
        public bool allowAutoHeal = false;
        public bool requiresResources = false;
        public float resourceCostMultiplier = 1.0f;
        public float nutritionCost = 0f;
        public MedicineRequirement medicineRequirement = MedicineRequirement.None;
        public float healingInterval = 250f;

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
        public bool noThreshold = false;

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
                return healingRate;
            return Eternal_Mod.GetSettings().baseHealingRate;
        }

        /// <summary>
        /// Resets the healing rate to use the global value.
        /// </summary>
        public void ResetToGlobalRate()
        {
            healingRate = USE_GLOBAL_RATE;
        }

        // Legacy properties for backward compatibility
        public bool isEnabled
        {
            get => enabled;
            set => enabled = value;
        }

        // Advanced healing properties
        public float maxSeverityThreshold = 1.0f;
        public bool healPermanentInjuries = true;
        public bool healScars = true;
        public bool healMissingParts = true;

        // Category-based filtering
        public HediffCategory allowedCategories = HediffCategory.All;
        public bool isInjuryFilter = false;
        public bool isDiseaseFilter = false;
        public bool isConditionFilter = false;

        // Sorting (priority removed - now automatic via cost calculation)
        public int sortOrder = 0;

        // Resource management (legacy)
        public float nutritionCostMultiplier = 1.0f;
        public bool consumeExtraResources = false;

        // UI state
        public bool isExpanded = false;
        public bool isSearchMatch = true;

        // Custom per-hediff overrides (nullable = use default if null)
        public float? maxSeverityThresholdOverride = null;  
        // Severity threshold to start healing. null = use default based on hediff type
        // Example: 0.5 = start healing when severity reaches 50%

        public float? healSpeedOverride = null;
        // Custom healing speed multiplier. null = use global heal amount
        // 0 = never heal, 1 = normal speed, 2 = twice as fast

        public bool? needToCureOverride = null;
        // Whether hediff must be cured before resurrection. null = use default logic
        // true = must cure before resurrection, false = can resurrect with this hediff

        public bool? regrowHediffOverride = null;
        // Whether hediff participates in body part regrowth system. null = use default
        // true = can trigger/participate in regrowth, false = excluded from regrowth
        // Example: Set to false for cosmetic scars that shouldn't trigger regrowth

        // Source tracking (set during initialization)
        public string modSource = "Unknown";        // Mod that added this hediff
        public string modContentPackId = "";        // Content pack ID
        public bool isFromBaseGame = false;         // Is from RimWorld base game
        public bool isFromDLC = false;              // Is from official DLC
        public string dlcName = "";                 // DLC name if applicable

        // Hediff properties (cached for filtering)
        public bool isBad = false;                  // Is harmful hediff
        public bool isLethal = false;               // Can kill the pawn
        public bool isTendable = false;             // Can be tended by doctors
        public bool isChronic = false;              // Is chronic condition
        public bool isPermanent = false;            // Is permanent condition
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

                // Cache hediff properties for filtering and default logic
                isBad = hediffDef.isBad;
                isLethal = hediffDef.lethalSeverity > 0;
                isTendable = hediffDef.tendable;
                isChronic = hediffDef.chronic;
                isPermanent = hediffDef.HasComp(typeof(HediffComp_GetsPermanent));

                isInjuryFilter = hediffDef.injuryProps != null;
                isDiseaseFilter = hediffDef.lethalSeverity > 0;
                isConditionFilter = hediffDef.isBad && !isInjuryFilter && !isDiseaseFilter;

                allowedCategories = GetDefaultCategory(hediffDef);

                // Apply Immortals-inspired default behaviour for healing flags
                ConfigureDefaultFlags(hediffDef);

                // Store the computed default for HasCustomSettings() comparison
                defaultCanHeal = canHeal;

                // Populate source information
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
            bool canBePermanent = hediffDef.HasComp(typeof(HediffComp_GetsPermanent)) || hediffDef.chronic;
            bool lethal = hediffDef.lethalSeverity > 0f;
            bool bad = hediffDef.isBad;
            bool beneficial = !bad;

            // Cache beneficial status for UI display
            isBeneficial = beneficial;

            // === CONSERVATIVE DEFAULTS ===
            // Only injuries, scars, and regrowth heal by default.
            // Everything else is user opt-in.

            // Beneficial / neutral hediffs: VISIBLE in UI but not healed by default
            if (beneficial)
            {
                enabled = true;   // Show in UI (was false - hidden)
                canHeal = false;  // Don't heal by default (user can enable)
                requireCureToResurrect = false;
                return;
            }

            // Non-permanent injuries: primary auto-heal targets
            if (isInjury && !canBePermanent)
            {
                enabled = true;
                canHeal = true;
                requireCureToResurrect = false;
                return;
            }

            // Permanent/chronic injuries & scars: NOW healed by default (user requested)
            if (isInjury && canBePermanent)
            {
                enabled = true;
                canHeal = true;   // Changed from false - scars heal by default now
                requireCureToResurrect = false;
                return;
            }

            // Non-injury lethal diseases/conditions: USER OPT-IN (was auto-healed)
            // Still require cure before resurrection if enabled
            if (!isInjury && lethal)
            {
                enabled = true;
                canHeal = false;  // Changed from true - user must opt-in
                requireCureToResurrect = true;  // Still require cure when healing is enabled
                return;
            }

            // Other bad but non-lethal conditions: visible but not healed unless user opts in
            enabled = true;
            canHeal = false;
            requireCureToResurrect = false;
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
                modContentPackId = modContentPack.PackageId;
                isFromBaseGame = modContentPack.IsCoreMod;

                // Check if from official DLC
                if (modContentPack.IsOfficialMod && !isFromBaseGame)
                {
                    isFromDLC = true;
                    dlcName = modContentPack.Name;
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
                return;

            // Populate source information
            PopulateSourceInfo(hediffDef);

            // Cache hediff properties for filtering
            isBad = hediffDef.isBad;
            isLethal = hediffDef.lethalSeverity > 0;
            isTendable = hediffDef.tendable;
            isChronic = hediffDef.chronic;
            isPermanent = hediffDef.HasComp(typeof(HediffComp_GetsPermanent));
            isBeneficial = !hediffDef.isBad;  // Beneficial = not bad

            // Update category filters if not already set
            if (!isInjuryFilter && !isDiseaseFilter && !isConditionFilter)
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
        /// Gets the default healing speed for a hediff based on its properties.
        /// </summary>
        private float GetDefaultHealingSpeed(HediffDef hediffDef)
        {
            if (hediffDef.injuryProps != null)
            {
                return hediffDef.HasComp(typeof(HediffComp_GetsPermanent)) ? 0.5f : 1.0f;
            }

            if (hediffDef.lethalSeverity > 0)
            {
                return hediffDef.tendable ? 0.8f : 0.3f;
            }

            return 1.0f;
        }

        /// <summary>
        /// Gets the default category for a hediff.
        /// </summary>
        private HediffCategory GetDefaultCategory(HediffDef hediffDef)
        {
            if (hediffDef.injuryProps != null) return HediffCategory.Injury;
            if (hediffDef.lethalSeverity > 0) return HediffCategory.Disease;
            if (hediffDef.isBad) return HediffCategory.Condition;
            return HediffCategory.Other;
        }

        /// <summary>
        /// Checks if this setting has non-default values.
        /// Simplified: Only checks the 3 user-configurable fields.
        /// </summary>
        public bool HasCustomSettings()
        {
            // Only check the 3 fields that matter for the simplified UI:
            // 1. canHeal differs from its context-aware default
            // 2. Custom healing rate (not using global)
            // 3. Custom nutrition cost multiplier
            return canHeal != defaultCanHeal ||
                   HasCustomHealingRate ||
                   nutritionCostMultiplier != 1.0f;
        }

        /// <summary>
        /// Legacy HasCustomSettings check for migration purposes.
        /// Returns true if ANY field differs from defaults (old behavior).
        /// </summary>
        public bool HasAnyCustomSettings()
        {
            return !canHeal ||
                   requireCureToResurrect ||
                   allowAutoHeal ||
                   requiresResources ||
                   resourceCostMultiplier != 1.0f ||
                   nutritionCost != 0f ||
                   medicineRequirement != MedicineRequirement.None ||
                   healingInterval != 250f ||
                   HasCustomHealingRate ||
                   noThreshold ||
                   maxSeverityThreshold != 1.0f ||
                   !healPermanentInjuries ||
                   !healScars ||
                   !healMissingParts ||
                   allowedCategories != HediffCategory.All ||
                   nutritionCostMultiplier != 1.0f ||
                   consumeExtraResources ||
                   maxSeverityThresholdOverride.HasValue ||
                   healSpeedOverride.HasValue ||
                   needToCureOverride.HasValue ||
                   regrowHediffOverride.HasValue;
        }

        /// <summary>
        /// Resets this setting to default values.
        /// Context-aware: re-applies ConfigureDefaultFlags to get correct canHeal default.
        /// </summary>
        public void ResetToDefaults()
        {
            // Reset the user-configurable fields to defaults
            healingRate = USE_GLOBAL_RATE;  // Reset to use global rate
            nutritionCostMultiplier = 1.0f;

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

            // Reset legacy fields (for backwards compatibility)
            isEnabled = true;
            requireCureToResurrect = false;
            allowAutoHeal = false;
            requiresResources = false;
            resourceCostMultiplier = 1.0f;
            nutritionCost = 0f;
            medicineRequirement = MedicineRequirement.None;
            healingInterval = 250f;
            noThreshold = false;
            maxSeverityThreshold = 1.0f;
            healPermanentInjuries = true;
            healScars = true;
            healMissingParts = true;
            allowedCategories = HediffCategory.All;
            sortOrder = 0;
            consumeExtraResources = false;

            // Clear custom overrides
            maxSeverityThresholdOverride = null;
            healSpeedOverride = null;
            needToCureOverride = null;
            regrowHediffOverride = null;
        }

        /// <summary>
        /// Serializes and deserializes the setting data.
        /// </summary>
        public void ExposeData()
        {
            // Core identifiers (new in simplified version)
            Scribe_Values.Look(ref defName, "defName", "");
            Scribe_Values.Look(ref defaultCanHeal, "defaultCanHeal", true);

            // Core healing properties
            Scribe_Values.Look(ref enabled, "isEnabled", true);
            Scribe_Values.Look(ref canHeal, "canHeal", true);
            Scribe_Values.Look(ref requireCureToResurrect, "requireCureToResurrect", false);

            // Auto-healing properties
            Scribe_Values.Look(ref allowAutoHeal, "allowAutoHeal", false);
            Scribe_Values.Look(ref requiresResources, "requiresResources", false);
            Scribe_Values.Look(ref resourceCostMultiplier, "resourceCostMultiplier", 1.0f);
            Scribe_Values.Look(ref nutritionCost, "nutritionCost", 0f);
            Scribe_Values.Look(ref medicineRequirement, "medicineRequirement", MedicineRequirement.None);
            Scribe_Values.Look(ref healingInterval, "healingInterval", 250f);
            Scribe_Values.Look(ref healingRate, "healingRate", USE_GLOBAL_RATE);
            Scribe_Values.Look(ref noThreshold, "noThreshold", false);

            Scribe_Values.Look(ref maxSeverityThreshold, "maxSeverityThreshold", 1.0f);
            Scribe_Values.Look(ref healPermanentInjuries, "healPermanentInjuries", true);
            Scribe_Values.Look(ref healScars, "healScars", true);
            Scribe_Values.Look(ref healMissingParts, "healMissingParts", true);

            Scribe_Values.Look(ref allowedCategories, "allowedCategories", HediffCategory.All);
            Scribe_Values.Look(ref isInjuryFilter, "isInjuryFilter", false);
            Scribe_Values.Look(ref isDiseaseFilter, "isDiseaseFilter", false);
            Scribe_Values.Look(ref isConditionFilter, "isConditionFilter", false);

            Scribe_Values.Look(ref sortOrder, "sortOrder", 0);
            Scribe_Values.Look(ref nutritionCostMultiplier, "nutritionCostMultiplier", 1.0f);
            Scribe_Values.Look(ref consumeExtraResources, "consumeExtraResources", false);

            // Custom per-hediff overrides
            Scribe_Values.Look(ref maxSeverityThresholdOverride, "maxSeverityThresholdOverride", null);
            Scribe_Values.Look(ref healSpeedOverride, "healSpeedOverride", null);
            Scribe_Values.Look(ref needToCureOverride, "needToCureOverride", null);
            Scribe_Values.Look(ref regrowHediffOverride, "regrowHediffOverride", null);

            // UI state is not saved

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
            return $"EternalHediffSetting: Enabled={enabled}, CanHeal={canHeal}";
        }
    }

    /// <summary>
    /// Medicine requirements for auto-healing.
    /// </summary>
    public enum MedicineRequirement
    {
        None,
        Basic,
        Herbal,
        Industrial,
        Glitterworld,
        Best
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

    /// <summary>
    /// Order for healing hediffs based on resource cost calculation.
    /// Cost = Food Required + Time to Heal
    /// </summary>
    public enum HealingOrder
    {
        CheapestFirst,      // Heal low-cost hediffs first (efficient, heal many small things)
        MostExpensiveFirst  // Heal high-cost hediffs first (serious injuries prioritized)
    }

}