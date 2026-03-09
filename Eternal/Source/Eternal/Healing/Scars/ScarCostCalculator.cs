// Relative Path: Eternal/Source/Eternal/Healing/Scars/ScarCostCalculator.cs
// Creation Date: 01-01-2025
// Last Edit: 13-01-2026
// Author: 0Shard
// Description: Calculates healing costs and time estimates for scars.
//              Uses unified healing formula matching EternalHediffHealer for consistency.
//              Refactored to instance class with ISettingsProvider injection for SOLID compliance.

using Verse;
using Eternal.DI;
using Eternal.Extensions;
using Eternal.Interfaces;

namespace Eternal.Healing.Scars
{
    /// <summary>
    /// Calculates healing costs and time estimates for scar healing.
    /// Uses unified healing formula: scars heal at the same rate as injuries,
    /// differentiated only by tick frequency (rareTickRate vs normalTickRate).
    /// </summary>
    public class ScarCostCalculator
    {
        /// <summary>
        /// Base nutrition cost: 250 severity = 1 nutrition.
        /// Matches SettingsDefaults.SeverityToNutritionRatio.
        /// Simplified system with no type-specific multipliers.
        /// </summary>
        private const float BASE_NUTRITION_COST_PER_SEVERITY = 0.004f;

        private readonly ISettingsProvider _settings;

        /// <summary>
        /// Gets the default instance using the service container.
        /// Provides backward compatibility for static-style access.
        /// </summary>
        public static ScarCostCalculator Default => _defaultInstance ??= new ScarCostCalculator(
            EternalServiceContainer.Instance.Settings);

        private static ScarCostCalculator _defaultInstance;

        /// <summary>
        /// Creates a new scar cost calculator.
        /// </summary>
        /// <param name="settings">Settings provider for healing rates and multipliers</param>
        public ScarCostCalculator(ISettingsProvider settings)
        {
            _settings = settings;
        }

        /// <summary>
        /// Gets the cost value for sorting scar healing based on severity and body part.
        /// Lower cost scars are healed first (severity-based approach).
        /// </summary>
        public float GetScarCost(ScarHealingRecord record)
        {
            if (record?.Scar == null)
                return float.MaxValue;

            // Cost based on severity - lower severity = lower cost = heal first
            float costValue = record.InitialSeverity * 1000f;

            // Critical body parts cost less (heal first)
            if (record.Scar.Part != null && record.Scar.Part.IsCritical())
                costValue *= 0.5f;

            return costValue;
        }

        /// <summary>
        /// Estimates the healing time for a scar in ticks.
        /// Uses unified formula: severity / baseHealingRate * rareTickRate
        /// All scars heal at the same rate regardless of body part.
        /// </summary>
        public float EstimateHealingTime(Hediff scar, Pawn pawn)
        {
            if (scar == null)
                return float.MaxValue;

            float baseRate = _settings.BaseHealingRate;
            int rareTickRate = _settings.RareTickRate;

            // Unified formula: cycles needed × tick interval
            // healingCycles = severity / baseRate (how many healing ticks to reduce severity to 0)
            float healingCycles = scar.Severity / baseRate;

            // No part-specific multipliers - all scars heal at uniform rate
            return healingCycles * rareTickRate;
        }

        /// <summary>
        /// Calculates nutrition cost for healing a scar.
        /// Uses simplified formula: severity × 0.01 (100 severity = 1 nutrition).
        /// </summary>
        public float CalculateNutritionCost(ScarHealingRecord record)
        {
            if (record?.Scar == null)
                return 0f;

            // Uniform cost: severity directly determines nutrition cost
            // No part-specific multipliers
            float baseCost = record.InitialSeverity * BASE_NUTRITION_COST_PER_SEVERITY;

            // Apply global nutrition cost multiplier from settings
            float nutritionMultiplier = _settings.NutritionCostMultiplier;
            baseCost *= nutritionMultiplier;

            return baseCost;
        }

        /// <summary>
        /// Gets the base healing rate for a scar.
        /// Uses unified formula: returns global baseHealingRate for consistency with injury healing.
        /// </summary>
        public float GetBaseHealingRate(Hediff scar)
        {
            if (scar == null)
                return 0f;

            // Unified formula: use global baseHealingRate directly
            // This matches EternalHediffHealer for consistent healing speed
            return _settings.BaseHealingRate;
        }

        /// <summary>
        /// Calculates the healing amount for a given time delta.
        /// Uses unified formula: baseHealingRate directly per rare tick.
        /// </summary>
        public float CalculateHealingAmount(float deltaTime, Hediff scar)
        {
            if (scar == null)
                return 0f;

            float baseRate = _settings.BaseHealingRate;
            int rareTickRate = _settings.RareTickRate;

            // Unified formula: baseRate per tick (matches EternalHediffHealer)
            // deltaTime represents ticks since last heal, normalize by rareTickRate
            return baseRate * (deltaTime / rareTickRate);
        }

        /// <summary>
        /// Determines if a pawn has enough nutrition to heal.
        /// </summary>
        public bool HasSufficientNutrition(Pawn pawn, float cost)
        {
            float minThreshold = _settings.MinimumNutritionThreshold;
            float availableNutrition = pawn?.needs?.food?.CurLevel ?? 0f;
            return availableNutrition > (cost + minThreshold);
        }

        /// <summary>
        /// Resets the default instance (for testing or mod reload).
        /// </summary>
        public static void ResetDefault()
        {
            _defaultInstance = null;
        }
    }
}
