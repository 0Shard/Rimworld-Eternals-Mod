// Relative Path: Eternal/Source/Eternal/Extensions/HediffExtensions.cs
// Creation Date: 28-10-2025
// Last Edit: 13-01-2026
// Author: 0Shard
// Description: Extension methods for Hediff classification and analysis.
//              Uses RimWorld's built-in properties first, with string matching only for edge cases.
//              Includes IsDebuffWithStages() for identifying hediffs that use healing thresholds.
//              Includes HasStationarySeverity() to detect hediffs with fixed severity that bypass thresholds.

using RimWorld;
using Verse;

namespace Eternal.Extensions
{
    /// <summary>
    /// Extension methods for Hediff classification and analysis.
    /// These use RimWorld's built-in properties first, with string matching only for edge cases.
    /// </summary>
    public static class HediffExtensions
    {
        #region Constants

        /// <summary>
        /// Multiplier applied to hediffs with low maxSeverity (&lt;= 1.0).
        /// Slows healing to match high-severity injury healing speeds.
        /// </summary>
        private const float LOW_MAX_SEVERITY_MULTIPLIER = 0.1f;

        /// <summary>
        /// Threshold for maxSeverity below which the multiplier is applied.
        /// </summary>
        private const float LOW_MAX_SEVERITY_THRESHOLD = 1.0f;

        #endregion

        /// <summary>
        /// Checks if a hediff is harmful and should be healed.
        /// Uses RimWorld's built-in isBad property first.
        /// </summary>
        public static bool IsHarmful(this Hediff hediff)
        {
            if (hediff == null)
                return false;

            // Never heal Eternal essence
            if (hediff.def == EternalDefOf.Eternal_Essence)
                return false;

            // PRIMARY: Use RimWorld's built-in classification
            if (hediff.def.isBad)
                return true;

            if (hediff is Hediff_Injury)
                return true;

            if (hediff.def.lethalSeverity > 0)
                return true;

            if (hediff.def.makesSickThought)
                return true;

            // FALLBACK: String matching for edge cases not covered by API
            string defName = hediff.def.defName.ToLowerInvariant();
            return defName.Contains("infection") ||
                   defName.Contains("food poisoning") ||
                   defName.Contains("scar") ||
                   defName.Contains("permanent");
        }

        /// <summary>
        /// Checks if a hediff is a disease using RimWorld's API.
        /// </summary>
        public static bool IsDisease(this Hediff hediff)
        {
            if (hediff == null)
                return false;

            // PRIMARY: Check for discoverable comp (diseases have this)
            if (hediff is HediffWithComps hwc &&
                hwc.TryGetComp<HediffComp_Discoverable>() != null)
                return true;

            // PRIMARY: Lethal severity indicates disease
            if (hediff.def.lethalSeverity > 0)
                return true;

            // FALLBACK: String matching for modded diseases
            string defName = hediff.def.defName.ToLowerInvariant();
            return defName.Contains("disease") ||
                   defName.Contains("plague") ||
                   defName.Contains("flu") ||
                   defName.Contains("infection") ||
                   defName.Contains("food poisoning");
        }

        /// <summary>
        /// Checks if this hediff is a permanent injury or scar.
        /// </summary>
        public static bool IsPermanentOrScar(this Hediff hediff)
        {
            if (hediff == null)
                return false;

            // PRIMARY: Use RimWorld's built-in check
            if (HediffUtility.IsPermanent(hediff))
                return true;

            if (hediff.def.chronic)
                return true;

            // FALLBACK: String matching
            string defName = hediff.def.defName.ToLowerInvariant();
            return defName.Contains("scar") ||
                   defName.Contains("permanent") ||
                   defName.Contains("old wound") ||
                   defName.Contains("chronic");
        }

