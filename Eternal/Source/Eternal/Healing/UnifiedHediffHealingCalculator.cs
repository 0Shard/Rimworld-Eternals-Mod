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
using Eternal.Extensions;
using Eternal.Interfaces;

namespace Eternal.Healing
{
    /// <summary>
    /// Unified hediff healing calculator.
    /// Keeps game inputs and guards at the boundary of the shared scalar healing policy.
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
        public const float DEBUFF_RATE_FACTOR = HediffHealingPolicy.DebuffRateFactor;

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
            {
                return 0f;
            }

            // Formula: effectiveRate × stageMultiplier × bodySize × debuffRateFactor
            float effectiveRate = GetEffectiveRate(setting);
            float stageMultiplier = GetStageMultiplier(hediff);
            float bodySize = GetBodySizeScaling(pawn);
            return HediffHealingPolicy.Calculate(effectiveRate, stageMultiplier, bodySize, hediff.IsDebuffWithStages());
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
            {
                return 1.0f;
            }

            // Only debuff hediffs with stages get the penalty
            // Uses extension method from HediffExtensions for consistency
            if (!hediff.IsDebuffWithStages())
            {
                return 1.0f; // Injuries, scars, regrowth = constant speed
            }

            // Stage-based speed: higher stage = slower healing
            int stageIndex = hediff.CurStageIndex;
            return HediffHealingPolicy.StageMultiplier(stageIndex);
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
            return HediffHealingPolicy.SeverityScaling;
        }

        /// <inheritdoc/>
        public float GetBodySizeScaling(Pawn pawn)
        {
            return HediffHealingPolicy.BodySize(pawn?.BodySize);
        }

    }
}
