// Author: 0Shard
// Description: Real native map-removal entrypoints, surviving destinations and owned transfer-failure rollback.
#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Eternal.DI;
using Eternal.Corpse;
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

namespace Eternal.InGameTests.FailureScenarios
{
    public static class MapRemovalFailureTests
    {
        private enum Removal { Game, Holder, Parent, Site }

        public static TestSuiteResult RunAll(MapType map)
        {
            var result = new TestSuiteResult(0, 0, new List<string>());
            result.RunTest("MapRemovalFailures", "HomeRecoveryBeforeDeinit", HomeRecoveryBeforeDeinit);
            result.RunTest("MapRemovalFailures", "NoHomeRecoverySitePreservesAnatomy", NoHomeRecoverySitePreservesAnatomy);
            result.RunTest("MapRemovalFailures", "ClosingRecoverySiteIsNotReused", ClosingRecoverySiteIsNotReused);
            result.RunTest("MapRemovalFailures", "GameRemovalFailureAndRetry", () => FailureAndRetry(Removal.Game));
            result.RunTest("MapRemovalFailures", "HolderRemovalFailureAndRetry", () => FailureAndRetry(Removal.Holder));
            result.RunTest("MapRemovalFailures", "ParentDestroyFailureAndRetry", () => FailureAndRetry(Removal.Parent));
            result.RunTest("MapRemovalFailures", "SiteDestroySkipsPostDestroyOnFailure", () => FailureAndRetry(Removal.Site));
            result.RunTest("MapRemovalFailures", "PartialSuccessRetainsFailedSubset", PartialSuccessRetainsFailedSubset);
            result.RunTest("MapRemovalFailures", "HeldCorpseRollbackKeepsExactOwner", HeldCorpseRollbackKeepsExactOwner);
            result.RunTest("MapRemovalFailures", "OrdinaryRetentionAndDeliberateDestroy", OrdinaryRetentionAndDeliberateDestroy);
            result.RunTest("MapRemovalFailures", "ProtectedMapSchedulesStaggeredCorpsesWithoutReset", StaggeredTeleports);
            result.RunTest("MapRemovalFailures", "AnchorSurvivesDeathAndDisposesOnceOnDeparture", AnchorDeparture);
            result.RunTest("MapRemovalFailures", "DeadAnchorScribeRestoresPawnGridAndTickerOnce", () => AnchorRoundTrip(false));
            result.RunTest("MapRemovalFailures", "GraceAnchorScribeRetainsOriginalDeadlineAndDisposesOnce", () => AnchorRoundTrip(true));
            return result;
        }

        private static void StaggeredTeleports()
        {
            using (var fixture = new RemovalFixture())
            {
                var source = fixture.Maps.Create(50, false, true);
                var home = fixture.Maps.Create(50);
                var first = fixture.CreateCorpse(source);
                ProtectOwnedMap(source);
                var scheduled = new TeleportSnapshot(PendingTeleports().Cast<object>().Single(row =>
                    ReferenceEquals(AccessTools.Field(row.GetType(), "Corpse").GetValue(row), first.Corpse)));
                var second = fixture.CreateCorpse(source);
                ProtectOwnedMap(source);
                scheduled.AssertUnchanged();
                TestAssert.AreEqual(1, PendingTeleports().Cast<object>().Count(row =>
                    ReferenceEquals(AccessTools.Field(row.GetType(), "Corpse").GetValue(row), second.Corpse)),
                    "Later corpse on an already protected map must receive one pending teleport");
                ProtectOwnedMap(source);
                scheduled.AssertUnchanged();
            }
        }

        private static void AnchorDeparture()
        {
            using (var fixture = new RemovalFixture())
            {
                var source = fixture.Maps.Create(50, false, true);
                var home = fixture.Maps.Create(50);
                var entry = fixture.CreateCorpse(source);
                var manager = source.GetComponent<Eternal.Map.EternalMapManager>();
                var anchor = manager.GetAnchorForPawn(entry.OriginalPawn)
                    ?? manager.CreateCorpseAnchor(entry.Corpse);
                TestAssert.IsNotNull(anchor, "Real death must create an enabled native anchor");
                var anchorPosition = anchor.Position;
                TestAssert.IsFalse(anchor.ShouldRemoveAnchor(), "Native dead pawn Destroyed flag must not retire its valid corpse's anchor");
                TestAssert.IsTrue(EternalServiceContainer.Instance.CorpseManager.TryRelocateCorpse(entry.OriginalPawn, home, home.Center),
                    "Departure must move the actual corpse");
                TestAssert.IsTrue(anchor.ShouldRemoveAnchor(), "Physical departure retires old map retention");
                manager.RemoveAnchor(anchor);
                anchor.DisposeAnchor();
                TestAssert.IsFalse(anchor.Spawned || source.thingGrid.ThingsListAt(anchorPosition).Contains(anchor),
                    "Idempotent native disposal removes map ownership and grid registration");
                TestAssert.IsFalse(manager.GetActiveAnchors().Contains(anchor), "Disposal retires manager registration once");
                TestAssert.IsTrue(anchor.ShouldRemoveAnchor(), "Retired anchor remains terminal after repeated disposal");
            }
        }

