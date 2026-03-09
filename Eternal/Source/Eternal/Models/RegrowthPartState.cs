// file path: Eternal/Source/Eternal/Models/RegrowthPartState.cs
// Author Name: 0Shard
// Date Created: 03-12-2025
// Description: Consolidated data structure for regrowth state, eliminating parallel dictionaries.

using Verse;

// Use the RegrowthPhase from Eternal namespace as the single source of truth
using RegrowthPhase = Eternal.RegrowthPhase;

namespace Eternal.Models
{
    /// <summary>
    /// Consolidated data structure for tracking regrowth state of a body part.
    /// Combines phase and progress in a single structure,
    /// eliminating the need for parallel dictionaries.
    /// </summary>
    public class RegrowthPartState : IExposable
    {
        /// <summary>
        /// Current regrowth phase.
        /// </summary>
        public RegrowthPhase Phase { get; set; }

        /// <summary>
        /// Progress within current phase (0.0 to 1.0).
        /// </summary>
        public float Progress { get; set; }

        /// <summary>
        /// Creates a new regrowth part state with default values.
        /// </summary>
        public RegrowthPartState()
        {
            Phase = RegrowthPhase.InitialFormation;
            Progress = 0f;
        }

        /// <summary>
        /// Creates a new regrowth part state with specified values.
        /// </summary>
        public RegrowthPartState(RegrowthPhase phase, float progress = 0f)
        {
            Phase = phase;
            Progress = progress;
        }

        /// <summary>
        /// Checks if regrowth is complete.
        /// </summary>
        public bool IsComplete => Phase == RegrowthPhase.Complete;

        /// <summary>
        /// Gets the overall progress percentage (0-100) across all phases.
        /// </summary>
        public float OverallProgressPercent
        {
            get
            {
                float phaseContribution = Phase switch
                {
                    RegrowthPhase.InitialFormation => 0f,
                    RegrowthPhase.TissueDevelopment => 25f,
                    RegrowthPhase.NerveIntegration => 50f,
                    RegrowthPhase.FunctionalCompletion => 75f,
                    RegrowthPhase.Complete => 100f,
                    _ => 0f
                };

                if (Phase == RegrowthPhase.Complete)
                    return 100f;

                // Each phase contributes 25%, progress within phase adds to that
                return phaseContribution + (Progress * 25f);
            }
        }

        /// <summary>
        /// Resets progress to zero while keeping current phase.
        /// </summary>
        public void ResetProgress()
        {
            Progress = 0f;
        }

        /// <summary>
        /// Resets to initial state.
        /// </summary>
        public void Reset()
        {
            Phase = RegrowthPhase.InitialFormation;
            Progress = 0f;
        }

        /// <summary>
        /// Validates and clamps progress to valid range.
        /// </summary>
        /// <returns>True if progress was valid, false if it was clamped.</returns>
        public bool ValidateProgress()
        {
            if (Progress < 0f || Progress > 1f || float.IsNaN(Progress) || float.IsInfinity(Progress))
            {
                Progress = System.Math.Max(0f, System.Math.Min(1f, Progress));
                if (float.IsNaN(Progress) || float.IsInfinity(Progress))
                    Progress = 0f;
                return false;
            }
            return true;
        }

        /// <summary>
        /// Gets display string for current state.
        /// </summary>
        public string GetDisplayString()
        {
            if (Phase == RegrowthPhase.Complete)
                return "Complete";

            string phaseName = Phase switch
            {
                RegrowthPhase.InitialFormation => "Initial Formation",
                RegrowthPhase.TissueDevelopment => "Tissue Development",
                RegrowthPhase.NerveIntegration => "Nerve Integration",
                RegrowthPhase.FunctionalCompletion => "Functional Completion",
                _ => "Unknown"
            };

            return $"{phaseName}: {Progress:P0}";
        }

        /// <summary>
        /// Serializes and deserializes the regrowth state.
        /// </summary>
        public void ExposeData()
        {
            // Use local variables since properties cannot be passed by ref
            var phase = Phase;
            var progress = Progress;
            Scribe_Values.Look(ref phase, "phase", RegrowthPhase.InitialFormation);
            Scribe_Values.Look(ref progress, "progress", 0f);
            Phase = phase;
            Progress = progress;
        }

        // Allow implicit conversion from tuple for convenience
        public static implicit operator RegrowthPartState((RegrowthPhase phase, float progress) tuple)
        {
            return new RegrowthPartState(tuple.phase, tuple.progress);
        }
    }
}
