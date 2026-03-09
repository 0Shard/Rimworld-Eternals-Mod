// Relative Path: Eternal/Source/Eternal/Healing/UnifiedPartRestorer.cs
// Creation Date: 29-12-2025
// Last Edit: 20-02-2026
// Author: 0Shard
// Description: Unified body part restoration that consolidates the two implementations:
//              - EternalRegrowthState.RestoreBodyPart()
//              - EternalCorpseHealingProcessor.RestoreMissingBodyParts()

using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Eternal.Exceptions;
using Eternal.Interfaces;
using Eternal.Utils;

namespace Eternal.Healing
{
    /// <summary>
    /// Unified body part restorer.
    /// Centralizes restoration logic that was previously duplicated across:
    /// - EternalRegrowthState.RestoreBodyPart()
    /// - EternalCorpseHealingProcessor.RestoreMissingBodyParts()
    /// </summary>
    public class UnifiedPartRestorer : IPartRestorer
    {
        private readonly ISettingsProvider _settings;

        /// <summary>
        /// Creates a new unified part restorer.
        /// </summary>
        /// <param name="settings">Settings provider for debug logging control</param>
        public UnifiedPartRestorer(ISettingsProvider settings)
        {
            _settings = settings;
        }

        /// <inheritdoc/>
        public void RestoreAllMissingParts(Pawn pawn)
        {
            if (pawn?.health == null)
                return;

            var partsToRestore = GetMissingPartsToRestore(pawn).ToList();

            foreach (var part in partsToRestore)
            {
                TryRestorePart(pawn, part);
            }
        }

        /// <inheritdoc/>
        public bool TryRestorePart(Pawn pawn, BodyPartRecord part)
        {
            if (pawn?.health == null || part == null)
                return false;

            try
            {
                pawn.health.RestorePart(part);

                if (_settings?.DebugMode == true)
                {
                    Log.Message($"[Eternal] Restored body part: {part.def.defName} on {pawn.Name}");
                }

                return true;
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                    "TryRestorePart", pawn, ex);
                return false;
            }
        }

        /// <inheritdoc/>
        public IEnumerable<BodyPartRecord> GetMissingPartsToRestore(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null)
                return Enumerable.Empty<BodyPartRecord>();

            // Get common ancestors to avoid redundant restoration
            // (restoring a leg automatically restores foot, toes, etc.)
            return pawn.health.hediffSet
                .GetMissingPartsCommonAncestors()
                .Where(mp => mp.Part != null)
                .Select(mp => mp.Part);
        }
    }
}
