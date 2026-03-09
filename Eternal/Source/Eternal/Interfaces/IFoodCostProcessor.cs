// Relative Path: Eternal/Source/Eternal/Interfaces/IFoodCostProcessor.cs
// Creation Date: 29-12-2025
// Last Edit: 21-02-2026
// Author: 0Shard
// Description: Interface for processing food costs during healing operations.
//              Handles instant food drain and debt accumulation when food is depleted.

using Verse;

namespace Eternal.Interfaces
{
    /// <summary>
    /// Processes food costs during healing operations.
    /// Implements a two-tier cost system:
    /// 1. Instant drain from food bar until threshold
    /// 2. Debt accumulation when food is at threshold
    /// </summary>
    public interface IFoodCostProcessor
    {
        /// <summary>
        /// Processes the food cost for a healing operation on a living pawn.
        /// First drains from food bar, then adds to debt if at threshold.
        /// </summary>
        /// <param name="pawn">The pawn being healed</param>
        /// <param name="healingAmount">Amount healed (used to calculate cost)</param>
        /// <returns>Actual cost processed (sum of food drained and debt added)</returns>
        float ProcessHealingCost(Pawn pawn, float healingAmount);

        /// <summary>
        /// Gets the food drain threshold from settings.
        /// Below this threshold, healing costs go to debt instead of food drain.
        /// </summary>
        float FoodDrainThreshold { get; }

        /// <summary>
        /// Checks if pawn's food level is above the drain threshold.
        /// </summary>
        /// <param name="pawn">The pawn to check</param>
        /// <returns>True if food can be drained, false if at or below threshold</returns>
        bool CanDrainFood(Pawn pawn);

        /// <summary>
        /// Gets the amount of food that can be drained from the pawn.
        /// This is the current food level minus the threshold.
        /// </summary>
        /// <param name="pawn">The pawn to check</param>
        /// <returns>Drainable amount, or 0 if at threshold</returns>
        float GetDrainableAmount(Pawn pawn);
    }
}
