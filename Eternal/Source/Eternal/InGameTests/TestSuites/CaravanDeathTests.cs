// Relative Path: Eternal/Source/Eternal/InGameTests/TestSuites/CaravanDeathTests.cs
// Creation Date: 24-02-2026
// Last Edit: 24-02-2026
// Author: 0Shard
// Description: Native caravan death, stored-corpse resurrection, assignment/debt transfer and solo-loss regression.

#if DEBUG

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Eternal.Caravan;
using Eternal.DI;
using Eternal.InGameTests.Helpers;
using Eternal.InGameTests.Performance;
using Eternal.World;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using CaravanType = RimWorld.Planet.Caravan;
using MapType = Verse.Map;

namespace Eternal.InGameTests.TestSuites
{
    /// <summary>
    /// Uses native caravan creation and real death/ticks; owns every added pawn and world object.
    /// </summary>
    public static class CaravanDeathTests
    {
        public static TestSuiteResult RunAll(Verse.Map map)
        {
            var result = new TestSuiteResult(0, 0, new List<string>());

            result.RunTest("CaravanDeath", "MapPawnIsNotInCaravan", () => MapPawnIsNotInCaravan(map));
            result.RunTest("CaravanDeath", "DeadMapPawnIsNotInCaravan", () => DeadMapPawnIsNotInCaravan(map));
            result.RunTest("CaravanDeath", "NativeCaravanDeathAndResurrection", () => NativeResurrection(true));
            result.RunTest("CaravanDeath", "StoredCorpseInNativeCaravanResurrects", () => NativeResurrection(false));
            result.RunTest("CaravanDeath", "SoloCaravanLastEternalDeathPreservesCorpse", SoloCaravanDeath);
            result.RunTest("CaravanDeath", "SoloRecoveryStagesWhenPublicDeliveryFails", SoloRecoveryPublicDeliveryFailure);
            result.RunTest("CaravanDeath", "SoloPreflightFailureDefersDeathAndRetry", () => SoloPreflightFailure(false));
            result.RunTest("CaravanDeath", "SoloSitePreflightFailureWithHealthyHomeDefersAndRetry", () => SoloPreflightFailure(true));

            return result;
        }

        private static void MapPawnIsNotInCaravan(Verse.Map map)
        {
            var pawn = TestPawnFactory.SpawnEternalPawn(map);
            TestAssert.IsNotNull(pawn, "Pawn should be spawned");

            bool inCaravan = EternalCaravanDeathHandler.IsPawnInCaravan(pawn);
            TestAssert.IsFalse(inCaravan, "Map-spawned pawn should NOT be detected as in caravan");
        }

        private static void DeadMapPawnIsNotInCaravan(Verse.Map map)
        {
            var pawn = TestPawnFactory.SpawnEternalPawn(map);
            TestPawnFactory.KillPawn(pawn);
            TestAssert.IsTrue(pawn.Dead, "Pawn should be dead");

            bool inCaravan = EternalCaravanDeathHandler.IsPawnInCaravan(pawn);
            TestAssert.IsFalse(inCaravan, "Dead map pawn should NOT be detected as in caravan");
        }

