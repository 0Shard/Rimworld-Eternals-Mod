/*
 * Relative Path: Eternal/Source/Eternal/Regrowth/EternalRegrowthState.cs
 * Creation Date: 28-10-2025
 * Last Edit: 20-02-2026
 * Author: 0Shard
 * Description: Regrowth state class that serves as a compatibility wrapper for the hediff-per-part approach.
 *              Maps EternalRegrowing_Hediff severity to RegrowthPhase for UI display.
 *              Maintains backwards compatibility with legacy save format.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Eternal.Constants;
using Eternal.DI;
using Eternal.Exceptions;
using Eternal.Interfaces;
using Eternal.Models;
using Eternal.Utils;
using Eternal.Utilities;

namespace Eternal
{
    /// <summary>
    /// Regrowth state class providing UI-compatible access to regrowth data.
    /// In the hediff-per-part approach, this serves as a compatibility wrapper that
    /// translates hediff severity into the legacy partPhases/partProgress format.
    /// </summary>
    /// <remarks>
    /// Two usage modes:
    /// 1. Compatibility wrapper: Constructed with (pawn, hediffs) to present hediff data as legacy format
    /// 2. Legacy saves: Uses ExposeData() for backwards compatibility with old saves
    ///
    /// Severity-to-Phase Mapping:
    /// - 0.00-0.25 = InitialFormation
    /// - 0.25-0.50 = TissueDevelopment
    /// - 0.50-0.75 = NerveIntegration
    /// - 0.75-1.00 = FunctionalCompletion
    /// - 1.00+ = Complete
    /// </remarks>
    public class EternalRegrowthState : IExposable
    {
        #region Fields

        /// <summary>
        /// The pawn this regrowth state belongs to.
        /// </summary>
        public Pawn pawn;

        /// <summary>
        /// Whether regrowth is currently active.
        /// </summary>
        public bool isActive;

        /// <summary>
        /// Flag indicating if this is a wrapper around hediffs (vs legacy save data).
        /// </summary>
        private bool isHediffWrapper;

        /// <summary>
        /// Cached hediff references when in wrapper mode.
        /// </summary>
        private List<EternalRegrowing_Hediff> cachedHediffs;

        // Legacy state tracking (used for backwards compatibility with old saves)
        private Dictionary<BodyPartRecord, RegrowthPartState> partStates = new Dictionary<BodyPartRecord, RegrowthPartState>();

        #endregion

        #region Cached Properties (PERF Optimization)

        private Dictionary<BodyPartRecord, RegrowthPhase> _cachedPartPhases;
        private Dictionary<BodyPartRecord, float> _cachedPartProgress;
        private bool _cacheValid = false;

        /// <summary>
        /// Invalidates cached dictionaries. Call whenever state changes.
        /// </summary>
        private void InvalidateCache()
        {
            _cacheValid = false;
        }

        /// <summary>
        /// Rebuilds cached dictionaries from current state.
        /// </summary>
        private void RebuildCacheIfNeeded()
        {
            if (_cacheValid)
                return;

            if (isHediffWrapper && cachedHediffs != null)
            {
                // Build from hediffs
                _cachedPartPhases = new Dictionary<BodyPartRecord, RegrowthPhase>();
                _cachedPartProgress = new Dictionary<BodyPartRecord, float>();

                foreach (var hediff in cachedHediffs)
                {
                    if (hediff?.forPart == null)
                        continue;

                    var (phase, progress) = SeverityToPhaseAndProgress(hediff.Severity);
                    _cachedPartPhases[hediff.forPart] = phase;
                    _cachedPartProgress[hediff.forPart] = progress;
                }
            }
            else
            {
                // Build from legacy partStates
                _cachedPartPhases = partStates.ToDictionary(x => x.Key, x => x.Value.Phase);
                _cachedPartProgress = partStates.ToDictionary(x => x.Key, x => x.Value.Progress);
            }

            _cacheValid = true;
        }

        #endregion

        #region Constructors

        /// <summary>
        /// Default constructor for legacy save compatibility.
        /// </summary>
        public EternalRegrowthState()
        {
            isHediffWrapper = false;
        }

        /// <summary>
        /// Compatibility wrapper constructor that presents hediff data as legacy format.
        /// Used by EternalRegrowthManager.GetRegrowthState() in the hediff-per-part approach.
        /// </summary>
        /// <param name="pawn">The pawn being tracked.</param>
        /// <param name="hediffs">The regrowing hediffs to wrap.</param>
        public EternalRegrowthState(Pawn pawn, List<EternalRegrowing_Hediff> hediffs)
        {
            this.pawn = pawn;
            this.cachedHediffs = hediffs;
            this.isActive = hediffs.Count > 0;
            this.isHediffWrapper = true;
            InvalidateCache();
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// Gets phase information for all regrowing parts.
        /// In wrapper mode, derives phases from hediff severity.
        /// </summary>
        public Dictionary<BodyPartRecord, RegrowthPhase> partPhases
        {
            get
            {
                RebuildCacheIfNeeded();
                return _cachedPartPhases;
            }
            set
            {
                // Only allow setting in legacy mode
                if (!isHediffWrapper)
                {
                    foreach (var kvp in value)
                    {
                        if (!partStates.ContainsKey(kvp.Key))
                            partStates[kvp.Key] = new RegrowthPartState();
                        partStates[kvp.Key].Phase = kvp.Value;
                    }
                    InvalidateCache();
                }
            }
        }

        /// <summary>
        /// Gets progress information for all regrowing parts.
        /// In wrapper mode, derives progress from hediff severity.
        /// </summary>
        public Dictionary<BodyPartRecord, float> partProgress
        {
            get
            {
                RebuildCacheIfNeeded();
                return _cachedPartProgress;
            }
            set
            {
                // Only allow setting in legacy mode
                if (!isHediffWrapper)
                {
                    foreach (var kvp in value)
                    {
                        if (!partStates.ContainsKey(kvp.Key))
                            partStates[kvp.Key] = new RegrowthPartState();
                        partStates[kvp.Key].Progress = kvp.Value;
                    }
                    InvalidateCache();
                }
            }
        }

        #endregion

        #region Severity-to-Phase Conversion

        /// <summary>
        /// Converts hediff severity (0-1) to RegrowthPhase and progress within that phase.
        /// </summary>
        /// <param name="severity">Hediff severity from 0 to 1.</param>
        /// <returns>Tuple of (phase, progressWithinPhase).</returns>
        private static (RegrowthPhase phase, float progress) SeverityToPhaseAndProgress(float severity)
        {
            if (severity >= RegrowthConstants.PHASE_COMPLETE)
                return (RegrowthPhase.Complete, 1.0f);

            if (severity < RegrowthConstants.PHASE_INITIAL_FORMATION)
                return (RegrowthPhase.InitialFormation, severity / RegrowthConstants.PHASE_DURATION);

            if (severity < RegrowthConstants.PHASE_TISSUE_DEVELOPMENT)
                return (RegrowthPhase.TissueDevelopment,
                    (severity - RegrowthConstants.PHASE_INITIAL_FORMATION) / RegrowthConstants.PHASE_DURATION);

            if (severity < RegrowthConstants.PHASE_NERVE_INTEGRATION)
                return (RegrowthPhase.NerveIntegration,
                    (severity - RegrowthConstants.PHASE_TISSUE_DEVELOPMENT) / RegrowthConstants.PHASE_DURATION);

            return (RegrowthPhase.FunctionalCompletion,
                (severity - RegrowthConstants.PHASE_NERVE_INTEGRATION) / RegrowthConstants.PHASE_DURATION);
        }

        #endregion

        #region Part Restorer Access

        /// <summary>
        /// Gets the part restorer from the service container.
        /// </summary>
        private IPartRestorer PartRestorer => EternalServiceContainer.Instance.PartRestorer;

        #endregion

        #region Legacy Methods (Backwards Compatibility)

        /// <summary>
        /// Initializes regrowth state for all missing body parts.
        /// Only used in legacy mode - hediff wrapper mode doesn't need this.
        /// </summary>
        public void InitializeMissingParts()
        {
            if (isHediffWrapper || pawn?.health?.hediffSet == null)
                return;

            var missingParts = pawn.health.hediffSet.GetMissingPartsCommonAncestors()
                .Select(p => p.Part)
                .Where(p => p != null);

            bool stateChanged = false;
            foreach (var part in missingParts)
            {
                if (!partStates.ContainsKey(part))
                {
                    partStates[part] = new RegrowthPartState(RegrowthPhase.InitialFormation, 0f);
                    stateChanged = true;
                }
            }

            if (stateChanged)
            {
                InvalidateCache();
            }
        }

        /// <summary>
        /// Applies heal amount to all eligible parts.
        /// In wrapper mode, this delegates to EternalRegrowthManager.ProgressRegrowth().
        /// </summary>
        /// <param name="healAmount">The heal amount to apply.</param>
        public void ApplyHealing(float healAmount)
        {
            if (isHediffWrapper)
            {
                // Delegate to regrowth manager in hediff-per-part mode
                var regrowthManager = EternalServiceContainer.Instance.RegrowthManager;
                regrowthManager?.ProgressRegrowth(pawn, healAmount);
                InvalidateCache(); // Refresh from hediffs
                return;
            }

            // Legacy healing logic
            if (healAmount <= 0f || float.IsNaN(healAmount) || float.IsInfinity(healAmount))
            {
                Log.Warning($"[Eternal] Invalid heal amount: {healAmount}");
                return;
            }

            if (pawn?.health?.hediffSet == null)
            {
                Log.Warning("[Eternal] Cannot apply healing - pawn or health is null");
                return;
            }

            try
            {
                var eligibleParts = GetEligibleParts();

                bool progressChanged = false;
                foreach (var part in eligibleParts)
                {
                    if (!partStates.TryGetValue(part, out var state))
                        continue;

                    state.Progress += healAmount;
                    progressChanged = true;

                    if (state.Progress >= 1.0f)
                    {
                        AdvancePartPhase(part);
                    }
                }

                if (progressChanged)
                {
                    InvalidateCache();
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Regrowth,
                    "ApplyHealing", pawn, ex);
                ResetToSafeState();
            }
        }

        /// <summary>
        /// Gets parts that are eligible for regrowth based on dependency logic.
        /// </summary>
        private IEnumerable<BodyPartRecord> GetEligibleParts()
        {
            var allParts = partStates.Keys.ToList();
            var eligibleParts = new List<BodyPartRecord>();

            var activeCritical = GetActiveCriticalPart();

            foreach (var part in allParts)
            {
                if (IsCriticalPart(part))
                {
                    if (activeCritical != null && part == activeCritical && CanUpdatePart(part))
                    {
                        eligibleParts.Add(part);
                    }
                    continue;
                }

                if (CanUpdatePart(part))
                    eligibleParts.Add(part);
            }

            return eligibleParts;
        }

        /// <summary>
        /// Gets the critical body part that should currently be advanced.
        /// </summary>
        private BodyPartRecord GetActiveCriticalPart()
        {
            foreach (var part in GetCriticalPartsInOrder())
            {
                if (partStates.TryGetValue(part, out var state) && state.Phase != RegrowthPhase.Complete)
                {
                    return part;
                }
            }
            return null;
        }

        /// <summary>
        /// Checks if a part can be updated based on dependency logic.
        /// </summary>
        public bool CanUpdatePart(BodyPartRecord part)
        {
            if (part == null || pawn?.health?.hediffSet == null)
                return false;

            // In wrapper mode, check if part has a regrowing hediff
            if (isHediffWrapper)
            {
                return cachedHediffs?.Any(h => h.forPart == part && h.Severity < 1.0f) == true;
            }

            // Legacy mode checks
            if (partStates.TryGetValue(part, out var state))
            {
                if (state.Phase == RegrowthPhase.Complete)
                    return false;

                if (!state.ValidateProgress())
                    return false;
            }

            if (IsCriticalPart(part))
                return true;

            if (part.parent == null)
                return true;

            bool parentMissing = pawn.health.hediffSet.PartIsMissing(part.parent);
            bool parentRegrowing = IsParentPartRegrowing(part.parent);

            return !parentMissing && !parentRegrowing;
        }

        /// <summary>
        /// Checks if a parent part is currently regrowing.
        /// </summary>
        private bool IsParentPartRegrowing(BodyPartRecord parentPart)
        {
            if (parentPart == null)
                return false;

            if (isHediffWrapper)
            {
                return cachedHediffs?.Any(h => h.forPart == parentPart && h.Severity < 1.0f) == true;
            }

            return partStates.TryGetValue(parentPart, out var state) &&
                   state.Phase != RegrowthPhase.Complete;
        }

        #endregion

        #region Static Utility Methods

        /// <summary>
        /// Gets display name for regrowth phase with progress percentage.
        /// </summary>
        public static string GetPhaseDisplayName(RegrowthPhase phase, float progress)
        {
            string percentage = $"{progress:P0}";

            return phase switch
            {
                RegrowthPhase.InitialFormation => $"Initial Formation: {percentage}",
                RegrowthPhase.TissueDevelopment => $"Tissue Development: {percentage}",
                RegrowthPhase.NerveIntegration => $"Nerve Integration: {percentage}",
                RegrowthPhase.FunctionalCompletion => $"Functional Completion: {percentage}",
                RegrowthPhase.Complete => "Complete",
                _ => $"Unknown Phase: {percentage}"
            };
        }

        /// <summary>
        /// Gets simple phase name without progress.
        /// </summary>
        public static string GetPhaseSimpleName(RegrowthPhase phase)
        {
            return phase switch
            {
                RegrowthPhase.InitialFormation => "Initial Formation",
                RegrowthPhase.TissueDevelopment => "Tissue Development",
                RegrowthPhase.NerveIntegration => "Nerve Integration",
                RegrowthPhase.FunctionalCompletion => "Functional Completion",
                RegrowthPhase.Complete => "Complete",
                _ => "Unknown Phase"
            };
        }

        #endregion

        #region Critical Part Logic

        /// <summary>
        /// Determines if a part is critical for death loop prevention.
        /// Uses CriticalPartConstants as single source of truth.
        /// </summary>
        private bool IsCriticalPart(BodyPartRecord part)
        {
            return CriticalPartConstants.IsSequencedPart(part);
        }

        /// <summary>
        /// Gets critical parts in their required regrowth order.
        /// Uses CriticalPartConstants.RegrowthSequence as single source of truth.
        /// </summary>
        public IEnumerable<BodyPartRecord> GetCriticalPartsInOrder()
        {
            IEnumerable<BodyPartRecord> parts;

            if (isHediffWrapper)
            {
                parts = cachedHediffs?.Select(h => h.forPart).Where(p => p != null && IsCriticalPart(p)) ?? Enumerable.Empty<BodyPartRecord>();
            }
            else
            {
                parts = partStates.Keys.Where(IsCriticalPart);
            }

            var criticalParts = parts.ToList();
            var orderedParts = new List<BodyPartRecord>();

            // Use CriticalPartConstants.RegrowthSequence to maintain consistent order
            foreach (var sequencePart in CriticalPartConstants.RegrowthSequence)
            {
                var matchingPart = criticalParts.FirstOrDefault(p =>
                    p.def.defName.Equals(sequencePart, StringComparison.OrdinalIgnoreCase));
                if (matchingPart != null)
                {
                    orderedParts.Add(matchingPart);
                }
            }

            return orderedParts;
        }

        #endregion

        #region Legacy Phase Advancement

        /// <summary>
        /// Advances a body part to the next regrowth phase (legacy mode only).
        /// </summary>
        private void AdvancePartPhase(BodyPartRecord part)
        {
            if (isHediffWrapper)
                return; // Not used in wrapper mode

            if (!partStates.TryGetValue(part, out var state))
                return;

            var nextPhase = state.Phase switch
            {
                RegrowthPhase.InitialFormation => RegrowthPhase.TissueDevelopment,
                RegrowthPhase.TissueDevelopment => RegrowthPhase.NerveIntegration,
                RegrowthPhase.NerveIntegration => RegrowthPhase.FunctionalCompletion,
                RegrowthPhase.FunctionalCompletion => RegrowthPhase.Complete,
                _ => RegrowthPhase.Complete
            };

            state.Phase = nextPhase;
            state.Progress = 0.0f;
            InvalidateCache();

            if (nextPhase == RegrowthPhase.Complete)
            {
                RestoreBodyPart(part);
            }
        }

        /// <summary>
        /// Restores a body part when regrowth is complete (legacy mode only).
        /// </summary>
        private void RestoreBodyPart(BodyPartRecord part)
        {
            if (isHediffWrapper)
                return; // Not used in wrapper mode

            try
            {
                if (pawn == null)
                    return;

                bool restored = PartRestorer?.TryRestorePart(pawn, part) ?? false;

                if (!restored && pawn.health != null)
                {
                    pawn.health.RestorePart(part);
                }

                partStates.Remove(part);
                InvalidateCache();
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Regrowth,
                    "RestoreBodyPart", pawn, ex);
            }
        }

        #endregion

        #region State Management

        /// <summary>
        /// Checks if all regrowth is complete.
        /// </summary>
        public bool IsRegrowthComplete()
        {
            if (isHediffWrapper)
            {
                return cachedHediffs == null || cachedHediffs.Count == 0 || cachedHediffs.All(h => h.Severity >= 1.0f);
            }
            return partStates.Count == 0;
        }

        /// <summary>
        /// Resets all regrowth progress to safe defaults (legacy mode only).
        /// </summary>
        private void ResetToSafeState()
        {
            if (isHediffWrapper)
                return;

            Log.Warning("[Eternal] Resetting regrowth state to safe defaults due to error");

            foreach (var state in partStates.Values)
            {
                state.Reset();
            }

            isActive = false;
            InvalidateCache();
        }

        #endregion

        #region Serialization

        /// <summary>
        /// Saves and loads regrowth state data (legacy mode only).
        /// Wrapper mode doesn't need serialization - hediffs are saved by the game.
        /// </summary>
        public void ExposeData()
        {
            // Wrapper mode doesn't save - hediffs handle their own persistence
            if (isHediffWrapper)
                return;

            try
            {
                Scribe_Values.Look(ref isActive, "isActive", false);
                Scribe_References.Look(ref pawn, "pawn");

                if (Scribe.mode == LoadSaveMode.Saving)
                {
                    var phases = partStates.ToDictionary(x => x.Key, x => x.Value.Phase);
                    var progress = partStates.ToDictionary(x => x.Key, x => x.Value.Progress);

                    var phasesList = phases.ToList();
                    var progressList = progress.ToList();

                    Scribe_Collections.Look(ref phasesList, "partPhases", LookMode.Reference, LookMode.Value);
                    Scribe_Collections.Look(ref progressList, "partProgress", LookMode.Reference, LookMode.Value);
                }
                else
                {
                    var phasesList = new List<KeyValuePair<BodyPartRecord, RegrowthPhase>>();
                    var progressList = new List<KeyValuePair<BodyPartRecord, float>>();

                    Scribe_Collections.Look(ref phasesList, "partPhases", LookMode.Reference, LookMode.Value);
                    Scribe_Collections.Look(ref progressList, "partProgress", LookMode.Reference, LookMode.Value);

                    partStates = new Dictionary<BodyPartRecord, RegrowthPartState>();

                    if (phasesList != null)
                    {
                        foreach (var entry in phasesList)
                        {
                            if (entry.Key != null)
                            {
                                partStates[entry.Key] = new RegrowthPartState(entry.Value, 0f);
                            }
                        }
                    }

                    if (progressList != null)
                    {
                        foreach (var entry in progressList)
                        {
                            if (entry.Key != null && partStates.TryGetValue(entry.Key, out var state))
                            {
                                state.Progress = entry.Value;
                            }
                        }
                    }

                    InvalidateCache();
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Regrowth,
                    "ExposeData", pawn, ex);

                if (Scribe.mode == LoadSaveMode.LoadingVars)
                {
                    ResetToSafeState();
                }
            }
        }

        #endregion
    }

    /// <summary>
    /// Critical body part sequence for death loop prevention.
    /// Defines the order in which critical parts must regrow: Neck -> HeadSkull -> Brain.
    /// </summary>
    public enum CriticalPartSequence
    {
        Neck,           // Must regrow first - connects head to body
        HeadSkull,      // Must regrow second - provides head structure
        Brain,          // Must regrow third - restores consciousness
        NonCritical     // All other body parts including facial/sensory - can regrow in parallel
    }

    /// <summary>
    /// Biological 4-phase regrowth system for all body parts.
    /// Every missing body part follows this exact progression.
    /// </summary>
    public enum RegrowthPhase
    {
        InitialFormation,     // 0-25% progress - Basic structure forming
        TissueDevelopment,    // 25-50% progress - Muscles and tendons growing
        NerveIntegration,     // 50-75% progress - Neural connections forming
        FunctionalCompletion, // 75-100% progress - Part becoming fully functional
        Complete              // 100% complete
    }
}
