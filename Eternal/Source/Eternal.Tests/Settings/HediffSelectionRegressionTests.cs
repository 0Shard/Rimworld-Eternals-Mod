using System.Linq;
using Eternal.Settings;
using Eternal.Tests.UI;
using Xunit;

namespace Eternal.Tests.Settings
{
    public class HediffSelectionRegressionTests
    {
        [Fact]
        public void SelectAllCallerUsesFilteredNamesAndSelectedApplicationEnumeratesThem()
        {
            var manager = MenuAssemblyContract.Calls("Eternal.EternalHediffManager", "SelectAllHediffs");
            Assert.Contains("Eternal.EternalHediffManager.GetFilteredHediffs", manager);
            Assert.Contains("Eternal.Settings.HediffSelectionManager.SelectAll", manager);
            var apply = MenuAssemblyContract.Calls("Eternal.UI.HediffSettings.EternalHediffPresenter", "OnApplyToSelected");
            Assert.Contains("Eternal.UI.HediffSettings.EternalHediffModel.GetSelectedHediffs", apply);
            Assert.Contains("Eternal.UI.HediffSettings.EternalHediffModel.ApplySettingsToGroupWithCount", apply);
        }

        [Fact]
        public void SelectAllEnumeratesItsConcreteNamesAndDeselectPreservesOthers()
        {
            var selection = new HediffSelectionManager();
            selection.SelectAll(new[] { "Flu", "Burn", "Burn" });
            Assert.Equal(new[] { "Burn", "Flu" }, selection.GetSelected().OrderBy(name => name));
            Assert.Equal(2, selection.Count);
            Assert.False(selection.IsSelected("OutsideFilter"));
            selection.Deselect("Flu");
            Assert.Equal(new[] { "Burn" }, selection.GetSelected());
            Assert.Equal(1, selection.Count);
            Assert.True(selection.IsSelected("Burn"));
            Assert.False(selection.IsSelected("Flu"));
        }

        [Fact]
        public void SelectionSnapshotCanApplyToEveryRemainingName()
        {
            var selection = new HediffSelectionManager();
            selection.Select("OutsideFilter");
            selection.SelectAll(new[] { "A", "B", "C" });
            selection.Deselect("B");
            var applied = selection.GetSelected().ToDictionary(name => name, name => true);
            Assert.Equal(new[] { "A", "C" }, applied.Keys.OrderBy(name => name));
            selection.ClearAll();
            Assert.Empty(selection.GetSelected());
            Assert.Equal(0, selection.Count);
        }
    }
}
