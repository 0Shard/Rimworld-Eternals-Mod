// Relative Path: Eternal/Source/Eternal/Resources/FoodCostProcessor.cs
// Creation Date: 29-12-2025
// Last Edit: 21-02-2026
// Author: 0Shard
// Description: Implements instant food drain during healing with debt fallback.
//              When healing occurs, food is drained first. When food reaches threshold,
//              remaining costs go to debt.
//              Added: Early exit for pawns with food need disabled (no cost processing).

using System;
using Verse;
using Eternal.Extensions;
using Eternal.Interfaces;

namespace Eternal.Resources
{
    /// <summary>
    /// Processes food costs during healing operations.
    /// Implements instant food drain with debt fallback.
    /// </summary>
    /// <remarks>
    /// Cost flow:
    /// 1. Calculate food cost: healingAmount × nutritionCostMultiplier
    /// 2. Drain from food bar until threshold (15% by default)
    /// 3. Add remaining cost to debt
    /// </remarks>
    public class FoodCostProcessor : IFoodCostProcessor
    {
        private readonly ISettingsProvider _settings;
        private readonly IFoodDebtSystem _debtSystem;

        /// <summary>
        /// Creates a new food cost processor.
        /// </summary>
        /// <param name="settings">Settings provider for cost multiplier and thresholds</param>
        /// <param name="debtSystem">Food debt system for tracking debt</param>
        public FoodCostProcessor(ISettingsProvider settings, IFoodDebtSystem debtSystem)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _debtSystem = debtSystem ?? throw new ArgumentNullException(nameof(debtSystem));
        }

        /// <inheritdoc/>
        public float FoodDrainThreshold => _settings.FoodDrainThreshold;

        public float GetAvailableNutrition(Pawn pawn)
        {
            if (pawn == null)
            {
                return 0f;
            }
            if (pawn.HasFoodNeedDisabled())
            {
                return float.PositiveInfinity;
            }
            return GetDrainableAmount(pawn) + Math.Max(0f, _debtSystem.GetRemainingCapacity(pawn));
        }

        public float SettleNutrition(Pawn pawn, float nutrition)
        {
            if (pawn == null || pawn.HasFoodNeedDisabled() || float.IsNaN(nutrition) ||
                float.IsInfinity(nutrition) || nutrition <= 0f)
            {
                return 0f;
            }
            float drained = pawn.Dead ? 0f : DrainFromFoodBar(pawn, nutrition);
            float remainder = Math.Max(0f, nutrition - drained);
            float accepted = remainder > 0f ? _debtSystem.AddDebtAndGetAccepted(pawn, remainder) : 0f;
            float paid = drained + accepted;
            if (nutrition - paid > Math.Max(0.000001f, nutrition * 0.00001f))
            {
                throw new InvalidOperationException($"[Eternal] Healing settlement shortfall for {pawn.thingIDNumber}: quoted {nutrition}, paid {paid}");
            }
            return paid;
        }

        /// <inheritdoc/>
        public bool CanDrainFood(Pawn pawn)
        {
            if (pawn?.needs?.food == null)
            {
                return false;
            }

            float currentLevel = pawn.needs.food.CurLevel;
            float maxLevel = pawn.needs.food.MaxLevel;

            if (maxLevel <= 0f)
            {
                return false;
            }

            return (currentLevel / maxLevel) > FoodDrainThreshold;
        }

        /// <inheritdoc/>
        public float GetDrainableAmount(Pawn pawn)
        {
            if (pawn?.needs?.food == null)
            {
                return 0f;
            }

            var food = pawn.needs.food;
            float threshold = food.MaxLevel * FoodDrainThreshold;
            return Math.Max(0f, food.CurLevel - threshold);
        }

        /// <inheritdoc/>
        public float ProcessHealingCost(Pawn pawn, float healingAmount)
        {
            // Legacy callers supplied pre-global-multiplier nutrition.
            return SettleNutrition(pawn, healingAmount * _settings.NutritionCostMultiplier);
        }

        /// <summary>
        /// Drains nutrition directly from the pawn's food bar.
        /// </summary>
        /// <param name="pawn">The pawn to drain from</param>
        /// <param name="cost">The amount to drain</param>
        /// <returns>Actual amount drained</returns>
        private float DrainFromFoodBar(Pawn pawn, float cost)
        {
            var food = pawn.needs?.food;
            if (food == null)
            {
                return 0f;
            }

            float threshold = food.MaxLevel * FoodDrainThreshold;
            float drainable = Math.Max(0f, food.CurLevel - threshold);
            float toDrain = Math.Min(cost, drainable);

            if (toDrain > 0f)
            {
                float before = food.CurLevel;
                food.CurLevel = FoodDebtPolicy.FoodAfterPayment(before, toDrain, cost, threshold);
                return Math.Max(0f, before - food.CurLevel);
            }

            return toDrain;
        }

    }
}
