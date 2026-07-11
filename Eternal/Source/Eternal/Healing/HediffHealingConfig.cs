// Relative Path: Eternal/Source/Eternal/Healing/HediffHealingConfig.cs
// Creation Date: 28-10-2025
// Last Edit: 11-07-2026
// Author: 0Shard
// Description: Creates default healing configurations for hediffs.
//              Determines if hediffs should be healed by default and with what parameters.
//              Includes threshold checking for debuff hediffs on living Eternal pawns.
//              Simplified healing check: only canHeal matters (enabled is always true for visibility).
//              BUGFIX: Removed HasCustomSettings() gate that was preventing user settings from being respected.
//              BUGFIX: canHeal decides IF a hediff heals; the activation threshold decides WHEN.
//              The 10-07 "enabled = heals, always" change made the threshold dead code (every
//              hediff materializes a setting via GetOrCreate, so the setting != null early-return
//              always fired). Threshold is now enforced for rising staged debuffs; the explicit
//              bypasses are the "Instant Healing" (noThreshold) setting and ShouldBypassThreshold
//              (bloodloss, stationary, naturally-decaying hediffs such as toxic buildup).
//              Default heal flags unified through EternalHediffSetting.ConfigureDefaultFlags (single source of truth).

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

            var setting = (hediff.def.isBad || hediff.IsDefaultHarmful())
                ? CreateHarmfulSetting(hediff)
                : CreateNeutralSetting();

            // Heal flags come from ONE source of truth so transient defaults
            // (healing pipeline) and menu defaults (EternalHediffManager) agree.
            setting.ConfigureDefaultFlags(hediff.def);
            return setting;
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
        /// canHeal decides IF the hediff heals; for rising staged debuffs on living pawns,
        /// the random activation threshold decides WHEN healing starts.
        /// </summary>
        public static bool ShouldHealByDefault(Hediff hediff, EternalHediffSetting setting, bool pawnIsDead)
        {
            if (hediff == null)
                return false;

            var def = hediff.def;

            // Never heal the Eternal essence itself
            if (def == EternalDefOf.Eternal_Essence)
                return false;

            // Injuries: heal non-permanent ones by default
            if (def.injuryProps != null && def != HediffDefOf.MissingBodyPart)
            {
                if (HediffUtility.IsPermanent(hediff))
                    return false;
                return true;
            }

            // canHeal gate: the setting decides IF this hediff heals at all.
            // Without a setting, only lethal bad hediffs on living pawns heal by default.
            bool allowedToHeal = setting != null
                ? setting.canHeal
                : (!pawnIsDead && def.lethalSeverity > 0f && def.isBad);
            if (!allowedToHeal)
                return false;

            // Activation threshold: staged debuffs on living pawns wait for their random
            // threshold before healing starts. Gameplay variety: some infections heal early,
            // others progress further. Bypasses: "Instant Healing" (noThreshold) setting, and
            // ShouldBypassThreshold (bloodloss, stationary, naturally-decaying hediffs).
            if (!pawnIsDead && setting?.noThreshold != true
                && hediff.IsDebuffWithStages() && !hediff.ShouldBypassThreshold())
            {
                var tracker = EternalServiceContainer.Instance?.ThresholdTracker;
                if (tracker != null && !tracker.HasReachedThreshold(hediff.pawn, hediff))
                    return false; // Threshold not yet reached - don't heal yet
            }

            return true;
        }

        /// <summary>
        /// Creates a setting configured for harmful hediffs.
        /// All hediffs default to using global baseHealingRate.
        /// Heal flags (enabled/canHeal/requireCureToResurrect) are NOT set here —
        /// CreateDefaultSetting applies ConfigureDefaultFlags as the single source of truth.
        /// </summary>
        public static EternalHediffSetting CreateHarmfulSetting(Hediff hediff)
        {
            return new EternalHediffSetting
            {
                allowAutoHeal = true,
                requiresResources = true,
                resourceCostMultiplier = 1.0f,
                nutritionCost = hediff.GetHealingNutritionCost(),
                medicineRequirement = GetMedicineRequirement(hediff),
                healingInterval = 250f,
                healingRate = EternalHediffSetting.USE_GLOBAL_RATE  // Use global rate by default
            };
        }

        /// <summary>
        /// Creates a neutral setting for non-harmful hediffs.
        /// All hediffs default to using global baseHealingRate.
        /// Heal flags (enabled/canHeal/requireCureToResurrect) are NOT set here —
        /// CreateDefaultSetting applies ConfigureDefaultFlags as the single source of truth.
        /// </summary>
        public static EternalHediffSetting CreateNeutralSetting()
        {
            return new EternalHediffSetting
            {
                allowAutoHeal = false,
                requiresResources = false,
                resourceCostMultiplier = 1.0f,
                nutritionCost = 0f,
                medicineRequirement = MedicineRequirement.None,
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