        private static void AnchorRoundTrip(bool resurrected)
        {
            using (var fixture = new RemovalFixture())
            {
                var source = fixture.Maps.Create(50, false, true);
                var entry = fixture.CreateCorpse(source);
                var manager = source.GetComponent<Eternal.Map.EternalMapManager>();
                var anchor = manager.GetAnchorForPawn(entry.OriginalPawn) ?? manager.CreateCorpseAnchor(entry.Corpse);
                int originalGrace = Eternal_Mod.settings.anchorGracePeriodTicks;
                string path = Path.Combine(Path.GetTempPath(), "eternal-anchor-" + Guid.NewGuid().ToString("N") + ".xml");
                Eternal.Map.EternalMapManager loaded = null;
                int managerIndex = source.components.IndexOf(manager);
                try
                {
                    Eternal_Mod.settings.anchorGracePeriodTicks = 20;
                    int resurrectionTick = -1;
                    if (resurrected)
                    {
                        entry.HealingQueue = new List<HealingItem>();
                        entry.HealingQueueHydrated = true;
                        TestAssert.IsTrue(EternalServiceContainer.Instance.CorpseHealingProcessor.StartCorpseHealing(entry), "Grace fixture uses actual resurrection");
                        resurrectionTick = Find.TickManager.TicksGame - 10;
                        if (resurrectionTick == -1) { resurrectionTick--; }
                        AccessTools.Field(typeof(Eternal.Map.EternalAnchor), "resurrectionTick").SetValue(anchor, resurrectionTick);
                    }
                    TestAssert.IsFalse(anchor.ShouldRemoveAnchor(), "Fixture anchor is valid before saving");
                    int anchorId = anchor.thingIDNumber;
                    var position = anchor.Position;
                    Scribe.saver.InitSaving(path, "anchorFixture");
                    Scribe_Deep.Look(ref manager, "manager", source);
                    Scribe.saver.FinalizeSaving();
                    manager.RemoveAnchor(anchor);
                    Scribe.loader.InitLoading(path);
                    // The real pawn stays in its native map/corpse owner. Supply that
                    // external deep owner's identity to this component-only Scribe fixture.
                    var directory = (LoadedObjectDirectory)AccessTools.Field(typeof(CrossRefHandler), "loadedObjectDirectory").GetValue(Scribe.loader.crossRefs);
                    directory.RegisterLoaded(entry.OriginalPawn);
                    Scribe_Deep.Look(ref loaded, "manager", source);
                    Scribe.loader.FinalizeLoading();
                    source.components[managerIndex] = loaded;
                    var restored = loaded.GetActiveAnchors().Single();
                    TestAssert.IsTrue(ReferenceEquals(entry.OriginalPawn, restored.AnchoredPawn), "Native cross-reference phases retain even an ordinary destroyed dead pawn");
                    TestAssert.IsFalse(restored.Spawned, "Manager-deep-loaded mote awaits post-map attachment");
                    loaded.FinalizeInit();
                    loaded.FinalizeInit();
                    TestAssert.AreEqual(anchorId, restored.thingIDNumber, "Native attachment retains the saved anchor identity");
                    TestAssert.IsTrue(restored.Spawned && restored.Map == source, "Post-map load restores native map ownership");
                    TestAssert.AreEqual(1, source.thingGrid.ThingsListAt(position).Count(thing => ReferenceEquals(thing, restored)), "Repeated finalization registers one grid entry");
                    TestAssert.AreEqual(1, AnchorTickRegistrations(restored), "Repeated finalization registers one native ticker entry");
                    TestAssert.AreEqual(resurrectionTick, (int)AccessTools.Field(typeof(Eternal.Map.EternalAnchor), "resurrectionTick").GetValue(restored), "Load retains the original grace start");
                    TestAssert.IsFalse(restored.ShouldRemoveAnchor(), "Loaded anchor retains its existing dead/grace lifetime");
                    if (resurrected)
                    {
                        int remaining = resurrectionTick + 20 - Find.TickManager.TicksGame;
                        AuditHealingTimeFailureTests.Advance(remaining);
                        TestAssert.IsFalse(restored.ShouldRemoveAnchor(), "Original grace deadline remains inclusive");
                        AuditHealingTimeFailureTests.Advance(2);
                        restored.DisposeAnchor();
                        TestAssert.IsFalse(restored.Spawned || loaded.GetActiveAnchors().Contains(restored), "Grace expires and disposes only once at the original deadline");
                        TestAssert.AreEqual(0, AnchorTickRegistrations(restored), "Expired anchor releases native ticking");
                    }
                }
                finally
                {
                    Scribe.ForceStop();
                    if (loaded != null) { foreach (var restored in loaded.GetActiveAnchors()) { loaded.RemoveAnchor(restored); } }
                    source.components[managerIndex] = manager;
                    Eternal_Mod.settings.anchorGracePeriodTicks = originalGrace;
                    if (File.Exists(path)) { File.Delete(path); }
                }
            }
        }

