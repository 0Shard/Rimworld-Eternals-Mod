#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Eternal.DI;
using Eternal.Healing;
using Eternal.InGameTests.Helpers;
using Eternal.InGameTests.Performance;
using Eternal.Models;
using Eternal.Patches;
using Eternal.World;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using MapType = Verse.Map;

namespace Eternal.InGameTests.FailureScenarios
{
    public static class ResurrectionOutcomeFailureTests
    {
        public static TestSuiteResult RunAll(MapType map)
        {
            var result = new TestSuiteResult(0, 0, new List<string>());
            foreach (bool scheduled in new[] { false, true })
            {
                bool selected = scheduled;
                result.RunTest("ResurrectionOutcomeFailure", (selected ? "Scheduled" : "Immediate") + "NativeFalseRetainsRetry",
                    () => NativeOutcome(selected, false, false));
                result.RunTest("ResurrectionOutcomeFailure", (selected ? "Scheduled" : "Immediate") + "AliveBeforeSpawnRecoversMap",
                    () => NativeOutcome(selected, true, false));
                result.RunTest("ResurrectionOutcomeFailure", (selected ? "Scheduled" : "Immediate") + "HeldAliveBeforeSpawnRecoversMap",
                    () => NativeOutcome(selected, true, true));
            }
            result.RunTest("ResurrectionOutcomeFailure", "AssignmentScribeAllPhasesThenNativeWorkReset", AssignmentRoundTrip);
            result.RunTest("ResurrectionOutcomeFailure", "PhysicalCaravanMembershipOverridesCachedId", CaravanTransfer);
            result.RunTest("ResurrectionOutcomeFailure", "MapRetryKeepsSelectedRecoverySiteOwner", () => RetainedPlacement(false));
            result.RunTest("ResurrectionOutcomeFailure", "CaravanRetryKeepsSelectedRecoverySiteOwner", () => RetainedPlacement(true));
            result.RunTest("ResurrectionOutcomeFailure", "ActiveCrashQueuePreservesPaidWorkAndAddsRegrowth", ActiveCrashQueue);
            result.RunTest("ResurrectionOutcomeFailure", "SuppressedNpcMarkerUsesNativeRemoval", SuppressedMarker);
            result.RunTest("ResurrectionOutcomeFailure", "FatalFallRetainsActualCorpse", () => FallOutcome(true));
            result.RunTest("ResurrectionOutcomeFailure", "NonfatalFallRetainsLivingPawn", () => FallOutcome(false));
            return result;
        }

        private static void FallOutcome(bool fatal)
        {
            using (var scope = new TestMapScope())
            {
                var pawn = scope.SpawnEternalPawn(scope.Create(50));
                var site = SpaceCrashRescueService.CreateOrGetCrashSite(scope.FindUnusedSurfaceTile());
                float originalChance = Find.Storyteller.difficulty.allowInstantKillChance;
                var harmony = new Harmony("Eternal.Debug.FallOutcome");
                fallPawn = pawn;
                fallPart = fatal ? pawn.RaceProps.body.corePart
                    : pawn.RaceProps.body.AllParts.First(part => part.def == BodyPartDefOf.Arm);
                try
                {
                    Find.Storyteller.difficulty.allowInstantKillChance = 1f;
                    harmony.Patch(AccessTools.Method(typeof(Thing), "TakeDamage"),
                        prefix: new HarmonyMethod(typeof(ResurrectionOutcomeFailureTests), nameof(TargetFallDamage)));
                    pawn.DeSpawn();
                    TestAssert.IsTrue(SpaceCrashRescueService.StageAfterFallDamage(pawn, site), "Post-damage handoff must accept the actual surviving Thing");
                    TestAssert.AreEqual(fatal, pawn.Dead, "Fixture's native hit target determines fatal versus nonfatal outcome");
                    TestAssert.IsTrue(fatal ? site.HasPendingCorpse(pawn.Corpse) && !site.HasPendingPawn(pawn)
                        : site.HasPendingPawn(pawn), "Site deep owner must retain corpse only after fatal native damage");
                }
                finally
                {
                    harmony.UnpatchAll(harmony.Id);
                    fallPawn = null; fallPart = null;
                    Find.Storyteller.difficulty.allowInstantKillChance = originalChance;
                    ((List<Thing>)AccessTools.Field(typeof(WorldObject_EternalCrashSite), "crashedThings").GetValue(site)).Clear();
                    if (site.Spawned) { Find.WorldObjects.Remove(site); }
                }
            }
        }
        private static Pawn fallPawn;
        private static BodyPartRecord fallPart;
        private static void TargetFallDamage(Thing __instance, ref DamageInfo dinfo)
        {
            if (__instance == fallPawn) { dinfo.SetHitPart(fallPart); }
        }

