// Author: 0Shard
// Description: Native recovery-site failure, persistent ownership, teardown and late acceptance rollback.
#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Eternal.DI;
using Eternal.InGameTests.Helpers;
using Eternal.InGameTests.Performance;
using Eternal.Models;
using Eternal.World;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using MapType = Verse.Map;
using CorpseType = Verse.Corpse;
using CaravanType = RimWorld.Planet.Caravan;

namespace Eternal.InGameTests.FailureScenarios
{
    public static class RecoverySiteFailureTests
    {
        private enum Removal { Holder, Destroy, Game }
        public static TestSuiteResult RunAll(MapType map)
        {
            var result = new TestSuiteResult(0, 0, new List<string>());
            result.RunTest("RecoverySiteFailure", "MapEntryRetainsFailedSubsetAndRetries", MapEntryFailure);
            result.RunTest("RecoverySiteFailure", "CaravanPickupLateFailureRetainsFailedSubset", PickupFailure);
            result.RunTest("RecoverySiteFailure", "LateSiteAcceptanceRollsBackBothOwners", LateAcceptanceFailure);
            result.RunTest("RecoverySiteFailure", "GroundCollectionIncludesDeadCorpseAndCellFilter", GroundCollection);
            result.RunTest("RecoverySiteFailure", "PublicInsertionAndNonDetachingHandoffPreserveInventory", PreserveHeldCorpse);
            result.RunTest("RecoverySiteFailure", "DistantPickupStartsNativeTravelAndRejectsPrematureArrival", DistantPickup);
            result.RunTest("RecoverySiteFailure", "GeneratedSiteNewDeliveryRetriesFailedSubset", GeneratedSiteDelivery);
            result.RunTest("RecoverySiteFailure", "OwnSiteProductionGenerationMakesContentsControllable", OwnSiteGeneration);
            result.RunTest("RecoverySiteFailure", "HeldMapCollectionUsesHeldPositionAndExcludesOtherCells", HeldCollection);
            result.RunTest("RecoverySiteFailure", "SoloAcceptanceReentrantHolderRemovalDefers", () => UnregisteredAcceptance(Removal.Holder));
            result.RunTest("RecoverySiteFailure", "SoloAcceptanceReentrantDestroyDefers", () => UnregisteredAcceptance(Removal.Destroy));
            foreach (bool late in new[] { false, true })
            {
                bool selected = late;
                result.RunTest("RecoverySiteFailure", (late ? "LateAcceptance" : "RejectedAcceptance") + "ProtectsSourceDestroyAndRollback",
                    () => SourceTransferFailure(Removal.Destroy, selected));
                result.RunTest("RecoverySiteFailure", (late ? "LateAcceptance" : "RejectedAcceptance") + "ProtectsSourceHolderRemovalAndRollback",
                    () => SourceTransferFailure(Removal.Holder, selected));
            }
            foreach (bool loaded in new[] { false, true })
            {
                bool selected = loaded;
                result.RunTest("RecoverySiteFailure", (loaded ? "Loaded" : "Unloaded") + "HolderRemovalFailsAndRetries",
                    () => RemovalFailure(selected, Removal.Holder));
                result.RunTest("RecoverySiteFailure", (loaded ? "Loaded" : "Unloaded") + "DestroyFailsAndRetries",
                    () => RemovalFailure(selected, Removal.Destroy));
            }
            result.RunTest("RecoverySiteFailure", "LoadedDirectGameRemovalFailsAndRetries",
                () => RemovalFailure(true, Removal.Game));
            return result;
        }

        private static void OwnSiteGeneration()
        {
            using (var fixture = new Fixture())
            {
                var site = fixture.CreateSite();
                var entry = fixture.CreateCorpse();
                fixture.Stage(site, entry);
                var previousSize = Find.World.info.initialMapSize;
                try
                {
                    Find.World.info.initialMapSize = new IntVec3(50, 1, 50);
                    TestAssert.IsTrue(site.GetGizmos().OfType<Command_Action>().Any(command => command.defaultLabel == "Enter recovery site"),
                        "Waiting player corpse must expose the selected-site control action without an external caravan");
                    var map = site.GenerateRecoveryMap();
                    TestAssert.IsNotNull(map, "Production native generation must return a map for this parent");
                    TestAssert.AreEqual(site, map.Parent, "Recovery generation must preserve the existing MapParent");
                    TestAssert.IsTrue(entry.Corpse.Spawned && entry.Corpse.Map == map && !site.HasPendingCorpse(entry.Corpse),
                        "Generated control route must place the actual stored corpse for resurrection");
                }
                finally
                {
                    Find.World.info.initialMapSize = previousSize;
                    foreach (var map in Find.Maps.Where(map => map.Parent == site).ToArray()) { fixture.Maps.OwnMap(map); }
                }
            }
        }