        private static int AnchorTickRegistrations(Eternal.Map.EternalAnchor anchor)
        {
            var ticks = AccessTools.Field(typeof(TickManager), "tickListNormal").GetValue(Find.TickManager);
            var lists = (List<List<Thing>>)AccessTools.Field(typeof(TickList), "thingLists").GetValue(ticks);
            var additions = (List<Thing>)AccessTools.Field(typeof(TickList), "thingsToRegister").GetValue(ticks);
            var removals = (List<Thing>)AccessTools.Field(typeof(TickList), "thingsToDeregister").GetValue(ticks);
            return lists.Sum(list => list.Count(thing => ReferenceEquals(thing, anchor)))
                + additions.Count(thing => ReferenceEquals(thing, anchor)) - removals.Count(thing => ReferenceEquals(thing, anchor));
        }

        private static void HomeRecoveryBeforeDeinit()
        {
            using (var fixture = new RemovalFixture())
            {
                var source = fixture.Maps.Create(50, false, true);
                var home = fixture.Maps.Create(50);
                var entry = fixture.CreateCorpse(source);
                var snapshot = new CorpseSnapshot(entry);
                using (new MapRemovalObservation(source, new[] { home }))
                {
                    Current.Game.DeinitAndRemoveMap(source, false);
                }

                AssertRemoved(source);
                TestAssert.IsTrue(entry.Corpse.Spawned && entry.Corpse.Map == home,
                    "Native deinit should transfer the exact corpse to another surviving home");
                AssertIndexed(entry, home);
                snapshot.AssertHealingUnchanged();
            }
        }

        private static void NoHomeRecoverySitePreservesAnatomy()
        {
            using (var fixture = new RemovalFixture())
            {
                var source = fixture.CreateSiteMap();
                var entry = fixture.CreateCorpse(source);
                var snapshot = new CorpseSnapshot(entry);
                using (new MapRemovalObservation(source, new MapType[0]))
                {
                    Find.WorldObjects.Remove(source.Parent);
                }

                AssertRemoved(source);
                TestAssert.IsFalse(Find.WorldObjects.AllWorldObjects.Contains(source.Parent),
                    "Direct holder removal should commit after recovery");
                var owner = Find.WorldObjects.AllWorldObjects.OfType<WorldObject_EternalCrashSite>()
                    .SingleOrDefault(site => site.GetCrashedPawns().Contains(entry.OriginalPawn));
                TestAssert.IsNotNull(owner, "No-home recovery must deep-own the exact pawn's corpse at a site");
                TestAssert.AreEqual(source.Tile, owner.Tile, "Recovery site should use the closing ground tile");
                TestAssert.IsFalse(entry.Corpse.Spawned || entry.Corpse.Destroyed,
                    "Recovery must retain the existing unspawned corpse");
                TestAssert.IsNull(entry.CurrentMap, "A recovery-site corpse must leave the source map index");
                AssertIndexed(entry, null);
                snapshot.AssertHealingUnchanged();
            }
        }

