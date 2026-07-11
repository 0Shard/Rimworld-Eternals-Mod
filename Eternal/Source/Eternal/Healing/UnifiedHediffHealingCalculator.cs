// Relative Path: Eternal/Source/Eternal/Healing/UnifiedHediffHealingCalculator.cs
// Creation Date: 29-12-2025
// Last Edit: 11-07-2026
// Author: 0Shard
// Description: Unified implementation of hediff-specific healing calculations.
//              Consolidates logic from EternalHediffHealer and NutritionExtensions.
//              Handles per-hediff rates, stage multipliers, and the debuff rate factor.
//              BALANCE: Removed maxSeverity-based severity scaling and the 0.1x low-maxSev
//              auto multiplier. Together they made total heal TIME constant (~500 ticks for
//              every staged debuff, instant for maxSeverity > 1) instead of proportional to
//              severity — diseases healed 10x-200x faster than the Immortals mod reference.
//              Staged debuffs now use DEBUFF_RATE_FACTOR for per-hediff parity with
//              Immortals' slowHealSpeed (0.0002 severity/tick at default settings).

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
        /// <summary>
        /// Rate factor for staged debuffs (diseases, infections, blood loss, parasites).
        /// Calibrated for per-hediff parity with the Immortals mod's disease healing:
        /// Immortals heals 0.002 (base) x 0.1 (slowHealSpeed) = 0.0002 severity/tick;
        /// Eternal fires every normalTickRate (60) ticks at baseHealingRate 1.2, so
        /// 1.2 x 0.01 = 0.012 severity per cycle = 0.0002 severity/tick.
        /// </summary>
        public const float DEBUFF_RATE_FACTOR = 0.01f;

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

            // Formula: effectiveRate × stageMultiplier × bodySize × debuffRateFactor
            float effectiveRate = GetEffectiveRate(setting);
            float stageMultiplier = GetStageMultiplier(hediff);
            float bodySize = GetBodySizeScaling(pawn);
            float debuffRateFactor = hediff.IsDebuffWithStages() ? DEBUFF_RATE_FACTOR : 1f;

            return effectiveRate * stageMultiplier * bodySize * debuffRateFactor;
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
            return _settings?.BaseHealingRate ?? SettingsDefaults.BaseHealingRate;
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
        /// <remarks>
        /// Always 1.0: all hediffs heal at a flat severity rate. The old maxSeverity-based
        /// scaling made total heal time constant instead of proportional to severity
        /// (a maxSeverity-5 disease healed its whole range as fast as a maxSeverity-1 one).
        /// Kept on the interface so callers/UI can still display the factor.
        /// </remarks>
        public float GetSeverityScaling(Hediff hediff, Pawn pawn)
        {
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
            float debuffRateFactor = hediff.IsDebuffWithStages() ? DEBUFF_RATE_FACTOR : 1f;

            return effectiveRate * stageMultiplier * bodySize * debuffRateFactor;
        }
    }
}