        private static void DistantPickup()
        {
            using (var fixture = new Fixture())
            {
                var site = fixture.CreateSite();
                var entry = fixture.CreateCorpse();
                fixture.Stage(site, entry);
                var carrier = fixture.Maps.SpawnNonEternalPawn(fixture.Maps.Create(50));
                var caravan = fixture.MakeCaravan(carrier, carrier.Tile);
                var pickup = new CaravanArrivalAction_EternalCrashSite(site, true);
                pickup.Arrived(caravan);
                TestAssert.IsTrue(site.HasPendingCorpse(entry.Corpse) && !caravan.AllThings.Contains(entry.Corpse),
                    "A premature arrival on a distant full PlanetTile must not transfer anything");
                var option = site.GetFloatMenuOptions(caravan).First(item => item.Label == "RescueEternalsWithCaravan".Translate());
                option.action();
                TestAssert.IsTrue(caravan.pather.Moving && caravan.pather.ArrivalAction is CaravanArrivalAction_EternalCrashSite,
                    "Actual menu action must start the native path with the serialized recovery arrival");
                var otherLayer = Find.WorldGrid.PlanetLayers.Values.FirstOrDefault(layer => layer != site.Tile.Layer);
                TestAssert.Available(otherLayer, "Full-layer arrival rejection requires a second actual native planet layer");
                TestAssert.IsFalse(pickup.StillValid(caravan, new PlanetTile(site.Tile.tileId, otherLayer.LayerID)),
                    "Same numeric tile on another layer is not the site's destination");
                caravan.pather.StopDead();
            }
        }

        private static void GeneratedSiteDelivery()
        {
            using (var fixture = new Fixture())
            {
                var site = fixture.CreateSite();
                var map = fixture.Maps.CreateWithParent(50, site, false);
                var entry = fixture.CreateCorpse();
                using (var fault = new Fault(entry.OriginalPawn, site, failSpawn: true))
                {
                    TestAssert.AreEqual(1, SpaceCrashRescueService.DeliverCorpsesToCrashSite(new[] { entry.Corpse }, site.Tile),
                        "A failed physical delivery must still commit the existing site's deep owner");
                    TestAssert.IsTrue(fault.SpawnFaults > 0 && site.HasPendingCorpse(entry.Corpse),
                        "Generated-site spawn failure must retain exactly the failed subset");
                }
                site.DeliverPendingContents();
                TestAssert.IsTrue(entry.Corpse.Spawned && entry.Corpse.Map == map && !site.HasPendingCorpse(entry.Corpse),
                    "Retry drains new contents onto the already-generated map without a second generation");
            }
        }

        private static void HeldCollection()
        {
            using (var fixture = new Fixture())
            {
                var map = fixture.Maps.Create(50);
                var carrier = fixture.Maps.SpawnNonEternalPawn(map);
                var entry = fixture.CreateCorpse();
                entry.Corpse.DeSpawn();
                TestAssert.IsTrue(carrier.inventory.innerContainer.TryAdd(entry.Corpse), "Native inventory must accept corpse");
                var living = new List<Pawn>(); var corpses = new List<CorpseType>();
                SpaceCrashRescueService.CollectEternalsOnMap(map, cell => cell == carrier.Position, living, corpses);
                TestAssert.IsTrue(corpses.Contains(entry.Corpse), "Held corpse collection follows MapHeld and PositionHeld");
                corpses.Clear();
                SpaceCrashRescueService.CollectEternalsOnMap(map, cell => cell != carrier.Position, living, corpses);
                TestAssert.IsFalse(corpses.Contains(entry.Corpse), "Area-limited teardown cannot claim a held corpse outside its cells");
            }
        }

