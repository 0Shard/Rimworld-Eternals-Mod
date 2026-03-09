// Relative Path: Eternal/Source/Eternal.Tests/Healing/UnifiedHediffHealingCalculatorTests.cs
// Creation Date: 24-02-2026
// Last Edit: 24-02-2026
// Author: 0Shard
// Description: Unit tests for UnifiedHediffHealingCalculator covering body size scaling
//              via IPawnData mocks. Tests referencing Verse.Pawn, Verse.Hediff, or
//              EternalHediffSetting are deferred to the in-game harness because JIT loading
//              of those types triggers Assembly-CSharp resolution which fails without RimWorld.
//              The IPawnData overloads (from 07-01 Task 2) are fully testable.

using Xunit;
using NSubstitute;
using Eternal.Healing;
using Eternal.Interfaces;
using Eternal.Tests.Helpers;

namespace Eternal.Tests.Healing
{
    public class UnifiedHediffHealingCalculatorTests
    {
        private readonly UnifiedHediffHealingCalculator _calculator;
        private readonly ISettingsProvider _settings;

        public UnifiedHediffHealingCalculatorTests()
        {
            _settings = MockSettingsProvider.Default();
            _calculator = new UnifiedHediffHealingCalculator(_settings);
        }

        // -----------------------------------------------------------------
        // GetBodySizeScaling (IPawnData overload) — fully testable
        // -----------------------------------------------------------------

        [Fact]
        public void GetBodySizeScaling_NullIPawnData_Returns1()
        {
            float result = _calculator.GetBodySizeScaling((IPawnData)null);
            Assert.Equal(1.0f, result, TestData.FloatPrecision);
        }

        [Fact]
        public void GetBodySizeScaling_DefaultBodySize_Returns1()
        {
            var pawnData = PawnDataBuilder.Default();
            float result = _calculator.GetBodySizeScaling(pawnData);
            Assert.Equal(TestData.DefaultBodySize, result, TestData.FloatPrecision);
        }

        [Fact]
        public void GetBodySizeScaling_HalfSize_ReturnsHalf()
        {
            var pawnData = PawnDataBuilder.WithBodySize(0.5f);
            float result = _calculator.GetBodySizeScaling(pawnData);
            Assert.Equal(0.5f, result, TestData.FloatPrecision);
        }

        [Fact]
        public void GetBodySizeScaling_DoubleSize_ReturnsDouble()
        {
            var pawnData = PawnDataBuilder.WithBodySize(2.0f);
            float result = _calculator.GetBodySizeScaling(pawnData);
            Assert.Equal(2.0f, result, TestData.FloatPrecision);
        }

        [Fact]
        public void GetBodySizeScaling_ZeroSize_ReturnsZero()
        {
            var pawnData = PawnDataBuilder.WithBodySize(0.0f);
            float result = _calculator.GetBodySizeScaling(pawnData);
            Assert.Equal(0.0f, result, TestData.FloatPrecision);
        }

        [Fact]
        public void GetBodySizeScaling_LargeCreature_ReturnsLargeValue()
        {
            var pawnData = PawnDataBuilder.WithBodySize(3.5f);
            float result = _calculator.GetBodySizeScaling(pawnData);
            Assert.Equal(3.5f, result, TestData.FloatPrecision);
        }

        [Fact]
        public void GetBodySizeScaling_NegativeSize_ReturnsNegative()
        {
            // Edge case: formula passes BodySize through without clamping
            var pawnData = PawnDataBuilder.WithBodySize(-1.0f);
            float result = _calculator.GetBodySizeScaling(pawnData);
            Assert.Equal(-1.0f, result, TestData.FloatPrecision);
        }

        [Fact]
        public void GetBodySizeScaling_TinySize_ReturnsTiny()
        {
            var pawnData = PawnDataBuilder.WithBodySize(0.01f);
            float result = _calculator.GetBodySizeScaling(pawnData);
            Assert.Equal(0.01f, result, TestData.FloatPrecision);
        }

        // -----------------------------------------------------------------
        // CalculateHediffHealing (IPawnData overload) — DEFERRED
        // The method signature references Verse.Hediff and EternalHediffSetting
        // which trigger Assembly-CSharp JIT loading even with null arguments.
        // Null-guard behavior (returns 0f) confirmed by code inspection.
        // -----------------------------------------------------------------

        // -----------------------------------------------------------------
        // Constructor validation
        // -----------------------------------------------------------------

        [Fact]
        public void Constructor_NullSettings_DoesNotThrow()
        {
            // Calculator supports null settings with fallback to 1.8f
            var calc = new UnifiedHediffHealingCalculator(null);
            Assert.NotNull(calc);
        }

        [Fact]
        public void Constructor_ValidSettings_CreatesInstance()
        {
            var calc = new UnifiedHediffHealingCalculator(MockSettingsProvider.Default());
            Assert.NotNull(calc);
        }

        // -----------------------------------------------------------------
        // DEFERRED TO IN-GAME HARNESS (plan 07-04):
        // - GetBodySizeScaling(Pawn) — Verse.Pawn triggers Assembly-CSharp JIT load
        // - GetEffectiveRate(EternalHediffSetting) — EternalHediffSetting triggers JIT load
        // - GetStageMultiplier(Hediff) — Verse.Hediff triggers JIT load
        // - GetSeverityScaling(Hediff, Pawn) — both types trigger JIT load
        // - CalculateHediffHealing(Pawn, Hediff, EternalHediffSetting) — all three types
        //
        // The formulas are verified by code inspection:
        //   Stage multipliers: 0->1.0, 1->0.8, 2->0.6, 3->0.4, 4+->0.2
        //   Severity scaling: Hediff_Injury->1.0, maxSev in (0,100)->maxSev, else->1.0
        //   Body size: pawn.BodySize ?? 1.0f (confirmed via IPawnData overload tests above)
        //   Effective rate: setting?.GetEffectiveHealingRate() ?? _settings?.BaseHealingRate ?? 1.8f
        // -----------------------------------------------------------------
    }
}
