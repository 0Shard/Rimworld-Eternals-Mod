using Eternal.Healing.Scars;
using Xunit;

namespace Eternal.Tests.Healing
{
    public class ScarHealingTimingTests
    {
        [Fact]
        public void GlobalScarPassRunsOnItsRareDeadlineWithoutAMapNormalTrigger()
        {
            var timing = ScarHealingTiming.Calculate(0, 0, 0, null, 1f, 0, 1.2f, 60, 250);
            Assert.Equal(500L, timing.RemainingTicks);
        }

        [Fact]
        public void DefaultSnapshotCannotInventAnEstimate()
        {
            Assert.False(default(ScarHealingTimingResult).HasEstimate);
        }

        [Theory]
        [InlineData(250, 2500L, 2250L, 288d)]
        [InlineData(120, 1200L, 1080L, 600d)]
        public void FreshScarPaysInitializationPassBeforeFullRateParallelHealing(
            int rareRate, long freshTicks, long initializedTicks, double dailyRate)
        {
            var fresh = ScarHealingTiming.Calculate(0, 0, 0, null, 10, 0, 1.2, 60, rareRate);
            int initializationTick = rareRate;
            var tracked = ScarHealingTiming.Calculate(initializationTick, initializationTick,
                initializationTick, initializationTick, 10, 0, 1.2, 60, rareRate);

            Assert.True(fresh.HasEstimate);
            Assert.Equal(ScarHealingTimingState.AwaitingInitialization, fresh.State);
            Assert.False(fresh.IsTracked);
            Assert.Equal(freshTicks, fresh.RemainingTicks);
            Assert.Equal(initializedTicks, tracked.RemainingTicks);
            Assert.Equal(ScarHealingTimingState.Estimated, tracked.State);
            Assert.True(tracked.IsTracked);
            Assert.Equal(dailyRate, tracked.SeverityPerDay, 6);
        }

        [Theory]
        [InlineData(301, 250, 250, 199L)]
        [InlineData(499, 250, 250, 1L)]
        [InlineData(500, 500, 500, 250L)]
        [InlineData(650, 500, 250, 100L)]
        public void PendingElapsedWorkWaitsForNextActualPass(int now, int normal, int scar, long expected)
        {
            var timing = ScarHealingTiming.Calculate(now, normal, scar, 300, 0.1, 0, 1.2, 60, 250);
            Assert.Equal(expected, timing.RemainingTicks);
            Assert.True(timing.RemainingTicks > 0);
        }

        [Fact]
        public void OverdueNormalPassResetsItsPhaseOnNextGameTick()
        {
            var timing = ScarHealingTiming.Calculate(1000, 300, 300, 300, 1, 0, 1.2, 60, 250);
            Assert.Equal(1L, timing.RemainingTicks);
        }

        [Fact]
        public void NormalPhaseAndScarGateHaveIndependentAnchors()
        {
            // Rare passes and the scar gate retain their own saved anchors.
            var timing = ScarHealingTiming.Calculate(310, 300, 300, 300, 0.1, 0, 1.2, 60, 250);
            Assert.Equal(240L, timing.RemainingTicks);
        }

        [Fact]
        public void SettingsChangesReprojectCurrentRateAndScheduleWithoutDiscardingElapsedWork()
        {
            var original = ScarHealingTiming.Calculate(500, 480, 300, 300, 10, 0, 1.2, 60, 250);
            var faster = ScarHealingTiming.Calculate(500, 480, 300, 300, 10, 0, 2.4, 60, 250);
            var shorterGate = ScarHealingTiming.Calculate(500, 480, 300, 300, 10, 0, 1.2, 60, 120);
            Assert.Equal(1980L, original.RemainingTicks);
            Assert.Equal(980L, faster.RemainingTicks);
            Assert.Equal(820L, shorterGate.RemainingTicks);
        }

        [Fact]
        public void SeparateRecordsRetainTheirIndividualElapsedTime()
        {
            var older = ScarHealingTiming.Calculate(650, 600, 600, 300, 2, 0, 1.2, 60, 250);
            var newer = ScarHealingTiming.Calculate(650, 600, 600, 600, 2, 0, 1.2, 60, 250);
            Assert.Equal(200L, older.RemainingTicks);
            Assert.Equal(450L, newer.RemainingTicks);
        }

        [Theory]
        [InlineData(12.96f, 2750L)]
        [InlineData(43.2f, 9000L)]
        public void ExactFloatSeverityWritesDetermineCompletionPass(float severity, long expected)
        {
            var timing = ScarHealingTiming.Calculate(300, 300, 300, 300, severity, 0, 1.2f, 60, 250);
            Assert.True(timing.HasEstimate);
            Assert.Equal(expected, timing.RemainingTicks);

            var fresh = ScarHealingTiming.Calculate(0, 0, 0, null, severity, 0, 1.2f, 60, 250);
            Assert.Equal(expected + 250, fresh.RemainingTicks);
        }

        [Theory]
        [InlineData(1.1999999284744263f, 250L)]
        [InlineData(1.2000000476837158f, 250L)]
        [InlineData(1.2000001668930054f, 500L)]
        public void AdjacentFloatsAroundActualHealingAmountKeepDistinctCompletionPasses(float severity, long expected)
        {
            Assert.Equal(1.440000057220459f, ScarHealingTiming.CalculateHealingAmount(300, 1.2f, 250));
            var timing = ScarHealingTiming.Calculate(300, 300, 300, 300, severity, 0, 1.2f, 60, 250);
            Assert.Equal(expected, timing.RemainingTicks);
        }