        private static void SourceTransferFailure(Removal removal, bool late)
        {
            using (var fixture = new Fixture())
            {
                var source = fixture.CreateSite();
                var destination = fixture.CreateSite();
                var entry = fixture.CreateCorpse();
                fixture.Stage(source, entry);
                var snapshot = new Snapshot(entry);
                using (var fault = new Fault(entry.OriginalPawn, source,
                    reentrantSourceRemoval: removal, sourceRemovalAfterAcceptance: late))
                {
                    TestAssert.AreEqual(0, SpaceCrashRescueService.DeliverCorpsesToCrashSite(new[] { entry.Corpse }, destination.Tile),
                        "Rejected destination transaction must not report committed delivery");
                    TestAssert.AreEqual(1, fault.ReentrantCalls,
                        "Destination hook must attempt the actual native source teardown during its empty-list window");
                    TestAssert.AreEqual(1, fault.AcceptanceFaults, "The requested destination rejection/late exception must actually fire");
                }
                TestAssert.IsTrue(source.Spawned && !source.Destroyed && source.HasPendingCorpse(entry.Corpse),
                    "Failed delivery must retain the live source and restore its exact deep ownership");
                TestAssert.IsFalse(destination.HasPendingCorpse(entry.Corpse),
                    "Failed delivery must leave no destination deep-save duplicate");
                TestAssert.IsTrue(!entry.Corpse.Destroyed && !entry.Corpse.Spawned && entry.Corpse.holdingOwner == null,
                    "Failed delivery must leave the original corpse unspawned and exclusively deep-owned");
                var owners = Find.WorldObjects.AllWorldObjects.OfType<WorldObject_EternalCrashSite>()
                    .Where(site => site.HasPendingCorpse(entry.Corpse)).ToArray();
                TestAssert.AreEqual(1, owners.Length, "Rollback must establish exactly one persistent owner");
                TestAssert.AreEqual(source, owners[0], "Rollback owner must be the original source site");
                snapshot.AssertUnchanged();
                TestAssert.AreEqual(1, SpaceCrashRescueService.DeliverCorpsesToCrashSite(new[] { entry.Corpse }, destination.Tile),
                    "Released transaction reservation must permit a fault-free delivery retry");
                TestAssert.IsTrue(destination.HasPendingCorpse(entry.Corpse) && !source.HasPendingCorpse(entry.Corpse),
                    "Successful retry must commit only the destination deep owner");
                source.Destroy();
                TestAssert.IsTrue(source.Destroyed && !Find.WorldObjects.AllWorldObjects.Contains(source),
                    "Successful outer recovery must release its source reservation and permit ordinary empty-site destruction");
                snapshot.AssertUnchanged();
            }
        }

        private static void GroundCollection()
        {
            using (var fixture = new Fixture())
            {
                var map = fixture.Maps.Create(50);
                var livingPawn = fixture.Maps.SpawnEternalPawn(map);
                var entry = fixture.CreateCorpse();
                TestAssert.IsTrue(CellFinder.TryFindRandomCellNear(map.Center, map, 20,
                    cell => cell.Standable(map) && cell != livingPawn.Position, out var position),
                    "Collection fixture needs distinct standable living/corpse cells");
                TestAssert.IsTrue(EternalServiceContainer.Instance.CorpseManager.TryRelocateCorpse(entry.OriginalPawn, map, position),
                    "Actual manager transfer must place the real corpse on the collection map");
                var living = new List<Pawn>();
                var corpses = new List<CorpseType>();
                SpaceCrashRescueService.CollectEternalsOnMap(map, null, living, corpses);
                TestAssert.AreEqual(1, living.Count, "Ground sweep must include the exact living Eternal once");
                TestAssert.AreEqual(livingPawn, living[0], "Ground sweep must retain living pawn identity");
                TestAssert.AreEqual(1, corpses.Count, "Ground sweep must include a dead native Eternal corpse once");
                TestAssert.AreEqual(entry.Corpse, corpses[0], "Ground sweep must retain dead corpse identity");
                living.Clear();
                corpses.Clear();
                SpaceCrashRescueService.CollectEternalsOnMap(map, cell => cell == position, living, corpses);
                TestAssert.AreEqual(0, living.Count, "Corpse-cell filter must exclude the living pawn's different cell");
                TestAssert.AreEqual(1, corpses.Count, "Corpse-cell filter must still include the actual dead corpse");
                living.Clear();
                corpses.Clear();
                SpaceCrashRescueService.CollectEternalsOnMap(map, cell => cell == livingPawn.Position, living, corpses);
                TestAssert.AreEqual(1, living.Count, "Living-cell filter must include the living pawn");
                TestAssert.AreEqual(0, corpses.Count, "Living-cell filter must exclude the dead corpse's different cell");
            }
        }

        private static void PreserveHeldCorpse()
        {
            using (var fixture = new Fixture())
            {
                var site = fixture.CreateSite();
                var entry = fixture.CreateCorpse();
                var snapshot = new Snapshot(entry);
                var carrier = fixture.Maps.SpawnNonEternalPawn(fixture.Maps.Create(50));
                var caravan = fixture.MakeCaravan(carrier, site.Tile);
                TestAssert.IsTrue(EternalServiceContainer.Instance.CorpseManager.TryReleaseCorpseToUnspawnedOwner(
                    entry.OriginalPawn, () =>
                    {
                        entry.Corpse.DeSpawn(DestroyMode.WillReplace);
                        caravan.AddPawnOrItem(entry.Corpse, true);
                        return caravan.AllThings.Contains(entry.Corpse);
                    }), "Fixture must store its actual corpse in native inventory");
                var owner = entry.Corpse.holdingOwner;
                TestAssert.IsNotNull(owner, "Non-detaching fixture must have an actual native inventory owner");
                var handoff = AccessTools.Method(typeof(SpaceCrashRescueService), "TryDeliverCorpseToCrashSite");
                TestAssert.IsFalse((bool)handoff.Invoke(null, new object[] { entry.Corpse, site, false }),
                    "A non-detaching production handoff must refuse a still-held corpse");
                site.AddCorpse(entry.Corpse);
                TestAssert.IsTrue(ReferenceEquals(owner, entry.Corpse.holdingOwner) && owner.Contains(entry.Corpse),
                    "Public insertion must never implicitly detach the native inventory owner");
                TestAssert.IsFalse(site.HasPendingCorpse(entry.Corpse),
                    "Refused insertion must not duplicate a held corpse in the deep-save list");
                snapshot.AssertUnchanged();
            }
        }