        private static CorpseTrackingEntry Kill(TestMapScope scope, MapType map)
        {
            var pawn = scope.SpawnEternalPawn(map);
            pawn.workSettings.EnableAndInitialize();
            pawn.workSettings.SetPriority(DefDatabase<WorkTypeDef>.GetNamed("Doctor"), 2);
            pawn.health.AddHediff(HediffDefOf.Cut, pawn.RaceProps.body.corePart).Severity = 1f;
            TestPawnFactory.KillPawn(pawn);
            var entry = EternalServiceContainer.Instance.CorpseManager.GetCorpseData(pawn);
            TestAssert.IsNotNull(entry, "Native death must register the exact corpse");
            return entry;
        }

        private static void NativeOutcome(bool scheduled, bool partial, bool held)
        {
            using (var scope = new TestMapScope())
            {
                var map = scope.Create(50);
                var entry = Kill(scope, map);
                var pawn = entry.OriginalPawn;
                if (held)
                {
                    var carrier = scope.SpawnNonEternalPawn(map);
                    entry.Corpse.DeSpawn();
                    TestAssert.IsTrue(carrier.inventory.innerContainer.TryAdd(entry.Corpse), "Native inventory must own corpse");
                }
                var set = pawn.health.hediffSet;
                var immunity = pawn.health.immunity;
                entry.HealingQueue = new List<HealingItem>();
                entry.HealingQueueHydrated = true;
                entry.IsHealingActive = scheduled;
                var processor = EternalServiceContainer.Instance.CorpseHealingProcessor;
                processor.RebuildActiveHealingCorpses();
                EternalServiceContainer.Instance.FoodDebtSystem.AddResurrectionDebt(pawn, 0.125f);
                var debt = EternalServiceContainer.Instance.FoodDebtSystem.GetDebt(pawn);
                using (var fault = new ResurrectionFault(pawn, partial))
                {
                    if (scheduled) { processor.ProcessCorpseInjuryHealing(); }
                    else { TestAssert.AreEqual(partial, processor.StartCorpseHealing(entry), "Immediate caller must honor explicit outcome"); }
                    TestAssert.AreEqual(1, fault.Calls, "Requested native failure seam must actually execute once");
                }
                TestAssert.IsTrue(ReferenceEquals(set, pawn.health.hediffSet) && ReferenceEquals(immunity, pawn.health.immunity),
                    "Both native outcomes must restore original health references");
                TestAssert.AreEqual(debt, EternalServiceContainer.Instance.FoodDebtSystem.GetDebt(pawn), "Same exact pawn keeps charged work");
                if (partial)
                {
                    TestAssert.IsFalse(pawn.Dead, "Fault must occur after native Notify_Resurrected");
                    TestAssert.IsTrue(pawn.Spawned && pawn.Map == map, "Alive-before-spawn fault must recover captured held map");
                    TestAssert.IsFalse(EternalServiceContainer.Instance.CorpseManager.IsTracked(pawn), "Owned success retires consumed corpse");
                    TestAssert.IsFalse(entry.IsHealingActive, "Owned success removes dead-only active state");
                    TestAssert.AreEqual(2, pawn.workSettings.GetPriority(DefDatabase<WorkTypeDef>.GetNamed("Doctor")),
                        "Partial native initialization still restores captured work through native SetPriority");
                }
                else
                {
                    TestAssert.IsTrue(pawn.Dead && !entry.Corpse.Destroyed, "Native false must not consume the real corpse");
                    TestAssert.IsTrue(EternalServiceContainer.Instance.CorpseManager.IsTracked(pawn), "False result retains retry entry");
                    TestAssert.AreEqual(scheduled, entry.IsHealingActive, "False result preserves coherent scheduled retry state");
                }
            }
        }