        private static void NativeResurrection(bool dieInCaravan)
        {
            using (var fixture = new CaravanFixture())
            {
                var preparation = fixture.Maps.Create(50);
                var pawn = fixture.Maps.SpawnEternalPawn(preparation);
                var controlMap = fixture.Maps.Create(50);
                var control = fixture.Maps.SpawnEternalPawn(controlMap);
                var controlEssence = control.health.hediffSet.GetFirstHediffOfDef(EternalDefOf.Eternal_Essence);
                var controlDebt = EternalServiceContainer.Instance.FoodDebtSystem.GetDebt(control);
                // An ordinary carrier is a separate edge-case fixture, on its own preparation
                // map; it keeps native caravan ownership alive while the Eternal is dead.
                var carrierMap = fixture.Maps.Create(50);
                var carrier = fixture.Maps.SpawnNonEternalPawn(carrierMap);
                var assignments = new AssignmentObservation(pawn);
                var wound = pawn.health.AddHediff(HediffDefOf.Cut, pawn.RaceProps.body.corePart);
                wound.Severity = 1f;
                var caravan = fixture.Make(dieInCaravan ? new[] { pawn, carrier } : new[] { carrier });
                if (dieInCaravan)
                {
                    TestAssert.IsTrue(EternalCaravanDeathHandler.IsPawnInCaravan(pawn),
                        "Actual native caravan membership must be detected before death");
                }

                using (var observation = new CaravanObservation(pawn, caravan))
                {
                    TestPawnFactory.KillPawn(pawn);
                    TestAssert.IsTrue(pawn.Dead, "Native caravan fixture must actually kill the Eternal");
                    var services = EternalServiceContainer.Instance;
                    var entry = services.CorpseManager.GetCorpseData(pawn);
                    TestAssert.IsNotNull(entry, "Real death hook must register the corpse");
                    var corpse = entry.Corpse;
                    TestAssert.IsNotNull(corpse, "Real death must retain the exact corpse");
                    TestAssert.IsTrue(entry.PreCalculatedHealingQueue?.Count > 0,
                        "Death-time injury work must be captured for real tick healing");
                    TestAssert.AreEqual(dieInCaravan ? 1 : 0, observation.DeathRegistrations,
                        "Only an actual caravan death should call the caravan registration overload");
                    if (dieInCaravan)
                    {
                        TestAssert.AreEqual(caravan.ID, entry.CaravanId, "Death hook must save the native caravan identity");
                    }
                    else
                    {
                        TestAssert.IsTrue(services.CorpseManager.TryReleaseCorpseToUnspawnedOwner(pawn, () =>
                        {
                            corpse.DeSpawn(DestroyMode.WillReplace);
                            caravan.AddPawnOrItem(corpse, true);
                            return caravan.AllThings.Contains(corpse);
                        }), "Native caravan inventory must accept the stored map corpse");
                    }

                    var owner = CaravanInventoryUtility.GetOwnerOf(caravan, corpse);
                    TestAssert.AreEqual(carrier, owner, "The real ordinary carrier must own the corpse inventory");
                    TestAssert.IsTrue(ReferenceEquals(carrier.inventory.innerContainer, corpse.holdingOwner)
                        && caravan.AllThings.Contains(corpse), "Corpse must have native container ownership");
                    TestAssert.IsFalse(corpse.Spawned || corpse.Destroyed,
                        "Caravan corpse must be retained and unspawned");
                    TestAssert.IsTrue(EternalCaravanDeathHandler.IsPawnInCaravan(pawn),
                        "Detection must follow actual corpse inventory ownership");
                    TestAssert.IsNotNull(entry.AssignmentSnapshot, "Death must capture assignment restoration state");
                    var essence = pawn.health.hediffSet.GetFirstHediffOfDef(EternalDefOf.Eternal_Essence);
                    TestAssert.IsTrue(services.CorpseHealingProcessor.StartCorpseHealing(entry) && entry.IsHealingActive,
                        "StartCorpseHealing must activate the nonempty native corpse queue");
                    int elapsed = TickAdvancer.AdvanceUntil(() => !pawn.Dead, 100000,
                        "Native caravan corpse did not resurrect within the real tick limit");
                    TestAssert.IsTrue(elapsed > 0, "Nonempty caravan injury work must run through actual ticks");
                    TickAdvancer.AdvanceTicks(10);
                    TestAssert.IsFalse(pawn.Dead || pawn.Downed, "Resurrected caravan pawn must survive and remain mobile");
                    TestAssert.IsTrue(caravan.ContainsPawn(pawn) && pawn.GetCaravan() == caravan && !pawn.Spawned,
                        "Resurrection must re-add the exact pawn to the original native caravan");
                    TestAssert.AreEqual(1, caravan.PawnsListForReading.Count(member => member == pawn),
                        "Resurrection must not duplicate caravan membership");
                    TestAssert.IsTrue(corpse.Destroyed && !caravan.AllThings.Contains(corpse)
                        && !carrier.inventory.innerContainer.Contains(corpse), "Consumed corpse must leave native inventory");
                    TestAssert.IsFalse(services.CorpseManager.IsTracked(pawn), "Successful resurrection must retire corpse tracking");
                    TestAssert.IsTrue(ReferenceEquals(essence,
                        pawn.health.hediffSet.GetFirstHediffOfDef(EternalDefOf.Eternal_Essence)),
                        "Caravan resurrection must retain the original Essence instance");
                    assignments.AssertRestored(pawn);
                    observation.AssertOneResurrection();
                    TestAssert.IsFalse(control.Dead, "Healthy isolated Eternal control must survive caravan work");
                    TestAssert.AreEqual(controlMap, control.Map, "Control Eternal must keep its own map ownership");
                    TestAssert.IsTrue(ReferenceEquals(controlEssence,
                        control.health.hediffSet.GetFirstHediffOfDef(EternalDefOf.Eternal_Essence)),
                        "Control Eternal must retain its Essence");
                    TestAssert.AreEqual(controlDebt, services.FoodDebtSystem.GetDebt(control),
                        "Caravan resurrection must not transfer debt to another Eternal");
                }
            }
        }

