using System;

namespace Eternal.Healing.Scars
{
    public enum ScarHealingTimingState
    {
        SchedulerUnavailable,
        Estimated,
        AwaitingInitialization,
        Disabled,
        PawnUnavailable,
        NoMapTrigger,
        Ineligible,
        InvalidSettings,
        PositiveSeverityFloor,
        ProjectionLimit,
        NoProgress
    }

    public readonly struct ScarHealingTimingResult
    {
        public ScarHealingTimingState State { get; }
        public bool HasEstimate => State == ScarHealingTimingState.Estimated
            || State == ScarHealingTimingState.AwaitingInitialization;
        public long RemainingTicks { get; }
        public double SeverityPerDay { get; }
        public bool IsTracked { get; }

        internal ScarHealingTimingResult(ScarHealingTimingState state, long remainingTicks,
            double severityPerDay, bool isTracked)
        {
            State = state;
            RemainingTicks = remainingTicks;
            SeverityPerDay = severityPerDay;
            IsTracked = isTracked;
        }

        public static ScarHealingTimingResult Unavailable(ScarHealingTimingState state)
            => new ScarHealingTimingResult(state, 0, double.NaN, false);
    }

    /// <summary>
    /// Read-only projection of the native rare-pass scheduler and global scar gate.
    /// A newly discovered scar spends its first pass initializing its unsaved record.
    /// </summary>
    public static class ScarHealingTiming
    {
        // UI previews run on repaint. Bound their work independently of save contents.
        public const int MaximumProjectionPasses = 4096;

        public static float CalculateHealingAmount(float elapsedTicks, float baseHealingRate, int rareTickRate)
            => baseHealingRate * (elapsedTicks / rareTickRate);

        public static ScarHealingTimingResult Calculate(int currentTick, int lastRareHealTick,
            int lastProcessingTick, int? lastHealedTick, double severity, double minimumSeverity,
            double baseHealingRate, int normalTickRate, int rareTickRate)
        {
            float remainingSeverity = (float)severity;
            float healingRate = (float)baseHealingRate;
            if (!IsFinite(severity) || severity < 0 || !IsFinite(remainingSeverity) || !IsFinite(minimumSeverity)
                || !IsFinite(baseHealingRate) || baseHealingRate <= 0
                || !IsFinite(healingRate) || healingRate <= 0
                || normalTickRate <= 0 || rareTickRate <= 0 || currentTick < 0
                || lastRareHealTick < 0 || lastRareHealTick > currentTick
                || lastProcessingTick < 0 || lastProcessingTick > currentTick
                || (lastHealedTick.HasValue && (lastHealedTick.Value < 0 || lastHealedTick.Value > currentTick)))
            {
                return ScarHealingTimingResult.Unavailable(ScarHealingTimingState.InvalidSettings);
            }

            if (minimumSeverity > 0)
            {
                return ScarHealingTimingResult.Unavailable(ScarHealingTimingState.PositiveSeverityFloor);
            }

            long cadence = rareTickRate;
            // The scheduler input is its rare-pass stamp. An overdue pass runs next tick.
            long nextRare = Math.Max((long)currentTick + 1, (long)lastRareHealTick + rareTickRate);
            long untilGate = Math.Max(0, (long)lastProcessingTick + rareTickRate - nextRare);
            long firstPass = nextRare + rareTickRate * ((untilGate + rareTickRate - 1) / rareTickRate);
            double severityPerDay = baseHealingRate / rareTickRate * 60000d;
            if (!IsFinite(severityPerDay))
            {
                return ScarHealingTimingResult.Unavailable(ScarHealingTimingState.InvalidSettings);
            }

            // Continuous-work division disagrees with the live path near float boundaries:
            // both each healing amount and each severity write round to a game float.
            long scheduledTick = lastHealedTick.HasValue ? firstPass : firstPass + cadence;
            long previousHealTick = lastHealedTick ?? firstPass;
            float steadyHealingAmount = CalculateHealingAmount(cadence, healingRate, rareTickRate);
            for (int pass = 0; pass < MaximumProjectionPasses; pass++)
            {
                if (scheduledTick > int.MaxValue)
                {
                    return ScarHealingTimingResult.Unavailable(ScarHealingTimingState.ProjectionLimit);
                }

                float healingAmount = CalculateHealingAmount(scheduledTick - previousHealTick, healingRate, rareTickRate);
                float nextSeverity = remainingSeverity - healingAmount;
                if (nextSeverity <= 0)
                {
                    return new ScarHealingTimingResult(lastHealedTick.HasValue
                            ? ScarHealingTimingState.Estimated : ScarHealingTimingState.AwaitingInitialization,
                        scheduledTick - currentTick, severityPerDay, lastHealedTick.HasValue);
                }
                if (nextSeverity >= remainingSeverity && nextSeverity - steadyHealingAmount >= nextSeverity)
                {
                    return ScarHealingTimingResult.Unavailable(ScarHealingTimingState.NoProgress);
                }

                remainingSeverity = nextSeverity;
                previousHealTick = scheduledTick;
                scheduledTick += cadence;
            }

            return ScarHealingTimingResult.Unavailable(ScarHealingTimingState.ProjectionLimit);
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