        /// <summary>
        /// Checks if a hediff is critical and needs priority healing.
        /// </summary>
        public static bool IsCritical(this Hediff hediff)
        {
            if (hediff == null)
                return false;

            // Blood loss is always critical
            if (hediff.def.defName.Contains("BloodLoss"))
                return true;

            // Life-threatening conditions
            if (hediff.def.lethalSeverity > 0 && hediff.Severity > hediff.def.lethalSeverity * 0.5f)
                return true;

            // Injuries to vital body parts
            if (hediff is Hediff_Injury injury && injury.Part != null)
                return injury.Part.IsCritical();

            return false;
        }

        /// <summary>
        /// Determines the healing type for a hediff.
        /// </summary>
        public static HealingType GetHealingType(this Hediff hediff)
        {
            if (hediff is Hediff_Injury)
                return HealingType.Injury;

            if (hediff.IsDisease())
                return HealingType.Disease;

            if (hediff is Hediff_MissingPart)
                return HealingType.Regrowth;

            if (hediff.IsPermanentOrScar())
                return HealingType.Scar;

            // Check for life-threatening conditions
            if (hediff.def.defName.Contains("BloodLoss") || hediff.IsCritical())
                return HealingType.Critical;

            return hediff.def.isBad ? HealingType.Condition : HealingType.Misc;
        }

        /// <summary>
        /// Checks if a hediff is a debuff with stages (NOT injuries, scars, or missing parts).
        /// Examples: blood loss, infections, diseases, toxic buildup, gut worms, parasites.
        /// These hediffs have stage-based healing multipliers and random healing thresholds.
        /// </summary>
        public static bool IsDebuffWithStages(this Hediff hediff)
        {
            if (hediff?.def == null)
                return false;

            // Must be a harmful hediff
            if (!hediff.def.isBad)
                return false;

            // Must have stages
            if (hediff.def.stages == null || hediff.def.stages.Count == 0)
                return false;

            // Injuries heal at constant speed (no threshold)
            if (hediff is Hediff_Injury)
                return false;

            // Missing parts use regrowth system (no threshold)
            if (hediff is Hediff_MissingPart)
                return false;

            // Scars/permanent conditions heal at constant speed (no threshold)
            if (hediff.IsPermanentOrScar())
                return false;

            // Everything else (diseases, infections, debuffs) uses stage-based healing
            return true;
        }

        /// <summary>
        /// Checks if a hediff should NEVER use threshold-based healing.
        /// These hediff types ALWAYS heal immediately regardless of settings.
        /// Categories: bloodloss, injuries, scars, missing parts (regrowth).
        /// </summary>
        public static bool ShouldBypassThreshold(this Hediff hediff)
        {
            if (hediff?.def == null)
                return true; // Default to no threshold if invalid

            // Bloodloss - ALWAYS heal immediately (critical for survival)
            if (hediff.def == HediffDefOf.BloodLoss)
                return true;

            // Injuries - heal at constant rate, no threshold
            if (hediff is Hediff_Injury)
                return true;

            // Missing parts - use regrowth system, no threshold
            if (hediff is Hediff_MissingPart)
                return true;

            // Scars and permanent conditions - heal at constant rate, no threshold
            if (hediff.IsPermanentOrScar())
                return true;

            // Stationary severity hediffs - bypass threshold (can never reach it naturally)
            if (hediff.HasStationarySeverity())
            {
                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Bypassing threshold for stationary hediff: {hediff.def.defName} " +
                                $"(severity fixed at {hediff.Severity:F2})");
                }
                return true;
            }

