// Relative Path: Eternal/Source/Eternal/Extensions/NutritionExtensions.cs
// Creation Date: 09-11-2025
// Last Edit: 19-02-2026
// Author: 0Shard
// Description: Extension methods for nutrition cost and healing speed calculations.
//              Uses configurable severity-to-nutrition ratio (default 250:1), no type-specific multipliers.
//              Stage-based healing speed retained for debuff hediffs.

using Verse;

namespace Eternal.Extensions
{
    /// <summary>
    /// Extension methods for nutrition cost calculations.
    /// Uses configurable severity-to-nutrition ratio, no type-specific multipliers.
    /// Stage-based healing speed is still applied for debuff hediffs.
    /// </summary>
    public static class NutritionExtensions
    {
        /// <summary>
        /// Default cost ratio: 250 severity = 1 nutrition (0.004f).
        /// Matches SettingsDefaults.SeverityToNutritionRatio.
        /// Actual ratio is configurable via settings.severityToNutritionRatio.
        /// </summary>
        private const float DEFAULT_COST_PER_SEVERITY = 0.004f;

        /// <summary>
        /// Calculates the nutrition cost to heal this hediff.
        /// Uses configurable severity-to-nutrition ratio (default 250:1) × global multiplier.
        /// </summary>
        public static float GetHealingNutritionCost(this Hediff hediff)
        {
            if (hediff == null || !hediff.def.isBad)
                return 0f;

            // Use configurable ratio (default 250:1). GetSettings() guarantees non-null (SAFE-08).
            var s = Eternal_Mod.GetSettings();
            float baseCost = hediff.Severity * s.severityToNutritionRatio;

            // Apply global nutrition cost multiplier from settings
            return baseCost * s.nutritionCostMultiplier;
        }

        /// <summary>
        /// Calculates the nutrition cost for a healing item.
        /// </summary>
        public static float GetNutritionCost(this HealingItem item)
        {
            if (item?.Hediff == null)
                return 0f;

            return item.Hediff.GetHealingNutritionCost();
        }

        /// <summary>
        /// Gets the stage-based healing speed multiplier for a hediff.
        /// Debuff hediffs with stages heal slower at higher stages (more severe = harder to heal).
        /// Injuries, scars, and regrowth heal at constant speed (1.0x).
        /// </summary>
        /// <param name="hediff">The hediff to check.</param>
        /// <returns>Speed multiplier (1.0 = normal, lower = slower).</returns>
        public static float GetStageBasedHealingSpeed(this Hediff hediff)
        {
            if (hediff == null)
                return 1.0f;

            // Only debuff hediffs with stages get the penalty
            if (!IsDebuffWithStages(hediff))
                return 1.0f;  // Injuries, scars, regrowth = constant speed

            // Stage-based speed: higher stage = slower healing
            int stageIndex = hediff.CurStageIndex;
            return stageIndex switch
            {
                0 => 1.0f,   // Minor
                1 => 0.8f,   // Moderate
                2 => 0.6f,   // Serious
                3 => 0.4f,   // Severe
                _ => 0.2f    // Extreme (stage 4+)
            };
        }

        /// <summary>
        /// Returns true for debuff hediffs with stages (NOT injuries, scars, or missing parts).
        /// Examples: bloodloss, infections, diseases, toxic buildup, gut worms, etc.
        /// Works with mod-added hediffs too.
        /// </summary>
        private static bool IsDebuffWithStages(Hediff hediff)
        {
            if (hediff == null || hediff.def == null)
                return false;

            // Must be a harmful hediff
            if (!hediff.def.isBad)
                return false;

            // Must have stages
            if (hediff.def.stages == null || hediff.def.stages.Count == 0)
                return false;

            // Injuries heal at constant speed
            if (hediff is Hediff_Injury)
                return false;

            // Missing parts heal at constant speed (regrowth)
            if (hediff is Hediff_MissingPart)
                return false;

            // Scars/permanent conditions heal at constant speed
            if (hediff.IsPermanent())
                return false;

            // Everything else (diseases, infections, debuffs) uses stage-based
            return true;
        }
    }
}
