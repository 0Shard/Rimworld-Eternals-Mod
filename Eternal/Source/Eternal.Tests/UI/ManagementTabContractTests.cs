// Relative Path: Eternal/Source/Eternal.Tests/UI/ManagementTabContractTests.cs
// Creation Date: 12-10-2026
// Author: 0Shard
// Description: Compiled menu wiring contracts without loading game-bound CLR types.
using Eternal.Tests.Infrastructure;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.Emit;
using Xunit;

namespace Eternal.Tests.UI
{
    public class ManagementTabContractTests
    {
        [Fact]
        public void LivingRowsUseProductionPriceTypeBudgetAndNativeCadenceWithoutMutatingState()
        {
            const string model = "Eternal.UI.Management.ManagementTabModel";
            var calls = MenuAssemblyContract.Calls(model, "CaptureLiving");
            foreach (string call in new[] { "Eternal.Healing.HealingCostProfiles.Resolve", "Eternal.EternalHealingPriority.GetHealingType",
                "Eternal.Healing.TypeSpecificHealing.SeverityFactor", "Eternal.UI.Management.LivingHealingProjection.CanAdvance",
                "Eternal.UI.Management.LivingHealingProjection.Ticks", "Eternal.Eternal_Component.get_LastNormalHealTick" })
            {
                Assert.Contains(call, calls);
            }
            Assert.Contains(calls, call => call?.EndsWith(".GetAvailableNutrition") == true);
            Assert.DoesNotContain(calls, call => call != null && (call.Contains("SettleNutrition") || call.Contains("AddDebt") ||
                call.Contains("RemoveHediff") || call.Contains("RegisterCorpse") || call.Contains("RegisterThreshold")));
        }
        [Fact]
        public void ScarRow_UsesOwnedTimingAndKeepsChargeDescriptionInHoverDetails()
        {
            var il = MenuAssemblyContract.Instructions("Eternal.UI.Management.ManagementTabModel", "CaptureLiving");
            Assert.Contains(il, i => Equals(i.Operand, "Eternal.UI.Management.ManagementTabModel.CaptureScarTiming"));
            Assert.Contains(il, i => Equals(i.Operand, "Eternal.UI.MenuValueCalculator.DescribeLivingScar"));
            int description = il.FindIndex(i => Equals(i.Operand, "Eternal.UI.LivingScarDisplay.get_Status"));
            Assert.True(description >= 0);
            Assert.Contains(il.Skip(description).Take(14), i => i.Code == OpCodes.Stfld && Equals(i.Operand, "Tooltip"));
            int ticks = il.FindIndex(i => Equals(i.Operand, "Eternal.UI.LivingScarDisplay.get_RemainingTicks"));
            Assert.True(ticks >= 0);
            Assert.Equal(OpCodes.Stfld, il[ticks + 1].Code);
            Assert.Equal("Ticks", il[ticks + 1].Operand);
            Assert.Contains("Eternal.EternalScarHealing.GetHealingTiming",
                MenuAssemblyContract.Calls("Eternal.UI.Management.ManagementTabModel", "CaptureScarTiming"));
            Assert.Contains(MenuAssemblyContract.Instructions("Eternal.UI.Management.ManagementTabPresenter", "Condition"),
                i => i.Code == OpCodes.Ldfld && Equals(i.Operand, "Ticks"));
            Assert.Contains("Eternal.UI.Management.ManagementTabPresenter.Condition",
                MenuAssemblyContract.Calls("Eternal.UI.Management.ManagementTabView", "DrawSnapshot"));
            Assert.Contains("Eternal.UI.Management.ManagementTabPresenter.ConditionTooltip",
                MenuAssemblyContract.Calls("Eternal.UI.Management.ManagementTabView", "DrawSnapshot"));
        }

