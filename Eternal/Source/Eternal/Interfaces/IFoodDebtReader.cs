// Relative Path: Eternal/Source/Eternal/Interfaces/IFoodDebtReader.cs
// Creation Date: 29-12-2025
// Last Edit: 29-12-2025
// Author: 0Shard
// Description: Read-only interface for food debt queries. Part of Interface Segregation
//              refactoring - clients that only need to query debt don't need write access.

using System.Collections.Generic;
using Verse;

namespace Eternal.Interfaces
{
    /// <summary>
    /// Read-only interface for querying food debt.
    /// Use this interface for clients that only need to check debt status without modifying it.
    /// </summary>
    public interface IFoodDebtReader
    {
        /// <summary>
        /// Gets the current debt for a pawn.
        /// </summary>
        /// <param name="pawn">The pawn to get debt for.</param>
        /// <returns>Current debt amount.</returns>
        float GetDebt(Pawn pawn);

        /// <summary>
        /// Checks if a pawn has any debt.
        /// </summary>
        /// <param name="pawn">The pawn to check.</param>
        /// <returns>True if pawn has debt greater than zero.</returns>
        bool HasDebt(Pawn pawn);

        /// <summary>
        /// Checks if a pawn has excessive debt that should pause healing.
        /// </summary>
        /// <param name="pawn">The pawn to check.</param>
        /// <returns>True if debt exceeds maximum capacity.</returns>
        bool HasExcessiveDebt(Pawn pawn);

        /// <summary>
        /// Gets the maximum debt capacity for a pawn.
        /// </summary>
        /// <param name="pawn">The pawn to get capacity for.</param>
        /// <returns>Maximum debt capacity based on food need.</returns>
        float GetMaxCapacity(Pawn pawn);

        /// <summary>
        /// Gets remaining debt capacity for a pawn.
        /// </summary>
        /// <param name="pawn">The pawn to get remaining capacity for.</param>
        /// <returns>Remaining capacity (max - current).</returns>
        float GetRemainingCapacity(Pawn pawn);

        /// <summary>
        /// Gets all pawns currently being tracked.
        /// </summary>
        /// <returns>Collection of tracked pawns.</returns>
        IEnumerable<Pawn> GetTrackedPawns();

        /// <summary>
        /// Gets debt status string for UI display.
        /// </summary>
        /// <param name="pawn">The pawn to get status for.</param>
        /// <returns>Formatted status string.</returns>
        string GetDebtStatusString(Pawn pawn);
    }
}