        [Fact]
        public void FirstProjectedPassUsesPendingDeltaRatherThanSteadyCadence()
        {
            float pendingAmount = ScarHealingTiming.CalculateHealingAmount(900, 1.2f, 250);
            var timing = ScarHealingTiming.Calculate(900, 900, 900, 300, pendingAmount, 0, 1.2f, 60, 250);
            Assert.Equal(500L, timing.RemainingTicks);
        }

        [Theory]
        [InlineData(1e30f, 1.2f)]
        [InlineData(1f, float.Epsilon)]
        public void FloatSubtractionThatCannotLowerSeverityHasNoFiniteEstimate(float severity, float rate)
        {
            var timing = ScarHealingTiming.Calculate(300, 300, 300, 300, severity, 0, rate, 60, 250);
            Assert.False(timing.HasEstimate);
            Assert.Equal(ScarHealingTimingState.NoProgress, timing.State);
        }

        [Fact]
        public void ShortFirstPassCannotDeclareNoProgressWhenSteadyPassWouldProgress()
        {
            var timing = ScarHealingTiming.Calculate(300, 300, 0, 300, 16777216f, 0, 1.2f, 60, 250);
            Assert.Equal(ScarHealingTimingState.ProjectionLimit, timing.State);
        }

        [Fact]
        public void ProjectionCompletesAtTheBoundAndReportsUnavailableBeyondIt()
        {
            int limit = ScarHealingTiming.MaximumProjectionPasses;
            var atLimit = ScarHealingTiming.Calculate(0, 0, 0, 0, limit, 0, 1, 60, 60);
            var freshAtLimit = ScarHealingTiming.Calculate(0, 0, 0, null, limit, 0, 1, 60, 60);
            var beyondLimit = ScarHealingTiming.Calculate(0, 0, 0, 0, limit + 1, 0, 1, 60, 60);
            Assert.True(atLimit.HasEstimate);
            Assert.Equal(limit * 60L, atLimit.RemainingTicks);
            Assert.Equal((limit + 1) * 60L, freshAtLimit.RemainingTicks);
            Assert.False(beyondLimit.HasEstimate);
            Assert.Equal(ScarHealingTimingState.ProjectionLimit, beyondLimit.State);
        }

        [Fact]
        public void FuturePassOutsideGameTickRangeHasNoExactEstimate()
        {
            var timing = ScarHealingTiming.Calculate(int.MaxValue, int.MaxValue, int.MaxValue,
                int.MaxValue, 1, 0, 1.2f, 60, 250);
            Assert.False(timing.HasEstimate);
            Assert.Equal(ScarHealingTimingState.ProjectionLimit, timing.State);
        }

        [Fact]
        public void PositiveSeverityFloorDoesNotPromiseRemoval()
        {
            var timing = ScarHealingTiming.Calculate(0, 0, 0, null, 10, 0.01, 1.2, 60, 250);
            Assert.False(timing.HasEstimate);
            Assert.Equal(ScarHealingTimingState.PositiveSeverityFloor, timing.State);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        public void InvalidRateIsUnavailable(double rate)
        {
            AssertInvalid(ScarHealingTiming.Calculate(0, 0, 0, null, 10, 0, rate, 60, 250));
        }

        [Theory]
        [InlineData(0, 250)]
        [InlineData(-60, 250)]
        [InlineData(60, 0)]
        [InlineData(60, -250)]
        public void InvalidIntervalsAreUnavailable(int normal, int rare)
        {
            AssertInvalid(ScarHealingTiming.Calculate(0, 0, 0, null, 10, 0, 1.2, normal, rare));
        }

        [Fact]
        public void InvalidSeverityFutureAnchorsAndOverflowAreUnavailable()
        {
            AssertInvalid(ScarHealingTiming.Calculate(0, 0, 0, null, double.NaN, 0, 1.2, 60, 250));
            AssertInvalid(ScarHealingTiming.Calculate(0, 0, 0, null, -1, 0, 1.2, 60, 250));
            AssertInvalid(ScarHealingTiming.Calculate(10, 11, 0, null, 10, 0, 1.2, 60, 250));
            AssertInvalid(ScarHealingTiming.Calculate(10, 0, 11, null, 10, 0, 1.2, 60, 250));
            AssertInvalid(ScarHealingTiming.Calculate(10, 0, 0, 11, 10, 0, 1.2, 60, 250));
            AssertInvalid(ScarHealingTiming.Calculate(0, 0, 0, null, double.MaxValue, 0, 1.2, 60, 250));
        }

        [Theory]
        [InlineData(ScarHealingTimingState.Disabled)]
        [InlineData(ScarHealingTimingState.PawnUnavailable)]
        [InlineData(ScarHealingTimingState.SchedulerUnavailable)]
        [InlineData(ScarHealingTimingState.NoMapTrigger)]
        [InlineData(ScarHealingTimingState.Ineligible)]
        [InlineData(ScarHealingTimingState.NoProgress)]
        [InlineData(ScarHealingTimingState.ProjectionLimit)]
        public void UnavailableStatesHaveNoFinitePromise(ScarHealingTimingState state)
        {
            var timing = ScarHealingTimingResult.Unavailable(state);
            Assert.False(timing.HasEstimate);
            Assert.True(double.IsNaN(timing.SeverityPerDay));
        }

        private static void AssertInvalid(ScarHealingTimingResult timing)
        {
            Assert.False(timing.HasEstimate);
            Assert.Equal(ScarHealingTimingState.InvalidSettings, timing.State);
        }
    }
}
