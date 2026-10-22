// Relative Path: Eternal/Source/Eternal.Tests/Healing/CorpsePersistenceGateTests.cs
// Creation Date: 12-10-2026
// Author: 0Shard
// Description: Executed queue/resume scalars plus PE persistence/owner contracts; no game-bound CLR loading.
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection.Emit;
using System.Xml.Linq;
using Eternal.Healing;
using Eternal.Tests.Infrastructure;
using Eternal.UI;
using Eternal.Tests.UI;
using Xunit;

namespace Eternal.Tests.Healing
{
    public class CorpsePersistenceGateTests
    {
        [Fact]
        public void DeadPawnReferenceIsScribedEvenThoughNativePawnThingIsDestroyed()
        {
            var il = MenuAssemblyContract.Instructions("Eternal.Models.CorpseTrackingEntry", "ExposeData");
            int pawn = il.FindIndex(i => i.Code == OpCodes.Ldstr && Equals(i.Operand, "originalPawn"));
            Assert.True(pawn >= 0);
            Assert.Equal(OpCodes.Ldc_I4_1, il[pawn + 1].Code);
            Assert.Equal("Verse.Scribe_References.Look", il[pawn + 2].Operand);
        }

        [Fact]
        public void LegacyNullPawnReferenceIsRecoveredFromSurvivingCorpseBeforeIndexValidation()
        {
            const string entry = "Eternal.Models.CorpseTrackingEntry";
            var repair = MenuAssemblyContract.Instructions(entry, "RestoreCorpseReferencesAfterLoad");
            int innerPawn = repair.FindIndex(i => Equals(i.Operand, "Verse.Corpse.get_InnerPawn"));
            int owner = repair.FindIndex(i => i.Code == OpCodes.Stfld && Equals(i.Operand, "OriginalPawn"));
            Assert.True(innerPawn >= 0 && owner > innerPawn);
            Assert.Contains(repair, i => Equals(i.Operand, "Verse.Thing.get_Map"));
            Assert.DoesNotContain(repair, i => Equals(i.Operand, "Verse.Thing.get_Destroyed"));
            Assert.Contains(MenuAssemblyContract.Instructions(entry, "ExposeData"), i =>
                Equals(i.Operand, entry + ".RestoreCorpseReferencesAfterLoad"));
            var rebuild = MenuAssemblyContract.Instructions("Eternal.Corpse.EternalCorpseManager", "RebuildIndexesAfterLoad");
            int restore = rebuild.FindIndex(i => Equals(i.Operand, entry + ".RestoreCorpseReferencesAfterLoad"));
            int validate = rebuild.FindIndex(i => Equals(i.Operand, entry + ".IsValid"));
            Assert.True(restore >= 0 && validate > restore);
        }

        [Fact]
        public void LoadedQueueReferencesRebindOnlyAfterCorpseOwnerRecoveryAndRetainBodyHediffs()
        {
            const string entry = "Eternal.Models.CorpseTrackingEntry";
            var exposed = MenuAssemblyContract.Instructions(entry, "ExposeData");
            int owner = exposed.FindIndex(i => Equals(i.Operand, entry + ".RestoreCorpseReferencesAfterLoad"));
            int rebind = exposed.FindIndex(i => Equals(i.Operand, entry + ".RebindHealingQueueReferences"));
            Assert.True(owner >= 0 && rebind > owner);
            var queue = MenuAssemblyContract.Instructions(entry, "RebindQueue");
            int pawn = queue.FindIndex(i => Equals(i.Operand, "Eternal.HealingItem.set_Pawn"));
            int hediff = queue.FindIndex(i => Equals(i.Operand, "Eternal.HealingItem.TryRebindHediff"));
            Assert.True(pawn >= 0 && hediff > pawn);
            var identity = MenuAssemblyContract.ReachableInstructions("Eternal.HealingItem", "TryRebindHediff");
            Assert.Contains(identity, i => Equals(i.Operand, "Verse.Hediff.loadID"));
            Assert.DoesNotContain(identity, i => Equals(i.Operand, "Verse.Pawn_HealthTracker.AddHediff"));
            Assert.DoesNotContain(queue, i => Equals(i.Operand, "Eternal.HealingItem.set_Severity"));
            Assert.DoesNotContain(queue, i => (i.Operand as string)?.EndsWith(".Clear") == true);
        }

