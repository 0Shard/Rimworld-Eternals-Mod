using Eternal.Settings;
using Eternal.Tests.UI;
using System.Linq;
using Xunit;

namespace Eternal.Tests.Settings
{
    public class HediffSettingsDurabilityTests
    {
        [Fact]
        public void UnresolvedExplicitCanHealOverrideSurvivesUnknownDefinitionDefaults()
        {
            Assert.True(HediffPersistencePolicy.ShouldPersistSetting(false, false));
            Assert.True(HediffPersistencePolicy.ShouldPersistSetting(false, true));
            Assert.True(HediffPersistencePolicy.ShouldPersistSetting(true, true));
            Assert.False(HediffPersistencePolicy.ShouldPersistSetting(true, false));
        }

        [Fact]
        public void ExplicitResetPrunesUnresolvedAndLegacyRecordsAfterRejectedBackupGuard()
        {
            const string store = "Eternal.Settings.HediffSettingsStore";
            var reset = MenuAssemblyContract.ReachableInstructions(store, "ResetAllAndDeleteXml");
            Assert.Contains(reset, i => Equals(i.Operand, "Verse.DefDatabase`1<Verse.HediffDef>.GetNamedSilentFail"));
            Assert.Contains(reset, i => (i.Operand as string)?.EndsWith(".Remove") == true);
            var il = MenuAssemblyContract.Instructions(store, "ResetAllAndDeleteXml");
            int backup = il.FindIndex(i => Equals(i.Operand, "Eternal.Settings.HediffSettingsXmlStore.BackupRejectedSource"));
            int remove = il.FindIndex(i => (i.Operand as string)?.EndsWith(".Remove") == true);
            int clear = il.FindIndex(i => (i.Operand as string)?.EndsWith(".Clear") == true);
            int defaults = il.FindIndex(i => Equals(i.Operand, store + ".ResetAll"));
            Assert.True(backup >= 0 && remove > backup && clear > remove && defaults > clear);
            Assert.Contains(il.Take(clear), i => Equals(i.Operand, "savedSettings"));
        }

        [Fact]
        public void FreshAndLoadedRootsScheduleOnceAndSaveOnlyAfterInitialization()
        {
            const string root = "Eternal.Eternal_Settings";
            const string schedule = root + ".ScheduleHediffSettingsInitialization";
            Assert.Contains(schedule, MenuAssemblyContract.Calls("Eternal.Eternal_Mod", ".ctor"));
            Assert.Contains(schedule, MenuAssemblyContract.Calls(root, "ExposeData"));
            var guard = MenuAssemblyContract.Calls(root, "ScheduleHediffSettingsInitialization");
            Assert.Contains(MenuAssemblyContract.Instructions(root, "ScheduleHediffSettingsInitialization"),
                instruction => Equals(instruction.Operand, "Verse.Scribe.mode"));
            Assert.Contains("Verse.LongEventHandler.ExecuteWhenFinished", guard);
            var save = MenuAssemblyContract.Instructions(root, "SaveHediffSettings");
            Assert.Contains(save, instruction => Equals(instruction.Operand, "hediffSettingsInitialized"));
            Assert.Contains(save, instruction => Equals(instruction.Operand, schedule));
        }

        [Fact]
        public void RejectedXmlOutcomeGatesOrdinarySaveAndExplicitResetMakesBackupFirst()
        {
            const string store = "Eternal.Settings.HediffSettingsStore";
            var save = MenuAssemblyContract.Instructions(store, "SaveToXml");
            Assert.Contains(save, instruction => Equals(instruction.Operand, store + ".get_XmlLoadOutcome"));
            var load = MenuAssemblyContract.Instructions(store, "LoadFromXml");
            Assert.Contains(load, instruction => Equals(instruction.Operand, store + ".set_XmlLoadOutcome"));
            var reset = MenuAssemblyContract.Calls(store, "ResetAllAndDeleteXml");
            Assert.True(System.Array.IndexOf(reset, "Eternal.Settings.HediffSettingsXmlStore.BackupRejectedSource") <
                System.Array.IndexOf(reset, "Eternal.Settings.HediffSettingsXmlStore.Delete"));
        }

        [Fact]
        public void GlobalNormalizationRunsOnLoadAndWriteBoundaries()
        {
            var calls = MenuAssemblyContract.Calls("Eternal.UI.Settings.SettingsValidator", "ValidateSettings");
            Assert.Equal(6, calls.Count(call => call == "Eternal.SettingsDefaults.NormalizeGlobalFloat"));
            foreach (string method in new[] { "ExposeData", "ValidateBeforeWrite" })
            {
                Assert.Contains("Eternal.UI.Settings.SettingsValidator.ValidateSettings",
                    MenuAssemblyContract.Calls("Eternal.Eternal_Settings", method));
            }
            Assert.DoesNotContain("Eternal.UI.Settings.SettingsValidator.ValidateSettings",
                MenuAssemblyContract.ReachableInstructions("Eternal.Eternal_Settings", "DoWindowContents").Select(instruction => instruction.Operand));
        }
    }
}
