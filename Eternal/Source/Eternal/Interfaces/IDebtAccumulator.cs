// Relative Path: Eternal/Source/Eternal/Interfaces/IDebtAccumulator.cs
// Creation Date: 29-12-2025
// Last Edit: 12-07-2026
// Author: 0Shard
// Description: Abstraction for debt accumulation during healing. Two patterns:
//              alive-capped debt (living healing) and uncapped resurrection debt (corpse healing).

using Verse;

namespace Eternal.Interfaces
{
    /// <summary>
    /// Accumulates food debt during healing operations.
    /// Provides two modes:
    /// - Standard debt: capped at the alive-time capacity (maxDebtMultiplier on top of any resurrection baseline)
    /// - Resurrection debt: uncapped, raises the pawn's resurrection debt baseline
    /// </summary>
    public interface IDebtAccumulator
    {
        /// <summary>
        /// Adds debt using the system's maximum capacity.
        /// Used for living pawn healing.
        /// </summary>
        /// <param name="pawn">The pawn to add debt to</param>
        /// <param name="amount">Amount of debt to add</param>
        /// <returns>True if debt was added, false if at capacity</returns>
        bool AddDebt(Pawn pawn, float amount);

        /// <summary>
        /// Adds uncapped resurrection debt during corpse healing.
        /// Resurrection is never blocked by cost; the full amount is always charged.
        /// </summary>
        /// <param name="pawn">The pawn to add debt to</param>
        /// <param name="amount">Amount of debt to add</param>
        /// <returns>True if debt was added (or waived for food-need-disabled pawns)</returns>
        bool AddResurrectionDebt(Pawn pawn, float amount);
    }
}
