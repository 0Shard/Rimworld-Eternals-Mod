// Author: 0Shard
// Description: Native solo-caravan death interruption, reservation cancellation and persistent-owner checks.
#if DEBUG
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Eternal.Caravan;
using Eternal.DI;
using Eternal.InGameTests.Helpers;
using Eternal.InGameTests.Performance;
using Eternal.Models;
using Eternal.World;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using CaravanType = RimWorld.Planet.Caravan;
using CorpseType = Verse.Corpse;

namespace Eternal.InGameTests.FailureScenarios
{
    public static class SoloDeathFailureTests
    {
        public static TestSuiteResult RunAll(Verse.Map map)
        {
            var result = new TestSuiteResult(0, 0, new List<string>());
            result.RunTest("SoloDeathFailures", "BothStagingAttemptsFailBeforeFinalizerRecovery", BothStagingAttemptsFail);
            result.RunTest("SoloDeathFailures", "AcceptedCorpseWithSkippedDeathNotificationIsRegistered", AcceptanceFailure);
            result.RunTest("SoloDeathFailures", "CancelledDeathRemovesNewReservation", () => UnusedReservation(Fault.Cancel, false));
            result.RunTest("SoloDeathFailures", "CancelledDeathPreservesReusedReservation", () => UnusedReservation(Fault.Cancel, true));
            result.RunTest("SoloDeathFailures", "SnapshotFailureRemovesNewReservation", () => UnusedReservation(Fault.Capture, false));
            result.RunTest("SoloDeathFailures", "SnapshotFailurePreservesReusedReservation", () => UnusedReservation(Fault.Capture, true));
            result.RunTest("SoloDeathFailures", "AllOrdinaryStagingRejectionsRetainCorpseInReservedDeepList", AllStagingRejections);
            return result;
        }

        private static void BothStagingAttemptsFail()
        {
            using (var fixture = new Fixture())
            using (var injection = new Injection(fixture, Fault.FirstTwoStages))
            {
                TestPawnFactory.KillPawn(fixture.Pawn);
                TestAssert.AreEqual(2, injection.StagingRejections, "Both delivery and direct reserved staging must actually reject");
                TestAssert.AreEqual(1, injection.PublicAcceptanceRejections, "Ordinary public AddCorpse acceptance must reject");
                TestAssert.AreEqual(1, injection.ReservedStagingRejections, "Explicit reserved staging must reject independently");
                TestAssert.AreEqual(3, injection.StagingCalls, "The Kill finalizer must retry the existing reserved owner once");
                TestAssert.AreEqual(0, injection.PrimaryNotifications, "Native Kill must skip its later notification after the staging exception");
                AssertRecovered(fixture, injection.ReservedSite);
            }
        }

        private static void AcceptanceFailure()
        {
            using (var fixture = new Fixture())
            using (var injection = new Injection(fixture, Fault.AfterAcceptance))
            {
                TestPawnFactory.KillPawn(fixture.Pawn);
                TestAssert.AreEqual(1, injection.AcceptanceFailures, "Exception must fire after the native corpse has been accepted");
                TestAssert.AreEqual(0, injection.PrimaryNotifications, "Interrupted native Kill must skip the primary notification");
                TestAssert.IsFalse(fixture.Pawn.health.hediffSet.hediffs.Contains(fixture.Wound),
                    "Injected partial-death mutation must remove the injury before fallback registration");
                AssertRecovered(fixture, injection.ReservedSite);
            }
        }

