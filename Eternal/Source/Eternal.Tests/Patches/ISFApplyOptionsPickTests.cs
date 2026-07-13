// Relative Path: Eternal/Source/Eternal.Tests/Patches/ISFApplyOptionsPickTests.cs
// Creation Date: 13-07-2026
// Last Edit: 13-07-2026
// Author: 0Shard
// Description: Unit tests for ISF_ApplyOptions_Patch.PickBestOptionIndex — the pure, Verse-free
//              perk-priority core used to auto-pick level-up options for Eternal pawns.
//              Reflection/Harmony paths are deferred to the in-game harness (they need
//              ItsSorceryFramework loaded).

using System;
using System.Collections.Generic;
using Xunit;
using Eternal.Patches.ItsSorcery;

namespace Eternal.Tests.Patches
{
    public class ISFApplyOptionsPickTests
    {
        // -----------------------------------------------------------------
        // Helper: Build an OptionScoreEntry for test data
        // -----------------------------------------------------------------

        private static ISF_ApplyOptions_Patch.OptionScoreEntry Entry(
            string label = "",
            params (string defName, float value)[] stats)
        {
            var entry = new ISF_ApplyOptions_Patch.OptionScoreEntry
            {
                Label = label,
                StatEntries = new List<KeyValuePair<string, float>>()
            };

            foreach (var (defName, value) in stats)
            {
                entry.StatEntries.Add(new KeyValuePair<string, float>(defName, value));
            }

            return entry;
        }

        // -----------------------------------------------------------------
        // Tier priority tests
        // -----------------------------------------------------------------

        [Fact]
        public void MuscleLabel_WinsOverAllOtherTiers()
        {
            var options = new List<ISF_ApplyOptions_Patch.OptionScoreEntry>
            {
                Entry("Cultivation", ("CultivationSpeed", 0.3f)),
                Entry("Tribulation", ("TribulationChance", -0.01f)),
                Entry("Energy Recovery", ("CTR_StaminaEnergyRecovery", 0.5f)),
                Entry("Muscle Training")
            };

            int result = ISF_ApplyOptions_Patch.PickBestOptionIndex(options);
            Assert.Equal(3, result);
        }

        [Fact]
        public void EnergyRecovery_BeatsTribulationAndCultivation()
        {
            var options = new List<ISF_ApplyOptions_Patch.OptionScoreEntry>
            {
                Entry("Cultivation", ("CultivationSpeed", 0.3f)),
                Entry("Tribulation", ("TribulationChance", -0.01f)),
                Entry("Energy Recovery", ("CTR_QiEnergyRecovery", 0.5f))
            };

            int result = ISF_ApplyOptions_Patch.PickBestOptionIndex(options);
            Assert.Equal(2, result);
        }

        [Fact]
        public void NegativeTribulationChance_BeatsCultivationSpeed()
        {
            var options = new List<ISF_ApplyOptions_Patch.OptionScoreEntry>
            {
                Entry("Cultivation", ("CultivationSpeed", 0.3f)),
                Entry("Tribulation", ("TribulationChance", -0.01f))
            };

            int result = ISF_ApplyOptions_Patch.PickBestOptionIndex(options);
            Assert.Equal(1, result);
        }

        [Fact]
        public void PositiveTribulationChance_DoesNotMatch()
        {
            var options = new List<ISF_ApplyOptions_Patch.OptionScoreEntry>
            {
                Entry("Tribulation Bad", ("TribulationChance", 0.05f)),
                Entry("Cultivation", ("CultivationSpeed", 0.3f))
            };

            int result = ISF_ApplyOptions_Patch.PickBestOptionIndex(options);
            Assert.Equal(1, result);
        }

        [Fact]
        public void MuscleLabel_CaseInsensitive()
        {
            var options = new List<ISF_ApplyOptions_Patch.OptionScoreEntry>
            {
                Entry("Toughness"),
                Entry("MUSCLE training")
            };

            int result = ISF_ApplyOptions_Patch.PickBestOptionIndex(options);
            Assert.Equal(1, result);
        }

        [Fact]
        public void NoMatch_ReturnsMinusOne()
        {
            var options = new List<ISF_ApplyOptions_Patch.OptionScoreEntry>
            {
                Entry("Hiding", ("ArmorRating_Sharp", 0.1f)),
                Entry("Energy Max", ("CTR_StaminaEnergyMax", 0.2f))
            };

            int result = ISF_ApplyOptions_Patch.PickBestOptionIndex(options);
            Assert.Equal(-1, result);
        }

        [Fact]
        public void EmptyList_ReturnsMinusOne()
        {
            var options = new List<ISF_ApplyOptions_Patch.OptionScoreEntry>();

            int result = ISF_ApplyOptions_Patch.PickBestOptionIndex(options);
            Assert.Equal(-1, result);
        }

        [Fact]
        public void NullList_ReturnsMinusOne()
        {
            int result = ISF_ApplyOptions_Patch.PickBestOptionIndex(null);
            Assert.Equal(-1, result);
        }

        [Fact]
        public void FirstMatchWithinTier_Wins()
        {
            var options = new List<ISF_ApplyOptions_Patch.OptionScoreEntry>
            {
                Entry("muscle A"),
                Entry("muscle B")
            };

            int result = ISF_ApplyOptions_Patch.PickBestOptionIndex(options);
            Assert.Equal(0, result);
        }
    }
}
