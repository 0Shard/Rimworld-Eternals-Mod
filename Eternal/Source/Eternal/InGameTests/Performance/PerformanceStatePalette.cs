// Author: 0Shard
// Description: Verified input states; transitions use production health, debt, regrowth, and resurrection paths.
#if DEBUG
using System;
using System.Collections.Generic;
using System.Linq;
using Eternal.DI;
using Eternal.Extensions;
using Eternal.InGameTests.Helpers;
using RimWorld;
using Verse;
using MapType = Verse.Map;

namespace Eternal.InGameTests.Performance
{
    internal sealed class PerformancePawnState
    {
        internal readonly Pawn Pawn;
        internal readonly MapType Map;
        internal readonly string Kind;
        internal readonly bool ExpectDeath;
        internal bool SawDeath;
        internal bool SawResurrection;
        internal bool SecondDeathTriggered;
        internal string LastSnapshot;
        internal PerformancePawnState(Pawn pawn, MapType map, string kind, bool expectDeath)
        { Pawn = pawn; Map = map; Kind = kind; ExpectDeath = expectDeath; SawDeath = pawn.Dead; }
    }

    internal static class PerformanceStatePalette
    {
        internal static readonly string[] Kinds =
        {
            "healthy", "minor-injury", "major-injury", "scar", "disease", "debt-blocked", "food-floor", "food-waiver",
            "regrowth-phase-0", "regrowth-phase-1", "regrowth-phase-2", "regrowth-phase-3", "critical-neck",
            "corpse-waiting", "corpse-active", "dying-bleeding", "fire-exposure", "roof-death", "post-resurrection", "second-death"
        };

