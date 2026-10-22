// Author: 0Shard
// Description: Compiled contracts for the retained inline Advanced hediff editor.
using System.Linq;
using System.Collections.Generic;
using System.Reflection.Emit;
using Eternal.UI.HediffSettings;
using Xunit;

namespace Eternal.Tests.UI
{
    public class InlineHediffSettingsContractTests
    {
        private const string Drawer = "Eternal.UI.Settings.SettingsDrawer";
        private const string View = "Eternal.UI.HediffSettings.EternalHediffView";
        private const string Presenter = "Eternal.UI.HediffSettings.EternalHediffPresenter";

        [Fact]
        public void PricingValueOnlyCacheRebuildsPreserveOrderedEditorScope()
        {
            var rows = new[] { new KeyValuePair<string, float>("Cut", 1f), new KeyValuePair<string, float>("Burn", 1f) };
            foreach (float edited in new[] { 2f, 25f })
            {
                var rebuilt = new[] { new KeyValuePair<string, float>("Cut", edited), new KeyValuePair<string, float>("Burn", 1f) };
                Assert.True(HediffEditorScopePolicy.HasSameRows(rows, rebuilt));
                rows = rebuilt;
            }
            Assert.False(HediffEditorScopePolicy.HasSameRows(rows, rows.Reverse().ToArray()));
            Assert.False(HediffEditorScopePolicy.HasSameRows(rows, rows.Take(1).ToArray()));
            Assert.False(HediffEditorScopePolicy.HasSameRows(rows, new[] { new KeyValuePair<string, float>("Flu", 25f), rows[1] }));
            Assert.False(HediffEditorScopePolicy.HasSameRows<float>(null, rows));
        }

        [Fact]
        public void PricingDirtyCacheAndRedrawUseTheProductionScopePolicy()
        {
            Assert.Contains(Presenter + ".NotifyHediffSettingChanged", MenuAssemblyContract.Calls(View, "DrawPricingControls"));
            Assert.Contains("Eternal.EternalHediffManager.MarkFilterDirty",
                MenuAssemblyContract.Calls("Eternal.UI.HediffSettings.EternalHediffModel", "MarkFilterDirty"));
            Assert.Contains("Eternal.Settings.HediffFilterEngine.FilterToList",
                MenuAssemblyContract.Calls("Eternal.EternalHediffManager", "GetFilteredHediffsCached"));
            Assert.Contains("Eternal.UI.HediffSettings.HediffEditorScopePolicy.HasSameRows", MenuAssemblyContract.Calls(View, "DrawHediffList"));
            foreach (string suffix in new[] { "-nutrition", "-range" })
            {
                Assert.Contains(MenuAssemblyContract.Instructions(View, "DrawHediffList"), i => Equals(i.Operand, suffix));
            }
        }

        [Fact]
        public void DisabledSlidersHavePassiveRailsAndBothWrappersGuardAssignment()
        {
            Assert.Contains(Drawer + ".DrawDisabledSlider", MenuAssemblyContract.Calls(Drawer, "DrawInlineSlider"));
            foreach (string wrapper in new[] { "SliderWithInlineEdit", "SliderWithInlineEditInt" })
            {
                Assert.Contains("UnityEngine.GUI.get_enabled", MenuAssemblyContract.Calls(Drawer, wrapper));
            }
            var passive = MenuAssemblyContract.Calls(Drawer, "DrawDisabledSlider");
            Assert.DoesNotContain("Verse.Widgets.HorizontalSlider", passive);
            Assert.DoesNotContain("Verse.Widgets.ButtonInvisible", passive);
            var draw = MenuAssemblyContract.Instructions(Drawer, "DrawInlineSlider");
            Assert.Contains(draw, instruction => Equals(instruction.Operand, "activeEditControlName"));
            Assert.Contains(draw, instruction => Equals(instruction.Operand, "UnityEngine.GUI.FocusControl"));
        }