        private static void UnregisteredAcceptance(Removal removal)
        {
            using (var fixture = new Fixture())
            {
                var site = fixture.CreateSite();
                var pawn = fixture.Maps.SpawnEternalPawn(fixture.Maps.Create(50));
                pawn.health.AddHediff(HediffDefOf.Cut, pawn.RaceProps.body.corePart).Severity = 1f;
                var caravan = fixture.MakeCaravan(pawn, site.Tile);
                using (var observation = new Fault(pawn, site, reentrantRemoval: removal))
                {
                    TestPawnFactory.KillPawn(pawn);
                    TestAssert.AreEqual(1, observation.ReentrantCalls,
                        "Actual solo AddCorpse acceptance must execute the reentrant native teardown once");
                    TestAssert.AreEqual(1, observation.DeathNotifications,
                        "Preserved solo death must reach its real primary death hook once");
                    TestAssert.IsTrue(site.Spawned && !site.Destroyed,
                        "The accepted persistent site must survive reentrant teardown until registration");
                    var entry = EternalServiceContainer.Instance.CorpseManager.GetCorpseData(pawn);
                    TestAssert.IsNotNull(entry, "Real primary death hook must register the staged corpse after reentrant teardown is refused");
                    TestAssert.IsTrue(site.HasPendingCorpse(entry.Corpse) && !entry.Corpse.Destroyed,
                        "Finalized registration must retain the exact deep-owned corpse");
                    TestAssert.IsTrue(entry.PreCalculatedHealingQueue?.Count > 0,
                        "Only the actual death hook should capture the injury queue");
                    TestAssert.IsTrue(caravan.Destroyed && !Find.WorldObjects.AllWorldObjects.Contains(caravan),
                        "The synchronous solo window must still finish empty-caravan cleanup");
                }
            }
        }

        private static void MapEntryFailure()
        {
            using (var fixture = new Fixture())
            {
                var site = fixture.CreateSite();
                var failed = fixture.CreateCorpse();
                var accepted = fixture.CreateCorpse();
                fixture.Stage(site, failed);
                fixture.Stage(site, accepted);
                var snapshot = new Snapshot(failed);
                MapType entered;
                using (var fault = new Fault(failed.OriginalPawn, site, failSpawn: true))
                {
                    entered = fixture.Maps.CreateWithParent(50, site, false);
                    TestAssert.IsTrue(fault.SpawnFaults > 0, "Native map-entry SpawnSetup fault must actually fire");
                }
                TestAssert.IsTrue(site.HasPendingCorpse(failed.Corpse),
                    "Failed map entry must retain the exact persistent deep owner");
                TestAssert.IsFalse(failed.Corpse.Spawned, "Failed map entry must not report a physical map owner");
                TestAssert.IsTrue(accepted.Corpse.Spawned && accepted.Corpse.Map == entered
                    && !site.HasPendingCorpse(accepted.Corpse), "Only accepted map entries should leave deep storage");
                snapshot.AssertUnchanged();
                site.PostMapGenerate();
                TestAssert.IsTrue(failed.Corpse.Spawned && failed.Corpse.Map == entered
                    && !site.HasPendingCorpse(failed.Corpse), "Actual native map-entry retry must commit the failed subset");
                snapshot.AssertUnchanged();
            }
        }

        private static void PickupFailure()
        {
            using (var fixture = new Fixture())
            {
                var site = fixture.CreateSite();
                var failed = fixture.CreateCorpse();
                var accepted = fixture.CreateCorpse();
                fixture.Stage(site, failed);
                fixture.Stage(site, accepted);
                var snapshot = new Snapshot(failed);
                var carrier = fixture.Maps.SpawnNonEternalPawn(fixture.Maps.Create(50));
                var caravan = fixture.MakeCaravan(carrier, site.Tile);
                using (var fault = new Fault(failed.OriginalPawn, site, latePickup: caravan))
                {
                    InvokePickup(site, caravan);
                    TestAssert.IsTrue(fault.PickupFaults > 0, "Native inventory acceptance postfix fault must actually fire");
                }
                TestAssert.IsTrue(Find.WorldObjects.AllWorldObjects.Contains(site) && site.HasPendingCorpse(failed.Corpse),
                    "Failed pickup must retain the source site and exact stored corpse");
                TestAssert.IsFalse(caravan.AllThings.Contains(failed.Corpse) || failed.Corpse.holdingOwner != null,
                    "Failed pickup must compensate the destination inventory after late acceptance");
                TestAssert.IsTrue(caravan.AllThings.Contains(accepted.Corpse) && !site.HasPendingCorpse(accepted.Corpse),
                    "Independent successful pickup must commit without dropping the failed subset");
                snapshot.AssertUnchanged();
                InvokePickup(site, caravan);
                TestAssert.IsFalse(Find.WorldObjects.AllWorldObjects.Contains(site), "Successful retry should retire an empty source site");
                TestAssert.IsTrue(caravan.AllThings.Contains(failed.Corpse),
                    "Actual menu pickup retry must place the exact failed corpse in native caravan inventory");
                snapshot.AssertUnchanged();
            }
        }