        [Fact]
        public void NormalAndScarEligibility_ReadsExistingThresholdWithoutRegistrationOrLatch()
        {
            var capture = MenuAssemblyContract.Calls("Eternal.UI.Management.ManagementTabModel", "CaptureLiving");
            Assert.Contains("Eternal.UI.Management.ManagementTabModel.GetNormalHealingEligibility", capture);
            Assert.DoesNotContain("Eternal.Healing.HediffHealingConfig.ShouldHealByDefault", capture);
            var il = MenuAssemblyContract.Instructions("Eternal.UI.Management.ManagementTabModel", "GetNormalHealingEligibility");
            int baseEligibility = il.FindIndex(i => Equals(i.Operand, "Eternal.Healing.HediffHealingConfig.ShouldHealByDefault"));
            Assert.True(baseEligibility >= 0);
            Assert.Equal(OpCodes.Ldc_I4_1, il[baseEligibility - 1].Code);
            Assert.Contains(il, i => (i.Operand as string)?.EndsWith(".TryGetThreshold") == true);
            Assert.Contains(il, i => Equals(i.Operand, "Eternal.UI.MenuValueCalculator.TrackedThresholdEligibility"));
            Assert.DoesNotContain(il, i => (i.Operand as string)?.Contains("HasReachedThreshold") == true ||
                (i.Operand as string)?.Contains("RegisterThreshold") == true ||
                (i.Operand as string)?.Contains("GetOrCreate") == true);
            Assert.Contains(MenuAssemblyContract.Instructions("Eternal.UI.Management.ManagementTabModel", "CaptureLiving"),
                i => (i.Operand as string)?.EndsWith(".injuryProps") == true);
        }

        [Fact]
        public void ScarRatePath_UsesElapsedRecordTicksAndRareIntervalNormalization()
        {
            var apply = MenuAssemblyContract.Instructions("Eternal.Healing.Scars.ScarHealingApplier", "HealScar");
            int elapsed = apply.FindIndex(i => Equals(i.Operand, "Eternal.ScarHealingRecord.get_LastHealedTick"));
            int calculate = apply.FindIndex(i => Equals(i.Operand, "Eternal.Healing.Scars.ScarCostCalculator.CalculateHealingAmount"));
            Assert.True(elapsed >= 0 && calculate > elapsed);
            Assert.Contains(apply.Skip(elapsed).Take(calculate - elapsed), i => i.Code == OpCodes.Sub);
            var formula = MenuAssemblyContract.Instructions("Eternal.Healing.Scars.ScarCostCalculator", "CalculateHealingAmount");
            Assert.Contains(formula, i => (i.Operand as string)?.EndsWith(".get_RareTickRate") == true);
            Assert.Contains(formula, i => (i.Operand as string)?.EndsWith(".get_BaseHealingRate") == true);
            Assert.Contains(formula, i => Equals(i.Operand, "Eternal.Healing.Scars.ScarHealingTiming.CalculateHealingAmount"));
            var arithmetic = MenuAssemblyContract.Instructions("Eternal.Healing.Scars.ScarHealingTiming", "CalculateHealingAmount");
            Assert.Contains(arithmetic, i => i.Code == OpCodes.Div);
            Assert.Contains(arithmetic, i => i.Code == OpCodes.Mul);
        }
        [Fact]
        public void PartSnapshot_UsesProductionParentGateHealthAndPendingOwnership()
        {
            var il = MenuAssemblyContract.Instructions("Eternal.UI.Management.ManagementTabModel", "CaptureParts");
            Assert.Contains(il, i => Equals(i.Operand, "Eternal.EternalRegrowthManager.CanCompleteRegrowth"));
            Assert.Contains(il, i => Equals(i.Operand, "Eternal.EternalRegrowthManager.HasRegrowthHediff"));
            Assert.Contains(il, i => Equals(i.Operand, "Verse.HediffSet.PartIsMissing"));
            int status = il.FindIndex(i => Equals(i.Operand, "Eternal.UI.MenuValueCalculator.PartStatus"));
            Assert.True(status >= 0);
            Assert.Equal(OpCodes.Stfld, il[status + 1].Code);
            Assert.Equal("Status", il[status + 1].Operand);
            Assert.Contains(MenuAssemblyContract.Instructions("Eternal.UI.Management.ManagementTabPresenter", "Part"),
                i => i.Code == OpCodes.Ldfld && Equals(i.Operand, "Status"));
            Assert.Contains("Eternal.UI.Management.ManagementTabPresenter.Part",
                MenuAssemblyContract.Calls("Eternal.UI.Management.ManagementTabView", "DrawSnapshot"));
            Assert.Contains("Verse.HediffSet.PartIsMissing", MenuAssemblyContract.Calls("Eternal.EternalRegrowthManager", "CanCompleteRegrowth"));
        }