        [Fact]
        public void CompactAndBulkRatesShareNamedPersistentEditAndEnabledCommitPath()
        {
            foreach (string method in new[] { "DrawCompactSliderWithInput", "DrawSliderWithNumericInput" })
            {
                Assert.Contains(View + ".DrawRateInput", MenuAssemblyContract.Calls(View, method));
            }
            var calls = MenuAssemblyContract.Calls(View, "DrawRateInput");
            Assert.Contains("UnityEngine.GUI.SetNextControlName", calls);
            Assert.Contains("UnityEngine.GUI.GetNameOfFocusedControl", calls);
            Assert.Contains("Eternal.UI.HediffSettings.RateEditBuffer.Prepare", calls);
            Assert.Contains("Eternal.UI.HediffSettings.RateEditBuffer.Commit", calls);
            Assert.Contains("UnityEngine.GUI.get_enabled", calls);
            Assert.Contains(Drawer + ".DrawDisabledSlider", calls);
        }

        [Fact]
        public void LiveExamples_ResolveSelectionWithoutMainMenuUnsafeGetters()
        {
            var draw = MenuAssemblyContract.Instructions(Drawer, "DrawStatusTab");
            Assert.Contains(draw, i => Equals(i.Operand, "Verse.Find.get_UIRoot"));
            Assert.Contains(draw, i => i.Code == OpCodes.Isinst && Equals(i.Operand, "RimWorld.UIRoot_Play"));
            Assert.DoesNotContain(draw, i => Equals(i.Operand, "Verse.Find.get_Selector")
                || Equals(i.Operand, "Verse.Find.get_MapUI"));
        }

        [Fact]
        public void Window_CleansListingAndScrollEvenWhenContentThrows()
        {
            var cleanup = MenuAssemblyContract.ReachableInstructions(Drawer, "DoWindowContents", finallyOnly: true);
            Assert.Contains(cleanup, i => Equals(i.Operand, "Verse.Listing.End"));
            Assert.Contains(cleanup, i => Equals(i.Operand, "Verse.Widgets.EndScrollView"));
        }

        [Fact]
        public void AdvancedEditor_IsEmbeddedInsteadOfOpeningManagerWindow()
        {
            Assert.Equal("System.Object", MenuAssemblyContract.BaseType(View));
            var draw = MenuAssemblyContract.ReachableInstructions(Drawer, "DrawAdvancedHediffSettings");
            Assert.Contains(draw, i => Equals(i.Operand, View + ".Draw"));
            Assert.DoesNotContain(draw, i => Equals(i.Operand, "Verse.WindowStack.Add"));
            Assert.DoesNotContain(draw, i => Equals(i.Operand, "Open Hediff Manager"));
        }

        [Fact]
        public void HostRetainsDrawerAndDrawerRetainsOneEditorBoundToManager()
        {
            Assert.Contains("Eternal.UI.Settings.SettingsDrawer..ctor",
                MenuAssemblyContract.Calls("Eternal.Eternal_Settings", "DoWindowContents"));
            var draw = MenuAssemblyContract.ReachableInstructions(Drawer, "DrawAdvancedHediffSettings");
            Assert.Contains(draw, i => i.Code == OpCodes.Ldfld && Equals(i.Operand, "hediffView"));
            Assert.Contains(draw, i => i.Code == OpCodes.Stfld && Equals(i.Operand, "hediffView"));
            Assert.Contains(draw, i => Equals(i.Operand, "hediffManager"));
            Assert.Contains(View + ".Draw", draw.Select(i => i.Operand));
            Assert.Contains(Presenter + "..ctor", MenuAssemblyContract.Calls(View, ".ctor"));
            Assert.Contains("Eternal.UI.HediffSettings.EternalHediffModel..ctor", MenuAssemblyContract.Calls(View, ".ctor"));
        }

        [Fact]
        public void OnlyDrawerOwnsScrollAndGeneralUsesCachedVirtualRows()
        {
            Assert.Contains("Verse.Widgets.BeginScrollView", MenuAssemblyContract.Calls(Drawer, "DoWindowContents"));
            var draw = MenuAssemblyContract.ReachableInstructions(View, "Draw");
            Assert.DoesNotContain(draw, i => Equals(i.Operand, "Verse.Widgets.BeginScrollView"));
            Assert.Contains(draw, i => Equals(i.Operand, Presenter + ".GetFilteredHediffsCached"));
            Assert.Contains(draw, i => Equals(i.Operand, "Eternal.UI.HediffSettings.EmbeddedHediffLayout.GetVisibleRange"));
            Assert.Contains("Eternal.UI.HediffSettings.ListVirtualization.GetVisibleRange",
                MenuAssemblyContract.Calls("Eternal.UI.HediffSettings.EmbeddedHediffLayout", "GetVisibleRange"));
            Assert.Contains(draw, i => Equals(i.Operand, View + ".DrawBulkTabContent"));
        }

