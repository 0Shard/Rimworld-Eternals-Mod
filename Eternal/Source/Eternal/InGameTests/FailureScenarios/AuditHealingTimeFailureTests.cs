#if DEBUG
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Eternal.Components;
using Eternal.DI;
using Eternal.Extensions;
using Eternal.Healing;
using Eternal.Healing.Scars;
using Eternal.InGameTests.Helpers;
using Eternal.InGameTests.Performance;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace Eternal.InGameTests.FailureScenarios
{
    public static class AuditHealingTimeFailureTests
    {
        public static TestSuiteResult RunAll(Verse.Map unused)
        {
            var result = new TestSuiteResult(0, 0, new List<string>());
            result.RunTest("AuditHealingTime", "NativeTickReachesInjuryCostExactLedgerAndSavedEpisode", TickToLedger);
            result.RunTest("AuditHealingTime", "RegrowthAdmissionAndScariaClassification", AdmissionAndClassification);
            result.RunTest("AuditHealingTime", "ScarOptOutAfterDiscoveryPreservesSeverity", ScarOptOut);
            result.RunTest("AuditHealingTime", "NativeCasketSuspendsDebtAndEjectionResumes", CasketDebt);
            result.RunTest("AuditHealingTime", "TravellerOnlyWorklistHealsOnce", TravellerHealing);
            result.RunTest("AuditHealingTime", "SmallNativeSeverityWritesAreProgress", SmallWrites);
            result.RunTest("AuditHealingTime", "CustomNativeCadenceAndRepeatedFramesHaveExactCounts", CustomCadence);
            return result;
        }

        private static void CustomCadence()
        {
            WithIsolatedClock((scope, clock) =>
            {
                var pawn = scope.SpawnEternalPawn(scope.Create(50));
                Eternal_Mod.settings.normalTickRate = 37;
                Eternal_Mod.settings.rareTickRate = 113;
                int start = Find.TickManager.TicksGame;
                using (var profiler = new ProductionProfiler())
                {
                    for (int tick = 0; tick < 1000; tick++)
                    {
                        Find.TickManager.DoSingleTick();
                        // Frames and duplicate native callbacks must never create extra work.
                        Eternal_Component.Current.GameComponentUpdate();
                        Eternal_Component.Current.GameComponentTick();
                    }
                    var timing = clock.GetTimingValues();
                    TestAssert.AreEqual(start + 999, timing.normalHeal, "Custom normal cadence keeps all 27 due deadlines");
                    TestAssert.AreEqual(start + 904, timing.rareHeal, "Custom rare cadence keeps all eight due deadlines");
                    var measured = profiler.Snapshot();
                    foreach (var due in new[] { (Name: "ProcessNormalTick", Count: 27L), (Name: "ProcessRareTick", Count: 8L) })
                    {
                        long calls = measured.Elements("method").Where(row => ((string)row.Attribute("name")).Contains("." + due.Name + "("))
                            .Sum(row => (long)row.Attribute("calls"));
                        TestAssert.AreEqual(due.Count, calls, "Repeated callbacks preserve actual " + due.Name + " work count");
                    }
                }
                TestAssert.IsFalse(pawn.Dead, "Owned native control survives the cadence trace");
            });
        }

        private static void AdmissionAndClassification()
        {
            WithIsolatedClock((scope, clock) =>
            {
                var pawn = scope.SpawnEternalPawn(scope.Create(50));
                var part = pawn.RaceProps.body.AllParts.First(p => p.def == BodyPartDefOf.Arm);
                var setting = new EternalHediffSetting { canHeal = true };
                foreach (var def in new[] { HediffDefOf.MissingBodyPart, EternalDefOf.Eternal_Regrowing })
                {
                    var hediff = HediffMaker.MakeHediff(def, pawn, part);
                    TestAssert.IsFalse(HediffHealingConfig.IsEligibleForHealing(hediff, setting), "Persisted opt-in cannot admit " + def.defName);
                    TestAssert.IsFalse(HediffHealingConfig.ShouldHealByDefault(hediff, setting, false), "Generic healer cannot erase " + def.defName);
                    TestAssert.IsFalse(ScarIdentifier.IsScarHediff(hediff), "Regrowth-owned work is never a scar");
                }
                var infection = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("ScariaInfection"), pawn);
                TestAssert.IsFalse(ScarIdentifier.IsScarHediff(infection), "ScariaInfection is not a permanent injury scar");
                TestAssert.IsFalse(infection.IsPermanentOrScar(), "ScariaInfection must keep staged disease rates");
                TestAssert.IsTrue(infection.IsDebuffWithStages(), "Actual native ScariaInfection remains on the debuff path");
            });
        }

        private static void ScarOptOut()
        {
            WithIsolatedClock((scope, clock) =>
            {
                var pawn = scope.SpawnEternalPawn(scope.Create(50));
                var scar = (Hediff_Injury)HediffMaker.MakeHediff(HediffDefOf.Cut, pawn,
                    pawn.RaceProps.body.AllParts.First(p => p.def == BodyPartDefOf.Arm));
                scar.Severity = 10f;
                scar.TryGetComp<HediffComp_GetsPermanent>().IsPermanent = true;
                pawn.health.AddHediff(scar);
                var setting = Eternal_Mod.settings.hediffManager.GetHediffSetting(scar.def.defName);
                bool allowed = setting.canHeal;
                try
                {
                    setting.canHeal = true;
                    var applier = new ScarHealingApplier();
                    applier.UpdateHealingRecords(pawn, ScarIdentifier.GetPawnScars(pawn));
                    var record = applier.GetHealingRecord(scar, pawn);
                    TestAssert.IsNotNull(record, "Enabled permanent scar must be discovered");
                    setting.canHeal = false;
                    float severity = scar.Severity;
                    Advance(250);
                    applier.HealScar(record);
                    TestAssert.AreEqual(severity, scar.Severity, "Disabling after discovery must stop scar mutation");
                    TestAssert.IsFalse(ScarIdentifier.GetPawnScars(pawn).Contains(scar), "Disabled scar cannot create another record");
                }
                finally { setting.canHeal = allowed; }
            });
        }

        private static void CasketDebt()
        {
            WithIsolatedClock((scope, clock) =>
            {
                var map = scope.Create(50);
                var pawn = scope.SpawnEternalPawn(map);
                var debt = EternalServiceContainer.Instance.FoodDebtSystem;
                debt.ClearDebt(pawn);
                debt.AddResurrectionDebt(pawn, 0.1f);
                pawn.needs.food.CurLevel = pawn.needs.food.MaxLevel;
                var casket = (Building_CryptosleepCasket)ThingMaker.MakeThing(DefDatabase<ThingDef>.GetNamed("CryptosleepCasket"));
                TestPawnFactory.OwnThing(casket);
                GenSpawn.Spawn(casket, map.Center, map);
                pawn.DeSpawn();
                TestAssert.IsTrue(casket.TryAcceptThing(pawn, false), "Actual native casket must own fixture");
                TestAssert.IsTrue(pawn.Suspended, "Native holder must suspend the pawn");
                TestAssert.IsFalse(pawn.HasFoodNeedDisabled(), "Suspension must stay distinct from a food waiver");
                float food = pawn.needs.food.CurLevel, beforeDebt = debt.GetDebt(pawn), peak = debt.GetPeakDebt(pawn);
                Advance(500);
                TestAssert.AreEqual(food, pawn.needs.food.CurLevel, "Casket retains native food");
                TestAssert.AreEqual(beforeDebt, debt.GetDebt(pawn), "Casket retains debt");
                TestAssert.AreEqual(peak, debt.GetPeakDebt(pawn), "Casket retains episode peak");
                casket.EjectContents();
                TestAssert.IsFalse(pawn.Suspended, "Native ejection resumes metabolism");
                Advance(250);
                TestAssert.IsTrue(debt.GetDebt(pawn) < beforeDebt, "Repayment resumes after native ejection");
            });
        }

        private static void TravellerHealing()
        {
            WithIsolatedClock((scope, clock) =>
            {
                var map = scope.Create(50);
                var pawn = scope.SpawnEternalPawn(map);
                var injury = HediffMaker.MakeHediff(HediffDefOf.Cut, pawn,
                    pawn.RaceProps.body.AllParts.First(p => p.def == BodyPartDefOf.Arm));
                injury.Severity = 10f;
                pawn.health.AddHediff(injury);
                pawn.DeSpawn();
                var caravan = CaravanMaker.MakeCaravan(new[] { pawn }, Faction.OfPlayer, map.Tile, true);
                try
                {
                    PawnExtensions.InvalidateEternalPawnCache();
                    TestAssert.AreEqual(1, PawnExtensions.GetAllLivingEternalPawnsCached().Count(p => ReferenceEquals(p, pawn)),
                        "Traveller worklist must contain exactly one identity");
                    float before = injury.Severity;
                    Advance(60);
                    TestAssert.IsTrue(injury.Severity < before, "Traveller without another map Eternal must heal");
                    var leg = pawn.RaceProps.body.AllParts.First(p => p.def == BodyPartDefOf.Leg);
                    pawn.health.AddHediff(HediffMaker.MakeHediff(HediffDefOf.MissingBodyPart, pawn, leg));
                    var scar = (Hediff_Injury)HediffMaker.MakeHediff(HediffDefOf.Cut, pawn,
                        pawn.RaceProps.body.AllParts.First(p => p.def == BodyPartDefOf.Arm));
                    scar.Severity = 10f;
                    scar.TryGetComp<HediffComp_GetsPermanent>().IsPermanent = true;
                    pawn.health.AddHediff(scar);
                    Advance(190);
                    var regrowth = EternalServiceContainer.Instance.RegrowthManager.GetRegrowingHediffs(pawn)
                        .SingleOrDefault(h => h.forPart == leg);
                    TestAssert.IsNotNull(regrowth, "The actual rare scheduler starts traveller regrowth");
                    float progress = regrowth.Severity;
                    var internalSetting = Eternal_Mod.settings.hediffManager.GetHediffSetting(EternalDefOf.Eternal_Regrowing.defName);
                    bool internalOptIn = internalSetting.canHeal;
                    try
                    {
                        internalSetting.canHeal = true; // Model a previously persisted opt-in.
                        Advance(250);
                    }
                    finally { internalSetting.canHeal = internalOptIn; }
                    TestAssert.IsTrue(pawn.health.hediffSet.hediffs.Contains(regrowth), "Generic healing must retain the actual progress-owning instance");
                    TestAssert.IsTrue(regrowth.Severity > progress, "Traveller regrowth advances through the same native worklist");
                    TestAssert.IsTrue(pawn.health.hediffSet.PartIsMissing(leg), "The scar path must not remove an unfinished traveller part");
                    TestAssert.IsTrue(scar.Severity < 10f, "The global scar pass runs with no map Eternal trigger");
                }
                finally
                {
                    caravan.RemovePawn(pawn);
                    if (Find.WorldObjects.AllWorldObjects.Contains(caravan)) { Find.WorldObjects.Remove(caravan); }
                    GenSpawn.Spawn(pawn, map.Center, map);
                }
            });
        }

        private static void SmallWrites()
        {
            WithIsolatedClock((scope, clock) =>
            {
                var pawn = scope.SpawnEternalPawn(scope.Create(50));
                var hediff = HediffMaker.MakeHediff(DefDatabase<HediffDef>.GetNamed("FibrousMechanites"), pawn);
                hediff.Severity = 0.0099f;
                pawn.health.AddHediff(hediff);
                var tracker = new EternalHediffSeverityTracker();
                for (int write = 0; write < 3; write++)
                {
                    float before = hediff.Severity;
                    hediff.Severity -= 0.00003f;
                    tracker.RecordHealingAttempt(pawn, hediff, before, hediff.Severity, 0.00003f);
                }
                TestAssert.IsFalse(tracker.IsHediffStuck(pawn, hediff), "Small actual native setter decreases remain progress above floor");
            });
        }

        private static void TickToLedger()
        {
            WithIsolatedClock((scope, clock) =>
            {
                var pawn = scope.SpawnEternalPawn(scope.Create(50));
                var debt = EternalServiceContainer.Instance.FoodDebtSystem;
                debt.ClearDebt(pawn);
                pawn.needs.food.CurLevel = pawn.needs.food.MaxLevel * Eternal_Mod.settings.foodDrainThreshold;
                var injury = HediffMaker.MakeHediff(HediffDefOf.Cut, pawn,
                    pawn.RaceProps.body.AllParts.First(p => p.def == BodyPartDefOf.Arm));
                injury.Severity = 4f;
                pawn.health.AddHediff(injury);
                float before = injury.Severity;
                Advance(60);
                TestAssert.IsTrue(injury.Severity < before,
                    "Actual DoSingleTick -> GameComponentTick must perform scheduled injury work while debug-paused");
                TestAssert.IsTrue(debt.GetDebt(pawn) > 0f, "Actual injury work at the food floor must enter the exact ledger");
                TestAssert.AreEqual(debt.GetDebt(pawn), debt.GetPeakDebt(pawn), "The first actual charge starts an episode peak");
                // Remove the stimulus only after observing its actual scheduled work/cost.
                pawn.health.RemoveHediff(injury);
                string output;
                TestAssert.IsTrue(GenCommandLine.TryGetCommandLineArg("eternal-test-output", out output), "Private output required");
                string path = Path.Combine(output, "audit-tracer-debt.xml");
                Scribe.saver.InitSaving(path, "auditDebt");
                try { ((IExposable)debt).ExposeData(); }
                finally { Scribe.saver.FinalizeSaving(); }
                TestAssert.IsTrue(File.ReadAllText(path).Contains("debtExactUnits"), "Actual Scribe must save the exact charged episode");
                pawn.needs.food.CurLevel = pawn.needs.food.MaxLevel;
                Advance(440);
                // A remainder can still be payable in the low-food precision band while
                // being smaller than the ULP at a full bar. It must NOT be forgiven there.
                if (debt.GetDebt(pawn) > 0f)
                {
                    pawn.needs.food.CurLevel = pawn.needs.food.MaxLevel * 0.2f;
                    Advance(250);
                }
                TestAssert.AreEqual(0f, debt.GetDebt(pawn), "Paid episode must close only its proven unpayable tail");
                TestAssert.AreEqual(0f, debt.GetPeakDebt(pawn), "Terminal settlement resets the episode peak");
            });
        }

        internal static void Advance(int ticks)
        {
            for (int tick = 0; tick < ticks; tick++) { Find.TickManager.DoSingleTick(); }
        }

        internal static void WithIsolatedClock(Action<TestMapScope, TickOrchestrator> body)
        {
            var settings = Eternal_Mod.settings;
            int normal = settings.normalTickRate, rare = settings.rareTickRate;
            var speed = Find.TickManager.CurTimeSpeed;
            var clock = (TickOrchestrator)AccessTools.Field(typeof(Eternal_Component), "tickOrchestrator")
                .GetValue(Eternal_Component.Current);
            var timing = clock.GetTimingValues();
            try
            {
                settings.normalTickRate = 60;
                settings.rareTickRate = 250;
                Find.TickManager.CurTimeSpeed = TimeSpeed.Paused;
                int now = Find.TickManager.TicksGame;
                clock.SetTimingValues(now, now, now, now, now, now);
                using (var scope = new TestMapScope()) { body(scope, clock); }
            }
            finally
            {
                settings.normalTickRate = normal;
                settings.rareTickRate = rare;
                clock.SetTimingValues(timing.normalHeal, timing.rareHeal, timing.traitCheck,
                    timing.corpseCheck, timing.mapCheck, timing.healingSweep);
                Find.TickManager.CurTimeSpeed = speed;
            }
        }
    }
}
#endif