        [Fact]
        public void FinalizedCorpseComponentsReattachBeforeMapOnlyLocationCommitAndHealingHydration()
        {
            const string manager = "Eternal.Corpse.EternalCorpseManager";
            var reconcile = MenuAssemblyContract.Instructions(manager, "ReconcileLoadedCorpseLocations");
            int attach = reconcile.FindIndex(i => Equals(i.Operand, manager + ".AddCorpseComponent"));
            int cache = reconcile.FindIndex(i => Equals(i.Operand, "Eternal.Models.CorpseTrackingEntry.CacheComponents"));
            int spawned = reconcile.FindIndex(i => Equals(i.Operand, "Verse.Thing.get_Spawned"));
            int commit = reconcile.FindIndex(i => Equals(i.Operand, manager + ".CommitCorpseLocation"));
            Assert.True(attach >= 0 && cache > attach && spawned > cache && commit > spawned);
            var finalize = MenuAssemblyContract.Instructions("Eternal.Eternal_Component", "FinalizeInit");
            int repair = finalize.FindIndex(i => Equals(i.Operand, manager + ".ReconcileLoadedCorpseLocations"));
            int healing = finalize.FindIndex(i => Equals(i.Operand,
                "Eternal.Healing.EternalCorpseHealingProcessor.RebuildActiveHealingCorpses"));
            Assert.True(repair >= 0 && healing > repair);
        }

        private sealed class ScalarItem
        {
            public float Severity;
            public float EnergyCost;
            public int HediffLoadId;
        }

        [Fact]
        public void CompactCompletedQueue_RemovesCompletedItemsInOnePassAndPreservesOrder()
        {
            var first = new ScalarItem { Severity = 1f };
            var completed = new ScalarItem { Severity = 0f };
            var third = new ScalarItem { Severity = 3f };
            var fourth = new ScalarItem { Severity = 4f };
            var queue = new List<ScalarItem> { first, completed, third, null, fourth };
            HealingQueueSelector.Compact(queue, new HashSet<ScalarItem> { completed });
            Assert.Equal(new[] { first, third, fourth }, queue);
            Assert.Contains("Eternal.UI.HealingQueueSelector.Compact",
                MenuAssemblyContract.Calls("Eternal.Healing.EternalCorpseHealingProcessor", "CompactCompletedQueue"));
        }

        [Fact]
        public void HealingItem_ExposesStableHediffLoadIdForRebinding()
        {
            var serialize = MenuAssemblyContract.Instructions("Eternal.HealingItem", "ExposeData");
            Assert.Contains(serialize, i => Equals(i.Operand, "hediffLoadId"));
            Assert.Contains(serialize, i => Equals(i.Operand, "severity"));
            Assert.Contains(serialize, i => Equals(i.Operand, "energyCost"));
            Assert.Contains(serialize, i => Equals(i.Operand, "Verse.Hediff.loadID"));
            Assert.Contains(MenuAssemblyContract.Instructions("Eternal.HealingItem", "TryRebindHediff"),
                i => Equals(i.Operand, "Verse.Pawn_HealthTracker.hediffSet"));
        }

