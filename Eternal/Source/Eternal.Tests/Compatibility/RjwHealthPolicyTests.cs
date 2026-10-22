// Author: 0Shard
// Description: Pure RJW medical ownership, defaults and persisted healing choices.
using Eternal.Compatibility;
using Xunit;

namespace Eternal.Tests.Compatibility
{
    public class RjwHealthPolicyTests
    {
        [Theory]
        [InlineData("Sterilized", RjwHealthKind.FertilityImpairment)]
        [InlineData("DecreasedFertility", RjwHealthKind.FertilityImpairment)]
        [InlineData("RJW_IUD", RjwHealthKind.FertilityControl)]
        [InlineData("ImpregnationBlocker", RjwHealthKind.FertilityControl)]
        [InlineData("IncreasedFertility", RjwHealthKind.FertilityControl)]
        [InlineData("FertilityEnhancer", RjwHealthKind.FertilityControl)]
        public void CoreMedicalNames_RequireActualOwningPackage(string defName, RjwHealthKind expected)
        {
            Assert.Equal(expected, RjwHealthPolicy.Classify("rim.job.world", defName, null, true));
            Assert.Equal(expected, RjwHealthPolicy.Classify("RIM.JOB.WORLD", defName, typeof(string), true));
            Assert.Equal(RjwHealthKind.Unowned, RjwHealthPolicy.Classify("another.mod", defName, null, true));
            Assert.Equal(RjwHealthKind.Unowned, RjwHealthPolicy.Classify("ludeon.rimworld", defName, null, true));
            Assert.Equal(RjwHealthKind.Unowned, RjwHealthPolicy.Classify(null, defName, null, true));
        }

        [Theory]
        [InlineData("Pregnant")]
        [InlineData("RJW_UnknownEgg")]
        [InlineData("NaturalPenis")]
        [InlineData("SterilizedByAnotherMod")]
        [InlineData("DecreasedFertilityAddon")]
        [InlineData("sterilized")]
        [InlineData(null)]
        public void ResemblingNames_WithoutTypedAnatomyOrPregnancyContractRemainUnowned(string defName)
        {
            Assert.Equal(RjwHealthKind.Unowned, RjwHealthPolicy.Classify("rim.job.world", defName, null, true));
            Assert.Equal(RjwHealthKind.Unowned, RjwHealthPolicy.Classify("ludeon.rimworld", defName, typeof(string), true));
        }

        [Theory]
        [InlineData("Sterilized")]
        [InlineData("DecreasedFertility")]
        public void NewImpairmentDefaults_HealAndSavedDisabledChoiceRemainsDisabled(string defName)
        {
            var kind = RjwHealthPolicy.Classify("rim.job.world", defName, null, true);
            Assert.Equal(true, RjwHealthPolicy.DefaultCanHeal(kind));
            Assert.Equal(true, RjwHealthPolicy.ResolveHealingDecision(kind, null));
            Assert.Equal(false, RjwHealthPolicy.ResolveHealingDecision(kind, false));
            Assert.Equal(true, RjwHealthPolicy.ResolveHealingDecision(kind, true));
        }

        [Theory]
        [InlineData("RJW_IUD")]
        [InlineData("ImpregnationBlocker")]
        [InlineData("IncreasedFertility")]
        [InlineData("FertilityEnhancer")]
        public void FertilityControls_DefaultToPreservedAndHonorConfiguredChoice(string defName)
        {
            var kind = RjwHealthPolicy.Classify("rim.job.world", defName, null, true);
            Assert.Equal(false, RjwHealthPolicy.DefaultCanHeal(kind));
            Assert.Equal(false, RjwHealthPolicy.ResolveHealingDecision(kind, null));
            Assert.Equal(false, RjwHealthPolicy.ResolveHealingDecision(kind, false));
            Assert.Equal(true, RjwHealthPolicy.ResolveHealingDecision(kind, true));
        }

        [Theory]
        [InlineData(RjwHealthKind.Anatomy)]
        [InlineData(RjwHealthKind.Pregnancy)]
        public void AnatomyAndPregnancy_StayProtectedEvenWithLegacyEnabledSetting(RjwHealthKind kind)
        {
            Assert.Equal(false, RjwHealthPolicy.DefaultCanHeal(kind));
            Assert.Equal(false, RjwHealthPolicy.ResolveHealingDecision(kind, null));
            Assert.Equal(false, RjwHealthPolicy.ResolveHealingDecision(kind, false));
            Assert.Equal(false, RjwHealthPolicy.ResolveHealingDecision(kind, true));
        }

        [Theory]
        [InlineData(null)]
        [InlineData(false)]
        [InlineData(true)]
        public void UnownedCondition_LeavesOrdinaryHealingDecisionToExistingConsumer(bool? configured)
        {
            Assert.Null(RjwHealthPolicy.DefaultCanHeal(RjwHealthKind.Unowned));
            Assert.Null(RjwHealthPolicy.ResolveHealingDecision(RjwHealthKind.Unowned, configured));
        }

        [Theory]
        [InlineData(0.0, false, 0.0)]
        [InlineData(0.25, false, 0.25)]
        [InlineData(0.69, false, 0.69)]
        [InlineData(0.95, false, 0.95)]
        [InlineData(0.0, true, 0.0)]
        [InlineData(0.25, true, 0.25)]
        [InlineData(0.69, true, 0.69)]
        [InlineData(0.70, true, 0.69)]
        [InlineData(0.95, true, 0.69)]
        public void LegacySelector_ChangesOnlyScopedHighValues(double current, bool fallbackActive, double expected)
        {
            Assert.Equal(expected, RjwHealthPolicy.LimitLegacySelector(current, fallbackActive));
        }
    }
}
