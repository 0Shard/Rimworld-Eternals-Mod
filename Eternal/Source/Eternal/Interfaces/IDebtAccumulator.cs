// Relative Path: Eternal/Source/Eternal/Interfaces/IDebtAccumulator.cs
// Creation Date: 29-12-2025
// Last Edit: 13-01-2026
// Author: 0Shard
// Description: Abstraction for debt accumulation during healing. Unifies the two different
//              debt patterns: resurrection-capped (2x) and system-capped (5x).

using Verse;

namespace Eternal.Interfaces
{
    /// <summary>
    /// Accumulates food debt during healing operations.
    /// Provides two modes:
    /// - Standard debt: uses system's 5× max capacity
    /// - Resurrection-capped debt: additional 2× per-resurrection cap
    /// </summary>
    public interface IDebtAccumulator
    {
        /// <summary>
        /// Adds debt using the system's maximum capacity (5× nutrition).
        /// Used for living pawn healing.
        /// </summary>
        /// <param name="pawn">The pawn to add debt to</param>
        /// <param name="amount">Amount of debt to add</param>
        /// <returns>True if debt was added, false if at capacity</returns>
        bool AddDebt(Pawn pawn, float amount);

        /// <summary>
        /// Adds debt with an additional per-resurrection cap.
        /// Used for corpse healing to ensure each resurrection costs at most 2× nutrition,
        /// allowing a pawn to resurrect at least twice before hitting the 5× max.
        /// </summary>
        /// <param name="pawn">The pawn to add debt to</param>
        /// <param name="amount">Amount of debt to add</param>
        /// <param name="resurrectionCap">Maximum debt for this resurrection (typically 2× nutrition)</param>
        /// <returns>Actual amount of debt added (may be less than requested if capped)</returns>
        float AddDebtWithResurrectionCap(Pawn pawn, float amount, float resurrectionCap);
    }
}
