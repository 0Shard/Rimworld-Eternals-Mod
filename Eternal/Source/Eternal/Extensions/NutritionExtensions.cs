// Relative Path: Eternal/Source/Eternal/Extensions/NutritionExtensions.cs
// Creation Date: 09-11-2025
// Last Edit: 16-07-2026
// Author: 0Shard
// Description: Extension methods for nutrition cost and healing speed calculations.
//              Uses the internal severity conversion constant and the single global nutrition multiplier.

using Verse;

namespace Eternal.Extensions
{
    /// <summary>
    /// Extension methods for nutrition cost calculations.
    /// Uses the internal severity-to-nutrition conversion and one global cost multiplier.
    /// </summary>
    public static class NutritionExtensions
    {
        /// <summary>
        /// Calculates the nutrition cost to heal this hediff.
        /// Uses the internal severity conversion (250:1) × the global multiplier.
        /// </summary>
        public static float GetHealingNutritionCost(this Hediff hediff)
        {
            if (hediff == null || !hediff.def.isBad)
            {
                return 0f;
            }

            // GetSettings() guarantees a non-null root; the conversion constant keeps all
            // healing paths on the same balance formula.
            var settings = Eternal_Mod.GetSettings();
            float baseCost = hediff.Severity * SettingsDefaults.SeverityToNutritionRatio;

            return baseCost * settings.nutritionCostMultiplier;
        }

    }
}