        [Fact]
        public void LivingSnapshot_ReadsExistingSettingBeforeSelectionAndExcludesModOwnedConditions()
        {
            var il = MenuAssemblyContract.Instructions("Eternal.UI.Management.ManagementTabModel", "CaptureLiving");
            int get = il.FindIndex(i => (i.Operand as string)?.EndsWith("HediffSettingsStore.GetExisting") == true);
            int include = il.FindIndex(i => Equals(i.Operand, "Eternal.UI.MenuValueCalculator.IncludeLivingCondition"));
            Assert.True(get >= 0 && include > get);
            Assert.Contains(il.Skip(get).Take(include - get), i => Equals(i.Operand, "canHeal"));
            Assert.Contains(il.Skip(include).Take(8), i => i.Code.FlowControl == FlowControl.Cond_Branch);
            Assert.Contains(il, i => Equals(i.Operand, "Eternal_Essence"));
            Assert.Contains(il, i => Equals(i.Operand, "Eternal_MetabolicRecovery"));
            Assert.Contains(il, i => Equals(i.Operand, "Eternal.Hediffs.MetabolicRecovery_Hediff"));
            Assert.DoesNotContain(il, i => (i.Operand as string)?.Contains("GetOrCreate") == true);
        }
        [Fact]
        public void SharedInspectTab_HasParameterlessConstructor()
        {
            Assert.Contains(0, MenuAssemblyContract.ParameterCounts("Eternal.UI.Management.ManagementTabView", ".ctor"));
        }

        [Fact]
        public void Management_UsesSeparateLivingAndAuthoritativeCorpseBranches()
        {
            var capture = MenuAssemblyContract.Calls("Eternal.UI.Management.ManagementTabModel", "Capture");
            Assert.Contains("Eternal.UI.Management.ManagementTabModel.CaptureCorpse", capture);
            Assert.Contains("Eternal.UI.Management.ManagementTabModel.CaptureLiving", capture);
            var corpse = MenuAssemblyContract.Calls("Eternal.UI.Management.ManagementTabModel", "CaptureCorpse");
            Assert.Contains("Eternal.Corpse.EternalCorpseManager.GetCorpseData", corpse);
            Assert.Contains("Eternal.UI.ResurrectionPreview.Capture", corpse);
            Assert.DoesNotContain(corpse, c => c != null && c.Contains("ShouldHealByDefault"));
            var living = MenuAssemblyContract.Calls("Eternal.UI.Management.ManagementTabModel", "CaptureLiving");
            Assert.Contains("Eternal.UI.Management.ManagementTabModel.GetNormalHealingEligibility", living);
            Assert.Contains(living, c => c != null && c.EndsWith(".CalculateHediffHealing"));
        }

        [Fact]
        public void InspectTab_ResolvesCorpsePawnAndBindsCurrentSelection()
        {
            Assert.Equal("RimWorld.ITab", MenuAssemblyContract.BaseType("Eternal.UI.Management.ManagementTabView"));
            Assert.Contains("GetInspectTabs", MenuAssemblyContract.AttributeText("Eternal.Patches.Thing_GetInspectTabs_Patch"));
            Assert.Contains("Verse.Corpse.get_InnerPawn", MenuAssemblyContract.Calls("Eternal.UI.Management.ManagementTabView", "ResolvePawn"));
            var draw = MenuAssemblyContract.Calls("Eternal.UI.Management.ManagementTabView", "FillTab");
            Assert.Contains("Eternal.UI.Management.ManagementTabView.BindSelection", draw);
            Assert.Contains("Eternal.UI.Management.ManagementTabPresenter.Capture", draw);
            Assert.Contains(draw, c => c != null && c.EndsWith(".get_CurHeight"));
            Assert.Contains("Verse.InspectTabManager.GetSharedInstance",
                MenuAssemblyContract.Calls("Eternal.Patches.Thing_GetInspectTabs_Patch", "Postfix"));
        }

        [Fact]
        public void InspectTab_AccordionHeadersToggleWithoutBackAndSelectionStillResets()
        {
            const string view = "Eternal.UI.Management.ManagementTabView";
            var draw = MenuAssemblyContract.Instructions(view, "DrawSnapshot");
            Assert.DoesNotContain(draw, i => Equals(i.Operand, "Back"));
            Assert.DoesNotContain(draw, i => Equals(i.Operand, view + ".ResetSections"));
            foreach (string field in new[] { "conditionsExpanded", "partsExpanded" })
            {
                Assert.Contains(draw, i => i.Code == OpCodes.Stfld && Equals(i.Operand, field));
            }

            var state = MenuAssemblyContract.Instructions(view, "ResetSections");
            foreach (string field in new[] { "conditionsExpanded", "partsExpanded", "scroll", "contentHeight" })
            {
                Assert.Contains(state, i => i.Code == OpCodes.Stfld && Equals(i.Operand, field));
            }

            Assert.Contains(state, i => Equals(i.Operand, "UnityEngine.Vector2.get_zero"));
            Assert.Contains(view + ".ResetSections", MenuAssemblyContract.Calls(view, "BindSelection"));
        }

