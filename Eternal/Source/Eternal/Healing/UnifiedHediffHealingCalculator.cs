// Relative Path: Eternal/Source/Eternal/Healing/UnifiedHediffHealingCalculator.cs
// Creation Date: 29-12-2025
// Last Edit: 13-01-2026
// Author: 0Shard
// Description: Unified implementation of hediff-specific healing calculations.
//              Consolidates logic from EternalHediffHealer and NutritionExtensions.
//              Handles per-hediff rates, stage multipliers, and severity scaling.

using Verse;
using Eternal.Compat;
using Eternal.DI;
using Eternal.Extensions;
using Eternal.Interfaces;

namespace Eternal.Healing
{
    /// <summary>
    /// Unified hediff healing calculator.
    /// Consolidates complex healing logic that was previously scattered across:
    /// - EternalHediffHealer.HealHediff() (lines 163-168)
    /// - EternalHediffHealer.GetEffectiveMaxSeverity() (lines 219-236)
    /// - NutritionExtensions.GetStageBasedHealingSpeed() (lines 59-78)
    /// </summary>
    public class UnifiedHediffHealingCalculator : IHediffHealingCalculator
    {
        private readonly ISettingsProvider _settings;

        /// <summary>
        /// Creates a new unified hediff healing calculator.
        /// </summary>
        /// <param name="settings">Settings provider for base healing rate</param>
        public UnifiedHediffHealingCalculator(ISettingsProvider settings)
        {
            _settings = settings;
        }

        /// <inheritdoc/>
        public float CalculateHediffHealing(Pawn pawn, Hediff hediff, EternalHediffSetting setting)
        {
            if (pawn == null || hediff == null)
                return 0f;

            // Formula: effectiveRate × stageMultiplier × bodySize × severityScaling × autoMultiplier
            float effectiveRate = GetEffectiveRate(setting);
            float stageMultiplier = GetStageMultiplier(hediff);
            float bodySize = GetBodySizeScaling(pawn);
            float severityScaling = GetSeverityScaling(hediff, pawn);
            float autoMultiplier = hediff.GetAutoSeverityMultiplier();

            return effectiveRate * stageMultiplier * bodySize * severityScaling * autoMultiplier;
        }

        /// <inheritdoc/>
        public float GetEffectiveRate(EternalHediffSetting setting)
        {
            // Use per-hediff override if available
            if (setting != null)
            {
                return setting.GetEffectiveHealingRate();
            }

            // Fall back to global baseHealingRate
            return _settings?.BaseHealingRate ?? 1.8f;
        }

        /// <inheritdoc/>
        public float GetStageMultiplier(Hediff hediff)
        {
            if (hediff == null)
                return 1.0f;

            // Only debuff hediffs with stages get the penalty
            // Uses extension method from HediffExtensions for consistency
            if (!hediff.IsDebuffWithStages())
                return 1.0f; // Injuries, scars, regrowth = constant speed

            // Stage-based speed: higher stage = slower healing
            int stageIndex = hediff.CurStageIndex;
            return stageIndex switch
            {
                0 => 1.0f,   // Minor
                1 => 0.8f,   // Moderate
                2 => 0.6f,   // Serious
                3 => 0.4f,   // Severe
                _ => 0.2f    // Extreme (stage 4+)
            };
        }

        /// <inheritdoc/>
        public float GetSeverityScaling(Hediff hediff, Pawn pawn)
        {
            if (hediff == null)
                return 1.0f;

            // Injuries heal at flat rate (no partHP scaling) to match Immortal behavior
            // This makes injury healing speed consistent regardless of body part HP
            if (hediff is Hediff_Injury)
            {
                return 1.0f;
            }

            // For hediffs with defined maxSeverity (not infinite, reasonable range)
            float maxSev = hediff.def.maxSeverity;
            if (!float.IsInfinity(maxSev) && maxSev > 0f && maxSev < 100f)
            {
                return maxSev;
            }

            // Default: no scaling (conditions without clear max severity)
            return 1.0f;
        }

        /// <inheritdoc/>
        public float GetBodySizeScaling(Pawn pawn)
        {
            return pawn?.BodySize ?? 1.0f;
        }

        // -----------------------------------------------------------------
        // IPawnData overloads — testable without RimWorld runtime
        // -----------------------------------------------------------------

        /// <summary>
        /// Testable overload accepting <see cref="IPawnData"/> instead of <c>Pawn</c>.
        /// Production callers use the <c>Pawn</c> overload; unit tests use this one
        /// with NSubstitute mocks via <c>PawnDataBuilder</c>.
        /// </summary>
        public float GetBodySizeScaling(IPawnData pawnData)
        {
            return pawnData?.BodySize ?? 1.0f;
        }

        /// <summary>
        /// Testable overload for <see cref="CalculateHediffHealing"/> accepting <see cref="IPawnData"/>.
        /// Hediff and EternalHediffSetting remain RimWorld types — only the pawn dependency
        /// is replaced for testability of the body-size scaling path.
        /// </summary>
        public float CalculateHediffHealing(IPawnData pawnData, Hediff hediff, EternalHediffSetting setting)
        {
            if (pawnData == null || hediff == null)
                return 0f;

            float effectiveRate = GetEffectiveRate(setting);
            float stageMultiplier = GetStageMultiplier(hediff);
            float bodySize = GetBodySizeScaling(pawnData);
            float severityScaling = GetSeverityScaling(hediff, null);
            float autoMultiplier = hediff.GetAutoSeverityMultiplier();

            return effectiveRate * stageMultiplier * bodySize * severityScaling * autoMultiplier;
        }
    }
}