        private static void UnusedReservation(Fault fault, bool reuse)
        {
            using (var fixture = new Fixture())
            {
                var existing = reuse ? SpaceCrashRescueService.CreateOrGetCrashSite(fixture.Caravan.Tile) : null;
                var previousSites = Sites().ToArray();
                var health = fixture.Pawn.health.hediffSet;
                float debt = EternalServiceContainer.Instance.FoodDebtSystem.GetDebt(fixture.Pawn);
                using (var injection = new Injection(fixture, fault))
                {
                    fixture.Pawn.Kill(null, null);
                    TestAssert.AreEqual(1, fault == Fault.Cancel ? injection.Cancellations : injection.CaptureFailures,
                        "The selected failure must execute inside the native Kill call");
                    TestAssert.IsNotNull(injection.ReservedSite, "A reservation must be created or reused before the failure");
                    TestAssert.IsFalse(fixture.Pawn.Dead || fixture.Pawn.Destroyed, "Cancelled preflight must leave the actual pawn alive");
                    TestAssert.IsTrue(fixture.Caravan.Spawned && fixture.Caravan.ContainsPawn(fixture.Pawn),
                        "Cancelled preflight must retain native caravan membership");
                    TestAssert.IsTrue(ReferenceEquals(health, fixture.Pawn.health.hediffSet), "Cancelled preflight must retain anatomy");
                    TestAssert.AreEqual(debt, EternalServiceContainer.Instance.FoodDebtSystem.GetDebt(fixture.Pawn),
                        "Cancelled preflight must retain nutrition debt");
                    TestAssert.IsNull(EternalServiceContainer.Instance.CorpseManager.GetCorpseData(fixture.Pawn),
                        "Cancelled preflight must not register a phantom corpse");
                    TestAssert.IsFalse(Windows().Contains(fixture.Pawn), "Cancelled preflight must retire the synchronous death window");
                    TestAssert.IsTrue(previousSites.SequenceEqual(Sites()), "Only a newly owned unused reservation may be removed");
                    if (reuse)
                    {
                        TestAssert.IsTrue(ReferenceEquals(existing, injection.ReservedSite) && existing.Spawned && !existing.Destroyed,
                            "An empty reused site is still another operation's resource");
                    }
                    else
                    {
                        TestAssert.IsTrue(injection.ReservedSite.Destroyed && !Find.WorldObjects.AllWorldObjects.Contains(injection.ReservedSite),
                            "The new empty reservation must be destroyed after cancellation");
                    }
                }
                // No stale window may prevent the user's later genuine death attempt.
                TestPawnFactory.KillPawn(fixture.Pawn);
                var owner = Sites().SingleOrDefault(site => site.HasPendingCorpse(fixture.Pawn.Corpse));
                AssertRecovered(fixture, owner);
            }
        }

        private static void AllStagingRejections()
        {
            using (var fixture = new Fixture())
            using (var injection = new Injection(fixture, Fault.AllStages))
            {
                TestPawnFactory.KillPawn(fixture.Pawn);
                TestAssert.IsTrue(injection.StagingRejections >= 3,
                    "Both native-path staging attempts and finalizer ordinary staging must actually reject");
                TestAssert.AreEqual(injection.StagingCalls, injection.StagingRejections,
                    "The insertion-only fallback must work while every ordinary staging call still rejects");
                TestAssert.IsTrue(injection.PublicAcceptanceRejections > 0 && injection.ReservedStagingRejections >= 2,
                    "All-reject coverage must reject public acceptance and both explicit reserved staging attempts");
                TestAssert.AreEqual(0, injection.PrimaryNotifications,
                    "Native Kill must skip its later notification after the staging exception");
                AssertRecovered(fixture, injection.ReservedSite);
            }
        }

        private static void AssertRecovered(Fixture fixture, WorldObject_EternalCrashSite site)
        {
            var entry = EternalServiceContainer.Instance.CorpseManager.GetCorpseData(fixture.Pawn);
            TestAssert.IsTrue(fixture.Pawn.Dead, "Native death must actually occur");
            TestAssert.IsNotNull(entry, "Finalizer must establish real corpse-manager registration");
            TestAssert.IsNotNull(site, "Recovery must preserve the reserved persistent site");
            TestAssert.IsTrue(site.Spawned && !site.Destroyed && site.HasPendingCorpse(entry.Corpse),
                "The live recovery site must deep-own the exact registered corpse before caravan destruction");
            var deepThings = (IList)AccessTools.Field(typeof(WorldObject_EternalCrashSite), "crashedThings").GetValue(site);
            TestAssert.AreEqual(1, deepThings.Cast<Thing>().Count(thing => ReferenceEquals(thing, entry.Corpse)),
                "The existing Scribe-deep list must own the exact corpse once");
            TestAssert.IsFalse(entry.Corpse.Destroyed || entry.Corpse.Spawned || entry.Corpse.holdingOwner != null,
                "Deep-owned recovery cannot leave duplicate map/container ownership");
            TestAssert.IsTrue(entry.PreCalculatedHealingQueue?.Any(item => item.HediffLoadId == fixture.Wound.loadID
                && item.Severity == fixture.WoundSeverity) == true,
                "Fallback registration must preserve injury work captured before the interrupted native death");
            TestAssert.IsNotNull(entry.AssignmentSnapshot, "Fallback registration must retain pre-death assignments");
            TestAssert.IsTrue(fixture.Caravan.Destroyed && !Find.WorldObjects.AllWorldObjects.Contains(fixture.Caravan),
                "Successful finalization must finish native empty-caravan destruction");
            TestAssert.IsFalse(Windows().Contains(fixture.Pawn), "Successful finalization must retire its transient window");
        }

