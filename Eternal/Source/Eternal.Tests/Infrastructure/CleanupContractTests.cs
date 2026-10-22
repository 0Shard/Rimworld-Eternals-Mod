// Author: 0Shard
// Description: Removed API absence and surviving selection, menu and snapshot wiring.
using System;
using System.Linq;
using System.Reflection.Emit;
using Eternal.Settings;
using Xunit;

namespace Eternal.Tests.Infrastructure
{
    public class CleanupContractTests
    {
        [Fact]
        public void ObsoletePublicShortlistIsAbsentFromCompiledProduction()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            foreach (var row in new[]
            {
                ("Eternal.Caravan.EternalCaravanDeathHandler", "GetPendingTeleportations"),
                ("Eternal.Compatibility.RimImmortalDetection", "LogRimImmortalStatus,ResetTypeCache"),
                ("Eternal.Compatibility.SpaceModDetection", "LogSpaceModStatus"),
                ("Eternal.Compatibility.ItsSorceryDetection", "ResetTypeCache"),
                ("Eternal.Compatibility.CultivatorOfTheRimDetection", "ResetTypeCache"),
                ("Eternal.Constants.CriticalPartConstants", "IsSensoryPart"),
                ("Eternal.Extensions.BodyPartExtensions", "RequiresSequencedRegrowth"),
                ("Eternal.Corpse.EternalCorpsePreservation", "IsPreserved,GetPreservedCorpses,GetPreservedCorpsesOnMap"),
                ("Eternal.EternalHealingPriority", "IsCriticalHediff,IsCriticalBodyPart,FormatResurrectionTime"),
                ("Eternal.Healing.EternalResurrectionCalculator", "CalculateEstimatedTime"),
                ("Eternal.EternalRegrowthState", "GetPhaseDisplayName,InitializeMissingParts,ExposeData,set_partPhases,set_partProgress,GetCriticalPartsInOrder"),
                ("Eternal.Settings.HediffFilterState", "HasActiveFilters"),
                ("Eternal.Settings.HediffSelectionManager", "get_HasSelection,get_IsSelectAllMode,SetTotalCount"),
                ("Eternal.EternalHediffManager", "SetHediffSetting,RemoveHediffSetting,ResetSelected,ResetByCategory,ResetByModSource"),
                ("Eternal.HediffStatistics", "get_EnabledHediffs,get_HealingPercentage,get_CustomizedPercentage"),
                ("Eternal.EternalHediffSetting", "HasAnyCustomSettings"),
                ("Eternal.Healing.HediffHealingConfig", "CreateHarmfulSetting,CreateNeutralSetting,GetMedicineRequirement"),
                ("Eternal.UI.Settings.SettingsValidator", "ValidateHealAmount,ValidateNutritionCostMultiplier,ValidateTickRate,ValidateLoggingLevel"),
                ("Eternal.EternalHediffHealer", "EmergencyHealAllHarmfulHediffs"),
                ("Eternal.Healing.Scars.ScarCostCalculator", "HasSufficientNutrition,ResetDefault"),
                ("Eternal.Models.CorpseTrackingEntry", "InvalidateComponentCache")
            })
            {
                foreach (string name in row.Item2.Split(','))
                {
                    Assert.Empty(assembly.Methods(row.Item1, name));
                }
            }

            foreach (string type in new[] { "Eternal.Compatibility.EternalCompatibilityManager",
                "Eternal.Compatibility.ShipDestructionHandler", "Eternal.Models.RegrowthPartState",
                "Eternal.CriticalPartSequence", "Eternal.Interfaces.IEternalServices",
                "Eternal.MedicineRequirement", "Eternal.HealingOrder" })
            {
                Assert.False(assembly.TryType(type, out _), "Retired type remains: " + type);
            }
            Assert.Empty(assembly.Methods("Eternal.EternalRegrowthState", ".ctor")
                .Where(h => assembly.Signature(h).ParameterTypes.Length == 0));
            assembly.Method("Eternal.EternalRegrowthState", ".ctor", new[]
                { "Verse.Pawn", "System.Collections.Generic.List`1<Eternal.EternalRegrowing_Hediff>" });