        [Fact]
        public void HidingTransitionsReleaseFocusWithoutReplacingRetainedState()
        {
            foreach (var method in new[] { "SelectTab", "SetHediffExpanded" })
            {
                var il = MenuAssemblyContract.ReachableInstructions(Drawer, method);
                Assert.Contains(il, i => Equals(i.Operand, "UnityEngine.GUI.FocusControl"));
                Assert.DoesNotContain(il, i => i.Code == OpCodes.Stfld && Equals(i.Operand, "hediffView"));
                Assert.DoesNotContain(il, i => Equals(i.Operand, Presenter + "..ctor"));
                Assert.DoesNotContain(il, i => (i.Operand as string)?.Contains("ClearAllFilters") == true
                    || (i.Operand as string)?.Contains("ClearSelection") == true
                    || Equals(i.Operand, Presenter + ".OnTabSelected"));
                Assert.DoesNotContain(il, i => (i.Operand as string)?.Contains("SaveSettings") == true);
            }
            Assert.Contains("UnityEngine.GUI.FocusControl", MenuAssemblyContract.Calls(View, "SelectTab"));
            Assert.DoesNotContain(MenuAssemblyContract.Instructions(View, "SelectTab"),
                i => i.Code == OpCodes.Stfld);
        }

        [Fact]
        public void PageMeasuresOneColumnAndClampsBeforeAndAfterItsSoleScrollView()
        {
            var page = MenuAssemblyContract.Instructions(Drawer, "DoWindowContents");
            string clamp = "Eternal.UI.HediffSettings.EmbeddedHediffLayout.ClampScroll";
            int beginScroll = page.FindIndex(i => Equals(i.Operand, "Verse.Widgets.BeginScrollView"));
            int endScroll = page.FindIndex(i => Equals(i.Operand, "Verse.Widgets.EndScrollView"));
            var clamps = page.Select((instruction, index) => (instruction, index))
                .Where(pair => Equals(pair.instruction.Operand, clamp)).Select(pair => pair.index).ToArray();
            Assert.Equal(2, clamps.Length);
            Assert.True(clamps[0] < beginScroll && clamps[1] > endScroll);
            Assert.Equal(1, page.Count(i => Equals(i.Operand, "Verse.Widgets.BeginScrollView")));
            int singleColumn = page.FindIndex(i => i.Code == OpCodes.Stfld && Equals(i.Operand, "Verse.Listing.maxOneColumn"));
            int listingBegin = page.FindIndex(i => Equals(i.Operand, "Verse.Listing.Begin"));
            Assert.True(singleColumn >= 0 && singleColumn < listingBegin);
            Assert.Equal(OpCodes.Ldc_I4_1, page[singleColumn - 1].Code);
            var advanced = MenuAssemblyContract.Calls(Drawer, "DrawCurrentTabContent");
            Assert.True(System.Array.IndexOf(advanced, Drawer + ".DrawMapProtectionSettings") >
                System.Array.IndexOf(advanced, Drawer + ".DrawAdvancedHediffSettings"));
            Assert.Contains(Drawer + ".DrawStatusTab", advanced);
            Assert.Contains(Drawer + ".DrawThresholdsTab", advanced);
        }