        private static void AssignmentRoundTrip()
        {
            using (var scope = new TestMapScope())
            {
                var pawn = scope.SpawnEternalPawn(scope.Create(50));
                pawn.workSettings.EnableAndInitialize();
                var doctor = DefDatabase<WorkTypeDef>.GetNamed("Doctor");
                pawn.workSettings.SetPriority(doctor, 3);
                var snapshot = PawnAssignmentSnapshot.CaptureFrom(pawn);
                var vanilla = (Dictionary<string, int>)AccessTools.Field(typeof(PawnAssignmentSnapshot), "workTypePriorities").GetValue(snapshot);
                string giver = DefDatabase<WorkGiverDef>.AllDefs.First(def => def.workType == doctor).defName;
                var workTab = new Dictionary<string, int[]> { [giver] = Enumerable.Repeat(3, 24).ToArray() };
                AccessTools.Field(typeof(PawnAssignmentSnapshot), "workGiverPriorities").SetValue(snapshot, workTab);
                AccessTools.Field(typeof(PawnAssignmentSnapshot), "hasWorkTabData").SetValue(snapshot, true);
                string path = Path.Combine(Path.GetTempPath(), "eternal-assignments-" + Guid.NewGuid().ToString("N") + ".xml");
                try
                {
                    Scribe.saver.InitSaving(path, "assignmentFixture");
                    Scribe_Deep.Look(ref snapshot, "snapshot");
                    Scribe.saver.FinalizeSaving();
                    PawnAssignmentSnapshot loaded = null;
                    Scribe.loader.InitLoading(path);
                    Scribe_Deep.Look(ref loaded, "snapshot");
                    Scribe.loader.FinalizeLoading();
                    var loadedVanilla = (Dictionary<string, int>)AccessTools.Field(typeof(PawnAssignmentSnapshot), "workTypePriorities").GetValue(loaded);
                    var loadedWorkTab = (Dictionary<string, int[]>)AccessTools.Field(typeof(PawnAssignmentSnapshot), "workGiverPriorities").GetValue(loaded);
                    TestAssert.AreEqual(vanilla.Count, loadedVanilla.Count, "Actual PostLoadInit must retain vanilla dictionary");
                    TestAssert.IsTrue(workTab[giver].SequenceEqual(loadedWorkTab[giver]), "All 24 WorkTab values survive native load phases");
                    pawn.workSettings.EnableAndInitialize();
                    pawn.workSettings.SetPriority(doctor, 0);
                    loaded.ApplyTo(pawn);
                    TestAssert.AreEqual(3, pawn.workSettings.GetPriority(doctor), "Loaded vanilla fallback restores native reset priorities");
                }
                finally { Scribe.ForceStop(); if (File.Exists(path)) { File.Delete(path); } }
            }
        }

        private static void CaravanTransfer()
        {
            using (var scope = new TestMapScope())
            {
                var entry = Kill(scope, scope.Create(50));
                var carrier1 = scope.SpawnNonEternalPawn(scope.Create(50));
                var carrier2 = scope.SpawnNonEternalPawn(scope.Create(50));
                var tile = carrier1.Tile;
                carrier1.DeSpawn(); carrier2.DeSpawn();
                var first = CaravanMaker.MakeCaravan(new[] { carrier1 }, Faction.OfPlayer, tile, true);
                var second = CaravanMaker.MakeCaravan(new[] { carrier2 }, Faction.OfPlayer, first.Tile, true);
                try
                {
                    entry.Corpse.DeSpawn();
                    TestAssert.IsTrue(carrier1.inventory.innerContainer.TryAdd(entry.Corpse), "C1 initially owns corpse");
                    entry.CaravanId = first.ID;
                    carrier1.inventory.innerContainer.Remove(entry.Corpse);
                    TestAssert.IsTrue(carrier2.inventory.innerContainer.TryAdd(entry.Corpse), "C2 physically receives corpse");
                    entry.HealingQueue = new List<HealingItem>(); entry.HealingQueueHydrated = true;
                    TestAssert.IsTrue(EternalServiceContainer.Instance.CorpseHealingProcessor.StartCorpseHealing(entry), "Transferred corpse must resurrect");
                    TestAssert.IsTrue(first.Spawned && second.ContainsPawn(entry.OriginalPawn) && !first.ContainsPawn(entry.OriginalPawn),
                        "Living C1 cache must not redirect the physical C2 resurrection");
                    TestAssert.IsTrue(entry.OriginalPawn.IsWorldPawn(), "Reference-only caravan membership requires actual WorldPawns deep ownership");
                }
                finally
                {
                    foreach (var caravan in new[] { first, second })
                    {
                        foreach (var pawn in caravan.PawnsListForReading.ToArray()) { caravan.RemovePawn(pawn); }
                        if (caravan.Spawned) { Find.WorldObjects.Remove(caravan); }
                    }
                }
            }
        }