        private static void SoloCaravanDeath()
        {
            using (var fixture = new CaravanFixture())
            {
                var map = fixture.Maps.Create(50);
                var pawn = fixture.Maps.SpawnEternalPawn(map);
                var assignments = PrepareSoloPawn(pawn);
                var caravan = fixture.Make(new[] { pawn });
                TestAssert.IsTrue(caravan.ContainsPawn(pawn) && pawn.GetCaravan() == caravan,
                    "Solo fixture must be a real spawned native caravan before death");
                using (var observation = new CaravanObservation(pawn, caravan))
                {
                    TestPawnFactory.KillPawn(pawn);
                    FinishSoloRecovery(fixture, pawn, caravan, assignments, observation);
                }
            }
        }

        private static AssignmentObservation PrepareSoloPawn(Pawn pawn)
        {
            var assignments = new AssignmentObservation(pawn);
            pawn.health.AddHediff(HediffDefOf.Cut, pawn.RaceProps.body.corePart).Severity = 1f;
            return assignments;
        }

        private static void SoloRecoveryPublicDeliveryFailure()
        {
            using (var fixture = new CaravanFixture())
            {
                var pawn = fixture.Maps.SpawnEternalPawn(fixture.Maps.Create(50));
                var assignments = PrepareSoloPawn(pawn);
                var caravan = fixture.Make(new[] { pawn });
                using (var observation = new CaravanObservation(pawn, caravan, failPublicDelivery: true))
                {
                    TestPawnFactory.KillPawn(pawn);
                    TestAssert.IsTrue(observation.PublicDeliveryFailures > 0,
                        "Owned public AddCorpse fault must actually fire before fallback staging");
                    FinishSoloRecovery(fixture, pawn, caravan, assignments, observation);
                }
            }
        }

        private static void SoloPreflightFailure(bool healthyHomeExists)
        {
            using (var fixture = new CaravanFixture())
            {
                var pawn = fixture.Maps.SpawnEternalPawn(fixture.Maps.Create(50));
                var assignments = PrepareSoloPawn(pawn);
                var caravan = fixture.Make(new[] { pawn });
                MapType healthyHome = null;
                Pawn healthyControl = null;
                if (healthyHomeExists)
                {
                    healthyHome = fixture.Maps.Create(50);
                    healthyControl = fixture.Maps.SpawnEternalPawn(healthyHome);
                    TestAssert.IsTrue(healthyHome.IsPlayerHome && !healthyControl.Dead,
                        "Combined preflight fixture must have a real healthy surviving home");
                }
                var set = pawn.health.hediffSet;
                var age = pawn.ageTracker.AgeBiologicalTicks;
                var debt = EternalServiceContainer.Instance.FoodDebtSystem.GetDebt(pawn);
                var trackedBefore = EternalServiceContainer.Instance.CorpseManager.GetAllCorpses().ToArray();
                using (var observation = new CaravanObservation(pawn, caravan, failPreflight: true,
                    hideHomesOnPreflightFailure: !healthyHomeExists))
                {
                    TestPawnFactory.KillPawn(pawn);
                    TestAssert.IsTrue(observation.PreflightFailures > 0, "Owned destination preflight fault must actually fire");
                    TestAssert.IsFalse(pawn.Dead, "Broken recovery must defer solo death before native destructive work");
                    TestAssert.IsNull(pawn.Corpse, "Deferred death must not create or duplicate a corpse");
                    TestAssert.IsTrue(caravan.ContainsPawn(pawn) && caravan.Spawned,
                        "Deferred death must preserve original caravan ownership");
                    TestAssert.IsFalse(caravan.Destroyed, "Site preflight failure must not destroy the world caravan");
                    TestAssert.IsTrue(Find.WorldObjects.Caravans.Contains(caravan),
                        "Site preflight failure must not remove the caravan from the world");
                    TestAssert.IsTrue(ReferenceEquals(set, pawn.health.hediffSet), "Preflight failure must preserve anatomy tracker");
                    TestAssert.AreEqual(age, pawn.ageTracker.AgeBiologicalTicks, "Preflight failure must not alter pawn age");
                    TestAssert.AreEqual(debt, EternalServiceContainer.Instance.FoodDebtSystem.GetDebt(pawn),
                        "Preflight failure must not charge a death episode");
                    TestAssert.AreEqual(0, observation.PrimaryDeathNotifications, "Deferred death must not emit a false death notification");
                    TestAssert.IsFalse(EternalServiceContainer.Instance.CorpseManager.IsTracked(pawn),
                        "Deferred death must not register a phantom corpse");
                    TestAssert.IsTrue(trackedBefore.SequenceEqual(EternalServiceContainer.Instance.CorpseManager.GetAllCorpses()),
                        "Deferred death must preserve all existing corpse entries and their queues");
                    if (healthyHomeExists)
                    {
                        TestAssert.IsTrue(healthyHome.IsPlayerHome && !healthyControl.Dead && healthyControl.Map == healthyHome,
                            "A healthy home must remain available while unavailable site reservation still defers death");
                    }
                }
                using (var observation = new CaravanObservation(pawn, caravan))
                {
                    TestPawnFactory.KillPawn(pawn);
                    FinishSoloRecovery(fixture, pawn, caravan, assignments, observation);
                }
            }
        }