        [Fact]
        public void AllocationAndDrawShareWidthDependentMeasurementsAndFullLogicalListHeight()
        {
            var height = MenuAssemblyContract.ReachableInstructions(View, "GetContentHeight");
            var draw = MenuAssemblyContract.ReachableInstructions(View, "Draw");
            foreach (var method in new[] { "HeaderHeight", "MeasureFilters", "BulkHeight" })
            {
                Assert.Contains(height, i => Equals(i.Operand, View + "." + method));
                Assert.Contains(draw, i => Equals(i.Operand, View + "." + method));
            }
            Assert.Contains(height, i => Equals(i.Operand, "Verse.Text.CalcHeight"));
            Assert.Contains(height, i => Equals(i.Operand, "Eternal.UI.HediffSettings.EmbeddedHediffLayout.ListHeight"));
            Assert.Contains(draw, i => Equals(i.Operand, "Eternal.UI.HediffSettings.EmbeddedHediffLayout.ListHeight"));
            var section = MenuAssemblyContract.Instructions(Drawer, "DrawAdvancedHediffSettings");
            int drawIndex = section.FindIndex(i => Equals(i.Operand, View + ".Draw"));
            Assert.Contains(section.Skip(drawIndex + 1), i => Equals(i.Operand, View + ".GetContentHeight"));
            Assert.DoesNotContain(section, i => Equals(i.Operand, "Back"));
        }

        [Fact]
        public void EditsKeepValidationProtectionDirtyCacheAndExistingSaveBoundary()
        {
            var row = MenuAssemblyContract.ReachableInstructions(View, "DrawHediffEntry");
            foreach (string call in new[] { "Eternal.Compatibility.RjwHealthPolicy.IsProtected", Presenter + ".NotifyHediffSettingChanged",
                Presenter + ".OnResetSingleHediff", "Eternal.EternalHediffSetting.ResetToGlobalRate", "Eternal.UI.MenuValueCalculator.EditedRate" })
            {
                Assert.Contains(row, i => Equals(i.Operand, call));
            }
            Assert.Contains("Eternal.UI.HediffSettings.EternalHediffModel.MarkFilterDirty", MenuAssemblyContract.Calls(Presenter, "NotifyHediffSettingChanged"));
            var save = MenuAssemblyContract.Calls("Eternal.Eternal_Mod", "WriteSettings");
            Assert.True(System.Array.IndexOf(save, "Verse.Mod.WriteSettings") >= 0);
            Assert.True(System.Array.IndexOf(save, "Verse.Mod.WriteSettings") < System.Array.IndexOf(save, "Eternal.Eternal_Settings.SaveHediffSettings"));
        }

        [Fact]
        public void HeaderConfirmationAndBulkImmediateResetRemainDistinct()
        {
            Assert.Contains("Verse.Dialog_MessageBox..ctor", MenuAssemblyContract.Calls(View, "DrawHeader"));
            // Compiler-generated callbacks are separately reachable through their delegate target.
            var header = MenuAssemblyContract.ReachableInstructions(View, "DrawHeader");
            Assert.Contains(header, i => Equals(i.Operand, Presenter + ".OnResetAllToDefaults"));
            Assert.Contains(Presenter + ".OnResetAllToDefaults", MenuAssemblyContract.Calls(View, "DrawBulkTabContent"));
            Assert.DoesNotContain("Verse.Dialog_MessageBox..ctor", MenuAssemblyContract.Calls(View, "DrawBulkTabContent"));
        }

        [Fact]
        public void DebugProbeTargetsExpandedSettingsAndRestoresDrawerState()
        {
#if DEBUG
            const string probe = "Eternal.InGameTests.Helpers.NativeUiProbe";
            var stage = MenuAssemblyContract.ReachableInstructions(probe, "OpenStage");
            Assert.Contains(stage, i => Equals(i.Operand, Drawer + ".SetHediffExpanded"));
            Assert.Contains(stage, i => Equals(i.Operand, Drawer + ".SelectTab"));
            Assert.DoesNotContain(stage, i => (i.Operand as string)?.Contains("HediffWindow") == true);
            Assert.Contains(MenuAssemblyContract.ReachableInstructions(probe, "Dispose"),
                i => Equals(i.Operand, Drawer + ".RestoreNavigation"));
            Assert.Contains(MenuAssemblyContract.Instructions(probe, "Draw"),
                i => Equals(i.Operand, Drawer + ".get_HediffSectionRendered"));
            Assert.DoesNotContain("Verse.Dialog_ModSettings.PreClose",
                MenuAssemblyContract.Calls(probe + "+SettingsWindow", "PreClose"));
#else
            Assert.Null(MenuAssemblyContract.BaseType("Eternal.InGameTests.Helpers.NativeUiProbe"));
#endif
        }
    }
}
