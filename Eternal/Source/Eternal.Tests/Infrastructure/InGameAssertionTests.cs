// Relative Path: Eternal/Source/Eternal.Tests/Infrastructure/InGameAssertionTests.cs
// Author: 0Shard
// Description: Execute the primitive Debug assertion helpers without a game host.
#if DEBUG
using Eternal.InGameTests;
using Xunit;

namespace Eternal.Tests.Infrastructure
{
    public class InGameAssertionTests
    {
        [Theory]
        [InlineData(float.NaN)]
        [InlineData(float.PositiveInfinity)]
        [InlineData(float.NegativeInfinity)]
        public void NonfiniteInputsCannotPassNumericAssertions(float value)
        {
            Assert.Throws<TestFailedException>(() => TestAssert.GreaterThan(value, 0f, "actual"));
            Assert.Throws<TestFailedException>(() => TestAssert.LessThanOrEqual(value, 0f, "actual"));
            Assert.Throws<TestFailedException>(() => TestAssert.InRange(value, -1f, 1f, "actual"));
            Assert.Throws<TestFailedException>(() => TestAssert.AreEqual(value, value, "equal nonfinite"));
            Assert.Throws<TestFailedException>(() => TestAssert.GreaterThan(1f, value, "bound"));
            Assert.Throws<TestFailedException>(() => TestAssert.LessThanOrEqual(1f, value, "bound"));
            Assert.Throws<TestFailedException>(() => TestAssert.InRange(0f, value, 1f, "lower bound"));
        }

        [Fact]
        public void FiniteBoundariesAndInvalidRangesAreExplicit()
        {
            TestAssert.GreaterThan(1f, 0f, "finite");
            TestAssert.LessThanOrEqual(1f, 1f, "inclusive");
            TestAssert.InRange(1f, 1f, 1f, "inclusive");
            Assert.Throws<TestFailedException>(() => TestAssert.InRange(1f, 2f, 0f, "reversed"));
            Assert.Throws<TestFailedException>(() => TestAssert.GreaterThan(1f, 1f, "strict"));
            Assert.Throws<TestFailedException>(() => TestAssert.AreEqual(double.NaN, double.NaN, "nonfinite double"));
        }

        [Fact]
        public void MissingPrerequisiteThrowsNamedUnavailableOutcome()
        {
            var failure = Assert.Throws<TestUnavailableException>(() => TestAssert.Available(null, "RequiredFoodNeed"));
            Assert.Contains("RequiredFoodNeed", failure.Message);
            TestAssert.Available(new object(), "Present");
        }
    }
}
#endif
