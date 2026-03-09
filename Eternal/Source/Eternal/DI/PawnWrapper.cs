// Relative Path: Eternal/Source/Eternal/DI/PawnWrapper.cs
// Creation Date: 24-02-2026
// Last Edit: 24-02-2026
// Author: 0Shard
// Description: Production implementation wrapping a real Verse.Pawn behind IPawnData.
//              All member accesses are null-safe with sensible fallbacks.

using Verse;
using RimWorld;
using Eternal.Interfaces;

namespace Eternal.DI
{
    /// <summary>
    /// Wraps a real <see cref="Pawn"/> behind <see cref="IPawnData"/> for production use.
    /// Test code uses NSubstitute mocks of <see cref="IPawnData"/> directly.
    /// </summary>
    public class PawnWrapper : IPawnData
    {
        private readonly Pawn _pawn;

        public PawnWrapper(Pawn pawn)
        {
            _pawn = pawn;
        }

        /// <inheritdoc/>
        public float BodySize => _pawn?.BodySize ?? 1.0f;

        /// <inheritdoc/>
        public bool HasTrait(string traitDefName)
        {
            if (_pawn?.story?.traits == null || string.IsNullOrEmpty(traitDefName))
                return false;

            var traitDef = DefDatabase<TraitDef>.GetNamedSilentFail(traitDefName);
            return traitDef != null && _pawn.story.traits.HasTrait(traitDef);
        }

        /// <inheritdoc/>
        public bool IsValid => _pawn != null && !_pawn.Destroyed;
    }
}
