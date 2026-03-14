// Relative Path: Eternal/Source/Eternal.Tests/Infrastructure/ImmutableSettingsSnapshotTests.cs
// Creation Date: 24-02-2026
// Last Edit: 12-03-2026
// Author: 0Shard
// Description: Unit tests for ImmutableSettingsSnapshot readonly record struct.
//              Verifies default construction, initializer round-trip, with-expression independence,
//              value equality, and all 8 section types (including EffectsSection).

using Xunit;
using Eternal.Infrastructure;

namespace Eternal.Tests.Infrastructure
{
    public class ImmutableSettingsSnapshotTests
    {
        // -----------------------------------------------------------------
        // Default construction
        // -----------------------------------------------------------------

        [Fact]
        public void DefaultConstruct_GeneralSection_HasDefaultValues()
        {
            var snapshot = new ImmutableSettingsSnapshot();
            Assert.False(snapshot.General.ModEnabled);
            Assert.False(snapshot.General.DebugMode);
            Assert.Equal(0, snapshot.General.LoggingLevel);
        }

        [Fact]
        public void DefaultConstruct_PerfSection_HasZeroTickRates()
        {
            var snapshot = new ImmutableSettingsSnapshot();
            Assert.Equal(0, snapshot.Perf.NormalTickRate);
            Assert.Equal(0, snapshot.Perf.RareTickRate);
            Assert.Equal(0, snapshot.Perf.TraitCheckInterval);
            Assert.Equal(0, snapshot.Perf.CorpseCheckInterval);
            Assert.Equal(0, snapshot.Perf.MapCheckInterval);
        }

        [Fact]
        public void DefaultConstruct_HealingSection_HasZeroBaseRate()
        {
            var snapshot = new ImmutableSettingsSnapshot();
            Assert.Equal(0f, snapshot.Healing.BaseRate, TestData.FloatPrecision);
        }

        [Fact]
        public void DefaultConstruct_FoodDebtSection_HasZeroMultiplier()
        {
            var snapshot = new ImmutableSettingsSnapshot();
            Assert.Equal(0f, snapshot.FoodDebt.MaxDebtMultiplier, TestData.FloatPrecision);
        }

        // -----------------------------------------------------------------
        // Initializer round-trip
        // -----------------------------------------------------------------

        [Fact]
        public void Initializer_GeneralSection_RoundTripsValues()
        {
            var snapshot = new ImmutableSettingsSnapshot
            {
                General = new ImmutableSettingsSnapshot.GeneralSection
                {
                    ModEnabled = true,
                    DebugMode = false,
                    LoggingLevel = 2
                }
            };

            Assert.True(snapshot.General.ModEnabled);
            Assert.False(snapshot.General.DebugMode);
            Assert.Equal(2, snapshot.General.LoggingLevel);
        }

        [Fact]
        public void Initializer_FoodDebtSection_RoundTripsValues()
        {
            var snapshot = new ImmutableSettingsSnapshot
            {
                FoodDebt = new ImmutableSettingsSnapshot.FoodDebtSection
                {
                    MaxDebtMultiplier = TestData.DefaultMaxDebtMultiplier,
                    FoodDrainThreshold = TestData.DefaultFoodDrainThreshold,
                    MinDebtDrainRate = TestData.DefaultMinDebtDrainRate,
                    MaxDebtDrainRate = TestData.DefaultMaxDebtDrainRate,
                    SeverityToNutritionRatio = TestData.DefaultSeverityToNutritionRatio
                }
            };

            Assert.Equal(TestData.DefaultMaxDebtMultiplier, snapshot.FoodDebt.MaxDebtMultiplier, TestData.FloatPrecision);
            Assert.Equal(TestData.DefaultSeverityToNutritionRatio, snapshot.FoodDebt.SeverityToNutritionRatio, TestData.FloatPrecision);
        }