        private static void InvokePickup(WorldObject_EternalCrashSite site, CaravanType caravan)
        {
            var option = site.GetFloatMenuOptions(caravan).Single(item => item.Label == "RescueEternalsWithCaravan".Translate());
            TestAssert.IsNotNull(option.action, "Actual native recovery menu option must be executable");
            option.action();
        }

        private static void LateAcceptanceFailure()
        {
            using (var fixture = new Fixture())
            {
                var site = fixture.CreateSite();
                var entry = fixture.CreateCorpse();
                var sourceMap = entry.Corpse.Map;
                var sourcePosition = entry.Corpse.Position;
                var snapshot = new Snapshot(entry);
                using (var fault = new Fault(entry.OriginalPawn, null, lateAcceptance: true))
                {
                    TestAssert.AreEqual(0, SpaceCrashRescueService.DeliverCorpsesToCrashSite(new[] { entry.Corpse }, site.Tile),
                        "Late destination exception must report failed delivery");
                    TestAssert.IsTrue(fault.AcceptanceFaults > 0, "Public AddCorpse must accept before the injected postfix throws");
                }
                TestAssert.IsTrue(entry.Corpse.Spawned && entry.Corpse.Map == sourceMap,
                    "Late acceptance failure must restore the original native source map");
                TestAssert.AreEqual(sourcePosition, entry.Corpse.Position, "Late acceptance rollback must restore source position");
                TestAssert.AreEqual(sourceMap, entry.CurrentMap, "Late acceptance rollback must preserve committed source index");
                TestAssert.IsFalse(site.HasPendingCorpse(entry.Corpse),
                    "Late acceptance rollback must remove the destination deep-save duplicate");
                snapshot.AssertUnchanged();
                TestAssert.AreEqual(1, SpaceCrashRescueService.DeliverCorpsesToCrashSite(new[] { entry.Corpse }, site.Tile),
                    "Fault-free retry must accept the original corpse");
                TestAssert.IsTrue(site.HasPendingCorpse(entry.Corpse) && !entry.Corpse.Spawned,
                    "Retry must establish exact persistent destination ownership");
                snapshot.AssertUnchanged();
            }
        }

        private static void RemovalFailure(bool loaded, Removal removal)
        {
            using (var fixture = new Fixture())
            {
                var site = fixture.CreateSite();
                var sourceMap = loaded ? fixture.Maps.CreateWithParent(50, site, false) : null;
                var entry = fixture.CreateCorpse();
                fixture.Stage(site, entry);
                var snapshot = new Snapshot(entry);
                using (var fault = new Fault(entry.OriginalPawn, site, rejectAcceptance: true, hideHomes: true))
                {
                    Remove(site, sourceMap, removal);
                    TestAssert.IsTrue(fault.AcceptanceFaults > 0, "Actual forced removal must try a different site's acceptance boundary");
                }
                TestAssert.IsTrue(!site.Destroyed && Find.WorldObjects.AllWorldObjects.Contains(site),
                    "Failed removal must cancel before native site destruction and holder removal");
                if (loaded)
                {
                    TestAssert.IsTrue(Find.Maps.Contains(sourceMap), "Failed loaded-site removal must preserve its map");
                }

                TestAssert.IsTrue(site.HasPendingCorpse(entry.Corpse) && !entry.Corpse.Destroyed && !entry.Corpse.Spawned,
                    "Failed removal must preserve the exact raw-list corpse owner");
                snapshot.AssertUnchanged();
                using (new Fault(entry.OriginalPawn, site, hideHomes: true))
                {
                    Remove(site, sourceMap, removal);
                }

                if (loaded)
                {
                    TestAssert.IsFalse(Find.Maps.Contains(sourceMap), "Successful loaded-site retry must remove the original map");
                }

                if (removal != Removal.Game)
                {
                    TestAssert.IsFalse(Find.WorldObjects.AllWorldObjects.Contains(site), "Successful retry must remove its closing site");
                }

                TestAssert.IsFalse(site.HasPendingCorpse(entry.Corpse), "Accepted removal must retire the source deep-save entry");
                var owners = Find.WorldObjects.AllWorldObjects.OfType<WorldObject_EternalCrashSite>()
                    .Where(owner => owner.HasPendingCorpse(entry.Corpse)).ToArray();
                TestAssert.AreEqual(1, owners.Length, "Accepted removal must establish exactly one surviving deep owner");
                TestAssert.IsFalse(ReferenceEquals(site, owners[0]), "Recovery must exclude the closing unloaded or loaded site");
                snapshot.AssertUnchanged();
            }
        }

