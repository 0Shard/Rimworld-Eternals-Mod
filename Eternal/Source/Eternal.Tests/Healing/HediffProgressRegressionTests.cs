using Xunit;

namespace Eternal.Tests.Healing
{
    public class HediffProgressRegressionTests
    {
        [Theory]
        [InlineData(0.0099f, 0.00987f, 0f)]
        [InlineData(0.00109f, 0.00106f, 0.001f)]
        [InlineData(0.00000002f, 0.00000001f, 0f)]
        public void EveryRepresentableDecreaseAboveFloorIsProgress(float before, float after, float floor)
            => Assert.False(HediffProgressPolicy.IsFailedWrite(before, after, before - after, floor));

        [Fact]
        public void BlockedSetterStillCountsAsAFailedWrite()
            => Assert.True(HediffProgressPolicy.IsFailedWrite(0.0099f, 0.0099f, 0.00003f, 0f));

        [Fact]
        public void NoRequestedHealingDoesNotCountAsAFailedWrite()
            => Assert.False(HediffProgressPolicy.IsFailedWrite(0.0099f, 0.0099f, 0f, 0f));
    }
}
