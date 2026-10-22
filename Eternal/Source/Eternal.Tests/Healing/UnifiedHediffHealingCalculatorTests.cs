// Author: 0Shard
// Description: Executable scalar assertions; real Pawn/settings/constructor tests live in the Debug host.
using Eternal.Healing;
using Xunit;

namespace Eternal.Tests.Healing
{
    public class UnifiedHediffHealingCalculatorTests
    {
        [Theory]
        [InlineData(0, 1f)]
        [InlineData(1, 0.8f)]
        [InlineData(2, 0.6f)]
        [InlineData(3, 0.4f)]
        [InlineData(4, 0.2f)]
        [InlineData(20, 0.2f)]
        [InlineData(-1, 0.2f)]
        public void StageMultiplier_PreservesExistingTable(int stage, float expected)
            => Assert.Equal(expected, HediffHealingPolicy.StageMultiplier(stage));

        [Theory]
        [InlineData(null, 1f)]
        [InlineData(1f, 1f)]
        [InlineData(0.5f, 0.5f)]
        [InlineData(2f, 2f)]
        [InlineData(0f, 0f)]
        [InlineData(3.5f, 3.5f)]
        [InlineData(-1f, -1f)]
        [InlineData(0.01f, 0.01f)]
        public void BodySize_PreservesNullableFallbackAndUnclampedValues(float? size, float expected)
            => Assert.Equal(expected, HediffHealingPolicy.BodySize(size));

        [Fact]
        public void StagedDebuff_UsesIndependentRateStageAndBodyExpectation()
            => Assert.Equal(0.0192f, HediffHealingPolicy.Calculate(1.2f,
                HediffHealingPolicy.StageMultiplier(1), 2f, true), 7);

        [Fact]
        public void Injury_HasNoDebuffPenalty()
            => Assert.Equal(0.6f, HediffHealingPolicy.Calculate(1.2f, 1f, 0.5f, false), 7);

        [Fact]
        public void NondefaultRate_IsMultipliedWithoutNormalization()
            => Assert.Equal(0.0036f, HediffHealingPolicy.Calculate(0.3f, 0.6f, 2f, true), 7);

        [Fact]
        public void SeverityScaling_RemainsFlat() => Assert.Equal(1f, HediffHealingPolicy.SeverityScaling);
    }
}