        private static void ClosingRecoverySiteIsNotReused()
        {
            using (var fixture = new RemovalFixture())
            {
                TestAssert.Available(EternalDefOf.Eternal_CrashSite, "Recovery-site def");
                var parent = (WorldObject_EternalCrashSite)WorldObjectMaker.MakeWorldObject(EternalDefOf.Eternal_CrashSite);
                parent.Tile = fixture.Maps.FindUnusedSurfaceTile();
                parent.SetFaction(Faction.OfPlayer);
                var source = fixture.Maps.CreateWithParent(50, parent, false);
                var entry = fixture.CreateCorpse(source);
                using (new MapRemovalObservation(source, new MapType[0]))
                {
                    parent.Destroy();
                }

                AssertRemoved(source);
                var survivingOwner = Find.WorldObjects.AllWorldObjects.OfType<WorldObject_EternalCrashSite>()
                    .SingleOrDefault(site => site.GetCrashedPawns().Contains(entry.OriginalPawn));
                TestAssert.IsNotNull(survivingOwner, "Closing recovery site must transfer to a surviving owner");
                TestAssert.IsFalse(ReferenceEquals(parent, survivingOwner), "Recovery cannot reuse its closing parent");
                TestAssert.IsTrue(parent.Destroyed, "The original recovery-site destruction should commit");
            }
        }

        private static void FailureAndRetry(Removal removal)
        {
            using (var fixture = new RemovalFixture())
            {
                var source = removal == Removal.Site ? fixture.CreateSiteMap() : fixture.Maps.Create(50, false, true);
                var home = fixture.Maps.Create(50);
                var entry = fixture.CreateCorpse(source);
                var snapshot = new CorpseSnapshot(entry);
                var controlMap = fixture.Maps.Create(50, false, true);
                var control = fixture.CreateCorpse(controlMap);
                var controlSnapshot = new CorpseSnapshot(control);
                var scheduled = ScheduleOwnedTeleport(control, home);
                var sourceScheduled = ScheduleOwnedTeleport(entry, home);
                ProtectOwnedMap(source);

                string transferTrace;
                using (var observation = new MapRemovalObservation(source, new[] { home }, entry.OriginalPawn, true))
                {
                    RemoveNative(source, removal);
                    TestAssert.IsTrue(observation.HomeFaults > 0, "The target SpawnSetup fault must actually fire");
                    TestAssert.IsTrue(observation.SiteFaults > 0, "The fallback site-acceptance fault must actually fire");
                    TestAssert.AreEqual(0, observation.SitePostDestroyCalls,
                        "Cancelled derived Site.Destroy must skip all SitePart.PostDestroy calls");
                    transferTrace = observation.TransferTrace;
                }
                AssertRetained(source, entry, snapshot, transferTrace);
                TestAssert.IsTrue(ProtectedMaps().Contains(source), "Failure must retain source map protection");
                AssertScheduledUnchanged(scheduled);
                AssertScheduledUnchanged(sourceScheduled);
                controlSnapshot.AssertHealingUnchanged();
                TestAssert.AreEqual(controlMap, control.Corpse.MapHeld,
                    "Another Eternal's owner must be unaffected by failure");

                using (var observation = new MapRemovalObservation(source, new[] { home }))
                {
                    RemoveNative(source, removal);
                    if (removal == Removal.Site)
                    {
                        TestAssert.IsTrue(observation.SitePostDestroyCalls > 0,
                            "Successful retry must execute native derived Site destruction side effects");
                    }
                }
                AssertRemoved(source);
                TestAssert.IsFalse(ProtectedMaps().Contains(source), "Committed removal should retire source protection");
                TestAssert.AreEqual(home, entry.Corpse.Map, "Retry must recover the original corpse");
                if (removal == Removal.Parent || removal == Removal.Site)
                {
                    TestAssert.IsTrue(source.Parent.Destroyed, "Successful parent Destroy must commit its destroyed flag");
                }

                snapshot.AssertHealingUnchanged();
                AssertScheduledUnchanged(scheduled);
                controlSnapshot.AssertHealingUnchanged();
            }
        }

