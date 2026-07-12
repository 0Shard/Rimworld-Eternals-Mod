/*
 * Relative Path: Eternal/Source/Eternal/Components/EternalRegrowthManager.cs
 * Creation Date: 28-10-2025
 * Last Edit: 12-07-2026
 * Author: 0Shard
 * Description: Refactored regrowth manager using hediff-per-part approach (Immortals pattern).
 *              Adds Eternal_Regrowing hediff to each missing body part with partEfficiencyOffset stages.
 *              Severity progresses 0->1, when complete hediff is removed and part becomes functional.
 *              Critical part order (Neck->Head->Skull->Brain) is enforced to prevent death loops.
 *              AddRegrowthHediff removes Hediff_MissingPart before adding regrowth to prevent AddDirect block.
 *              Progress scales with part max HP (arm ~1 in-game day at defaults).
 *              Children start at parent phase 3 (severity >= 0.5); Brain waits for fully-regrown
 *              prerequisites. Completion is gated on the parent part existing so regrowth
 *              overlaps in progress but stays strictly ordered in completion.
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

            // Iterate ALL missing-part hediffs, not just common ancestors: RimWorld adds an
            // explicit Hediff_MissingPart to every descendant of a lost part
            // (Hediff_MissingPart.PostAdd), so children of a still-regrowing part are visible
            // here and can start early once the parent passes the phase-3 threshold.
            // CanStartRegrowthForPart decides eligibility.
            var missingParts = pawn.health.hediffSet.hediffs
                .OfType<Hediff_MissingPart>()
                .ToList();

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
        /// Progress scales inversely with part max HP: larger parts take proportionally longer
        /// (severityIncrease = healAmount / (partMaxHP × RegrowthWorkPerPartHP)).
        /// A part at 100% whose parent is still missing holds until the parent completes,
        /// so overlapped regrowth stays strictly ordered in completion.
        /// </summary>
        /// <param name="pawn">The pawn to progress regrowth for.</param>
        /// <param name="healAmount">The healing effort to apply this pass (baseRate × bodySize).</param>
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
                // Migrate pre-fix saves: hediffs created before Part was set post-add
                if (hediff.Part == null && hediff.forPart != null)
                    hediff.Part = hediff.forPart;

                float partMaxHP = hediff.forPart != null
                    ? EBFCompat.GetMaxHealth(hediff.forPart, pawn)
                    : 1f;
                if (partMaxHP <= 0f)
                    partMaxHP = 1f;

                float severityIncrease = healAmount / (partMaxHP * SettingsDefaults.RegrowthWorkPerPartHP);
                float beforeSeverity = hediff.Severity;
                hediff.Severity = Math.Min(hediff.Severity + severityIncrease, 1.0f);

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    // Enhanced debug logging for brain investigation
                    string partDef = hediff.forPart?.def?.defName ?? "NULL_forPart";
                    string partAlt = hediff.Part?.def?.defName ?? "NULL_Part";
                    Log.Message($"[Eternal DEBUG] Regrowth {pawn.Name?.ToStringShort}: " +
                               $"{partDef} (Part={partAlt}) " +
                               $"Severity: {beforeSeverity:F4} -> {hediff.Severity:F4} (+{severityIncrease:F4})");
                }

                // Complete only when the parent part exists again; a finished child under a
                // still-missing parent holds at 100% (completing it early would let RimWorld
                // re-add its Hediff_MissingPart when the parent completes, erasing the regrowth)
                if (hediff.Severity >= 1.0f && CanCompleteRegrowth(pawn, hediff.forPart))
                {
                    CompleteRegrowth(pawn, hediff);
                }
            }

            // After progressing, check if new parts can start regrowing
            // (parents past the phase-3 threshold release their children, completed
            // prerequisites release the next critical part)
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
                .Where(h => h.Severity >= 1.0f && CanCompleteRegrowth(pawn, h.forPart))
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
        /// Critical parts regrow in order: Neck -> Head -> Skull -> Brain.
        /// A part may start once its parent/prerequisite reaches phase 3
        /// (severity >= RegrowthChildStartThreshold) — except the Brain, whose
        /// prerequisites must be fully regrown (death-loop safety).
        /// </summary>
        /// <param name="pawn">The pawn to check.</param>
        /// <param name="part">The body part to check.</param>
        /// <returns>True if regrowth can start for this part.</returns>
        private bool CanStartRegrowthForPart(Pawn pawn, BodyPartRecord part)
        {
            // Use CriticalPartConstants as single source of truth
            int partIndex = CriticalPartConstants.GetSequenceIndex(part);

            // Non-critical parts can start when parent is fully regrown or past phase 3
            if (partIndex < 0)
            {
                if (part.parent != null && !PartAvailableForChildRegrowth(pawn, part.parent))
                    return false;
                return true;
            }

            // Brain (last in sequence) requires FULLY regrown prerequisites
            bool requireFullyRegrown = partIndex == CriticalPartConstants.RegrowthSequence.Count - 1;

            // Critical part: check all prerequisites in sequence
            for (int i = 0; i < partIndex; i++)
            {
                string prereqName = CriticalPartConstants.RegrowthSequence[i];

                var prereqParts = pawn.RaceProps.body.AllParts
                    .Where(p => p.def.defName.Equals(prereqName, StringComparison.OrdinalIgnoreCase));

                foreach (var prereqPart in prereqParts)
                {
                    bool prereqComplete = !pawn.health.hediffSet.PartIsMissing(prereqPart)
                                          && !HasRegrowthHediff(pawn, prereqPart);
                    if (prereqComplete)
                        continue;

                    if (requireFullyRegrown)
                        return false;

                    if (GetRegrowthSeverity(pawn, prereqPart) < SettingsDefaults.RegrowthChildStartThreshold)
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// True when a parent part is complete, or regrowing past the phase-3 threshold,
        /// so its children may start regrowing.
        /// </summary>
        private bool PartAvailableForChildRegrowth(Pawn pawn, BodyPartRecord parent)
        {
            bool parentComplete = !pawn.health.hediffSet.PartIsMissing(parent)
                                  && !HasRegrowthHediff(pawn, parent);
            if (parentComplete)
                return true;

            return GetRegrowthSeverity(pawn, parent) >= SettingsDefaults.RegrowthChildStartThreshold;
        }

        /// <summary>
        /// Gets the regrowth severity for a part, or 0 if it has no regrowth hediff.
        /// </summary>
        private float GetRegrowthSeverity(Pawn pawn, BodyPartRecord part)
        {
            var regrowth = pawn.health.hediffSet.hediffs
                .OfType<EternalRegrowing_Hediff>()
                .FirstOrDefault(h => h.forPart == part || h.Part == part);
            return regrowth?.Severity ?? 0f;
        }

        /// <summary>
        /// A regrowing part may complete only when its parent part exists again.
        /// Completing a child under a still-missing parent would let RimWorld re-add the
        /// child's Hediff_MissingPart when the parent completes, erasing the regrowth.
        /// </summary>
        private bool CanCompleteRegrowth(Pawn pawn, BodyPartRecord part)
        {
            return part?.parent == null || !pawn.health.hediffSet.PartIsMissing(part.parent);
        }

        #endregion

        #region Private Methods - Hediff Management

        /// <summary>
        /// Adds a regrowing hediff to a body part using the Immortals pattern.
        /// The hediff is created and added with Part = null (HediffSet.AddDirect rejects
        /// hediffs on missing parts), then Part is set post-add so the health tab groups
        /// it under the body part instead of Whole Body.
        /// The Hediff_MissingPart is removed only at completion in CompleteRegrowth().
        /// </summary>
        /// <param name="pawn">The pawn to add the hediff to.</param>
        /// <param name="part">The body part to add the hediff to.</param>
        private void AddRegrowthHediff(Pawn pawn, BodyPartRecord part)
        {
            try
            {
                // Part must be null AT ADD TIME: HediffSet.AddDirect rejects any hediff whose
                // Part is a currently-missing body part, and the part stays missing until
                // CompleteRegrowth(). The real part lives in forPart until the post-add sync below.
                var hediff = (EternalRegrowing_Hediff)HediffMaker.MakeHediff(
                    EternalDefOf.Eternal_Regrowing, pawn, null);

                // Store the actual part in forPart field (used for display and completion)
                hediff.Initialize(part, pawn);
                hediff.Severity = 0.01f;

                pawn.health.AddHediff(hediff);

                // Set Part only after AddDirect succeeded: the setter has no missing-part
                // validation and base Hediff.ExposeData persists it. Groups the hediff under
                // the body part in the health tab instead of Whole Body.
                hediff.Part = part;

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