        private static void FinishSoloRecovery(CaravanFixture fixture, Pawn pawn, CaravanType caravan,
            AssignmentObservation assignments, CaravanObservation observation)
        {
            var services = EternalServiceContainer.Instance;
            var entry = services.CorpseManager.GetCorpseData(pawn);
            TestAssert.IsTrue(pawn.Dead, "Recovered solo death must execute the native death pipeline");
            TestAssert.IsNotNull(entry, "Primary death callback must register the solo corpse");
            var corpse = entry.Corpse;
            TestAssert.IsNotNull(corpse, "Solo caravan death must preserve the actual native corpse");
            TestAssert.IsFalse(corpse.Destroyed || corpse.Spawned, "Solo corpse must be retained in persistent recovery storage");
            var sites = Find.WorldObjects.AllWorldObjects.OfType<WorldObject_EternalCrashSite>()
                .Where(site => site.GetCrashedPawns().Contains(pawn)).ToArray();
            TestAssert.AreEqual(1, sites.Length, "Exactly one native recovery site must own the solo corpse");
            var stored = (IEnumerable<Thing>)AccessTools.Field(typeof(WorldObject_EternalCrashSite), "crashedThings").GetValue(sites[0]);
            TestAssert.AreEqual(1, stored.Count(thing => ReferenceEquals(thing, corpse)),
                "The existing deep-save list must contain the exact corpse once");
            TestAssert.IsFalse(pawn.inventory.innerContainer.Contains(corpse), "A corpse must never be nested in its own dead pawn");
            TestAssert.IsTrue(caravan.Destroyed && !Find.WorldObjects.AllWorldObjects.Contains(caravan),
                "Successful recovery must finish ordinary removal of the now-empty caravan");
            TestAssert.AreEqual(1, observation.PrimaryDeathNotifications, "Real solo death must emit its primary callback exactly once");
            TestAssert.IsTrue(entry.PreCalculatedHealingQueue?.Count > 0, "Solo corpse must retain actual death-time injury work");
            // Enter the real persistent recovery site through native map generation before
            // requesting resurrection, as a player would after reaching the world object.
            var recoveryMap = fixture.Maps.CreateWithParent(50, sites[0], false);
            TestAssert.IsTrue(corpse.Spawned && corpse.Map == recoveryMap, "Native site entry must spawn the original tracked corpse");
            TestAssert.IsTrue(services.CorpseHealingProcessor.StartCorpseHealing(entry) && entry.IsHealingActive,
                "Recovered solo corpse must begin real injury healing");
            TestAssert.IsTrue(TickAdvancer.AdvanceUntil(() => !pawn.Dead, 100000,
                "Recovered solo Eternal did not resurrect through real ticks") > 0, "Solo recovery must process nonempty work");
            TickAdvancer.AdvanceTicks(10);
            TestAssert.IsFalse(pawn.Dead || pawn.Downed, "Recovered solo Eternal must survive resurrection");
            TestAssert.AreEqual(recoveryMap, pawn.Map, "Recovered solo Eternal must own the entered ground map");
            assignments.AssertRestored(pawn);
            observation.AssertOneResurrection(expectedReadds: 0);
        }