            return false;
        }

        /// <summary>
        /// Checks if a hediff has stationary (fixed) severity that will never change naturally.
        /// These hediffs should bypass the healing threshold since they can never reach it.
        /// Examples: bloodfeeder mark (HediffComp_Disappears), certain gene-based conditions.
        /// </summary>
        /// <remarks>
        /// Detection strategy:
        /// 1. Explicit detection: HediffComp_Disappears = time-limited hediff with fixed severity
        /// 2. Negative detection: absence of severity-changing components (SeverityPerDay, Immunizable, etc.)
        /// </remarks>
        public static bool HasStationarySeverity(this Hediff hediff)
        {
            if (hediff?.def == null)
                return false;

            // Check if hediff has any components that change severity
            if (hediff is HediffWithComps hwc && hwc.comps != null)
            {
                foreach (var comp in hwc.comps)
                {
                    // HediffComp_Disappears = time-limited hediff with fixed severity
                    // These will disappear on their own after ticksToDisappear, severity stays constant
                    // Examples: bloodfeeder mark, hangovers, drug effects
                    // Source: RimWorld-Decompiled/Verse/HediffComp_Disappears.cs
                    if (comp is HediffComp_Disappears)
                        return true; // Explicitly stationary - bypass threshold

                    // SeverityPerDay with non-zero value = severity changes over time
                    if (comp is HediffComp_SeverityPerDay)
                    {
                        var props = comp.props as HediffCompProperties_SeverityPerDay;
                        if (props?.severityPerDay != 0f)
                            return false; // Has active severity change
                    }

                    // Immunizable = severity changes based on immunity
                    if (comp is HediffComp_Immunizable)
                        return false;

                    // GrowthMode = severity changes over time
                    if (comp is HediffComp_GrowthMode)
                        return false;

                    // TendDuration = severity changes with tending
                    if (comp is HediffComp_TendDuration)
                        return false;
                }
            }

            // No severity-changing components found = stationary severity
            return true;
        }

        /// <summary>
        /// Checks if a hediff is a default harmful hediff that should be healed automatically.
        /// Uses Immortals-inspired rules.
        /// </summary>
        public static bool IsDefaultHarmful(this Hediff hediff)
        {
            if (hediff == null)
                return false;

            // Never heal Eternal essence
            if (hediff.def == EternalDefOf.Eternal_Essence)
                return false;

            // PRIMARY: Use RimWorld API
            if (hediff.def.isBad)
                return true;

            if (hediff is Hediff_Injury)
                return true;

            if (hediff.def.lethalSeverity > 0)
                return true;

            // FALLBACK: String matching for known harmful types
            string defName = hediff.def.defName.ToLowerInvariant();

            // Critical conditions
            if (defName.Contains("bloodloss") || defName.Contains("anesthetic"))
                return true;

            // Infections and diseases
            if (defName.Contains("infection") || defName.Contains("plague") ||
                defName.Contains("disease") || defName.Contains("flu") ||
                defName.Contains("malaria") || defName.Contains("gut worms") ||
                defName.Contains("muscle parasites") || defName.Contains("gut parasites"))
                return true;

            // Poisoning and toxins
            if (defName.Contains("poison") || defName.Contains("venom") ||
                defName.Contains("toxin") || defName.Contains("snake bite"))
                return true;

            // Pain and consciousness affecting conditions
            if (defName.Contains("pain") || defName.Contains("coma") ||
                defName.Contains("unconscious"))
                return true;

            // Fire and environmental damage
            if (defName.Contains("burn") || defName.Contains("frostbite") ||
                defName.Contains("heatstroke") || defName.Contains("hypothermia"))
                return true;

            // Malnutrition and starvation
            if (defName.Contains("malnutrition") || defName.Contains("starvation"))
                return true;

            return false;
        }

        /// <summary>
        /// Gets automatic severity scaling multiplier based on maxSeverity.
        /// Hediffs with maxSeverity &lt;= 1.0 get 0.1x multiplier to slow healing,
        /// making them heal at similar speeds to high-severity injuries.
        /// </summary>
        /// <param name="hediff">The hediff to check.</param>
        /// <returns>0.1 for low maxSeverity hediffs, 1.0 otherwise.</returns>
        public static float GetAutoSeverityMultiplier(this Hediff hediff)
        {
            if (hediff?.def == null)
                return 1.0f;

            // Injuries always heal at full speed
            if (hediff is Hediff_Injury)
                return 1.0f;

            float maxSev = hediff.def.maxSeverity;

            // Only apply to hediffs with defined, finite maxSeverity <= threshold
            if (!float.IsInfinity(maxSev) && maxSev > 0f && maxSev <= LOW_MAX_SEVERITY_THRESHOLD)
                return LOW_MAX_SEVERITY_MULTIPLIER;

            return 1.0f;
        }
    }
}