        private static void Remove(WorldObject_EternalCrashSite site, MapType map, Removal removal)
        {
            switch (removal)
            {
                case Removal.Holder: Find.WorldObjects.Remove(site); break;
                case Removal.Destroy: site.Destroy(); break;
                case Removal.Game: Current.Game.DeinitAndRemoveMap(map, false); break;
            }
        }

        private sealed class Snapshot
        {
            private readonly CorpseTrackingEntry entry;
            private readonly CorpseType corpse;
            private readonly Hediff[] hediffs;
            private readonly float[] severities;
            private readonly object queue;
            private readonly object captured;
            private readonly float[] queuedSeverity;
            private readonly float debt;
            public Snapshot(CorpseTrackingEntry entry)
            {
                this.entry = entry;
                corpse = entry.Corpse;
                hediffs = entry.OriginalPawn.health.hediffSet.hediffs.ToArray();
                severities = hediffs.Select(item => item.Severity).ToArray();
                queue = entry.HealingQueue;
                captured = entry.PreCalculatedHealingQueue;
                queuedSeverity = entry.PreCalculatedHealingQueue.Select(item => item.Severity).ToArray();
                debt = EternalServiceContainer.Instance.FoodDebtSystem.GetDebt(entry.OriginalPawn);
            }
            public void AssertUnchanged()
            {
                TestAssert.IsTrue(ReferenceEquals(entry, EternalServiceContainer.Instance.CorpseManager.GetCorpseData(entry.OriginalPawn))
                    && ReferenceEquals(corpse, entry.Corpse), "Transfer attempts must preserve tracking and corpse identity");
                TestAssert.IsTrue(hediffs.SequenceEqual(entry.OriginalPawn.health.hediffSet.hediffs)
                    && severities.SequenceEqual(hediffs.Select(item => item.Severity)), "Transfer attempts must preserve anatomy without extra damage");
                TestAssert.IsTrue(ReferenceEquals(queue, entry.HealingQueue) && ReferenceEquals(captured, entry.PreCalculatedHealingQueue)
                    && queuedSeverity.SequenceEqual(entry.PreCalculatedHealingQueue.Select(item => item.Severity)),
                    "Transfer attempts must preserve saved queues and remaining work");
                TestAssert.AreEqual(debt, EternalServiceContainer.Instance.FoodDebtSystem.GetDebt(entry.OriginalPawn),
                    "Transfer attempts must preserve debt");
            }
        }

        private sealed class Fixture : IDisposable
        {
            public readonly TestMapScope Maps = new TestMapScope();
            private readonly HashSet<WorldObject> originals = new HashSet<WorldObject>(Find.WorldObjects.AllWorldObjects);
            private readonly HashSet<PlanetTile> sites = new HashSet<PlanetTile>();
            private readonly List<CaravanType> caravans = new List<CaravanType>();
            public WorldObject_EternalCrashSite CreateSite()
            {
                var tile = (PlanetTile)Maps.FindUnusedSurfaceTile();
                sites.Add(tile);
                var site = SpaceCrashRescueService.CreateOrGetCrashSite(tile);
                TestAssert.IsNotNull(site, "Native persistent recovery site must exist");
                return site;
            }
            public CorpseTrackingEntry CreateCorpse()
            {
                var pawn = Maps.SpawnEternalPawn(Maps.Create(50));
                pawn.health.AddHediff(HediffDefOf.Cut, pawn.RaceProps.body.corePart).Severity = 1f;
                TestPawnFactory.KillPawn(pawn);
                var entry = EternalServiceContainer.Instance.CorpseManager.GetCorpseData(pawn);
                TestAssert.IsNotNull(entry, "Real death hook must register the native fixture corpse");
                TestAssert.IsTrue(entry.PreCalculatedHealingQueue?.Count > 0, "Fixture must capture actual injury work");
                return entry;
            }
            public void Stage(WorldObject_EternalCrashSite site, CorpseTrackingEntry entry)
            {
                TestAssert.AreEqual(1, SpaceCrashRescueService.DeliverCorpsesToCrashSite(new[] { entry.Corpse }, site.Tile),
                    "Actual non-stripping service must stage the fixture corpse");
                TestAssert.IsTrue(site.HasPendingCorpse(entry.Corpse), "Native recovery storage must own the exact fixture corpse");
            }
            public CaravanType MakeCaravan(Pawn carrier, PlanetTile tile)
            {
                var caravan = CaravanMaker.MakeCaravan(new[] { carrier }, Faction.OfPlayer, tile, true);
                caravans.Add(caravan);
                TestAssert.IsTrue(caravan.Spawned && caravan.ContainsPawn(carrier), "Fixture requires a real ordinary-carrier caravan");
                return caravan;
            }
            public void Dispose()
            {
                var failures = new List<Exception>();
                Action<Action> attempt = action => { try { action(); } catch (Exception ex) { failures.Add(ex); } };
                var owned = Maps.Pawns.ToArray();
                foreach (var site in Find.WorldObjects.AllWorldObjects.OfType<WorldObject_EternalCrashSite>().ToArray())
                {
                    foreach (var corpse in site.PendingCorpsesForRecovery())
                    {
                        if (owned.Contains(corpse.InnerPawn))
                        {
                            site.ForgetPendingCorpse(corpse);
                        }
                    }
                }

                foreach (var caravan in caravans)
                {
                    attempt(() =>
                    {
                        foreach (var pawn in caravan.PawnsListForReading.ToArray())
                        {
                            if (owned.Contains(pawn))
                            {
                                caravan.RemovePawn(pawn);
                            }
                        }

                        if (caravan.Spawned)
                        {
                            Find.WorldObjects.Remove(caravan);
                        }
                    });
                }

                attempt(Maps.Dispose);
                foreach (var site in Find.WorldObjects.AllWorldObjects.OfType<WorldObject_EternalCrashSite>().ToArray())
                {
                    if (!originals.Contains(site) && sites.Contains(site.Tile) && !site.HasRecoveryContents && !site.HasMap)
                    {
                        attempt(() => Find.WorldObjects.Remove(site));
                    }
                }

                foreach (var pawn in owned)
                {
                    attempt(() => { if (Find.WorldPawns.Contains(pawn)) { Find.WorldPawns.RemovePawn(pawn); } });
                }

                if (failures.Count > 0)
                {
                    throw new AggregateException("Owned recovery-site fixture cleanup failed", failures);
                }
            }
        }