        [Fact]
        public void Initializer_AllSevenSections_CanBeInitialized()
        {
            var snapshot = new ImmutableSettingsSnapshot
            {
                General = new ImmutableSettingsSnapshot.GeneralSection { ModEnabled = true },
                Healing = new ImmutableSettingsSnapshot.HealingSection { BaseRate = 1.8f },
                Resource = new ImmutableSettingsSnapshot.ResourceSection { NutritionCostMultiplier = 1.0f },
                FoodDebt = new ImmutableSettingsSnapshot.FoodDebtSection { MaxDebtMultiplier = 5.0f },
                Perf = new ImmutableSettingsSnapshot.PerfSection { NormalTickRate = 60 },
                AdvancedHediff = new ImmutableSettingsSnapshot.AdvancedHediffSection { AutoHealEnabled = true },
                Map = new ImmutableSettingsSnapshot.MapSection { EnableMapAnchors = true }
            };

            Assert.True(snapshot.General.ModEnabled);
            Assert.Equal(1.8f, snapshot.Healing.BaseRate, TestData.FloatPrecision);
            Assert.Equal(1.0f, snapshot.Resource.NutritionCostMultiplier, TestData.FloatPrecision);
            Assert.Equal(5.0f, snapshot.FoodDebt.MaxDebtMultiplier, TestData.FloatPrecision);
            Assert.Equal(60, snapshot.Perf.NormalTickRate);
            Assert.True(snapshot.AdvancedHediff.AutoHealEnabled);
            Assert.True(snapshot.Map.EnableMapAnchors);
        }

        [Fact]
        public void Initializer_AllEightSections_CanBeInitialized()
        {
            var snapshot = new ImmutableSettingsSnapshot
            {
                General = new ImmutableSettingsSnapshot.GeneralSection { ModEnabled = true },
                Healing = new ImmutableSettingsSnapshot.HealingSection { BaseRate = 1.8f },
                Resource = new ImmutableSettingsSnapshot.ResourceSection { NutritionCostMultiplier = 1.0f },
                FoodDebt = new ImmutableSettingsSnapshot.FoodDebtSection { MaxDebtMultiplier = 5.0f },
                Perf = new ImmutableSettingsSnapshot.PerfSection { NormalTickRate = 60 },
                AdvancedHediff = new ImmutableSettingsSnapshot.AdvancedHediffSection { AutoHealEnabled = true },
                Map = new ImmutableSettingsSnapshot.MapSection { EnableMapAnchors = true },
                Effects = new ImmutableSettingsSnapshot.EffectsSection
                {
                    ConsciousnessBuffEnabled = true,
                    ConsciousnessMultiplier  = 3.0f,
                    MoodBuffEnabled          = true,
                    MoodBuffValue            = 40,
                    PopulationCapEnabled     = true,
                    PopulationCap            = 3,
                }
            };

            Assert.True(snapshot.Effects.ConsciousnessBuffEnabled);
            Assert.Equal(3.0f, snapshot.Effects.ConsciousnessMultiplier, TestData.FloatPrecision);
            Assert.True(snapshot.Effects.MoodBuffEnabled);
            Assert.Equal(40, snapshot.Effects.MoodBuffValue);
            Assert.True(snapshot.Effects.PopulationCapEnabled);
            Assert.Equal(3, snapshot.Effects.PopulationCap);
        }

        // -----------------------------------------------------------------
        // EffectsSection tests
        // -----------------------------------------------------------------

        [Fact]
        public void EffectsSection_CanBeInitialized_AllFieldsAccessible()
        {
            var section = new ImmutableSettingsSnapshot.EffectsSection
            {
                ConsciousnessBuffEnabled = true,
                ConsciousnessMultiplier  = 5.5f,
                MoodBuffEnabled          = false,
                MoodBuffValue            = 100,
                PopulationCapEnabled     = true,
                PopulationCap            = 10,
            };

            Assert.True(section.ConsciousnessBuffEnabled);
            Assert.Equal(5.5f, section.ConsciousnessMultiplier, TestData.FloatPrecision);
            Assert.False(section.MoodBuffEnabled);
            Assert.Equal(100, section.MoodBuffValue);
            Assert.True(section.PopulationCapEnabled);
            Assert.Equal(10, section.PopulationCap);
        }

        [Fact]
        public void EffectsSection_DefaultValues_AreZeroOrFalse()
        {
            var section = default(ImmutableSettingsSnapshot.EffectsSection);

            Assert.False(section.ConsciousnessBuffEnabled);
            Assert.Equal(0f, section.ConsciousnessMultiplier, TestData.FloatPrecision);
            Assert.False(section.MoodBuffEnabled);
            Assert.Equal(0, section.MoodBuffValue);
            Assert.False(section.PopulationCapEnabled);
            Assert.Equal(0, section.PopulationCap);
        }