        private static IEnumerable<WorldObject_EternalCrashSite> Sites()
            => Find.WorldObjects.AllWorldObjects.OfType<WorldObject_EternalCrashSite>();
        private static IDictionary Windows() => (IDictionary)AccessTools.Field(typeof(EternalCaravanDeathHandler), "soloDeaths")
            .GetValue(EternalCaravanDeathHandler.Instance);

        private enum Fault { FirstTwoStages, AfterAcceptance, Cancel, Capture, AllStages }

        private sealed class Injection : IDisposable
        {
            private static Injection active;
            private readonly Harmony harmony = new Harmony("Eternal.Debug.SoloDeathFault." + Guid.NewGuid().ToString("N"));
            private readonly Fixture fixture;
            private readonly Fault fault;
            internal WorldObject_EternalCrashSite ReservedSite;
            internal int StagingCalls, StagingRejections, AcceptanceFailures, PrimaryNotifications, Cancellations, CaptureFailures;
            internal int PublicAcceptanceRejections, ReservedStagingRejections;
            internal Injection(Fixture fixture, Fault fault)
            {
                TestAssert.IsNull(active, "Solo-death fault scopes must not nest");
                this.fixture = fixture;
                this.fault = fault;
                active = this;
                try
                {
                    harmony.Patch(AccessTools.Method(typeof(SpaceCrashRescueService), nameof(SpaceCrashRescueService.CreateOrGetCrashSite),
                        new[] { typeof(PlanetTile) }), postfix: new HarmonyMethod(typeof(Injection), nameof(Reserved)));
                    harmony.Patch(AccessTools.Method(typeof(WorldObject_EternalCrashSite), "TryStageCorpseForRecovery"),
                        prefix: new HarmonyMethod(typeof(Injection), nameof(BeforeStage)));
                    harmony.Patch(AccessTools.Method(typeof(WorldObject_EternalCrashSite), nameof(WorldObject_EternalCrashSite.AddCorpse)),
                        prefix: new HarmonyMethod(typeof(Injection), nameof(BeforePublicAcceptance)));
                    harmony.Patch(AccessTools.Method(typeof(EternalCaravanDeathHandler), nameof(EternalCaravanDeathHandler.StageSoloCorpse)),
                        postfix: new HarmonyMethod(typeof(Injection), nameof(AfterAcceptance)));
                    harmony.Patch(AccessTools.Method(typeof(Pawn), nameof(Pawn.Kill), new[] { typeof(DamageInfo?), typeof(Hediff) }),
                        prefix: new HarmonyMethod(typeof(Injection), nameof(CancelKill)) { priority = Priority.Last });
                    harmony.Patch(AccessTools.Method(typeof(PawnAssignmentSnapshot), nameof(PawnAssignmentSnapshot.CaptureFrom)),
                        prefix: new HarmonyMethod(typeof(Injection), nameof(FailCapture)));
                    harmony.Patch(AccessTools.Method(typeof(Eternal_Hediff), nameof(Eternal_Hediff.Notify_PawnDied)),
                        prefix: new HarmonyMethod(typeof(Injection), nameof(PrimaryNotification)));
                }
                catch { Dispose(); throw; }
            }
            private static void Reserved(PlanetTile __0, WorldObject_EternalCrashSite __result)
            {
                if (active != null && __0 == active.fixture.Caravan.Tile)
                {
                    active.ReservedSite = __result;
                }
            }
            private static bool BeforePublicAcceptance(CorpseType __0)
            {
                if (!RejectOrdinaryStaging(__0))
                {
                    return true;
                }

                active.PublicAcceptanceRejections++;
                return false;
            }
            private static bool BeforeStage(CorpseType __0, ref bool __result)
            {
                if (!RejectOrdinaryStaging(__0))
                {
                    return true;
                }

                active.ReservedStagingRejections++;
                __result = false;
                return false;
            }
            private static bool RejectOrdinaryStaging(CorpseType corpse)
            {
                if (active == null || corpse?.InnerPawn != active.fixture.Pawn)
                {
                    return false;
                }

                var window = (EternalCaravanDeathHandler.SoloDeathRecovery)Windows()[active.fixture.Pawn];
                TestAssert.IsTrue(window.PreCalculatedQueue?.Any(item => item.HediffLoadId == active.fixture.Wound.loadID
                    && item.Severity == active.fixture.WoundSeverity) == true,
                    "The actual pre-Kill capture must include the wound before any staging fault executes");
                active.StagingCalls++;
                if (active.fault != Fault.AllStages && (active.fault != Fault.FirstTwoStages || active.StagingCalls > 2))
                {
                    return false;
                }

                active.StagingRejections++;
                return true;
            }
            private static void AfterAcceptance(Thing __1, bool __result)
            {
                if (active?.fault != Fault.AfterAcceptance || !__result
                    || __1 is not CorpseType corpse || corpse.InnerPawn != active.fixture.Pawn)
                {
                    return;
                }

                TestAssert.IsTrue(active.ReservedSite.HasPendingCorpse(corpse), "Post-acceptance fault requires an actual deep-owned corpse");
                active.fixture.Pawn.health.RemoveHediff(active.fixture.Wound);
                active.AcceptanceFailures++;
                throw new InvalidOperationException("Owned solo-death failure after corpse acceptance");
            }
            private static bool CancelKill(Pawn __instance)
            {
                if (active?.fault != Fault.Cancel || __instance != active.fixture.Pawn)
                {
                    return true;
                }

                active.Cancellations++;
                return false;
            }
            private static void FailCapture(Pawn __0)
            {
                if (active?.fault != Fault.Capture || __0 != active.fixture.Pawn)
                {
                    return;
                }

                active.CaptureFailures++;
                throw new InvalidOperationException("Owned solo-death assignment-capture failure");
            }
            private static void PrimaryNotification(Eternal_Hediff __instance)
            {
                if (active != null && __instance.pawn == active.fixture.Pawn)
                {
                    active.PrimaryNotifications++;
                }
            }
            public void Dispose() { try { harmony.UnpatchAll(harmony.Id); } finally { active = null; } }
        }

