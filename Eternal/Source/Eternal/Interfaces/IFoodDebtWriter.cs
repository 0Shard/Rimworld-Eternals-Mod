// Relative Path: Eternal/Source/Eternal/Interfaces/IFoodDebtWriter.cs
// Creation Date: 29-12-2025
// Last Edit: 06-03-2026
// Author: 0Shard
// Description: Write interface for food debt mutations. Part of Interface Segregation
//              refactoring - clients that need to modify debt use this interface.

using Verse;

namespace Eternal.Interfaces
{
    /// <summary>
    /// Write interface for modifying food debt.
    /// Use this interface for clients that need to add, repay, or clear debt.
    /// </summary>
    public interface IFoodDebtWriter
    {
        /// <summary>
        /// Registers a pawn for food debt tracking.
        /// </summary>
        /// <param name="pawn">The pawn to register.</param>
        void RegisterPawn(Pawn pawn);

        /// <summary>
        /// Unregisters a pawn from food debt tracking.
        /// </summary>
        /// <param name="pawn">The pawn to unregister.</param>
        void UnregisterPawn(Pawn pawn);

        /// <summary>
        /// Adds debt for a pawn during healing/regrowth.
        /// </summary>
        /// <param name="pawn">The pawn to add debt for.</param>
        /// <param name="amount">The amount of debt to add.</param>
        /// <returns>True if debt was added successfully, false if capacity exceeded.</returns>
        bool AddDebt(Pawn pawn, float amount);

        /// <summary>
        /// Repays debt for a pawn.
        /// </summary>
        /// <param name="pawn">The pawn to repay debt for.</param>
        /// <param name="amount">The amount to repay.</param>
        /// <returns>Actual amount repaid (may be less if debt is smaller).</returns>
        float RepayDebt(Pawn pawn, float amount);

        /// <summary>
        /// Clears all debt for a pawn.
        /// </summary>
        /// <param name="pawn">The pawn to clear debt for.</param>
        void ClearDebt(Pawn pawn);

        /// <summary>
        /// Cleans up invalid entries (destroyed pawns, non-Eternals).
        /// Should be called periodically to maintain data integrity.
        /// </summary>
        void CleanupInvalidEntries();
    }
}
