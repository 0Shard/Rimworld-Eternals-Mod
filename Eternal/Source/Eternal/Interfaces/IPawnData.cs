// Relative Path: Eternal/Source/Eternal/Interfaces/IPawnData.cs
// Creation Date: 24-02-2026
// Last Edit: 24-02-2026
// Author: 0Shard
// Description: Thin wrapper interface for Pawn data used in testable calculations.
//              Production code wraps real Pawn via PawnWrapper; tests use NSubstitute mocks.
//              Contains only the members that UnifiedHediffHealingCalculator needs from Pawn.

namespace Eternal.Interfaces
{
    /// <summary>
    /// Abstraction over <c>Verse.Pawn</c> for testable method signatures.
    /// Keeps the interface minimal — only members accessed by pure-logic calculators.
    /// </summary>
    public interface IPawnData
    {
        /// <summary>Body size of the pawn (default 1.0 for humans).</summary>
        float BodySize { get; }

        /// <summary>Whether the pawn has a specific trait by defName.</summary>
        bool HasTrait(string traitDefName);

        /// <summary>Whether the underlying pawn reference is valid (non-null, not destroyed).</summary>
        bool IsValid { get; }
    }
}
