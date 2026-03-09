// Relative Path: Eternal/Source/Eternal/Settings/HediffSettingSlim.cs
// Creation Date: 03-01-2026
// Last Edit: 21-02-2026
// Author: 0Shard
// Description: Minimal data class for hediff settings XML persistence.
//              Only stores the 3 user-configurable fields to keep save files small.
//              Includes Validate() for per-field bounds clamping with sentinel preservation.

using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Eternal.Settings
{
    /// <summary>
    /// Minimal hediff setting for XML persistence.
    /// Only stores the essential user-configurable fields.
    /// </summary>
    public class HediffSettingSlim : IExposable
    {
        /// <summary>
        /// Sentinel value indicating "use global baseHealingRate".
        /// </summary>
        public const float USE_GLOBAL_RATE = -1f;

        // Documented validation bounds
        private const float HEALING_RATE_MIN = 0.001f;
        private const float HEALING_RATE_MAX = 0.1f;
        private const float NUTRITION_COST_MIN = 0.01f;
        private const float NUTRITION_COST_MAX = 10.0f;

        /// <summary>
        /// The defName of the hediff this setting applies to.
        /// </summary>
        public string defName = "";

        /// <summary>
        /// Whether this hediff should be healed by Eternals.
        /// </summary>
        public bool canHeal = true;

        /// <summary>
        /// Custom healing rate. USE_GLOBAL_RATE (-1) means use global setting.
        /// </summary>
        public float healingRate = USE_GLOBAL_RATE;

        /// <summary>
        /// Nutrition cost multiplier for healing this hediff.
        /// </summary>
        public float nutritionCostMultiplier = 1.0f;

        /// <summary>
        /// Default constructor for serialization.
        /// </summary>
        public HediffSettingSlim()
        {
        }

        /// <summary>
        /// Creates a slim setting from a full EternalHediffSetting.
        /// </summary>
        public HediffSettingSlim(string defName, EternalHediffSetting fullSetting)
        {
            this.defName = defName;
            this.canHeal = fullSetting.canHeal;
            this.healingRate = fullSetting.healingRate;
            this.nutritionCostMultiplier = fullSetting.nutritionCostMultiplier;
        }

        /// <summary>
        /// Applies this slim setting to a full EternalHediffSetting.
        /// </summary>
        public void ApplyTo(EternalHediffSetting fullSetting)
        {
            fullSetting.canHeal = canHeal;
            fullSetting.healingRate = healingRate;
            fullSetting.nutritionCostMultiplier = nutritionCostMultiplier;
        }

        /// <summary>
        /// Returns true if this setting has a custom healing rate.
        /// </summary>
        public bool HasCustomHealingRate => healingRate != USE_GLOBAL_RATE;

        /// <summary>
        /// Validates and clamps all numeric fields to their documented bounds.
        /// The USE_GLOBAL_RATE sentinel (-1f) is intentionally excluded from clamping.
        /// Appends human-readable warnings to the supplied list for each corrected field.
        /// </summary>
        /// <param name="warnings">List to receive per-field warning messages.</param>
        /// <returns>True if any value was corrected.</returns>
        public bool Validate(List<string> warnings)
        {
            bool anyCorrection = false;

            // healingRate: skip the USE_GLOBAL_RATE sentinel — it is a valid special value.
            if (healingRate != USE_GLOBAL_RATE)
            {
                float clamped = Mathf.Clamp(healingRate, HEALING_RATE_MIN, HEALING_RATE_MAX);
                if (clamped != healingRate)
                {
                    warnings?.Add(
                        $"'{defName}'.healingRate: {healingRate} clamped to {clamped} " +
                        $"(valid: {HEALING_RATE_MIN}-{HEALING_RATE_MAX})");
                    healingRate = clamped;
                    anyCorrection = true;
                }
            }

            // nutritionCostMultiplier: always has a valid bounded range.
            {
                float clamped = Mathf.Clamp(nutritionCostMultiplier, NUTRITION_COST_MIN, NUTRITION_COST_MAX);
                if (clamped != nutritionCostMultiplier)
                {
                    warnings?.Add(
                        $"'{defName}'.nutritionCost: {nutritionCostMultiplier} clamped to {clamped} " +
                        $"(valid: {NUTRITION_COST_MIN}-{NUTRITION_COST_MAX})");
                    nutritionCostMultiplier = clamped;
                    anyCorrection = true;
                }
            }

            // canHeal (bool) and defName (string) have no range to validate.
            return anyCorrection;
        }

        /// <summary>
        /// Serializes and deserializes the setting data.
        /// </summary>
        public void ExposeData()
        {
            Scribe_Values.Look(ref defName, "defName", "");
            Scribe_Values.Look(ref canHeal, "canHeal", true);
            Scribe_Values.Look(ref healingRate, "healingRate", USE_GLOBAL_RATE);
            Scribe_Values.Look(ref nutritionCostMultiplier, "nutritionCost", 1.0f);
        }

        public override string ToString()
        {
            return $"HediffSettingSlim({defName}): canHeal={canHeal}, rate={healingRate}, nutrition={nutritionCostMultiplier}";
        }
    }
}
