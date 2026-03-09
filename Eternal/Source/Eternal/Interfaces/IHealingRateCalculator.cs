// Relative Path: Eternal/Source/Eternal/Interfaces/IHealingRateCalculator.cs
// Creation Date: 29-12-2025
// Last Edit: 21-02-2026
// Author: 0Shard
// Description: Abstraction for healing rate calculations. Unifies the different healing
//              rate implementations across the codebase into a single authoritative source.

using Verse;

namespace Eternal.Interfaces
{
    /// <summary>
    /// Calculates healing rates for pawns.
    /// Centralizes healing rate logic that was previously duplicated across:
    /// - EternalCorpseHealingProcessor
    /// - ScarCostCalculator
    /// - EternalHediffHealer
    /// </summary>
    public interface IHealingRateCalculator
    {
        /// <summary>
        /// Calculates the healing amount per tick for a pawn.
        /// Applies body size scaling: larger bodies heal faster to compensate for larger body parts.
        /// </summary>
        /// <param name="pawn">The pawn being healed</param>
        /// <returns>Healing amount per tick</returns>
        float CalculateHealingPerTick(Pawn pawn);

        /// <summary>
        /// Gets the body size scaling factor for a pawn.
        /// </summary>
        /// <param name="pawn">The pawn to get scaling for</param>
        /// <returns>Body size multiplier (default 1.0 for null pawn)</returns>
        float GetBodySizeScaling(Pawn pawn);

    }
}
