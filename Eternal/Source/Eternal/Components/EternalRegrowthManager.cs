/*
 * Relative Path: Eternal/Source/Eternal/Components/EternalRegrowthManager.cs
 * Creation Date: 28-10-2025
 * Last Edit: 20-02-2026
 * Author: 0Shard
 * Description: Refactored regrowth manager using hediff-per-part approach (Immortals pattern).
 *              Adds Eternal_Regrowing hediff to each missing body part with partEfficiencyOffset stages.
 *              Severity progresses 0->1, when complete hediff is removed and part becomes functional.
 *              Critical part order (Neck->Head->Skull->Brain) is enforced to prevent death loops.
 *              AddRegrowthHediff removes Hediff_MissingPart before adding regrowth to prevent AddDirect block.
 *              Flat severity rate: all parts regrow in same time regardless of HP.
 *              Non-critical parts wait for parent to fully complete before starting.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Eternal.Compat;
using Eternal.Constants;
using Eternal.Exceptions;
using Eternal.Utils;

namespace Eternal
{
    /// <summary>
    /// Refactored regrowth manager using hediff-per-part approach.
    /// Each missing body part gets an Eternal_Regrowing hediff directly on it.
    /// Severity progresses 0->1, partEfficiencyOffset stages control functionality.
    /// </summary>
    /// <remarks>
    /// Design Pattern: Hediff-per-part (Immortals mod pattern)
    /// - State is stored in hediffs, not in a separate dictionary
    /// - Game's hediff save/load handles persistence automatically
    /// - Critical part order enforced: Neck -> Head -> Skull -> Brain
    /// - Non-critical parts regrow in parallel once their parent exists
    /// </remarks>
    public class EternalRegrowthManager : IExposable
    {
        #region Public API - StartRegrowth

        /// <summary>
        /// Starts regrowth for a pawn by adding Eternal_Regrowing hediffs to missing parts.
        /// Safe to call multiple times - skips parts that already have regrowing hediffs.
        /// </summary>
        /// <param name="pawn">The pawn to start regrowth for.</param>
        public void StartRegrowth(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null)
                return;

            var missingParts = pawn.health.hediffSet.GetMissingPartsCommonAncestors().ToList();

            foreach (var missingHediff in missingParts)
            {
                var part = missingHediff.Part;
                if (part == null)
                    continue;

                // Skip if already has a regrowing hediff on this part
                if (HasRegrowthHediff(pawn, part))
                    continue;

                // Enforce critical part order
                if (!CanStartRegrowthForPart(pawn, part))
                    continue;

                // Create and add the regrowing hediff
                AddRegrowthHediff(pawn, part);
            }
        }

        #endregion

        #region Public API - Progress and Completion

        /// <summary>
        /// Progresses regrowth for all regrowing hediffs on a pawn.
        /// All parts regrow at the same severity rate regardless of HP (matches injury healing pattern).
        /// </summary>
        /// <param name="pawn">The pawn to progress regrowth for.</param>
        /// <param name="healAmount">The healing amount to apply (used directly as severity increase).</param>
        public void ProgressRegrowth(Pawn pawn, float healAmount)
        {
            if (pawn?.health?.hediffSet == null || healAmount <= 0f)
                return;

            // Get all regrowing hediffs (create copy to allow modification during iteration)
            var regrowingHediffs = pawn.health.hediffSet.hediffs
                .OfType<EternalRegrowing_Hediff>()
                .ToList();

            foreach (var hediff in regrowingHediffs)
            {
                // Flat severity rate: all parts regrow at same speed (same TIME to complete)
                // Unlike injuries, regrowth goes 0->1 for all parts, so flat rate = equal time
                float severityIncrease = healAmount;
                float beforeSeverity = hediff.Severity;
                hediff.Severity += severityIncrease;

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    // Enhanced debug logging for brain investigation
                    string partDef = hediff.forPart?.def?.defName ?? "NULL_forPart";
                    string partAlt = hediff.Part?.def?.defName ?? "NULL_Part";
                    Log.Message($"[Eternal DEBUG] Regrowth {pawn.Name?.ToStringShort}: " +
                               $"{partDef} (Part={partAlt}) " +
                               $"Severity: {beforeSeverity:F4} -> {hediff.Severity:F4} (+{severityIncrease:F4})");
                }

                // Check for completion
                if (hediff.Severity >= 1.0f)
                {
                    CompleteRegrowth(pawn, hediff);
                }
            }

            // After progressing, check if new parts can start regrowing
            // (e.g., neck completed, now head can start)
            StartRegrowth(pawn);
        }

        /// <summary>
        /// Removes completed regrowth states. No-op in hediff-per-part approach since
        /// completed hediffs are already removed in CompleteRegrowth().
        /// Maintained for backwards compatibility with callers.
        /// </summary>
        /// <param name="pawn">The pawn whose regrowth is complete.</param>
        public void RemoveCompletedRegrowth(Pawn pawn)
        {
            // In the hediff-per-part approach, completed hediffs are removed automatically
            // when severity >= 1.0 in CompleteRegrowth(). This method exists for backwards
            // compatibility with existing callers (TickOrchestrator, EternalCorpseHealingProcessor).

            // As a safety measure, check for any hediffs at 100% and remove them
            if (pawn?.health?.hediffSet == null)
                return;

            var completedHediffs = pawn.health.hediffSet.hediffs
                .OfType<EternalRegrowing_Hediff>()
                .Where(h => h.Severity >= 1.0f)
                .ToList();

            foreach (var hediff in completedHediffs)
            {
                CompleteRegrowth(pawn, hediff);
            }
        }

        #endregion

        #region Public API - State Queries

        /// <summary>
        /// Checks if a part already has a regrowing hediff.
        /// </summary>
        /// <param name="pawn">The pawn to check.</param>
        /// <param name="part">The body part to check.</param>
        /// <returns>True if the part has a regrowing hediff.</returns>
        public bool HasRegrowthHediff(Pawn pawn, BodyPartRecord part)
        {
            return pawn.health.hediffSet.hediffs
                .OfType<EternalRegrowing_Hediff>()
                .Any(h => h.forPart == part || h.Part == part);
        }

        /// <summary>
        /// Gets all currently regrowing hediffs for a pawn.
        /// </summary>
        /// <param name="pawn">The pawn to get regrowing hediffs for.</param>
        /// <returns>Enumerable of regrowing hediffs.</returns>
        public IEnumerable<EternalRegrowing_Hediff> GetRegrowingHediffs(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null)
                return Enumerable.Empty<EternalRegrowing_Hediff>();

            return pawn.health.hediffSet.hediffs.OfType<EternalRegrowing_Hediff>();
        }

        /// <summary>
        /// Checks if a pawn has any active regrowth.
        /// </summary>
        /// <param name="pawn">The pawn to check.</param>
        /// <returns>True if pawn has active regrowth hediffs.</returns>
        public bool IsPawnInRegrowth(Pawn pawn)
        {
            return GetRegrowingHediffs(pawn).Any();
        }

        /// <summary>
        /// Checks if all regrowth is complete for a pawn.
        /// </summary>
        /// <param name="pawn">The pawn to check.</param>
        /// <returns>True if pawn has no active regrowth hediffs.</returns>
        public bool IsRegrowthComplete(Pawn pawn)
        {
            return !IsPawnInRegrowth(pawn);
        }

        /// <summary>
        /// Gets a compatibility wrapper for EternalRegrowthState.
        /// Allows existing UI code to access regrowth data via the legacy interface.
        /// </summary>
        /// <param name="pawn">The pawn to get regrowth state for.</param>
        /// <returns>A compatibility wrapper or null if no active regrowth.</returns>
        public EternalRegrowthState GetRegrowthState(Pawn pawn)
        {
            var hediffs = GetRegrowingHediffs(pawn).ToList();
            if (hediffs.Count == 0)
                return null;

            // Create a compatibility wrapper that presents hediff data as EternalRegrowthState
            return new EternalRegrowthState(pawn, hediffs);
        }

        #endregion

        #region Private Methods - Part Validation

        /// <summary>
        /// Checks if regrowth can start for a part (critical part order enforcement).
        /// Critical parts must regrow in order: Neck -> Head -> Skull -> Brain
        /// Non-critical parts can start when their parent exists.
        /// </summary>
        /// <param name="pawn">The pawn to check.</param>
        /// <param name="part">The body part to check.</param>
        /// <returns>True if regrowth can start for this part.</returns>
        private bool CanStartRegrowthForPart(Pawn pawn, BodyPartRecord part)
        {
            // Use CriticalPartConstants as single source of truth
            int partIndex = CriticalPartConstants.GetSequenceIndex(part);

            // Non-critical parts can start when parent is fully regrown
            if (partIndex < 0)
            {
                if (part.parent != null)
                {
                    // Check parent is not missing
                    if (pawn.health.hediffSet.PartIsMissing(part.parent))
                        return false;

                    // Check parent is not still regrowing (must be 100% complete)
                    if (HasRegrowthHediff(pawn, part.parent))
                        return false;
                }
                return true;
            }

            // Critical part: check all prerequisites are complete
            for (int i = 0; i < partIndex; i++)
            {
                string prereqName = CriticalPartConstants.RegrowthSequence[i];

                // Check if any part matching this prereq is still missing or regrowing
                var prereqParts = pawn.RaceProps.body.AllParts
                    .Where(p => p.def.defName.Equals(prereqName, StringComparison.OrdinalIgnoreCase));

                foreach (var prereqPart in prereqParts)
                {
                    // If prereq is missing, cannot start this critical part
                    if (pawn.health.hediffSet.PartIsMissing(prereqPart))
                        return false;

                    // If prereq is still regrowing (has regrowing hediff), cannot start
                    if (HasRegrowthHediff(pawn, prereqPart))
                        return false;
                }
            }

            return true;
        }

        #endregion

        #region Private Methods - Hediff Management

        /// <summary>
        /// Adds a regrowing hediff to a body part using the Immortals pattern.
        /// Creates hediff with Part = null to avoid RimWorld race condition where vital organs
        /// get Hediff_MissingPart re-added immediately after removal.
        /// The Hediff_MissingPart is removed only at completion in CompleteRegrowth().
        /// </summary>
        /// <param name="pawn">The pawn to add the hediff to.</param>
        /// <param name="part">The body part to add the hediff to.</param>
        private void AddRegrowthHediff(Pawn pawn, BodyPartRecord part)
        {
            try
            {
                // IMMORTALS PATTERN: Create hediff with Part = null to avoid RimWorld validation
                // This prevents the race condition where RimWorld re-adds Hediff_MissingPart
                // for vital organs (brain, heart) immediately after we remove it.
                var hediff = (EternalRegrowing_Hediff)HediffMaker.MakeHediff(
                    EternalDefOf.Eternal_Regrowing, pawn, null);  // Part = null!

                // Store the actual part in forPart field (used for display and completion)
                hediff.Initialize(part, pawn);
                hediff.Severity = 0.01f;

                // Add hediff without specifying part (Part property stays null)
                // This allows it to coexist with Hediff_MissingPart without conflict
                pawn.health.AddHediff(hediff);

                // DON'T remove Hediff_MissingPart here - it's removed in CompleteRegrowth()
                // Both hediffs coexist during regrowth, preventing RimWorld from re-adding missing part

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Started regrowing {part.Label} for {pawn.Name?.ToStringShort}");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Regrowth,
                    "AddRegrowthHediff", pawn, ex);
            }
        }

        /// <summary>
        /// Completes regrowth for a hediff - removes Hediff_MissingPart and regrowth hediff.
        /// Uses Immortals pattern: missing part is only removed at completion to avoid race condition.
        /// </summary>
        /// <param name="pawn">The pawn whose regrowth is completing.</param>
        /// <param name="hediff">The regrowing hediff to complete.</param>
        private void CompleteRegrowth(Pawn pawn, EternalRegrowing_Hediff hediff)
        {
            try
            {
                var part = hediff.forPart;
                string partLabel = part?.Label ?? "unknown";

                // STEP 1: Remove the Hediff_MissingPart NOW (at completion)
                // This is safe because the part is about to become functional
                if (part != null)
                {
                    var missingPartHediff = pawn.health.hediffSet.hediffs
                        .FirstOrDefault(h => h is Hediff_MissingPart && h.Part == part);

                    if (missingPartHediff != null)
                    {
                        pawn.health.RemoveHediff(missingPartHediff);
                    }
                }

                // STEP 2: Remove the regrowth hediff - part is now functional
                pawn.health.RemoveHediff(hediff);

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Completed regrowing {partLabel} for {pawn.Name?.ToStringShort}");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Regrowth,
                    "CompleteRegrowth", pawn, ex);
            }
        }

        #endregion

        #region Serialization

        /// <summary>
        /// No longer needs to save state - hediffs are saved automatically by the game.
        /// The game's hediff save/load system handles persistence of EternalRegrowing_Hediff.
        /// </summary>
        public void ExposeData()
        {
            // State is now stored in hediffs themselves, not in a separate dictionary.
            // The game's hediff save/load system handles this automatically.
            // This method is kept for IExposable interface compliance.
        }

        #endregion
    }
}