        private static void PartialSuccessRetainsFailedSubset()
        {
            using (var fixture = new RemovalFixture())
            {
                var source = fixture.Maps.Create(50, false, true);
                var home = fixture.Maps.Create(50);
                var accepted = fixture.CreateCorpse(source);
                var rejected = fixture.CreateCorpse(source);
                var rejectedSnapshot = new CorpseSnapshot(rejected);
                string transferTrace;
                using (var observation = new MapRemovalObservation(source, new[] { home }, rejected.OriginalPawn, true))
                {
                    Current.Game.DeinitAndRemoveMap(source, false);
                    TestAssert.IsTrue(observation.HomeFaults > 0 && observation.SiteFaults > 0,
                        "Partial-success scenario must exercise both rejected transfers");
                    transferTrace = observation.TransferTrace;
                }
                AssertRetained(source, rejected, rejectedSnapshot, transferTrace);
                TestAssert.AreEqual(home, accepted.Corpse.Map, "An independent successful transfer should remain committed");
                AssertIndexed(accepted, home);
                AssertIndexed(rejected, source);
                var acceptedPosition = accepted.Corpse.Position;
                using (new MapRemovalObservation(source, new[] { home }))
                {
                    Current.Game.DeinitAndRemoveMap(source, false);
                }

                AssertRemoved(source);
                TestAssert.AreEqual(acceptedPosition, accepted.Corpse.Position,
                    "Retry must not relocate the already accepted corpse again");
                AssertIndexed(rejected, home);
            }
        }

        private static void HeldCorpseRollbackKeepsExactOwner()
        {
            using (var fixture = new RemovalFixture())
            {
                var source = fixture.Maps.Create(50, false, true);
                var home = fixture.Maps.Create(50);
                var entry = fixture.CreateCorpse(source);
                var casket = (Building_Casket)ThingMaker.MakeThing(ThingDefOf.Sarcophagus, ThingDefOf.WoodLog);
                TestPawnFactory.OwnThing(casket);
                GenSpawn.Spawn(casket, source.Center, source);
                var owner = casket.GetDirectlyHeldThings();
                TestAssert.IsTrue(EternalServiceContainer.Instance.CorpseManager.TryReleaseCorpseToUnspawnedOwner(
                    entry.OriginalPawn, () =>
                    {
                        entry.Corpse.DeSpawn(DestroyMode.WillReplace);
                        return owner.TryAdd(entry.Corpse, false);
                    }), "Fixture must put the exact corpse into a real source-map container");
                TestAssert.AreEqual(source, entry.Corpse.MapHeld, "Held corpse must physically belong to the closing map");
                var snapshot = new CorpseSnapshot(entry);
                using (var observation = new MapRemovalObservation(source, new[] { home }, entry.OriginalPawn, true))
                {
                    Find.WorldObjects.Remove(source.Parent);
                    TestAssert.IsTrue(observation.HomeFaults > 0 && observation.SiteFaults > 0,
                        "Both held-corpse target faults must actually fire");
                }
                AssertRetained(source, entry, snapshot);
                TestAssert.IsTrue(ReferenceEquals(owner, entry.Corpse.holdingOwner) && owner.Contains(entry.Corpse),
                    "Rollback must restore the exact original ThingOwner");
                using (new MapRemovalObservation(source, new[] { home }))
                {
                    Find.WorldObjects.Remove(source.Parent);
                }

                AssertRemoved(source);
                TestAssert.IsTrue(entry.Corpse.Spawned && entry.Corpse.Map == home,
                    "An unspawned source-owned corpse must recover on retry");
            }
        }

        private static void OrdinaryRetentionAndDeliberateDestroy()
        {
            using (var fixture = new RemovalFixture())
            {
                var source = fixture.Maps.Create(50, false, true);
                var entry = fixture.CreateCorpse(source);
                TestAssert.IsTrue(source.mapPawns.AnyPawnBlockingMapRemoval,
                    "Normal ground-map retention should still include a tracked Eternal corpse");
                source.Parent.CheckRemoveMapNow();
                TestAssert.IsTrue(Find.Maps.Contains(source), "An ordinary removal check must retain the map");
                entry.Corpse.Destroy(DestroyMode.Vanish);
                TestAssert.IsTrue(entry.Corpse.Destroyed,
                    "Deliberate destruction of a spawned corpse must retain the existing pass-through policy");
                Current.Game.DeinitAndRemoveMap(source, false);
                AssertRemoved(source);
            }
        }

        private static void RemoveNative(MapType source, Removal removal)
        {
            switch (removal)
            {
                case Removal.Game: Current.Game.DeinitAndRemoveMap(source, false); break;
                case Removal.Holder: Find.WorldObjects.Remove(source.Parent); break;
                case Removal.Parent: ((MapParent)source.Parent).Destroy(); break;
                case Removal.Site: ((Site)source.Parent).Destroy(); break;
            }
        }