        private sealed class Fixture : IDisposable
        {
            private readonly TestMapScope maps = new TestMapScope();
            private readonly HashSet<WorldObject> originals = new HashSet<WorldObject>(Find.WorldObjects.AllWorldObjects);
            internal readonly Pawn Pawn;
            internal readonly CaravanType Caravan;
            internal readonly Hediff Wound;
            internal readonly float WoundSeverity = 2f;
            internal Fixture()
            {
                try
                {
                    var preparation = maps.Create(50);
                    Pawn = maps.SpawnEternalPawn(preparation);
                    Wound = Pawn.health.AddHediff(HediffDefOf.Cut, Pawn.RaceProps.body.corePart);
                    Wound.Severity = WoundSeverity;
                    Caravan = CaravanMaker.MakeCaravan(new[] { Pawn }, Faction.OfPlayer, maps.FindUnusedSurfaceTile(), true);
                    TestAssert.IsTrue(Caravan.Spawned && Caravan.ContainsPawn(Pawn), "Native maker must establish solo caravan ownership");
                }
                catch { Dispose(); throw; }
            }
            public void Dispose()
            {
                var failures = new List<Exception>();
                Action<Action> attempt = action => { try { action(); } catch (Exception exception) { failures.Add(exception); } };
                if (Pawn != null)
                {
                    attempt(() => Windows().Remove(Pawn));
                    foreach (var site in Sites().ToArray())
                    {
                        attempt(() =>
                        {
                            var things = (IList)AccessTools.Field(typeof(WorldObject_EternalCrashSite), "crashedThings").GetValue(site);
                            for (int i = things.Count - 1; i >= 0; i--)
                            {
                                if (things[i] is CorpseType corpse && corpse.InnerPawn == Pawn || ReferenceEquals(things[i], Pawn))
                                {
                                    things.RemoveAt(i);
                                }
                            }
                        });
                    }
                }
                if (Caravan?.Spawned == true)
                {
                    attempt(() => { if (Caravan.ContainsPawn(Pawn)) { Caravan.RemovePawn(Pawn); } Find.WorldObjects.Remove(Caravan); });
                }

                attempt(maps.Dispose);
                if (Pawn != null)
                {
                    attempt(() => { if (Find.WorldPawns.Contains(Pawn)) { Find.WorldPawns.RemovePawn(Pawn); } });
                }

                foreach (var site in Sites().ToArray())
                {
                    if (!originals.Contains(site) && !site.HasMap && !site.HasRecoveryContents)
                    {
                        attempt(() => Find.WorldObjects.Remove(site));
                    }
                }

                if (failures.Count != 0)
                {
                    throw new AggregateException("Owned solo-death fixture cleanup failed", failures);
                }
            }
        }
    }
}
#endif
