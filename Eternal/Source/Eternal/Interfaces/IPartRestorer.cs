// Relative Path: Eternal/Source/Eternal/Interfaces/IPartRestorer.cs
// Creation Date: 29-12-2025
// Last Edit: 13-01-2026
// Author: 0Shard
// Description: Abstraction for body part restoration. Unifies the two different implementations
//              in EternalRegrowthState and EternalCorpseHealingProcessor.

using System.Collections.Generic;
using Verse;

namespace Eternal.Interfaces
{
    /// <summary>
    /// Restores missing body parts on pawns.
    /// Centralizes restoration logic that was previously duplicated across:
    /// - EternalRegrowthState.RestoreBodyPart()
    /// - EternalCorpseHealingProcessor.RestoreMissingBodyParts()
    /// </summary>
    public interface IPartRestorer
    {
        /// <summary>
        /// Restores all missing body parts on a pawn.
        /// Uses GetMissingPartsCommonAncestors() to get the minimal set of parts to restore.
        /// </summary>
        /// <param name="pawn">The pawn to restore parts on</param>
        void RestoreAllMissingParts(Pawn pawn);

        /// <summary>
        /// Attempts to restore a single body part.
        /// </summary>
        /// <param name="pawn">The pawn to restore the part on</param>
        /// <param name="part">The body part to restore</param>
        /// <returns>True if restoration succeeded, false otherwise</returns>
        bool TryRestorePart(Pawn pawn, BodyPartRecord part);

        /// <summary>
        /// Gets all missing body parts that need restoration.
        /// Returns the common ancestors to avoid redundant restoration.
        /// </summary>
        /// <param name="pawn">The pawn to check</param>
        /// <returns>Enumerable of missing body part records</returns>
        IEnumerable<BodyPartRecord> GetMissingPartsToRestore(Pawn pawn);
    }
}
