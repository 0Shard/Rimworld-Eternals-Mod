using System.Linq;
using Eternal.Tests.UI;
using Xunit;

namespace Eternal.Tests.Healing
{
    public class HealingAdmissionRegressionTests
    {
        [Theory]
        [InlineData("IsEligibleForHealing")]
        [InlineData("ShouldHealByDefault")]
        [InlineData("IsThresholdGated")]
        public void AllGenericAdmissionPathsUseTheSameRegrowthGuard(string method)
            => Assert.Contains("Eternal.Healing.HediffHealingConfig.AllowsGenericHealing",
                MenuAssemblyContract.Calls("Eternal.Healing.HediffHealingConfig", method));

        [Fact]
        public void GenericMutationAndScarMutationRevalidateCurrentAdmission()
        {
            Assert.Contains("Eternal.Healing.HediffHealingConfig.AllowsGenericHealing",
                MenuAssemblyContract.Calls("Eternal.EternalHediffHealer", "HealHediff"));
            Assert.Contains("Eternal.Healing.HediffHealingConfig.CanHealScar",
                MenuAssemblyContract.Calls("Eternal.Healing.Scars.ScarHealingApplier", "HealScar"));
            Assert.Contains("Eternal.Healing.HediffHealingConfig.CanHealScar",
                MenuAssemblyContract.Calls("Eternal.Healing.Scars.ScarIdentifier", "GetPawnScars"));
        }

        [Fact]
        public void DedicatedScarAndGenericClassifiersShareActualPermanentInjuryClassification()
        {
            foreach (var pair in new[] {
                (Type: "Eternal.Healing.Scars.ScarIdentifier", Method: "IsScarHediff"),
                (Type: "Eternal.Extensions.HediffExtensions", Method: "IsPermanentOrScar") })
            {
                var calls = MenuAssemblyContract.Calls(pair.Type, pair.Method);
                Assert.Contains("Eternal.Extensions.HediffExtensions.IsPermanentInjuryScar", calls);
                Assert.DoesNotContain(calls, call => call?.Contains("String.Contains") == true);
            }
        }

        [Fact]
        public void GlobalScarDispatchIsOnceOutsideTheLivingPawnLoop()
        {
            Assert.DoesNotContain("Eternal.EternalScarHealing.ProcessScarHealing",
                MenuAssemblyContract.Calls("Eternal.EternalHealingProcessor", "ProcessNormalHealing"));
            Assert.Single(MenuAssemblyContract.Calls("Eternal.Components.TickOrchestrator", "ProcessRareTick")
                .Where(call => call == "Eternal.EternalScarHealing.ProcessScarHealing"));
        }
    }
}
