// Relative Path: Eternal/Source/Eternal/Healing/HediffHealingConfig.cs
// Creation Date: 28-10-2025
// Last Edit: 03-01-2026
// Author: 0Shard
// Description: Creates default healing configurations for hediffs.
//              Determines if hediffs should be healed by default and with what parameters.
//              Includes threshold checking for debuff hediffs on living Eternal pawns.
//              Simplified healing check: only canHeal matters (enabled is always true for visibility).
//              BUGFIX: Removed HasCustomSettings() gate that was preventing user settings from being respected.

using Eternal.DI;
using Eternal.Extensions;
using RimWorld;
using Verse;

namespace Eternal.Healing
{
    /// <summary>
    /// Creates default healing configurations for hediffs.
    /// Determines if hediffs should be healed by default and with what parameters.
    /// </summary>
    public static class HediffHealingConfig
    {
        /// <summary>
        /// Creates a default setting for a hediff based on its type.
        /// </summary>
        public static EternalHediffSetting CreateDefaultSetting(Hediff hediff)
        {
            if (hediff == null)
                return CreateNeutralSetting();

            if (hediff.def.isBad || hediff.IsDefaultHarmful())
                return CreateHarmfulSetting(hediff);

            return CreateNeutralSetting();
        }

        /// <summary>
        /// Checks if a hediff is eligible for Eternal healing (ignores threshold).
        /// Used to determine whether to register a healing threshold for a hediff.
        /// </summary>
        /// <param name="hediff">The hediff to check</param>
        /// <param name="setting">Optional per-hediff setting</param>
        /// <returns>True if this hediff would be healed by the Eternal system</returns>
        public static bool IsEligibleForHealing(Hediff hediff, EternalHediffSetting setting)
        {
            if (hediff == null)
                return false;

            var def = hediff.def;

            // Never heal the Eternal essence itself
            if (def == EternalDefOf.Eternal_Essence)
                return false;

            // BUGFIX: Always honor user's explicit canHeal setting
            // Removed HasCustomSettings() gate that was incorrectly blocking user settings
            // when defaultCanHeal wasn't properly initialized
            if (setting != null)
                return setting.canHeal;

            // Lethal bad hediffs are healed by default
            if (def.lethalSeverity > 0f && def.isBad)
                return true;

            return false;
        }

        /// <summary>
        /// Checks if a hediff should be healed by default (Immortals-inspired rules).
        /// For living pawns with debuff hediffs, checks healing activation threshold.
        /// </summary>
        public static bool ShouldHealByDefault(Hediff hediff, EternalHediffSetting setting, bool pawnIsDead)
        {
            if (hediff == null)
                return false;

            var def = hediff.def;

            // Never heal the Eternal essence itself
            if (def == EternalDefOf.Eternal_Essence)
                return false;

            // For living pawns with debuff hediffs, check if healing threshold has been reached
            // This adds gameplay variety: some infections heal early, others must progress further
            // EXCEPTION: Bloodloss, injuries, scars, and regrowth ALWAYS bypass threshold
            if (!pawnIsDead && hediff.IsDebuffWithStages())
            {
                // Skip threshold for hardcoded categories (bloodloss, injuries, scars, regrowth)
                if (!hediff.ShouldBypassThreshold())
                {
                    // Check per-hediff "noThreshold" (Instant Healing) setting
                    bool userNoThreshold = setting?.noThreshold ?? false;

                    if (!userNoThreshold)
                    {
                        var tracker = EternalServiceContainer.Instance?.ThresholdTracker;
                        if (tracker != null && !tracker.HasReachedThreshold(hediff.pawn, hediff))
                            return false; // Threshold not yet reached - don't heal yet
                    }
                }
            }

            // Injuries: heal non-permanent ones by default
            if (def.injuryProps != null && def != HediffDefOf.MissingBodyPart)
            {
                if (HediffUtility.IsPermanent(hediff))
                    return false;
                return true;
            }

            // BUGFIX: Always honor user's explicit canHeal setting
            // Removed HasCustomSettings() gate that was incorrectly blocking user settings
            // when defaultCanHeal wasn't properly initialized
            if (setting != null)
                return setting.canHeal;

            // Dangerous diseases/conditions: default to healing lethal bad hediffs on living pawns
            if (!pawnIsDead && def.lethalSeverity > 0f && def.isBad)
                return true;

            // Everything else is not healed unless explicitly enabled
            return false;
        }

        /// <summary>
        /// Creates a setting configured for harmful hediffs.
        /// All hediffs default to using global baseHealingRate.
        /// </summary>
        public static EternalHediffSetting CreateHarmfulSetting(Hediff hediff)
        {
            return new EternalHediffSetting
            {
                enabled = true,
                allowAutoHeal = true,
                requiresResources = true,
                resourceCostMultiplier = 1.0f,
                nutritionCost = hediff.GetHealingNutritionCost(),
                medicineRequirement = GetMedicineRequirement(hediff),
                canHeal = true,
                healingInterval = 250f,
                healingRate = EternalHediffSetting.USE_GLOBAL_RATE  // Use global rate by default
            };
        }

        /// <summary>
        /// Creates a neutral setting for non-harmful hediffs.
        /// All hediffs default to using global baseHealingRate.
        /// </summary>
        public static EternalHediffSetting CreateNeutralSetting()
        {
            return new EternalHediffSetting
            {
                enabled = false,
                allowAutoHeal = false,
                requiresResources = false,
                resourceCostMultiplier = 1.0f,
                nutritionCost = 0f,
                medicineRequirement = MedicineRequirement.None,
                canHeal = true,
                healingInterval = 250f,
                healingRate = EternalHediffSetting.USE_GLOBAL_RATE  // Use global rate by default
            };
        }

        /// <summary>
        /// Gets medicine requirement for a hediff.
        /// </summary>
        public static MedicineRequirement GetMedicineRequirement(Hediff hediff)
        {
            if (hediff == null)
                return MedicineRequirement.None;

            string defName = hediff.def.defName.ToLowerInvariant();

            if (defName.Contains("infection") || defName.Contains("plague"))
                return MedicineRequirement.Basic;

            if (defName.Contains("disease") && hediff.Severity > 0.5f)
                return MedicineRequirement.Herbal;

            return MedicineRequirement.None;
        }
    }
}