        private sealed class Fault : IDisposable
        {
            private static Fault active;
            private readonly Harmony harmony = new Harmony("Eternal.Debug.RecoverySiteFailures");
            private readonly List<MethodBase> patched = new List<MethodBase>();
            private readonly Pawn pawn;
            private readonly WorldObject_EternalCrashSite source;
            private readonly bool failSpawn;
            private readonly bool lateAcceptance;
            private readonly bool rejectAcceptance;
            private readonly bool hideHomes;
            private readonly CaravanType latePickup;
            private readonly Removal? reentrantRemoval;
            private readonly Removal? reentrantSourceRemoval;
            private readonly bool sourceRemovalAfterAcceptance;
            public int SpawnFaults { get; private set; }
            public int AcceptanceFaults { get; private set; }
            public int PickupFaults { get; private set; }
            public int ReentrantCalls { get; private set; }
            public int DeathNotifications { get; private set; }
            public Fault(Pawn pawn, WorldObject_EternalCrashSite source, bool failSpawn = false,
                bool lateAcceptance = false, bool rejectAcceptance = false, bool hideHomes = false, CaravanType latePickup = null,
                Removal? reentrantRemoval = null, Removal? reentrantSourceRemoval = null,
                bool sourceRemovalAfterAcceptance = false)
            {
                TestAssert.IsNull(active, "Recovery-site instrumentation cannot nest");
                this.pawn = pawn;
                this.source = source;
                this.failSpawn = failSpawn;
                this.lateAcceptance = lateAcceptance;
                this.rejectAcceptance = rejectAcceptance;
                this.hideHomes = hideHomes;
                this.latePickup = latePickup;
                this.reentrantRemoval = reentrantRemoval;
                this.reentrantSourceRemoval = reentrantSourceRemoval;
                this.sourceRemovalAfterAcceptance = sourceRemovalAfterAcceptance;
                active = this;
                try
                {
                    Patch(AccessTools.Method(typeof(CorpseType), nameof(CorpseType.SpawnSetup), new[] { typeof(MapType), typeof(bool) }),
                        nameof(BeforeSpawn), true);
                    Patch(AccessTools.Method(typeof(WorldObject_EternalCrashSite), nameof(WorldObject_EternalCrashSite.AddCorpse)),
                        nameof(BeforeAcceptance), true);
                    Patch(AccessTools.Method(typeof(WorldObject_EternalCrashSite), nameof(WorldObject_EternalCrashSite.AddCorpse)),
                        nameof(AfterAcceptance), false);
                    Patch(AccessTools.Method(typeof(CaravanInventoryUtility), nameof(CaravanInventoryUtility.GiveThing)),
                        nameof(AfterPickup), false);
                    Patch(AccessTools.PropertyGetter(typeof(MapType), nameof(MapType.IsPlayerHome)), nameof(HomePostfix), false);
                    Patch(AccessTools.Method(typeof(Eternal_Hediff), nameof(Eternal_Hediff.Notify_PawnDied)), nameof(OnDeath), true);
                }
                catch { Dispose(); throw; }
            }
            private void Patch(MethodBase method, string callback, bool prefix)
            {
                TestAssert.IsNotNull(method, "Actual recovery-site native boundary " + callback);
                patched.Add(method);
                var hook = new HarmonyMethod(typeof(Fault), callback);
                harmony.Patch(method, prefix: prefix ? hook : null, postfix: prefix ? null : hook);
            }
            private static void BeforeSpawn(CorpseType __instance)
            {
                if (active?.failSpawn != true || __instance.InnerPawn != active.pawn)
                {
                    return;
                }

                active.SpawnFaults++;
                throw new InvalidOperationException("Owned recovery-site map-entry SpawnSetup failure");
            }
            private static void BeforeAcceptance(WorldObject_EternalCrashSite __instance, CorpseType __0)
            {
                if (active != null && active.reentrantSourceRemoval.HasValue && !active.sourceRemovalAfterAcceptance
                    && __0.InnerPawn == active.pawn && __instance != active.source)
                {
                    active.RemoveEmptySource(__0);
                    active.AcceptanceFaults++;
                    throw new InvalidOperationException("Owned recovery destination rejection after reentrant source teardown");
                }
                if (active?.rejectAcceptance != true || __0.InnerPawn != active.pawn || __instance == active.source)
                {
                    return;
                }

                active.AcceptanceFaults++;
                throw new InvalidOperationException("Owned recovery-site forced-removal acceptance failure");
            }
            private static void AfterAcceptance(WorldObject_EternalCrashSite __instance, CorpseType __0)
            {
                if (active == null || __0.InnerPawn != active.pawn)
                {
                    return;
                }

                if (active.reentrantSourceRemoval.HasValue && active.sourceRemovalAfterAcceptance && __instance != active.source)
                {
                    TestAssert.IsTrue(__instance.HasPendingCorpse(__0), "Source teardown postfix must run after actual destination acceptance");
                    active.RemoveEmptySource(__0);
                    active.AcceptanceFaults++;
                    throw new InvalidOperationException("Owned recovery destination late exception after reentrant source teardown");
                }

                if (active.reentrantRemoval.HasValue)
                {
                    TestAssert.IsFalse(EternalServiceContainer.Instance.CorpseManager.IsTracked(active.pawn),
                        "Reentrant teardown fixture must run before the primary death registration");
                    TestAssert.IsTrue(__instance.HasPendingCorpse(__0), "Reentrant teardown must occur after real site acceptance");
                    active.ReentrantCalls++;
                    Remove(__instance, null, active.reentrantRemoval.Value);
                    TestAssert.IsTrue(__instance.Spawned && !__instance.Destroyed && __instance.HasPendingCorpse(__0),
                        "Native teardown must preserve the sole deep owner throughout the unregistered death window");
                    TestAssert.IsFalse(EternalServiceContainer.Instance.CorpseManager.IsTracked(active.pawn),
                        "Removal guard must not fabricate registration or queue state while death is still in progress");
                }
                if (!active.lateAcceptance)
                {
                    return;
                }

                TestAssert.IsTrue(__instance.HasPendingCorpse(__0), "Late site exception must occur after exact destination acceptance");
                active.AcceptanceFaults++;
                throw new InvalidOperationException("Owned recovery-site late AddCorpse acceptance failure");
            }
            private void RemoveEmptySource(CorpseType corpse)
            {
                TestAssert.IsFalse(source.HasPendingCorpse(corpse) || source.HasRecoveryContents,
                    "Reentrant source teardown must run after its sole deep entry was released for handoff");
                ReentrantCalls++;
                Remove(source, null, reentrantSourceRemoval.Value);
                TestAssert.IsTrue(source.Spawned && !source.Destroyed,
                    "Actual native teardown must preserve the temporarily empty source throughout delivery");
            }
            private static void OnDeath(Eternal_Hediff __instance)
            {
                if (active != null && __instance.pawn == active.pawn)
                {
                    active.DeathNotifications++;
                }
            }
            private static void AfterPickup(CaravanType __0, Thing __1)
            {
                if (active == null || active.latePickup != __0 || !(__1 is CorpseType corpse) || corpse.InnerPawn != active.pawn)
                {
                    return;
                }

                TestAssert.IsTrue(__0.AllThings.Contains(corpse), "Late pickup exception must occur after native inventory acceptance");
                active.PickupFaults++;
                throw new InvalidOperationException("Owned recovery-site late native caravan pickup failure");
            }
            private static void HomePostfix(ref bool __result)
            {
                if (active?.hideHomes == true)
                {
                    __result = false;
                }
            }
            public void Dispose()
            {
                try
                {
                    foreach (var method in patched)
                    {
                        harmony.Unpatch(method, HarmonyPatchType.All, harmony.Id);
                    }
                }
                finally { active = null; }
            }
        }
    }
}
#endif
