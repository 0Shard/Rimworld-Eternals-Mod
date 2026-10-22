// Relative Path: Eternal/Source/Eternal/Regrowth/EternalRegrowthState.cs
// Creation Date: 28-10-2025
// Last Edit: 13-10-2026
// Author: 0Shard
// Description: Projects per-part regrowth hediffs into the management UI's phase and progress view.
using System.Collections.Generic;
using System.Linq;
using Verse;
using Eternal.Constants;
using Eternal.DI;
using Eternal.Healing;

namespace Eternal
{
    /// <summary>
    /// A view of the regrowth hediffs captured by EternalRegrowthManager.GetRegrowthState.
    /// Persistence and progression belong to the hediffs and manager respectively.
    /// </summary>
    public class EternalRegrowthState
    {
        public Pawn pawn;
        public bool isActive;

        private readonly List<EternalRegrowing_Hediff> capturedHediffs;
        private Dictionary<BodyPartRecord, RegrowthPhase> cachedPartPhases;
        private Dictionary<BodyPartRecord, float> cachedPartProgress;
        private bool cacheValid;

        public EternalRegrowthState(Pawn pawn, List<EternalRegrowing_Hediff> hediffs)
        {
            this.pawn = pawn;
            capturedHediffs = hediffs;
            isActive = hediffs.Count > 0;
        }

        public Dictionary<BodyPartRecord, RegrowthPhase> partPhases
        {
            get
            {
                RebuildCacheIfNeeded();
                return cachedPartPhases;
            }
        }

        public Dictionary<BodyPartRecord, float> partProgress
        {
            get
            {
                RebuildCacheIfNeeded();
                return cachedPartProgress;
            }
        }

        private void RebuildCacheIfNeeded()
        {
            if (cacheValid)
            {
                return;
            }

            cachedPartPhases = new Dictionary<BodyPartRecord, RegrowthPhase>();
            cachedPartProgress = new Dictionary<BodyPartRecord, float>();
            foreach (var hediff in capturedHediffs)
            {
                if (hediff?.forPart == null)
                {
                    continue;
                }

                var (phase, progress) = SeverityToPhaseAndProgress(hediff.Severity);
                cachedPartPhases[hediff.forPart] = phase;
                cachedPartProgress[hediff.forPart] = progress;
            }

            cacheValid = true;
        }

        private static (RegrowthPhase phase, float progress) SeverityToPhaseAndProgress(float severity)
        {
            if (severity >= RegrowthConstants.PHASE_COMPLETE)
            {
                return (RegrowthPhase.Complete, 1.0f);
            }

            if (severity < RegrowthConstants.PHASE_INITIAL_FORMATION)
            {
                return (RegrowthPhase.InitialFormation, severity / RegrowthConstants.PHASE_DURATION);
            }

            if (severity < RegrowthConstants.PHASE_TISSUE_DEVELOPMENT)
            {
                return (RegrowthPhase.TissueDevelopment,
                    (severity - RegrowthConstants.PHASE_INITIAL_FORMATION) / RegrowthConstants.PHASE_DURATION);
            }

            if (severity < RegrowthConstants.PHASE_NERVE_INTEGRATION)
            {
                return (RegrowthPhase.NerveIntegration,
                    (severity - RegrowthConstants.PHASE_TISSUE_DEVELOPMENT) / RegrowthConstants.PHASE_DURATION);
            }

            return (RegrowthPhase.FunctionalCompletion,
                (severity - RegrowthConstants.PHASE_NERVE_INTEGRATION) / RegrowthConstants.PHASE_DURATION);
        }

        /// <summary>
        /// Applies healing through the manager, then refreshes the captured batch's UI view.
        /// </summary>
        public RegrowthBatchResult ApplyHealing(float healAmount)
        {
            var result = EternalServiceContainer.Instance.RegrowthManager?.ProgressRegrowth(pawn, healAmount) ?? default;
            cacheValid = false;
            return result;
        }

        public bool CanUpdatePart(BodyPartRecord part)
        {
            if (part == null || pawn?.health?.hediffSet == null)
            {
                return false;
            }

            return capturedHediffs.Any(h => h.forPart == part && h.Severity < 1.0f);
        }

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

        /// <summary>
        /// Checks this captured healing batch. ProgressRegrowth may release new children,
        /// whose hediffs belong to a later batch and must not change this completion result.
        /// </summary>
        public bool IsRegrowthComplete()
        {
            return capturedHediffs.Count == 0 || capturedHediffs.All(h => h.Severity >= 1.0f);
        }
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
