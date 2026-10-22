using Eternal.Tests.UI;
using System.Linq;
using System.Reflection.Emit;
using Eternal.Healing;
using Xunit;

namespace Eternal.Tests.Healing
{
    public class RegrowthWorkAccountingTests
    {
        [Fact]
        public void LivingRegrowth_ZeroBudgetReachesCanonicalFreeWorkAndCompletion()
        {
            const string orchestrator = "Eternal.Components.TickOrchestrator";
            var il = MenuAssemblyContract.Instructions(orchestrator, "ProgressLivingPawnRegrowth");
            int budget = il.FindIndex(i => Equals(i.Operand, "Eternal.Interfaces.IFoodCostProcessor.GetAvailableNutrition"));
            int progress = il.FindIndex(i => Equals(i.Operand, "Eternal.EternalRegrowthManager.ProgressRegrowth"));
            Assert.DoesNotContain(il.Skip(budget + 1).Take(progress - budget - 1),
                i => i.Code.FlowControl == FlowControl.Cond_Branch && (int)i.Operand > il[progress].Offset);
            var start = MenuAssemblyContract.Calls(orchestrator, "UpdateAllRegrowth").ToList();
            Assert.True(start.IndexOf("Eternal.Healing.HealingWorkCostCalculator.AffordableSeverity") >= 0,
                "Starting living regrowth must use canonical affordability so a zero-price definition can start at cap/floor.");
            var free = new HealingCostProfile(0f);
            var priced = new HealingCostProfile(1f);
            Assert.Equal(1.2f, HealingWorkCostCalculator.AffordableSeverity(free, 297f, 1.2f, 0f));
            Assert.Equal(0f, HealingWorkCostCalculator.AffordableSeverity(priced, 297f, 1.2f, 0f));
            Assert.Equal(0f, HealingWorkCostCalculator.Cost(free, 297f, 295.8f));
        }

        [Fact]
        public void LivingAndDeadRegrowth_ConsumeMeasuredBatchWork()
        {
            var living = MenuAssemblyContract.Calls("Eternal.Components.TickOrchestrator", "ProgressLivingPawnRegrowth");
            Assert.Contains("Eternal.Interfaces.IFoodCostProcessor.GetAvailableNutrition", living);
            Assert.Contains("Eternal.Interfaces.IFoodCostProcessor.SettleNutrition", living);
            var dead = MenuAssemblyContract.Calls("Eternal.Healing.EternalCorpseHealingProcessor", "ProcessCorpseRegrowthTick");
            Assert.DoesNotContain("Eternal.EternalRegrowthManager.GetRegrowingHediffs", dead);
            Assert.Contains("Eternal.Healing.RegrowthBatchResult.get_NutritionCharge", dead);
            Assert.Contains("Eternal.EternalRegrowthManager.IsPawnInRegrowth", dead);
            Assert.Contains("Verse.HediffSet.GetMissingPartsCommonAncestors", dead);
        }

        [Fact]
        public void ConditionAndPreview_ResolveDefinitionProfilesAndRetainCostOverrides()
        {
            Assert.Contains("Eternal.Healing.HealingCostProfiles.Resolve",
                MenuAssemblyContract.Calls("Eternal.UI.ResurrectionPreview", "Capture"));
            var fields = MenuAssemblyContract.Instructions("Eternal.Settings.HediffSettingSlim", "ExposeData");
            Assert.Contains(fields, i => Equals(i.Operand, "nutritionCostMultiplier"));
            Assert.Contains(fields, i => Equals(i.Operand, "nutritionSeverityRangeOverride"));
        }
    }
}