        [Fact]
        public void EffectsSection_WithExpression_CreatesIndependentCopy()
        {
            var original = new ImmutableSettingsSnapshot.EffectsSection
            {
                ConsciousnessBuffEnabled = true,
                ConsciousnessMultiplier  = 3.0f,
                MoodBuffEnabled          = true,
                MoodBuffValue            = 40,
                PopulationCapEnabled     = true,
                PopulationCap            = 3,
            };

            var modified = original with
            {
                ConsciousnessMultiplier = 7.0f,
                MoodBuffValue           = 80,
                PopulationCap           = 15,
            };

            // Original is unchanged
            Assert.Equal(3.0f, original.ConsciousnessMultiplier, TestData.FloatPrecision);
            Assert.Equal(40, original.MoodBuffValue);
            Assert.Equal(3, original.PopulationCap);

            // Modified has new values
            Assert.Equal(7.0f, modified.ConsciousnessMultiplier, TestData.FloatPrecision);
            Assert.Equal(80, modified.MoodBuffValue);
            Assert.Equal(15, modified.PopulationCap);
        }

        [Fact]
        public void EffectsSection_ValueEquality_IdenticalSectionsAreEqual()
        {
            var sectionA = new ImmutableSettingsSnapshot.EffectsSection
            {
                ConsciousnessBuffEnabled = true,
                ConsciousnessMultiplier  = 3.0f,
                MoodBuffEnabled          = true,
                MoodBuffValue            = 40,
                PopulationCapEnabled     = true,
                PopulationCap            = 3,
            };

            var sectionB = new ImmutableSettingsSnapshot.EffectsSection
            {
                ConsciousnessBuffEnabled = true,
                ConsciousnessMultiplier  = 3.0f,
                MoodBuffEnabled          = true,
                MoodBuffValue            = 40,
                PopulationCapEnabled     = true,
                PopulationCap            = 3,
            };

            Assert.Equal(sectionA, sectionB);
        }

        // -----------------------------------------------------------------
        // With-expression creates independent copy
        // -----------------------------------------------------------------

        [Fact]
        public void WithExpression_ModifiedCopy_OriginalUnchanged()
        {
            var original = new ImmutableSettingsSnapshot
            {
                General = new ImmutableSettingsSnapshot.GeneralSection { ModEnabled = true, LoggingLevel = 1 }
            };

            var modified = original with
            {
                General = new ImmutableSettingsSnapshot.GeneralSection { ModEnabled = false, LoggingLevel = 3 }
            };

            Assert.True(original.General.ModEnabled);
            Assert.Equal(1, original.General.LoggingLevel);
            Assert.False(modified.General.ModEnabled);
            Assert.Equal(3, modified.General.LoggingLevel);
        }

        // -----------------------------------------------------------------
        // Value equality
        // -----------------------------------------------------------------

        [Fact]
        public void Equality_SameValues_AreEqual()
        {
            var a = new ImmutableSettingsSnapshot
            {
                General = new ImmutableSettingsSnapshot.GeneralSection { ModEnabled = true, LoggingLevel = 2 },
                Healing = new ImmutableSettingsSnapshot.HealingSection { BaseRate = 1.8f }
            };
            var b = new ImmutableSettingsSnapshot
            {
                General = new ImmutableSettingsSnapshot.GeneralSection { ModEnabled = true, LoggingLevel = 2 },
                Healing = new ImmutableSettingsSnapshot.HealingSection { BaseRate = 1.8f }
            };

            Assert.Equal(a, b);
        }

        [Fact]
        public void Equality_DifferentValues_AreNotEqual()
        {
            var a = new ImmutableSettingsSnapshot
            {
                General = new ImmutableSettingsSnapshot.GeneralSection { ModEnabled = true }
            };
            var b = new ImmutableSettingsSnapshot
            {
                General = new ImmutableSettingsSnapshot.GeneralSection { ModEnabled = false }
            };

            Assert.NotEqual(a, b);
        }

        // -----------------------------------------------------------------
        // MapSection string round-trip
        // -----------------------------------------------------------------

        [Fact]
        public void MapSection_MapProtectionAction_RoundTripsString()
        {
            var snapshot = new ImmutableSettingsSnapshot
            {
                Map = new ImmutableSettingsSnapshot.MapSection { MapProtectionAction = "teleport" }
            };

            Assert.Equal("teleport", snapshot.Map.MapProtectionAction);
        }

        [Fact]
        public void MapSection_MapProtectionAction_NullRoundTrips()
        {
            var snapshot = new ImmutableSettingsSnapshot
            {
                Map = new ImmutableSettingsSnapshot.MapSection { MapProtectionAction = null }
            };

            Assert.Null(snapshot.Map.MapProtectionAction);
        }
    }
}