        internal static PerformancePawnState Prepare(Pawn pawn, MapType map, int kindIndex, PerformanceConfiguration config, Action<string> progress = null)
        {
            var kind = Kinds[kindIndex % Kinds.Length];
            var services = EternalServiceContainer.Instance;
            // Input normalization precedes all observations. Output state is never rewritten to pass a check.
            foreach (var hediff in pawn.health.hediffSet.hediffs.ToArray())
            {
                if (hediff.def != EternalDefOf.Eternal_Essence)
                {
                    pawn.health.RemoveHediff(hediff);
                }
            }

            if (pawn.needs.food != null)
            {
                pawn.needs.food.CurLevel = pawn.needs.food.MaxLevel;
            }

            var finger = Part(pawn, "Finger", "Toe");
            var torso = Part(pawn, "Torso");
            switch (kind)
            {
                case "minor-injury": Add(pawn, HediffDefOf.Cut, finger, 0.5f); break;
                case "major-injury": Add(pawn, HediffDefOf.Cut, torso, 8f); break;
                case "scar":
                    var scar = Add(pawn, HediffDefOf.Cut, torso, 3f) as HediffWithComps;
                    var permanent = scar?.TryGetComp<HediffComp_GetsPermanent>();
                    TestAssert.Available(permanent, "Injury permanent component");
                    permanent.IsPermanent = true;
                    TestAssert.IsTrue(permanent.IsPermanent, "Prepared scar must be permanent");
                    break;
                case "disease":
                    var disease = DefDatabase<HediffDef>.GetNamedSilentFail("Flu");
                    TestAssert.Available(disease, "Flu fixture definition");
                    Add(pawn, disease, null, 0.4f); break;
                case "debt-blocked":
                    Add(pawn, HediffDefOf.Cut, torso, 8f);
                    FillDebt(pawn);
                    TestAssert.IsTrue(services.FoodDebtSystem.HasExcessiveDebt(pawn), "Prepared debt must block healing");
                    break;
                case "food-floor":
                    FillDebt(pawn);
                    pawn.needs.food.CurLevel = pawn.needs.food.MaxLevel * Eternal_Mod.settings.foodDrainThreshold;
                    TestAssert.AreEqual(pawn.needs.food.MaxLevel * Eternal_Mod.settings.foodDrainThreshold,
                        pawn.needs.food.CurLevel, "Prepared food floor");
                    break;
                case "food-waiver":
                    var waiver = new HediffDef
                    {
                        defName = "Eternal_Debug_FoodWaiver",
                        label = "test food waiver",
                        hediffClass = typeof(Hediff),
                        stages = new List<HediffStage> { new HediffStage { disablesNeeds = new List<NeedDef> { NeedDefOf.Food } } }
                    };
                    Add(pawn, waiver, null, 0.5f);
                    TestAssert.IsTrue(pawn.HasFoodNeedDisabled(), "Prepared waiver must disable food via native hediff stage");
                    TestAssert.IsTrue(services.FoodDebtSystem.AddDebt(pawn, 1f), "Waived cost call must succeed");
                    TestAssert.AreEqual(0f, services.FoodDebtSystem.GetDebt(pawn), "Waived prepared cost must be zero");
                    Add(pawn, HediffDefOf.Cut, torso, 3f); break;
                case "regrowth-phase-0":
                case "regrowth-phase-1":
                case "regrowth-phase-2":
                case "regrowth-phase-3":
                    pawn.health.AddHediff(HediffDefOf.MissingBodyPart, finger);
                    services.RegrowthManager.StartRegrowth(pawn);
                    var regrowth = services.RegrowthManager.GetRegrowingHediffs(pawn).FirstOrDefault(h => h.forPart == finger);
                    TestAssert.IsNotNull(regrowth, "Actual manager must start fixture regrowth");
                    var phase = kind[kind.Length - 1] - '0';
                    regrowth.Severity = 0.05f + phase * 0.25f;
                    TestAssert.IsTrue(pawn.health.hediffSet.PartIsMissing(finger), "Prepared regrowth must retain missing part");
                    TestAssert.AreEqual(0.05f + phase * 0.25f, regrowth.Severity, "Prepared regrowth phase");
                    break;
                case "critical-neck":
                    progress?.Invoke("death-before");
                    pawn.health.AddHediff(HediffDefOf.MissingBodyPart, Part(pawn, "Neck"));
                    TestAssert.IsTrue(pawn.Dead, "Removing neck must execute real health death");
                    progress?.Invoke("death-after");
                    progress?.Invoke("healing-request-before");
                    StartHealing(pawn);
                    progress?.Invoke("healing-request-after");
                    break;
                case "corpse-waiting":
                    progress?.Invoke("death-before");
                    TestPawnFactory.KillPawn(pawn);
                    progress?.Invoke("death-after");
                    break;
                case "corpse-active":
                    Add(pawn, HediffDefOf.Cut, torso, 15f);
                    progress?.Invoke("death-before");
                    TestPawnFactory.KillPawn(pawn);
                    progress?.Invoke("death-after");
                    progress?.Invoke("healing-request-before");
                    StartHealing(pawn);
                    progress?.Invoke("healing-request-after");
                    break;
                case "dying-bleeding":
                    Add(pawn, HediffDefOf.Cut, torso, 15f);
                    Add(pawn, HediffDefOf.BloodLoss, null, 0.99f);
                    FillDebt(pawn);
                    // Food floor prevents repayment opening healing capacity before the health tick bleeds.
                    pawn.needs.food.CurLevel = pawn.needs.food.MaxLevel * Eternal_Mod.settings.foodDrainThreshold;
                    TestAssert.IsFalse(pawn.Dead, "Bleeding fixture must start alive");
                    TestAssert.IsTrue(pawn.health.hediffSet.BleedRateTotal >= 0.1f, "Bleeding fixture must really bleed");
                    break;
                case "fire-exposure":
                    FillDebt(pawn);
                    pawn.needs.food.CurLevel = pawn.needs.food.MaxLevel * Eternal_Mod.settings.foodDrainThreshold;
                    pawn.TryAttachFire(1.75f, null);
                    TestAssert.IsTrue(pawn.IsBurning(), "Fire fixture must have a real attached fire");
                    break;
                case "roof-death":
                    map.roofGrid.SetRoof(pawn.Position, RoofDefOf.RoofRockThick);
                    progress?.Invoke("death-before");
                    RoofCollapserImmediate.DropRoofInCells(pawn.Position, map);
                    progress?.Invoke("death-after");
                    TestAssert.IsTrue(pawn.Dead, "Native thick roof collapse must kill fixture pawn");
                    TestAssert.IsNotNull(services.CorpseManager.GetCorpseData(pawn), "Roof death must retain a tracked corpse");
                    break;
                case "post-resurrection":
                case "second-death":
                    progress?.Invoke("death-before");
                    TestPawnFactory.KillPawn(pawn);
                    progress?.Invoke("death-after");
                    progress?.Invoke("healing-request-before");
                    StartHealing(pawn);
                    progress?.Invoke("healing-request-after");
                    progress?.Invoke("resurrection-wait-before");
                    TickAdvancer.AdvanceUntil(() => !pawn.Dead, config.ResurrectionValidationTicks, "Preparation resurrection did not complete");
                    progress?.Invoke("resurrection-wait-after");
                    TestAssert.IsTrue(pawn.Spawned && pawn.Map == map, "Resurrection input must actually return to its own map");
                    break;
            }
            if (pawn.Dead)
            {
                TestAssert.IsNotNull(services.CorpseManager.GetCorpseData(pawn), "Prepared death must be registered by actual hook");
            }

            return new PerformancePawnState(pawn, map, kind, kind == "dying-bleeding")
            { SawResurrection = kind == "post-resurrection" || kind == "second-death" };
        }

        internal static void StartHealing(Pawn pawn)
        {
            var services = EternalServiceContainer.Instance;
            var data = services.CorpseManager.GetCorpseData(pawn);
            TestAssert.IsNotNull(data, "Actual death hook must register corpse before healing");
            TestAssert.IsTrue(services.CorpseHealingProcessor.StartCorpseHealing(data), "Actual corpse healing must start");
        }
        private static void FillDebt(Pawn pawn)
        {
            var debt = EternalServiceContainer.Instance.FoodDebtSystem;
            debt.RegisterPawn(pawn);
            TestAssert.IsTrue(debt.AddDebt(pawn, debt.GetMaxCapacity(pawn)), "Prepared debt must be accepted");
            TestAssert.IsTrue(debt.GetDebt(pawn) > 0f, "Prepared debt must be nonzero");
        }
        private static BodyPartRecord Part(Pawn pawn, params string[] names)
        {
            var part = pawn.RaceProps.body.AllParts.FirstOrDefault(p => names.Contains(p.def.defName));
            TestAssert.Available(part, "Body part " + string.Join("/", names));
            return part;
        }
        private static Hediff Add(Pawn pawn, HediffDef definition, BodyPartRecord part, float severity)
        {
            var hediff = HediffMaker.MakeHediff(definition, pawn, part);
            hediff.Severity = severity;
            pawn.health.AddHediff(hediff);
            TestAssert.IsTrue(pawn.health.hediffSet.hediffs.Contains(hediff), "Input hediff must be accepted by actual health tracker");
            return hediff;
        }
    }
}
#endif