        [Fact]
        public void InspectTab_UsesHoverDetailsAndAlwaysCleansItsScrollScope()
        {
            const string view = "Eternal.UI.Management.ManagementTabView";
            var draw = MenuAssemblyContract.Instructions(view, "DrawSnapshot");
            Assert.DoesNotContain(draw, i => Equals(i.Operand, "Healing details") || Equals(i.Operand, "Current debt capacity: "));
            Assert.Contains("Verse.TooltipHandler.TipRegion", MenuAssemblyContract.Calls(view, "LabelWithTooltip"));
            var cleanup = MenuAssemblyContract.ReachableInstructions(view, "FillTab", finallyOnly: true);
            Assert.Contains(cleanup, i => Equals(i.Operand, "Verse.Listing.End"));
            Assert.Contains(cleanup, i => Equals(i.Operand, "Verse.Widgets.EndScrollView"));
        }

        [Fact]
        public void InspectTab_LongSectionsStayInOneMeasuredScrollColumn()
        {
            var draw = MenuAssemblyContract.Instructions("Eternal.UI.Management.ManagementTabView", "FillTab");
            int oneColumn = draw.FindIndex(i => i.Code == OpCodes.Stfld && Equals(i.Operand, "Verse.Listing.maxOneColumn"));
            int begin = draw.FindIndex(i => Equals(i.Operand, "Verse.Listing.Begin"));
            Assert.True(oneColumn > 0 && begin > oneColumn,
                "Expanded rows must increase the scroll height instead of wrapping into clipped horizontal columns.");
            Assert.Equal(OpCodes.Ldc_I4_1, draw[oneColumn - 1].Code);
            int endScroll = draw.FindIndex(i => Equals(i.Operand, "Verse.Widgets.EndScrollView"));
            Assert.Contains(draw.Skip(endScroll + 1), i => Equals(i.Operand, "UnityEngine.Mathf.Clamp"));
        }

        [Fact]
        public void PreviewAndHydration_UseTheSamePureQueueSelector()
        {
            Assert.Contains("Eternal.UI.HealingQueueSelector.Select",
                MenuAssemblyContract.Calls("Eternal.UI.ResurrectionPreview", "SelectQueue"));
            Assert.Contains("Eternal.UI.HealingQueueSelector.Select",
                MenuAssemblyContract.Calls("Eternal.Models.CorpseTrackingEntry", "TryHydrateHealingQueue"));
        }
    }

    // Retain established menu entrypoints; metadata ownership and decoding live in one helper.
    internal static class MenuAssemblyContract
    {
        public static string BaseType(string type)
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            return assembly.TryType(type, out var handle) ? assembly.MemberName(assembly.Reader.GetTypeDefinition(handle).BaseType) : null;
        }
        public static string AttributeText(string type)
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            return assembly.AttributeText(assembly.Reader.GetTypeDefinition(assembly.Type(type)).GetCustomAttributes());
        }
        public static int[] ParameterCounts(string type, string method)
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            return assembly.Methods(type, method).Select(h => assembly.Signature(h).ParameterTypes.Length).ToArray();
        }
        public static List<CompiledInstruction> Instructions(string type, string method, params string[] parameterTypes)
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            return assembly.Instructions(assembly.Method(type, method, parameterTypes.Length == 0 ? null : parameterTypes));
        }
        public static List<CompiledInstruction> ReachableInstructions(string type, string method, bool finallyOnly = false)
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            return assembly.ReachableInstructions(type, method, finallyOnly);
        }
        public static string MethodAttributeText(string type, string method)
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            return assembly.AttributeText(assembly.Reader.GetMethodDefinition(assembly.Method(type, method)).GetCustomAttributes());
        }
        public static string[] Calls(string type, string method) => Instructions(type, method)
            .Where(i => i.Code == OpCodes.Call || i.Code == OpCodes.Callvirt || i.Code == OpCodes.Newobj)
            .Select(i => i.Operand as string).ToArray();
    }
}
