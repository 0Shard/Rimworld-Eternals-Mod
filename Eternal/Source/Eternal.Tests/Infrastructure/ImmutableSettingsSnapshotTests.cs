// Relative Path: Eternal/Source/Eternal.Tests/Infrastructure/ImmutableSettingsSnapshotTests.cs
// Creation Date: 24-02-2026
// Last Edit: 13-10-2026
// Author: 0Shard
// Description: Snapshot defaults, retained values, independent copies and value equality.
using Eternal.Infrastructure;
using Xunit;

namespace Eternal.Tests.Infrastructure
{
    public class ImmutableSettingsSnapshotTests
    {
        private static ImmutableSettingsSnapshot Fixture() => new ImmutableSettingsSnapshot
        {
            General = new ImmutableSettingsSnapshot.GeneralSection
            {
                ModEnabled = true,
                DebugMode = false,
                LoggingLevel = 2
            },
            Healing = new ImmutableSettingsSnapshot.HealingSection { BaseRate = 1.8f, ShowProgress = true },
            Resource = new ImmutableSettingsSnapshot.ResourceSection { NutritionCostMultiplier = 1f },
            FoodDebt = new ImmutableSettingsSnapshot.FoodDebtSection
            {
                MaxDebtMultiplier = TestData.DefaultMaxDebtMultiplier,
                FoodDrainThreshold = TestData.DefaultFoodDrainThreshold,
                DebtRepaymentDays = TestData.DefaultDebtRepaymentDays,
                SeverityToNutritionRatio = TestData.DefaultSeverityToNutritionRatio
            },
            Perf = new ImmutableSettingsSnapshot.PerfSection
            {
                NormalTickRate = 60,
                RareTickRate = 250,
                TraitCheckInterval = 5000,
                CorpseCheckInterval = 1000,
                MapCheckInterval = 500,
                HealingHistorySweepInterval = 300000
            },
            Map = new ImmutableSettingsSnapshot.MapSection
            {
                EnableMapAnchors = true,
                AnchorGracePeriodTicks = 12345,
                EnableRoofCollapseProtection = true
            },
            Effects = EffectsFixture()
        };

        private static ImmutableSettingsSnapshot.EffectsSection EffectsFixture() => new ImmutableSettingsSnapshot.EffectsSection
        {
            ConsciousnessBuffEnabled = true,
            ConsciousnessMultiplier = 3f,
            MoodBuffEnabled = true,
            MoodBuffValue = 40,
            PopulationCapEnabled = true,
            PopulationCap = 3
        };

        [Fact]
        public void DefaultConstruction_LeavesEveryRetainedSectionAtZeroOrFalse()
        {
            var snapshot = new ImmutableSettingsSnapshot();
            Assert.False(snapshot.General.ModEnabled);
            Assert.False(snapshot.General.DebugMode);
            Assert.Equal(0, snapshot.General.LoggingLevel);
            Assert.Equal(0f, snapshot.Healing.BaseRate);
            Assert.False(snapshot.Healing.ShowProgress);
            Assert.Equal(0f, snapshot.Resource.NutritionCostMultiplier);
            Assert.Equal(0f, snapshot.FoodDebt.MaxDebtMultiplier);
            Assert.Equal(0f, snapshot.FoodDebt.FoodDrainThreshold);
            Assert.Equal(0f, snapshot.FoodDebt.DebtRepaymentDays);
            Assert.Equal(0f, snapshot.FoodDebt.SeverityToNutritionRatio);
            Assert.All(new[] { snapshot.Perf.NormalTickRate, snapshot.Perf.RareTickRate,
                snapshot.Perf.TraitCheckInterval, snapshot.Perf.CorpseCheckInterval,
                snapshot.Perf.MapCheckInterval, snapshot.Perf.HealingHistorySweepInterval }, value => Assert.Equal(0, value));
            Assert.False(snapshot.Map.EnableMapAnchors);
            Assert.Equal(0, snapshot.Map.AnchorGracePeriodTicks);
            Assert.False(snapshot.Map.EnableRoofCollapseProtection);
            Assert.False(snapshot.Effects.ConsciousnessBuffEnabled);
            Assert.Equal(0f, snapshot.Effects.ConsciousnessMultiplier);
            Assert.False(snapshot.Effects.MoodBuffEnabled);
            Assert.Equal(0, snapshot.Effects.MoodBuffValue);
            Assert.False(snapshot.Effects.PopulationCapEnabled);
            Assert.Equal(0, snapshot.Effects.PopulationCap);
        }

