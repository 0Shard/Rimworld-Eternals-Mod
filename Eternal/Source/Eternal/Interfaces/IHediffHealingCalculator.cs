// Relative Path: Eternal/Source/Eternal/Interfaces/IHediffHealingCalculator.cs
// Creation Date: 29-12-2025
// Last Edit: 29-12-2025
// Author: 0Shard
// Description: Interface for complex hediff-specific healing calculations.
//              Handles per-hediff rates, stage-based multipliers, and severity scaling.
//              Used by both living pawn healing (EternalHediffHealer) and corpse healing.

using Verse;

namespace Eternal.Interfaces
{
    /// <summary>
    /// Calculates healing amounts for specific hediffs with full complexity support.
    /// Unlike IHealingRateCalculator (simple regrowth/corpse healing), this interface
    /// handles the sophisticated healing logic for individual hediffs.
    /// </summary>
    /// <remarks>
    /// Key features:
    /// - Per-hediff custom healing rates via EternalHediffSetting
    /// - Stage-based speed multipliers (diseases heal slower at higher stages)
    /// - Severity scaling based on body part HP or hediff maxSeverity
    ///
    /// Used by both:
    /// - EternalHediffHealer (living pawns)
    /// - EternalCorpseHealingProcessor (dead pawns during resurrection)
    /// </remarks>
    public interface IHediffHealingCalculator
    {
        /// <summary>
        /// Calculates the healing amount for a specific hediff.
        /// Formula: effectiveRate × stageMultiplier × bodySize × severityScaling
        /// </summary>
        /// <param name="pawn">The pawn being healed</param>
        /// <param name="hediff">The hediff to heal</param>
        /// <param name="setting">Optional hediff-specific settings (null uses defaults)</param>
        /// <returns>Healing amount to apply this tick</returns>
        float CalculateHediffHealing(Pawn pawn, Hediff hediff, EternalHediffSetting setting);

        /// <summary>
        /// Gets the effective healing rate for a hediff.
        /// Uses per-hediff override if available, otherwise global baseHealingRate.
        /// </summary>
        /// <param name="setting">Hediff-specific settings (null uses global rate)</param>
        /// <returns>Effective healing rate</returns>
        float GetEffectiveRate(EternalHediffSetting setting);

        /// <summary>
        /// Gets the stage-based speed multiplier for debuff hediffs.
        /// Returns 1.0 for injuries, scars, and regrowth (constant speed).
        /// Returns 1.0→0.2 for diseases/infections based on stage (higher = slower).
        /// </summary>
        /// <param name="hediff">The hediff to check</param>
        /// <returns>Speed multiplier (1.0 = normal, lower = slower)</returns>
        float GetStageMultiplier(Hediff hediff);

        /// <summary>
        /// Gets the severity scaling factor for a hediff.
        /// For injuries: uses body part max HP (accounts for pawn health scale).
        /// For diseases/infections: uses hediff def maxSeverity.
        /// Returns 1.0 for hediffs without clear max severity.
        /// </summary>
        /// <param name="hediff">The hediff to check</param>
        /// <param name="pawn">The pawn (needed for body part HP lookup)</param>
        /// <returns>Severity scaling factor</returns>
        float GetSeverityScaling(Hediff hediff, Pawn pawn);

        /// <summary>
        /// Gets body size scaling for a pawn.
        /// Larger pawns heal faster to compensate for larger body parts.
        /// </summary>
        /// <param name="pawn">The pawn to check</param>
        /// <returns>Body size value (default 1.0)</returns>
        float GetBodySizeScaling(Pawn pawn);
    }
}