        private static void AssertRemoved(MapType map)
        {
            TestAssert.IsFalse(Find.Maps.Contains(map), "The real native removal must finish after successful recovery");
        }

        private static void AssertRetained(MapType source, CorpseTrackingEntry entry, CorpseSnapshot snapshot,
            string transferTrace = null)
        {
            TestAssert.IsTrue(Find.Maps.Contains(source), "Failed recovery must cancel before map deinitialization");
            TestAssert.IsTrue(Find.WorldObjects.AllWorldObjects.Contains(source.Parent),
                "Failed recovery must cancel before world-object holder removal");
            TestAssert.IsFalse(source.Parent.Destroyed, "Failed recovery must cancel the parent's destroyed flag");
            TestAssert.IsTrue(source.Parent.HasMap && source.Parent.Map == source, "Original map-parent ownership must survive");
            snapshot.AssertOwnerUnchanged(transferTrace);
            snapshot.AssertHealingUnchanged();
            TestAssert.IsTrue(ReferenceEquals(EternalServiceContainer.Instance.CorpseManager.GetCorpseData(entry.OriginalPawn), entry),
                "Failure must retain the exact tracking entry");
        }

        private static void AssertIndexed(CorpseTrackingEntry entry, MapType map)
        {
            var manager = EternalServiceContainer.Instance.CorpseManager;
            foreach (var candidate in Find.Maps)
            {
                TestAssert.AreEqual(candidate == map,
                    manager.GetCorpsesOnMap(candidate).Any(row => row.OriginalPawn == entry.OriginalPawn),
                    "The exact pawn must appear only in its committed map index");
            }

            TestAssert.AreEqual(map, entry.CurrentMap, "Tracking entry must agree with the committed index");
        }

        private static IList PendingTeleports() => (IList)AccessTools.Field(
            EternalServiceContainer.Instance.MapProtection.GetType(), "pendingTeleports")
            .GetValue(EternalServiceContainer.Instance.MapProtection);

        private static HashSet<MapType> ProtectedMaps() => (HashSet<MapType>)AccessTools.Field(
            EternalServiceContainer.Instance.MapProtection.GetType(), "protectedMaps")
            .GetValue(EternalServiceContainer.Instance.MapProtection);

        private static TeleportSnapshot ScheduleOwnedTeleport(CorpseTrackingEntry entry, MapType home)
        {
            var protection = EternalServiceContainer.Instance.MapProtection;
            AccessTools.Method(protection.GetType(), "ScheduleDelayedTeleport")
                .Invoke(protection, new object[] { entry, home, home.Center, 60000 });
            return new TeleportSnapshot(PendingTeleports().Cast<object>().Single(row =>
                ReferenceEquals(AccessTools.Field(row.GetType(), "Corpse").GetValue(row), entry.Corpse)));
        }

        private static void ProtectOwnedMap(MapType map)
        {
            var protection = EternalServiceContainer.Instance.MapProtection;
            AccessTools.Method(protection.GetType(), "ProcessMapProtection").Invoke(protection, new object[] { map });
            TestAssert.IsTrue(ProtectedMaps().Contains(map), "The source must enter ordinary protection before removal");
        }

        private static void AssertScheduledUnchanged(TeleportSnapshot scheduled)
        {
            TestAssert.IsTrue(PendingTeleports().Cast<object>().Any(row => ReferenceEquals(row, scheduled.Row)),
                "Another Eternal's pending work must retain the exact scheduled row");
            scheduled.AssertUnchanged();
        }

        private sealed class TeleportSnapshot
        {
            public readonly object Row;
            private readonly Dictionary<string, object> fields;
            public TeleportSnapshot(object row)
            {
                Row = row;
                fields = new[] { "Corpse", "TargetMap", "TargetLocation", "TriggerTick" }
                    .ToDictionary(name => name, name => AccessTools.Field(row.GetType(), name).GetValue(row));
            }
            public void AssertUnchanged()
            {
                foreach (var field in fields)
                {
                    TestAssert.AreEqual(field.Value, AccessTools.Field(Row.GetType(), field.Key).GetValue(Row),
                        "Removal must preserve scheduled " + field.Key);
                }
            }
        }