        private sealed class AssignmentObservation
        {
            private readonly WorkTypeDef[] work;
            private readonly TimeAssignmentDef[] schedule;
            private readonly object apparel;
            private readonly object drugs;
            private readonly object food;
            public AssignmentObservation(Pawn pawn)
            {
                TestAssert.Available(pawn.workSettings, "Native caravan work settings");
                TestAssert.Available(pawn.timetable, "Native caravan timetable");
                work = DefDatabase<WorkTypeDef>.AllDefsListForReading.Where(def => !pawn.WorkTypeIsDisabled(def)).Take(2).ToArray();
                TestAssert.IsTrue(work.Length == 2, "Native pawn requires two enabled work types for assignment proof");
                pawn.workSettings.SetPriority(work[0], 1);
                pawn.workSettings.SetPriority(work[1], 2);
                TestAssert.AreEqual(1, pawn.workSettings.GetPriority(work[0]), "First manual priority must be established before death");
                TestAssert.AreEqual(2, pawn.workSettings.GetPriority(work[1]), "Second manual priority must be established before death");
                schedule = Enumerable.Range(0, 24).Select(hour => hour < 8
                    ? TimeAssignmentDefOf.Sleep : TimeAssignmentDefOf.Anything).ToArray();
                for (int hour = 0; hour < 24; hour++)
                {
                    pawn.timetable.SetAssignment(hour, schedule[hour]);
                }

                apparel = pawn.outfits?.CurrentApparelPolicy;
                drugs = pawn.drugs?.CurrentPolicy;
                food = pawn.foodRestriction?.CurrentFoodPolicy;
            }
            public void AssertRestored(Pawn pawn)
            {
                TestAssert.AreEqual(1, pawn.workSettings.GetPriority(work[0]), "Caravan resurrection must restore first work priority");
                TestAssert.AreEqual(2, pawn.workSettings.GetPriority(work[1]), "Caravan resurrection must restore second work priority");
                for (int hour = 0; hour < 24; hour++)
                {
                    TestAssert.AreEqual(schedule[hour], pawn.timetable.GetAssignment(hour), "Caravan timetable hour " + hour);
                }

                TestAssert.IsTrue(ReferenceEquals(apparel, pawn.outfits?.CurrentApparelPolicy), "Apparel policy must survive");
                TestAssert.IsTrue(ReferenceEquals(drugs, pawn.drugs?.CurrentPolicy), "Drug policy must survive");
                TestAssert.IsTrue(ReferenceEquals(food, pawn.foodRestriction?.CurrentFoodPolicy), "Food policy must survive");
            }
        }

        private sealed class CaravanFixture : IDisposable
        {
            public readonly TestMapScope Maps = new TestMapScope();
            private readonly HashSet<WorldObject> originals = new HashSet<WorldObject>(Find.WorldObjects.AllWorldObjects);
            private readonly List<CaravanType> caravans = new List<CaravanType>();
            private readonly HashSet<PlanetTile> tiles = new HashSet<PlanetTile>();
            private readonly PlaySettings playSettings;
            private readonly bool originalManualPriorities;

            public CaravanFixture()
            {
                playSettings = Find.PlaySettings;
                TestAssert.Available(playSettings, "Native caravan fixture play settings");
                originalManualPriorities = playSettings.useWorkPriorities;
                // Native GetPriority normalizes every enabled job to 3 in simple mode.
                // Numeric restoration tests require manual mode for both capture and observation.
                playSettings.useWorkPriorities = true;
            }