        [Fact]
        public void CorpseTrackingEntry_ExposesQueueHydrationStateAndStableScribeKeys()
        {
            var serialize = MenuAssemblyContract.Instructions("Eternal.Models.CorpseTrackingEntry", "ExposeData");
            foreach (string key in new[] { "healingQueue", "preCalculatedHealingQueue", "healingQueueHydrated", "foodDebt", "healingProgress", "isHealingActive" })
            {
                int index = serialize.FindIndex(i => i.Code == OpCodes.Ldstr && Equals(i.Operand, key));
                Assert.True(index >= 0);
                Assert.Contains(serialize.Skip(index).Take(12), i => (i.Operand as string)?.StartsWith("Verse.Scribe_") == true);
            }
            int hydrated = serialize.FindIndex(i => i.Code == OpCodes.Ldstr && Equals(i.Operand, "healingQueueHydrated"));
            Assert.Equal(OpCodes.Ldc_I4_0, serialize[hydrated + 1].Code);
            Assert.Contains("Eternal.UI.HealingQueueSelector.Select", MenuAssemblyContract.Calls(
                "Eternal.Models.CorpseTrackingEntry", "TryHydrateHealingQueue"));
            Assert.Contains("Eternal.Models.CorpseTrackingEntry.RebindHealingQueueReferences", MenuAssemblyContract.Calls(
                "Eternal.Models.CorpseTrackingEntry", "TryHydrateHealingQueue"));
        }

        [Fact]
        public void CorpseHealingProcessor_ExposesPostLoadHydrationOwner()
        {
            Assert.Contains("Eternal.Healing.EternalCorpseHealingProcessor.EnsureHealingQueueHydrated",
                MenuAssemblyContract.Calls("Eternal.Healing.EternalCorpseHealingProcessor", "RebuildActiveHealingCorpses"));
            Assert.Contains("Eternal.Models.CorpseTrackingEntry.TryHydrateHealingQueue",
                MenuAssemblyContract.Calls("Eternal.Healing.EternalCorpseHealingProcessor", "EnsureHealingQueueHydrated"));
        }

        [Fact]
        public void CorpseManager_ExposesManagerOwnedRelocationOperation()
        {
            using var assembly = CompiledBuildReferences.OpenProduction();
            assembly.Method("Eternal.Corpse.EternalCorpseManager", "TryRelocateCorpse",
                new[] { "Verse.Pawn", "Verse.Map", "Verse.IntVec3" }, "System.Boolean", isStatic: false);
            var calls = MenuAssemblyContract.Calls("Eternal.Corpse.EternalCorpseManager", "TryRelocateCorpse");
            Assert.Contains(calls, c => c != null && c.Contains("GenSpawn.Spawn"));
            Assert.Contains(calls, c => c != null && c.Contains("UpdateLocation"));
        }

        [Fact]
        public void ScalarSaveResume_PreservesPriorDebtMetadataSourceAndFutureTailCharge()
        {
            float incurredDebt = 2.75f;
            var entry = new ScalarItem { Severity = 0.015f, EnergyCost = 123f, HediffLoadId = 4815 };
            var fixture = new XElement("resume",
                new XAttribute("debt", incurredDebt.ToString("R", CultureInfo.InvariantCulture)),
                new XAttribute("severity", entry.Severity.ToString("R", CultureInfo.InvariantCulture)),
                new XAttribute("energyCost", entry.EnergyCost), new XAttribute("hediffLoadId", entry.HediffLoadId));
            var loaded = XElement.Parse(fixture.ToString());
            var item = new ScalarItem
            {
                Severity = (float)loaded.Attribute("severity"),
                EnergyCost = (float)loaded.Attribute("energyCost"),
                HediffLoadId = (int)loaded.Attribute("hediffLoadId")
            };
            float prior = (float)loaded.Attribute("debt");
            var active = new List<ScalarItem> { item };
            var death = new List<ScalarItem> { new ScalarItem { Severity = 4f } };
            var selected = HealingQueueSelector.Select(true, active, death, () => new[] { new ScalarItem { Severity = 99f } });
            Assert.Equal(HealingQueueSource.Active, selected.Source);
            Assert.Same(active, selected.Items);
            var step = CorpseHealingCostCalculator.CalculateStep(selected.Items[0].Severity, 0.01f, 0.004f, 2f);
            Assert.Equal(0.00012f, step.NutritionCharge, 7);
            Assert.Equal(2.75012f, prior + step.NutritionCharge, 5);
            Assert.Equal(incurredDebt, prior);
            Assert.Equal(0.015f, item.Severity);
            Assert.Equal(123f, item.EnergyCost);
            Assert.Equal(4815, item.HediffLoadId);
            Assert.Equal(4f, death[0].Severity);
        }
    }
}