        private sealed class CorpseSnapshot
        {
            private readonly CorpseTrackingEntry entry;
            private readonly CorpseType corpse;
            private readonly MapType map;
            private readonly ThingOwner owner;
            private readonly IntVec3 position;
            private readonly bool spawned;
            private readonly HediffSet hediffSet;
            private readonly Hediff[] hediffs;
            private readonly float[] severities;
            private readonly List<HealingItem> queue;
            private readonly List<HealingItem> preQueue;
            private readonly HealingItem[] queueItems;
            private readonly float[] queueSeverity;
            private readonly HealingItem[] preItems;
            private readonly float[] preSeverity;
            private readonly bool active;
            private readonly bool hydrated;
            private readonly int startTick;
            private readonly float progress;
            private readonly float debt;
            private readonly float peakDebt;

            public CorpseSnapshot(CorpseTrackingEntry entry)
            {
                this.entry = entry;
                corpse = entry.Corpse;
                map = corpse.MapHeld;
                owner = corpse.holdingOwner;
                position = corpse.Position;
                spawned = corpse.Spawned;
                hediffSet = entry.OriginalPawn.health.hediffSet;
                hediffs = hediffSet.hediffs.ToArray();
                severities = hediffs.Select(hediff => hediff.Severity).ToArray();
                queue = entry.HealingQueue;
                preQueue = entry.PreCalculatedHealingQueue;
                queueItems = queue.ToArray();
                queueSeverity = queueItems.Select(item => item.Severity).ToArray();
                preItems = preQueue.ToArray();
                preSeverity = preItems.Select(item => item.Severity).ToArray();
                active = entry.IsHealingActive;
                hydrated = entry.HealingQueueHydrated;
                startTick = entry.HealingStartTick;
                progress = entry.HealingProgress;
                debt = EternalServiceContainer.Instance.FoodDebtSystem.GetDebt(entry.OriginalPawn);
                peakDebt = EternalServiceContainer.Instance.FoodDebtSystem.GetPeakDebt(entry.OriginalPawn);
            }

            public void AssertOwnerUnchanged(string transferTrace = null)
            {
                TestAssert.IsFalse(corpse.Destroyed, "Rollback must preserve the existing corpse");
                TestAssert.AreEqual(spawned, corpse.Spawned, "Rollback must preserve spawned state. " + transferTrace);
                TestAssert.AreEqual(map, corpse.MapHeld, "Rollback must preserve the physical source map");
                TestAssert.IsTrue(ReferenceEquals(owner, corpse.holdingOwner), "Rollback must preserve the exact ThingOwner");
                if (spawned)
                {
                    TestAssert.AreEqual(position, corpse.Position, "Rollback must preserve source position");
                }
            }

            public void AssertHealingUnchanged()
            {
                TestAssert.IsTrue(ReferenceEquals(corpse, entry.Corpse), "Recovery must preserve the exact corpse instance");
                TestAssert.IsTrue(ReferenceEquals(hediffSet, entry.OriginalPawn.health.hediffSet), "Recovery must retain anatomy tracker");
                TestAssert.IsTrue(hediffs.SequenceEqual(hediffSet.hediffs), "Recovery must preserve every anatomical hediff");
                TestAssert.IsTrue(severities.SequenceEqual(hediffSet.hediffs.Select(hediff => hediff.Severity)),
                    "Ground recovery must not add fall damage or strip anatomy");
                TestAssert.IsTrue(ReferenceEquals(queue, entry.HealingQueue) && queueItems.SequenceEqual(entry.HealingQueue),
                    "Recovery must preserve active queue identity and items");
                TestAssert.IsTrue(ReferenceEquals(preQueue, entry.PreCalculatedHealingQueue)
                    && preItems.SequenceEqual(entry.PreCalculatedHealingQueue), "Recovery must preserve the death-time queue");
                TestAssert.IsTrue(queueSeverity.SequenceEqual(queue.Select(item => item.Severity))
                    && preSeverity.SequenceEqual(preQueue.Select(item => item.Severity)), "Recovery must preserve queued work amounts");
                TestAssert.AreEqual(active, entry.IsHealingActive, "Recovery must preserve active-healing state");
                TestAssert.AreEqual(hydrated, entry.HealingQueueHydrated, "Recovery must preserve hydration state");
                TestAssert.AreEqual(startTick, entry.HealingStartTick, "Recovery must preserve healing start tick");
                TestAssert.AreEqual(progress, entry.HealingProgress, "Recovery must preserve healing progress");
                TestAssert.AreEqual(debt, EternalServiceContainer.Instance.FoodDebtSystem.GetDebt(entry.OriginalPawn),
                    "Recovery must not charge or erase debt");
                TestAssert.AreEqual(peakDebt, EternalServiceContainer.Instance.FoodDebtSystem.GetPeakDebt(entry.OriginalPawn),
                    "Recovery must preserve peak debt");
            }
        }