            public CaravanType Make(Pawn[] pawns)
            {
                var tile = (PlanetTile)Maps.FindUnusedSurfaceTile();
                tiles.Add(tile);
                TestAssert.IsTrue(tile.Valid && tile.LayerDef.canFormCaravans, "Fixture requires a valid caravan surface tile");
                var caravan = CaravanMaker.MakeCaravan(pawns, Faction.OfPlayer, tile, true);
                caravans.Add(caravan);
                TestAssert.IsTrue(caravan.Spawned && Find.WorldObjects.Caravans.Contains(caravan),
                    "Native maker must add the caravan to the real world");
                foreach (var pawn in pawns)
                {
                    TestAssert.IsTrue(caravan.ContainsPawn(pawn) && Find.WorldPawns.Contains(pawn) && !pawn.Spawned,
                        "Native maker must transfer each owned pawn from map to world caravan");
                }

                return caravan;
            }
            public void Dispose()
            {
                try
                {
                    var failures = new List<Exception>();
                    Action<Action> attempt = action => { try { action(); } catch (Exception ex) { failures.Add(ex); } };
                    var ownedPawns = Maps.Pawns.ToArray();
                    foreach (var caravan in Find.WorldObjects.Caravans.ToArray())
                    {
                        if (!originals.Contains(caravan) && (caravans.Contains(caravan) || tiles.Contains(caravan.Tile)))
                        {
                            attempt(() =>
                            {
                                foreach (var pawn in caravan.PawnsListForReading.ToArray())
                                {
                                    if (ownedPawns.Contains(pawn))
                                    {
                                        caravan.RemovePawn(pawn);
                                    }
                                }

                                TestAssert.AreEqual(0, caravan.PawnsListForReading.Count, "Owned caravan cleanup cannot remove unrelated members");
                                Find.WorldObjects.Remove(caravan);
                            });
                        }
                    }

                    attempt(Maps.Dispose);
                    foreach (var pawn in ownedPawns)
                    {
                        attempt(() => { if (Find.WorldPawns.Contains(pawn)) { Find.WorldPawns.RemovePawn(pawn); } });
                    }

                    if (failures.Count > 0)
                    {
                        throw new AggregateException("Owned caravan fixture cleanup failed", failures);
                    }
                }
                finally
                {
                    playSettings.useWorkPriorities = originalManualPriorities;
                }
            }
        }

