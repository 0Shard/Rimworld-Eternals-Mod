// Relative Path: Eternal/Source/Eternal.Tests/Compatibility/SOS2ReflectionCacheTests.cs
// Creation Date: 24-02-2026
// Last Edit: 24-02-2026
// Author: 0Shard
// Description: Unit tests for SOS2ReflectionCache and BoolLookupResult.
//              Tests requiring Initialize(), IsShipBurningUp(), or IsShipDestroyed() are deferred
//              to the in-game harness because those methods reference Verse.Map/MapParent which
//              triggers Assembly-CSharp loading at JIT time.
//              Constructor validation and BoolLookupResult (pure value type) are fully testable.

using System;
using Xunit;
using Eternal.Compatibility;

namespace Eternal.Tests.Compatibility
{
    public class SOS2ReflectionCacheTests
    {
        // -----------------------------------------------------------------
        // Constructor validation (no RimWorld types touched)
        // -----------------------------------------------------------------

        [Fact]
        public void Constructor_NullResolver_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new SOS2ReflectionCache(null));
        }

        [Fact]
        public void Constructor_ValidResolver_CreatesInstance()
        {
            var cache = new SOS2ReflectionCache(_ => null);
            Assert.NotNull(cache);
        }

        [Fact]
        public void IsInitialized_BeforeInitialize_IsFalse()
        {
            var cache = new SOS2ReflectionCache(_ => null);
            Assert.False(cache.IsInitialized);
        }

        [Fact]
        public void SOS2Present_BeforeInitialize_IsFalse()
        {
            var cache = new SOS2ReflectionCache(_ => null);
            Assert.False(cache.SOS2Present);
        }

        // -----------------------------------------------------------------
        // Initialize / IsShipBurningUp / IsShipDestroyed / IsOrbitingShip
        // DEFERRED to in-game harness — these methods reference Verse.Map,
        // MapParent, and Log.Warning which trigger Assembly-CSharp loading.
        // -----------------------------------------------------------------

        // -----------------------------------------------------------------
        // BoolLookupResult — pure value type, no RimWorld deps
        // -----------------------------------------------------------------

        [Fact]
        public void BoolLookupResult_ModNotPresent_IsSuccessIsFalse()
        {
            var result = BoolLookupResult.ModNotPresent();

            Assert.False(result.IsSuccess);
            Assert.False(result.IsTrue);
            Assert.False(result.Value);
            Assert.Equal(SpaceModLookupStatus.ModNotPresent, result.Status);
        }

        [Fact]
        public void BoolLookupResult_ReflectionFailed_IsSuccessIsFalse()
        {
            var result = BoolLookupResult.ReflectionFailed();

            Assert.False(result.IsSuccess);
            Assert.False(result.IsTrue);
            Assert.False(result.Value);
            Assert.Equal(SpaceModLookupStatus.ReflectionFailed, result.Status);
        }

        [Fact]
        public void BoolLookupResult_SuccessTrue_IsSuccessAndIsTrue()
        {
            var result = BoolLookupResult.SuccessTrue();

            Assert.True(result.IsSuccess);
            Assert.True(result.IsTrue);
            Assert.True(result.Value);
            Assert.Equal(SpaceModLookupStatus.Success, result.Status);
        }

        [Fact]
        public void BoolLookupResult_SuccessFalse_IsSuccessButNotIsTrue()
        {
            var result = BoolLookupResult.SuccessFalse();

            Assert.True(result.IsSuccess);
            Assert.False(result.IsTrue);
            Assert.False(result.Value);
            Assert.Equal(SpaceModLookupStatus.Success, result.Status);
        }

        // -----------------------------------------------------------------
        // SpaceModLookupStatus enum completeness
        // -----------------------------------------------------------------

        [Fact]
        public void SpaceModLookupStatus_HasExactlyThreeValues()
        {
            var values = Enum.GetValues(typeof(SpaceModLookupStatus));
            Assert.Equal(3, values.Length);
        }

        [Theory]
        [InlineData(SpaceModLookupStatus.Success)]
        [InlineData(SpaceModLookupStatus.ModNotPresent)]
        [InlineData(SpaceModLookupStatus.ReflectionFailed)]
        public void SpaceModLookupStatus_AllValuesExist(SpaceModLookupStatus status)
        {
            Assert.True(Enum.IsDefined(typeof(SpaceModLookupStatus), status));
        }
    }
}