            var fields = assembly.Reader.GetTypeDefinition(assembly.Type("Eternal.EternalHediffSetting")).GetFields();
            Assert.DoesNotContain(fields, h => new[]
            {
                "isExpanded", "isSearchMatch", "enabled", "isEnabled", "requireCureToResurrect",
                "maxSeverityThreshold", "allowAutoHeal", "requiresResources", "nutritionCost",
                "medicineRequirement", "healingInterval", "healPermanentInjuries", "healScars",
                "healMissingParts", "consumeExtraResources", "sortOrder", "maxSeverityThresholdOverride",
                "healSpeedOverride", "needToCureOverride", "regrowHediffOverride", "modContentPackId", "dlcName", "isPermanent"
            }.Contains(assembly.Reader.GetString(assembly.Reader.GetFieldDefinition(h).Name)));
        }

        [Fact]
        public void SettingsInitializationKeepsTheActualEngineLoadAndManagerConstructorRoute()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            using var engine = new CompiledAssemblyContract(CompiledBuildReferences.GameReferencePath);
            Assert.Empty(assembly.Methods("Eternal.Eternal_Mod", "Notify_SettingsLoaded"));
            Assert.Empty(engine.Methods("Verse.Mod", "Notify_SettingsLoaded", inherited: true));
            RequireCall(assembly, "Eternal.Eternal_Mod", ".ctor", "Verse.Mod.GetSettings");
            RequireCall(assembly, "Eternal.Eternal_Mod", ".ctor", "Eternal.EternalHediffManager..ctor");
            RequireCall(assembly, "Eternal.Eternal_Settings", ".ctor", "Eternal.EternalHediffManager..ctor");
            RequireCall(assembly, "Eternal.Eternal_Settings", "ExposeData", "Verse.Scribe_Deep.Look");
        }

        [Fact]
        public void RetainedSelectionAndFilterOperationsExecuteAgainstRealState()
        {
            var selection = new HediffSelectionManager();
            selection.Select(null);
            selection.Select("Burn");
            selection.Select("Burn");
            selection.Toggle("Cut");
            Assert.Equal(2, selection.Count);
            Assert.Equal(new[] { "Burn", "Cut" }, selection.GetSelected().OrderBy(n => n));
            selection.Toggle("Burn");
            Assert.False(selection.IsSelected("Burn"));
            selection.SelectAll(new[] { "Burn", "Cut" });
            Assert.Equal(2, selection.Count);
            Assert.False(selection.IsSelected("Unlisted"));
            selection.Deselect("Cut");
            Assert.Equal(1, selection.Count);
            Assert.True(selection.IsSelected("Burn"));
            Assert.False(selection.IsSelected("Unlisted"));
            selection.SelectAll(new[] { "Burn", "Cut", "Flu" });
            selection.ClearAll();
            Assert.Equal(0, selection.Count);
            Assert.Empty(selection.GetSelected());

            var filter = new HediffFilterState { SearchTerm = "Burn", ShowOnlyEnabled = true, ModSourceFilter = "Custom" };
            filter.SetPropertyFilters(true, true, true, true, true);
            filter.SetSourceTypeFilter(true, true, true);
            filter.ClearAll();
            Assert.Equal("", filter.SearchTerm);
            Assert.Equal("", filter.ModSourceFilter);
            Assert.Equal(HediffCategory.All, filter.CategoryFilter);
            Assert.All(new[] { filter.ShowOnlyEnabled, filter.ShowOnlyCustom, filter.FilterByBaseGame, filter.FilterByDLC,
                filter.FilterByMods, filter.FilterIsBad, filter.FilterIsLethal, filter.FilterIsTendable,
                filter.FilterIsChronic, filter.FilterBeneficial }, Assert.False);
        }

        [Fact]
        public void ActiveMenuResetAndBulkCallbacksKeepTheirProductionRoutes()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            foreach (string operation in new[] { "ResetAllToDefaults", "ResetSingleHediff", "SetHediffSelected" })
            {
                RequireCall(assembly, "Eternal.UI.HediffSettings.EternalHediffModel", operation, "Eternal.EternalHediffManager." + operation);
            }
            RequireCall(assembly, "Eternal.UI.HediffSettings.EternalHediffPresenter", "OnResetAllToDefaults", "Eternal.UI.HediffSettings.EternalHediffModel.ResetAllToDefaults");
            RequireCall(assembly, "Eternal.UI.HediffSettings.EternalHediffPresenter", "OnResetSingleHediff", "Eternal.UI.HediffSettings.EternalHediffModel.ResetSingleHediff");
            foreach (string operation in new[] { "OnApplyToSelected", "OnApplyToAll" })
            {
                RequireCall(assembly, "Eternal.UI.HediffSettings.EternalHediffPresenter", operation, "Eternal.UI.HediffSettings.EternalHediffModel.ApplySettingsToGroupWithCount");
            }

            RequireCall(assembly, "Eternal.EternalHediffManager", "ResetAllToDefaults", "Eternal.Settings.HediffSettingsStore.ResetAllAndDeleteXml");
            RequireCall(assembly, "Eternal.EternalHediffManager", "ResetSingleHediff", "Eternal.Settings.HediffSettingsStore.ResetSingle");
            RequireCall(assembly, "Eternal.EternalHediffManager", "ResetFiltered", "Eternal.Settings.HediffSettingsStore.ResetMany");
            foreach (string operation in new[] { "ResetAllToDefaults", "ResetSingleHediff", "ResetFiltered" })
            {
                RequireCall(assembly, "Eternal.EternalHediffManager", operation, "Eternal.EternalHediffManager.MarkFilterDirty");
            }

            RequireCall(assembly, "Eternal.Models.CorpseTrackingEntry", "ExposeData", "Eternal.Models.CorpseTrackingEntry.CacheComponents");
        }

        [Fact]
        public void SnapshotHasNoRetiredBridgesAndKeepsLiveGuardAndCadence()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            const string snapshot = "Eternal.Infrastructure.ImmutableSettingsSnapshot";
            Assert.False(assembly.TryType(snapshot + "+AdvancedHediffSection", out _));
            Assert.Empty(assembly.Methods(snapshot, "get_AdvancedHediff"));
            foreach (var section in new[]
            {
                ("HealingSection", "ShowEffects"),
                ("ResourceSection", "PauseOnResourceDepletion,MinimumNutritionThreshold,AllowResourceBorrowing"),
                ("MapSection", "MapProtectionAction")
            })
            {
                foreach (string member in section.Item2.Split(','))
                {
                    Assert.Empty(assembly.Methods(snapshot + "+" + section.Item1, "get_" + member));
                }
            }

            var tick = assembly.Instructions(assembly.Method("Eternal.Components.TickOrchestrator", "ProcessTick"));
            foreach (string member in new[] { "NormalTickRate", "RareTickRate", "TraitCheckInterval", "CorpseCheckInterval", "MapCheckInterval", "HealingHistorySweepInterval" })
            {
                Assert.Contains(tick, i => Equals(i.Operand, snapshot + "+PerfSection.get_" + member));
            }

            Assert.Contains(tick, i => Equals(i.Operand, snapshot + "+GeneralSection.get_ModEnabled"));
            RequireCall(assembly, "Eternal.Eternal_Settings", "CreateSnapshot", snapshot + "+GeneralSection.set_ModEnabled");
        }

        private static void RequireCall(CompiledAssemblyContract assembly, string type, string method, string target)
        {
            Assert.Contains(assembly.Instructions(assembly.Method(type, method)), i =>
                (i.Code == OpCodes.Call || i.Code == OpCodes.Callvirt || i.Code == OpCodes.Newobj) && Equals(i.Operand, target));
        }
    }
}