        private static void RetainedPlacement(bool useCaravan)
        {
            using (var scope = new TestMapScope())
            {
                var map = scope.Create(50);
                var pawn = scope.SpawnEternalPawn(map);
                pawn.DeSpawn();
                RimWorld.Planet.Caravan caravan = null;
                if (useCaravan)
                {
                    var carrier = scope.SpawnNonEternalPawn(map);
                    carrier.DeSpawn();
                    caravan = CaravanMaker.MakeCaravan(new[] { carrier }, Faction.OfPlayer, map.Tile, true);
                }
                var site = SpaceCrashRescueService.CreateOrGetCrashSite(scope.FindUnusedSurfaceTile());
                var ensure = AccessTools.Method(typeof(EternalCorpseHealingProcessor), "EnsureResurrectedOwner");
                try
                {
                    using (var fault = new PlacementFault(pawn, useCaravan))
                    {
                        object[] arguments = { pawn, caravan, useCaravan ? null : map, map.Center, site };
                        TestAssert.IsTrue((bool)ensure.Invoke(null, arguments), "First rejected destination must fall back to a real site deep owner");
                        TestAssert.AreEqual(1, fault.Calls, "First native placement was actually rejected");
                        TestAssert.IsTrue(site.HasPendingPawn(pawn) && !pawn.Spawned && !pawn.IsWorldPawn(), "Fallback has exactly one deep owner");
                        TestAssert.IsTrue((bool)ensure.Invoke(null, arguments), "Cleanup accepts the selected fallback destination");
                        TestAssert.AreEqual(1, fault.Calls, "A now-allowed second placement cannot transfer the site-owned pawn");
                        TestAssert.IsFalse(pawn.Spawned || pawn.IsWorldPawn() || (caravan?.ContainsPawn(pawn) ?? false),
                            "No map or reference-only caravan owner is introduced after site retention");
                        TestAssert.AreEqual(1, Find.WorldObjects.AllWorldObjects.OfType<WorldObject_EternalCrashSite>().Count(owner => owner.HasPendingPawn(pawn)),
                            "Only the selected recovery site's deep list owns the pawn");
                    }
                }
                finally
                {
                    ((List<Thing>)AccessTools.Field(typeof(WorldObject_EternalCrashSite), "crashedThings").GetValue(site)).Clear();
                    if (site.Spawned) { Find.WorldObjects.Remove(site); }
                    if (caravan != null)
                    {
                        foreach (var member in caravan.PawnsListForReading.ToArray()) { caravan.RemovePawn(member); }
                        if (caravan.Spawned) { Find.WorldObjects.Remove(caravan); }
                    }
                }
            }
        }