        private sealed class RemovalFixture : IDisposable
        {
            public readonly TestMapScope Maps = new TestMapScope();
            private readonly HashSet<WorldObject> originalObjects = new HashSet<WorldObject>(Find.WorldObjects.AllWorldObjects);
            private readonly List<Pawn> ownedPawns = new List<Pawn>();

            public MapType CreateSiteMap()
            {
                var part = DefDatabase<SitePartDef>.GetNamedSilentFail("PreciousLump");
                TestAssert.Available(part, "Initialized native site main-part def");
                var site = SiteMaker.MakeSite(part, Maps.FindUnusedSurfaceTile(), Faction.OfPlayer,
                    threatPoints: 0f);
                TestAssert.IsNotNull(site.MainSitePartDef, "Native SiteMaker must initialize a valid main site part");
                return Maps.CreateWithParent(50, site, false);
            }

            public CorpseTrackingEntry CreateCorpse(MapType map)
            {
                var pawn = Maps.SpawnEternalPawn(map);
                ownedPawns.Add(pawn);
                var wound = pawn.health.AddHediff(HediffDefOf.Cut, pawn.RaceProps.body.corePart);
                wound.Severity = 4f;
                TestPawnFactory.KillPawn(pawn);
                var entry = EternalServiceContainer.Instance.CorpseManager.GetCorpseData(pawn);
                TestAssert.IsNotNull(entry, "Actual death hook must register the test-owned corpse");
                var component = entry.Corpse.GetComp<EternalCorpseComponent>();
                TestAssert.IsNotNull(component, "Death hook must attach the native corpse protection component");
                TestAssert.IsTrue(ReferenceEquals(entry.Corpse, component.parent),
                    "Attached component must reference its exact native corpse parent");
                TestAssert.IsTrue(component.props is CompProperties_EternalCorpse
                    && component.props.compClass == typeof(EternalCorpseComponent),
                    "Attached component must initialize the actual Eternal corpse properties");
                TestAssert.IsTrue(ReferenceEquals(entry, component.CorpseData),
                    "Attached component must retain its exact tracking entry");
                TestAssert.IsTrue(entry.PreCalculatedHealingQueue?.Count > 0,
                    "The fixture must capture real injury work before death");
                TestAssert.IsTrue(EternalServiceContainer.Instance.CorpseHealingProcessor.StartCorpseHealing(entry)
                    && entry.IsHealingActive, "Fixture must activate actual corpse healing with nonempty work");
                TestAssert.IsTrue(EternalServiceContainer.Instance.FoodDebtSystem.AddResurrectionDebt(pawn, 4f),
                    "Fixture resurrection debt setup must succeed");
                return entry;
            }

            public void Dispose()
            {
                var failures = new List<Exception>();
                Action<Action> attempt = action => { try { action(); } catch (Exception ex) { failures.Add(ex); } };
                // Crash sites deep-own things through a private list rather than ThingOwner.
                // Remove only these owned pawn references before the shared corpse cleanup.
                foreach (var site in Find.WorldObjects.AllWorldObjects.OfType<WorldObject_EternalCrashSite>().ToArray())
                {
                    attempt(() =>
                    {
                        var things = (IList)AccessTools.Field(typeof(WorldObject_EternalCrashSite), "crashedThings").GetValue(site);
                        for (int i = things.Count - 1; i >= 0; i--)
                        {
                            if ((things[i] is CorpseType corpse && ownedPawns.Contains(corpse.InnerPawn))
                                || (things[i] is Pawn pawn && ownedPawns.Contains(pawn)))
                            {
                                things.RemoveAt(i);
                            }
                        }
                    });
                }

                attempt(Maps.Dispose);
                foreach (var site in Find.WorldObjects.AllWorldObjects.OfType<WorldObject_EternalCrashSite>().ToArray())
                {
                    if (!originalObjects.Contains(site) && !site.HasMap && !site.GetCrashedPawns().Any())
                    {
                        attempt(() => Find.WorldObjects.Remove(site));
                    }
                }

                if (failures.Count > 0)
                {
                    throw new AggregateException("Owned removal fixture cleanup failed", failures);
                }
            }
        }
    }
}
#endif