        private sealed class CaravanObservation : IDisposable
        {
            private static CaravanObservation active;
            private readonly Harmony harmony = new Harmony("Eternal.Debug.NativeCaravanLifecycle");
            private readonly List<MethodBase> patched = new List<MethodBase>();
            private readonly Pawn pawn;
            private readonly CaravanType caravan;
            private readonly List<string> terminalDebtCalls = new List<string>();
            private bool terminal;
            private float capturedDebt;
            private float restoredDebtAmount;
            private int resurrections;
            private int readds;
            private readonly bool failPublicDelivery;
            private readonly bool failPreflight;
            private readonly bool hideHomesOnPreflightFailure;
            public int DeathRegistrations { get; private set; }
            public int PrimaryDeathNotifications { get; private set; }
            public int PublicDeliveryFailures { get; private set; }
            public int PreflightFailures { get; private set; }
            public CaravanObservation(Pawn pawn, CaravanType caravan, bool failPublicDelivery = false, bool failPreflight = false,
                bool hideHomesOnPreflightFailure = true)
            {
                TestAssert.IsNull(active, "Native caravan observation cannot nest");
                this.pawn = pawn;
                this.caravan = caravan;
                this.failPublicDelivery = failPublicDelivery;
                this.failPreflight = failPreflight;
                this.hideHomesOnPreflightFailure = hideHomesOnPreflightFailure;
                active = this;
                try
                {
                    Patch(AccessTools.Method(typeof(EternalCaravanDeathHandler), nameof(EternalCaravanDeathHandler.RegisterDeath),
                        new[] { typeof(Pawn), typeof(Verse.Corpse) }), nameof(OnDeathRegistration));
                    Patch(typeof(ResurrectionUtility).GetMethods(BindingFlags.Static | BindingFlags.Public)
                        .Single(method => method.Name == "TryResurrect" && method.GetParameters().Length == 2
                            && method.GetParameters()[0].ParameterType == typeof(Pawn)), nameof(BeforeResurrection));
                    Patch(AccessTools.Method(typeof(CaravanType), nameof(CaravanType.AddPawn)), nameof(OnReadd));
                    Patch(AccessTools.Method(typeof(Eternal_Hediff), nameof(Eternal_Hediff.Notify_PawnDied)), nameof(OnPrimaryDeath));
                    Patch(AccessTools.Method(typeof(WorldObject_EternalCrashSite), nameof(WorldObject_EternalCrashSite.AddCorpse)),
                        nameof(BeforePublicDelivery));
                    Patch(AccessTools.Method(typeof(SpaceCrashRescueService), nameof(SpaceCrashRescueService.CreateOrGetCrashSite),
                        new[] { typeof(PlanetTile) }), nameof(BeforeReservation));
                    var home = AccessTools.PropertyGetter(typeof(MapType), nameof(MapType.IsPlayerHome));
                    patched.Add(home);
                    harmony.Patch(home, postfix: new HarmonyMethod(typeof(CaravanObservation), nameof(HomePostfix)));
                    var debt = EternalServiceContainer.Instance.FoodDebtSystem.GetType();
                    foreach (string method in new[] { "UnregisterPawn", "RegisterPawn", "AddResurrectionDebt" })
                    {
                        Patch(AccessTools.Method(debt, method), nameof(OnDebt));
                    }
                }
                catch { Dispose(); throw; }
            }
            private void Patch(MethodBase target, string callback)
            {
                TestAssert.IsNotNull(target, "Native caravan observation boundary " + callback);
                patched.Add(target);
                harmony.Patch(target, prefix: new HarmonyMethod(typeof(CaravanObservation), callback));
            }
            private static void OnDeathRegistration(Pawn __0)
            {
                if (active != null && __0 == active.pawn)
                {
                    active.DeathRegistrations++;
                }
            }
            private static void OnPrimaryDeath(Eternal_Hediff __instance)
            {
                if (active != null && __instance.pawn == active.pawn)
                {
                    active.PrimaryDeathNotifications++;
                }
            }
            private static void BeforePublicDelivery(Verse.Corpse __0)
            {
                if (active?.failPublicDelivery == true && __0.InnerPawn == active.pawn)
                {
                    active.PublicDeliveryFailures++;
                    throw new InvalidOperationException("Owned solo-caravan public AddCorpse failure");
                }
            }
            private static bool BeforeReservation(PlanetTile __0, ref WorldObject_EternalCrashSite __result)
            {
                if (active?.failPreflight != true || __0 != active.caravan.Tile)
                {
                    return true;
                }

                active.PreflightFailures++;
                __result = null;
                return false;
            }
            private static void HomePostfix(ref bool __result)
            {
                if (active?.failPreflight == true && active.hideHomesOnPreflightFailure)
                {
                    __result = false;
                }
            }
            private static void BeforeResurrection(Pawn __0)
            {
                if (active == null || __0 != active.pawn)
                {
                    return;
                }

                active.resurrections++;
                active.capturedDebt = EternalServiceContainer.Instance.CorpseManager.GetCorpseData(__0).FoodDebt;
                active.terminal = true;
            }
            private static void OnReadd(CaravanType __instance, Pawn __0)
            {
                if (active?.terminal == true && __0 == active.pawn && __instance == active.caravan)
                {
                    active.readds++;
                }
            }
            private static void OnDebt(Pawn __0, object[] __args, MethodBase __originalMethod)
            {
                if (active?.terminal != true || __0 != active.pawn)
                {
                    return;
                }

                active.terminalDebtCalls.Add(__originalMethod.Name);
                if (__originalMethod.Name == "AddResurrectionDebt")
                {
                    active.restoredDebtAmount += (float)__args[1];
                }
            }
            public void AssertOneResurrection(int expectedReadds = 1)
            {
                TestAssert.AreEqual(1, resurrections, "Native resurrection utility must execute exactly once");
                TestAssert.AreEqual(expectedReadds, readds, "Native caravan re-add must match the surviving owner's path");
                TestAssert.IsTrue(capturedDebt > 0f, "Real injured corpse must accumulate measured resurrection debt");
                TestAssert.AreEqual("UnregisterPawn,RegisterPawn,AddResurrectionDebt", string.Join(",", terminalDebtCalls),
                    "Healed caravan resurrection must transfer its debt episode exactly once");
                TestAssert.AreEqual(capturedDebt, restoredDebtAmount, "Exact captured debt amount must transfer once");
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
