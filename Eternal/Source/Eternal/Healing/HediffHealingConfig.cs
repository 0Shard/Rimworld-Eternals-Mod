// Relative Path: Eternal/Source/Eternal/Healing/HediffHealingConfig.cs
// Creation Date: 28-10-2025
// Last Edit: 16-07-2026
// Author: 0Shard
// Description: Creates default healing configurations for hediffs.
//              Determines if hediffs should be healed by default and with what parameters.
//              Includes threshold checking for debuff hediffs on living Eternal pawns.
//              Per-hediff canHeal controls healing eligibility.
//              BUGFIX: Removed HasCustomSettings() gate that was preventing user settings from being respected.
//              BUGFIX: canHeal decides IF a hediff heals; the activation threshold decides WHEN.
//              The 10-07 "enabled = heals, always" change made the threshold dead code (every
//              hediff materializes a setting via GetOrCreate, so the setting != null early-return
//              always fired). Threshold is now enforced for rising staged debuffs; the explicit
//              bypasses are the "Instant Healing" (noThreshold) setting and ShouldBypassThreshold
//              (bloodloss, stationary, naturally-decaying hediffs such as toxic buildup).
//              Default heal flags unified through EternalHediffSetting.ConfigureDefaultFlags (single source of truth).

using Eternal.Compatibility;
using Eternal.DI;
using Eternal.Extensions;
using Eternal.Hediffs;
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
        public static bool IsRegrowthManaged(HediffDef def)
            => def != null && (def == HediffDefOf.MissingBodyPart || def == EternalDefOf.Eternal_Regrowing
                || def.defName == "MissingBodyPart" || def.defName == "Eternal_Regrowing"
                || (def.hediffClass != null && (typeof(Hediff_MissingPart).IsAssignableFrom(def.hediffClass)
                    || typeof(EternalRegrowing_Hediff).IsAssignableFrom(def.hediffClass))));

        public static bool AllowsGenericHealing(Hediff hediff)
            => hediff?.def != null && !(hediff is Hediff_MissingPart) && !(hediff is EternalRegrowing_Hediff)
                && !(hediff is MetabolicRecovery_Hediff) && !IsRegrowthManaged(hediff.def)
                && hediff.def != EternalDefOf.Eternal_Essence && hediff.def != EternalDefOf.Eternal_MetabolicRecovery
                && !RjwHealthPolicy.IsProtected(hediff.def);

        public static bool CanHealScar(Hediff hediff)
        {
            if (!AllowsGenericHealing(hediff) || !hediff.IsPermanentInjuryScar())
            {
                return false;
            }
            var setting = Eternal_Mod.GetSettings().hediffManager?.GetHediffSetting(hediff.def.defName)
                ?? CreateDefaultSetting(hediff);
            return IsEligibleForHealing(hediff, setting);
        }

        /// <summary>
        /// Creates a default setting for a hediff based on its type.
        /// </summary>
        public static EternalHediffSetting CreateDefaultSetting(Hediff hediff)
        {
            var setting = new EternalHediffSetting();
            if (hediff != null)
            {
                // Transient healing defaults and menu defaults use the same policy.
                setting.ConfigureDefaultFlags(hediff.def);
            }
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
            if (!AllowsGenericHealing(hediff))
            {
                return false;
            }

            var def = hediff.def;

            if (RjwHealthPolicy.IsProtected(def))
            {
                return false;
            }

            // Never heal the Eternal essence itself
            if (def == EternalDefOf.Eternal_Essence)
            {
                return false;
            }

            // BUGFIX: Always honor user's explicit canHeal setting
            // Removed HasCustomSettings() gate that was incorrectly blocking user settings
            // when defaultCanHeal wasn't properly initialized
            if (setting != null)
            {
                return RjwHealthPolicy.GetHealingDecision(hediff, setting) ?? setting.canHeal;
            }

            var rjwDecision = RjwHealthPolicy.GetHealingDecision(hediff, null);
            if (rjwDecision.HasValue)
            {
                return rjwDecision.Value;
            }

            // Lethal bad hediffs are healed by default
            if (def.lethalSeverity > 0f && def.isBad)
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Checks if a hediff should be healed by default (Immortals-inspired rules).
        /// canHeal decides IF the hediff heals; for rising staged debuffs on living pawns,
        /// the random activation threshold decides WHEN healing starts.
        /// </summary>
        public static bool ShouldHealByDefault(Hediff hediff, EternalHediffSetting setting, bool pawnIsDead)
        {
            if (!AllowsGenericHealing(hediff))
            {
                return false;
            }

            var def = hediff.def;

            if (RjwHealthPolicy.IsProtected(def))
            {
                return false;
            }

            // Never heal the Eternal essence itself
            if (def == EternalDefOf.Eternal_Essence)
            {
                return false;
            }

            // Injuries: heal non-permanent ones by default
            if (def.injuryProps != null && def != HediffDefOf.MissingBodyPart)
            {
                if (HediffUtility.IsPermanent(hediff))
                {
                    return false;
                }

                return true;
            }

            // canHeal gate: the setting decides IF this hediff heals at all.
            // Without a setting, only lethal bad hediffs on living pawns heal by default.
            bool allowedToHeal = RjwHealthPolicy.GetHealingDecision(hediff, setting) ?? (setting != null
                ? setting.canHeal
                : (!pawnIsDead && def.lethalSeverity > 0f && def.isBad));
            if (!allowedToHeal)
            {
                return false;
            }

            // Activation threshold: staged debuffs on living pawns wait for their random
            // threshold before healing starts. Gameplay variety: some infections heal early,
            // others progress further.
            if (!pawnIsDead && IsThresholdGated(hediff, setting))
            {
                var tracker = EternalServiceContainer.Instance?.ThresholdTracker;
                if (tracker != null && !tracker.HasReachedThreshold(hediff.pawn, hediff))
                {
                    return false; // Threshold not yet reached - don't heal yet
                }
            }

            return true;
        }

        /// <summary>
        /// Single source of truth for whether a hediff's healing is gated behind a severity
        /// activation threshold. Used by the healing pipeline (ShouldHealByDefault) and the
        /// Thresholds settings tab so display and enforcement can never drift apart.
        /// Gated = allowed to heal (canHeal, or lethal+bad fallback without a setting) AND
        /// a rising staged debuff AND not bypassed ("Instant Healing" noThreshold setting,
        /// bloodloss, stationary, or naturally-decaying hediffs).
        /// </summary>
        public static bool IsThresholdGated(Hediff hediff, EternalHediffSetting setting)
        {
            if (!AllowsGenericHealing(hediff))
            {
                return false;
            }

            bool allowedToHeal = RjwHealthPolicy.GetHealingDecision(hediff, setting) ?? (setting != null
                ? setting.canHeal
                : (hediff.def.lethalSeverity > 0f && hediff.def.isBad));
            if (!allowedToHeal)
            {
                return false;
            }

            if (setting?.noThreshold == true)
            {
                return false;
            }

            return hediff.IsDebuffWithStages() && !hediff.ShouldBypassThreshold();
        }

    }
}