        [Fact]
        public void SharedFixture_RetainsValuesAcrossAllSevenSections()
        {
            var snapshot = Fixture();
            Assert.True(snapshot.General.ModEnabled);
            Assert.False(snapshot.General.DebugMode);
            Assert.Equal(2, snapshot.General.LoggingLevel);
            Assert.Equal(1.8f, snapshot.Healing.BaseRate, TestData.FloatPrecision);
            Assert.True(snapshot.Healing.ShowProgress);
            Assert.Equal(1f, snapshot.Resource.NutritionCostMultiplier, TestData.FloatPrecision);
            Assert.Equal(TestData.DefaultMaxDebtMultiplier, snapshot.FoodDebt.MaxDebtMultiplier, TestData.FloatPrecision);
            Assert.Equal(TestData.DefaultFoodDrainThreshold, snapshot.FoodDebt.FoodDrainThreshold, TestData.FloatPrecision);
            Assert.Equal(TestData.DefaultDebtRepaymentDays, snapshot.FoodDebt.DebtRepaymentDays, TestData.FloatPrecision);
            Assert.Equal(TestData.DefaultSeverityToNutritionRatio, snapshot.FoodDebt.SeverityToNutritionRatio, TestData.FloatPrecision);
            Assert.Equal(new[] { 60, 250, 5000, 1000, 500, 300000 }, new[]
            {
                snapshot.Perf.NormalTickRate, snapshot.Perf.RareTickRate, snapshot.Perf.TraitCheckInterval,
                snapshot.Perf.CorpseCheckInterval, snapshot.Perf.MapCheckInterval, snapshot.Perf.HealingHistorySweepInterval
            });
            Assert.True(snapshot.Map.EnableMapAnchors);
            Assert.Equal(12345, snapshot.Map.AnchorGracePeriodTicks);
            Assert.True(snapshot.Map.EnableRoofCollapseProtection);
            Assert.True(snapshot.Effects.ConsciousnessBuffEnabled);
            Assert.Equal(3f, snapshot.Effects.ConsciousnessMultiplier, TestData.FloatPrecision);
            Assert.True(snapshot.Effects.MoodBuffEnabled);
            Assert.Equal(40, snapshot.Effects.MoodBuffValue);
            Assert.True(snapshot.Effects.PopulationCapEnabled);
            Assert.Equal(3, snapshot.Effects.PopulationCap);
        }

        [Theory]
        [InlineData("General")]
        [InlineData("Healing")]
        [InlineData("Resource")]
        [InlineData("FoodDebt")]
        [InlineData("Perf")]
        [InlineData("Map")]
        [InlineData("Effects")]
        public void WithExpression_ChangesOneSectionWithoutChangingOriginal(string section)
        {
            var original = Fixture();
            var copy = section switch
            {
                "General" => original with { General = original.General with { ModEnabled = false, LoggingLevel = 3 } },
                "Healing" => original with { Healing = original.Healing with { BaseRate = 2f } },
                "Resource" => original with { Resource = original.Resource with { NutritionCostMultiplier = 2f } },
                "FoodDebt" => original with { FoodDebt = original.FoodDebt with { MaxDebtMultiplier = 9f } },
                "Perf" => original with { Perf = original.Perf with { NormalTickRate = 120 } },
                "Map" => original with { Map = original.Map with { AnchorGracePeriodTicks = 67890 } },
                "Effects" => original with { Effects = original.Effects with { MoodBuffValue = 80 } },
                _ => throw new System.ArgumentOutOfRangeException(nameof(section))
            };
            Assert.Equal(Fixture(), original);
            Assert.NotEqual(original, copy);
            Assert.Equal(section == "General", original.General != copy.General);
            Assert.Equal(section == "Healing", original.Healing != copy.Healing);
            Assert.Equal(section == "Resource", original.Resource != copy.Resource);
            Assert.Equal(section == "FoodDebt", original.FoodDebt != copy.FoodDebt);
            Assert.Equal(section == "Perf", original.Perf != copy.Perf);
            Assert.Equal(section == "Map", original.Map != copy.Map);
            Assert.Equal(section == "Effects", original.Effects != copy.Effects);
        }

        [Fact]
        public void IndependentlyConstructedSnapshotsAndEffects_AreValueEqual()
        {
            Assert.Equal(Fixture(), Fixture());
            Assert.Equal(EffectsFixture(), EffectsFixture());
        }

        [Fact]
        public void EffectsWithExpression_PreservesOriginalAndUnmodifiedFlags()
        {
            var original = EffectsFixture();
            var copy = original with { ConsciousnessMultiplier = 7f, MoodBuffValue = 80, PopulationCap = 15 };
            Assert.Equal(EffectsFixture(), original);
            Assert.Equal(3f, original.ConsciousnessMultiplier, TestData.FloatPrecision);
            Assert.Equal(40, original.MoodBuffValue);
            Assert.Equal(3, original.PopulationCap);
            Assert.Equal(7f, copy.ConsciousnessMultiplier, TestData.FloatPrecision);
            Assert.Equal(80, copy.MoodBuffValue);
            Assert.Equal(15, copy.PopulationCap);
            Assert.Equal(original.ConsciousnessBuffEnabled, copy.ConsciousnessBuffEnabled);
            Assert.Equal(original.MoodBuffEnabled, copy.MoodBuffEnabled);
            Assert.Equal(original.PopulationCapEnabled, copy.PopulationCapEnabled);
            Assert.NotEqual(original, copy);
        }
    }
}
