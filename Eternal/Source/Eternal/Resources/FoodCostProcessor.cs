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

        /// <inheritdoc/>
        public bool CanDrainFood(Pawn pawn)
        {
            if (pawn?.needs?.food == null)
                return false;

            float currentLevel = pawn.needs.food.CurLevel;
            float maxLevel = pawn.needs.food.MaxLevel;

            if (maxLevel <= 0f)
                return false;

            return (currentLevel / maxLevel) > FoodDrainThreshold;
        }

        /// <inheritdoc/>
        public float GetDrainableAmount(Pawn pawn)
        {
            if (pawn?.needs?.food == null)
                return 0f;

            var food = pawn.needs.food;
            float threshold = food.MaxLevel * FoodDrainThreshold;
            return Math.Max(0f, food.CurLevel - threshold);
        }

        /// <inheritdoc/>
        public float ProcessHealingCost(Pawn pawn, float healingAmount)
        {
            if (pawn == null || healingAmount <= 0f)
                return 0f;

            // Skip all cost processing for pawns with food need disabled
            if (pawn.HasFoodNeedDisabled())
                return 0f;

            float foodCost = healingAmount * _settings.NutritionCostMultiplier;

            // Dead pawns or pawns without food needs: all cost goes to debt
            if (pawn.Dead || pawn.needs?.food == null)
            {
                return ProcessAsDebt(pawn, foodCost);
            }

            // Living pawns: try to drain food first
            float drainedFromFood = DrainFromFoodBar(pawn, foodCost);
            float remainingCost = foodCost - drainedFromFood;

            // If we couldn't drain enough, add rest to debt
            if (remainingCost > 0f)
            {
                ProcessAsDebt(pawn, remainingCost);
            }

            // Log if debug mode
            if (_settings.DebugMode && foodCost > 0f)
            {
                Log.Message($"[Eternal] {pawn.Name?.ToStringShort ?? "Pawn"} healing cost: " +
                    $"{drainedFromFood:F3} from food, {remainingCost:F3} to debt. " +
                    $"Food: {pawn.needs.food.CurLevel:F2}/{pawn.needs.food.MaxLevel:F2}");
            }

            return foodCost;
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
                return 0f;

            float threshold = food.MaxLevel * FoodDrainThreshold;
            float drainable = Math.Max(0f, food.CurLevel - threshold);
            float toDrain = Math.Min(cost, drainable);

            if (toDrain > 0f)
            {
                food.CurLevel -= toDrain;
            }

            return toDrain;
        }

        /// <summary>
        /// Adds cost to the pawn's food debt.
        /// </summary>
        /// <param name="pawn">The pawn to add debt to</param>
        /// <param name="cost">The amount to add</param>
        /// <returns>Actual amount added</returns>
        private float ProcessAsDebt(Pawn pawn, float cost)
        {
            if (_debtSystem.AddDebt(pawn, cost))
            {
                return cost;
            }
            return 0f; // Debt at capacity
        }
    }
}