        private sealed class PlacementFault : IDisposable
        {
            private static PlacementFault active;
            private readonly Harmony harmony = new Harmony("Eternal.Debug.RetainedPlacement");
            private readonly Pawn pawn;
            public int Calls;
            internal PlacementFault(Pawn pawn, bool caravan)
            {
                this.pawn = pawn;
                active = this;
                var target = caravan ? AccessTools.Method(typeof(RimWorld.Planet.Caravan), "AddPawn")
                    : AccessTools.Method(typeof(GenSpawn), "Spawn", new[] { typeof(Thing), typeof(IntVec3), typeof(MapType), typeof(Rot4), typeof(WipeMode), typeof(bool), typeof(bool) });
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(PlacementFault), nameof(RejectFirst)));
            }
            private static void RejectFirst(object __0)
            {
                if (active == null || !ReferenceEquals(__0, active.pawn)) { return; }
                active.Calls++;
                if (active.Calls == 1) { throw new InvalidOperationException("Owned first placement failure; subsequent placement is allowed"); }
            }
            public void Dispose() { harmony.UnpatchAll(harmony.Id); active = null; }
        }

        private static void ActiveCrashQueue()
        {
            using (var scope = new TestMapScope())
            {
                var entry = Kill(scope, scope.Create(50));
                var pawn = entry.OriginalPawn;
                entry.HealingQueue = new List<HealingItem> { new HealingItem { Pawn = pawn, Type = HealingType.Injury, Severity = 1f } };
                entry.HealingQueueHydrated = true; entry.IsHealingActive = true;
                var original = entry.HealingQueue[0];
                var hand = pawn.RaceProps.body.AllParts.First(part => part.def == BodyPartDefOf.Hand);
                pawn.health.AddHediff(HediffDefOf.MissingBodyPart, hand);
                EternalServiceContainer.Instance.RegrowthManager.StartRegrowth(pawn);
                var growth = pawn.health.hediffSet.hediffs.OfType<EternalRegrowing_Hediff>().Single(item => item.forPart == hand);
                growth.Severity = 0.4f;
                EternalServiceContainer.Instance.FoodDebtSystem.AddResurrectionDebt(pawn, 0.25f);
                float debt = EternalServiceContainer.Instance.FoodDebtSystem.GetDebt(pawn);
                SpaceCrashRescueService.StripToTorso(pawn);
                TestAssert.IsTrue(pawn.health.hediffSet.hediffs.Contains(growth) && growth.Severity == 0.4f,
                    "Native ancestor stripping must preserve the exact already-paid per-part progress object");
                var fresh = new EternalResurrectionCalculator().CalculateHealingQueue(pawn);
                var processor = EternalServiceContainer.Instance.CorpseHealingProcessor;
                processor.ReconcileCrashRegrowth(pawn, fresh);
                int count = entry.HealingQueue.Count;
                processor.ReconcileCrashRegrowth(pawn, fresh);
                TestAssert.AreEqual(count, entry.HealingQueue.Count, "Repeated crash reconciliation must not duplicate new work");
                TestAssert.IsTrue(entry.HealingQueue.Contains(original) && original.Severity == 1f,
                    "Hydrated active injury work retains identity and remaining severity");
                TestAssert.IsTrue(entry.HealingQueue.Any(item => item.Type == HealingType.Regrowth), "Injury-only active corpse gains regrowth entries");
                TestAssert.AreEqual(debt, EternalServiceContainer.Instance.FoodDebtSystem.GetDebt(pawn), "Reconciliation must not charge old work");
            }
        }

        private static void SuppressedMarker()
        {
            using (var scope = new TestMapScope())
            {
                var pawn = scope.SpawnEternalPawn(scope.Create(50));
                pawn.SetFaction(null);
                var marker = pawn.story.traits.allTraits.Single(t => t.def == EternalDefOf.Eternal_GeneticMarker);
                marker.suppressedByTrait = true;
                PawnGenerator_GenerateNewPawnInternal_Patch.Postfix(pawn);
                TestAssert.IsFalse(pawn.story.traits.allTraits.Any(t => t.def == EternalDefOf.Eternal_GeneticMarker), "NPC cleanup removes suppressed marker");
                TestAssert.IsFalse(pawn.health.hediffSet.HasHediff(EternalDefOf.Eternal_Essence), "NPC cleanup removes Essence after marker notifications");
            }
        }

        private sealed class ResurrectionFault : IDisposable
        {
            private static ResurrectionFault active;
            private readonly Harmony harmony = new Harmony("Eternal.Debug.ResurrectionOutcome");
            private readonly Pawn pawn;
            public int Calls;
            public ResurrectionFault(Pawn pawn, bool partial)
            {
                this.pawn = pawn; active = this;
                harmony.Patch(partial ? AccessTools.Method(typeof(Pawn_WorkSettings), "EnableAndInitialize")
                    : AccessTools.Method(typeof(ResurrectionUtility), "TryResurrect"),
                    prefix: new HarmonyMethod(typeof(ResurrectionFault), partial ? nameof(ThrowBeforeSpawn) : nameof(Reject)));
            }
            private static bool Reject(Pawn pawn, ref bool __result)
            {
                if (active?.pawn != pawn) { return true; }
                active.Calls++; __result = false; return false;
            }
            private static void ThrowBeforeSpawn(Pawn_WorkSettings __instance)
            {
                if (active == null || AccessTools.Field(typeof(Pawn_WorkSettings), "pawn").GetValue(__instance) != active.pawn) { return; }
                active.Calls++;
                throw new InvalidOperationException("Owned failure after Notify_Resurrected and before native spawn");
            }
            public void Dispose() { harmony.UnpatchAll(harmony.Id); active = null; }
        }
    }
}
#endif
