using System.Linq;
using System.Reflection.Emit;
using Xunit;

namespace Eternal.Tests.UI
{
    public class ResurrectionNavigationContractTests
    {
        [Fact]
        public void BulkPricing_UnchangedRateStillReachesEditableResettableControls()
        {
            const string view = "Eternal.UI.HediffSettings.EternalHediffView";
            var il = MenuAssemblyContract.Instructions(view, "DrawTemplateSettings");
            int rateWrite = il.FindIndex(i => i.Code == OpCodes.Stfld && Equals(i.Operand, "healingRate"));
            int pricing = il.FindIndex(i => Equals(i.Operand, view + ".DrawPricingControls"));
            var unchanged = il.Take(rateWrite).Last(i => i.Code.FlowControl == FlowControl.Cond_Branch);
            Assert.True((int)unchanged.Operand <= il[pricing].Offset,
                "Keeping the existing healing rate must not branch past the Bulk nutrition controls.");
            var controls = MenuAssemblyContract.Instructions(view, "DrawPricingControls");
            foreach (string field in new[] { "nutritionCostMultiplier", "nutritionSeverityRangeOverride" })
            {
                Assert.Contains(controls, i => i.Code == OpCodes.Stfld && Equals(i.Operand, field));
                Assert.Contains(MenuAssemblyContract.Instructions("Eternal.EternalHediffManager", "ApplySettingsToGroupWithCount"),
                    i => i.Code == OpCodes.Stfld && Equals(i.Operand, field));
            }
            var inputs = MenuAssemblyContract.Calls(view, "NutritionInput");
            Assert.Contains("Verse.Widgets.ButtonText", inputs);
            Assert.Contains("Verse.Widgets.TextField", inputs);
        }

        [Fact]
        public void ResurrectionFooter_OffersBackAfterScrollAndSingleColumnMeasurement()
        {
            var il = MenuAssemblyContract.Instructions("Eternal.UI.ResurrectionGizmo+ResurrectionConfirmation", "DoWindowContents");
            int end = il.FindIndex(i => Equals(i.Operand, "Verse.Widgets.EndScrollView"));
            Assert.Contains(il.Skip(end + 1), i => Equals(i.Operand, "Back"));
            Assert.Contains(il, i => Equals(i.Operand, "Verse.Listing.maxOneColumn"));
        }

        [Fact]
        public void AdvancedAccordionsHaveNoSeparateBackRoute()
        {
            var il = MenuAssemblyContract.Instructions("Eternal.UI.Settings.SettingsDrawer", "DoWindowContents");
            Assert.DoesNotContain(il, i => Equals(i.Operand, "Back"));
            Assert.DoesNotContain(il, i => (i.Operand as string)?.EndsWith(".CollapseAdvanced") == true);
            Assert.Contains(il, i => Equals(i.Operand, "Verse.Listing.maxOneColumn"));
        }

        [Fact]
        public void BackLeavesConfirmationWhileHeadersOwnInlineExpansionAndFocus()
        {
            var back = MenuAssemblyContract.ReachableInstructions("Eternal.UI.ResurrectionGizmo+ResurrectionConfirmation", "GoBack");
            Assert.Contains(back, i => Equals(i.Operand, "Verse.Window.Close"));
            Assert.DoesNotContain(back, i => Equals(i.Operand, "detailed") || Equals(i.Operand, "scroll"));
            Assert.DoesNotContain(back, i => (i.Operand as string)?.Contains("StartResurrection") == true);
            var draw = MenuAssemblyContract.Instructions("Eternal.UI.ResurrectionGizmo+ResurrectionConfirmation", "DoWindowContents");
            Assert.Contains(draw, i => i.Code == OpCodes.Stfld && Equals(i.Operand, "detailed"));
            Assert.Contains(draw, i => Equals(i.Operand, "Remaining work details"));
            var advanced = MenuAssemblyContract.ReachableInstructions("Eternal.UI.Settings.SettingsDrawer", "SetHediffExpanded");
            Assert.Contains(advanced, i => Equals(i.Operand, "UnityEngine.GUI.FocusControl"));
            Assert.DoesNotContain(advanced, i => Equals(i.Operand, "hediffView") && i.Code == System.Reflection.Emit.OpCodes.Stfld);
        }
    }
}
