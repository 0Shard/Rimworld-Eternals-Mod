// Relative Path: Eternal/Source/Eternal/InGameTests/TestSuites/ResurrectionSurvivalTests.cs
// Creation Date: 26-03-2026
// Last Edit: 26-03-2026
// Author: 0Shard
// Description: Resurrection survival E2E tests (RSRV-01/02/03). Verifies that lethal regrowth
//              hediffs on brain, MetabolicRecovery, and residual injuries are sanitized before
//              swap-back during resurrection. These tests inject lethal conditions into the
//              savedHediffSet and confirm the pawn survives, catching regressions if the
//              sanitization in SanitizeHediffSetBeforeRestore is ever accidentally removed.

#if DEBUG

using System.Collections.Generic;
using System;
using System.Reflection;
using HarmonyLib;
using System.Linq;
using Verse;
using Eternal.DI;
using Eternal.InGameTests.Helpers;
using RimWorld;
using Eternal.Hediffs;

namespace Eternal.InGameTests.TestSuites
{
    /// <summary>
    /// E2E tests verifying that the sanitization pass in SanitizeHediffSetBeforeRestore
    /// correctly strips lethal state before the HediffSet is swapped back onto the pawn.
    /// Covers RSRV-01 (brain regrowth survival), RSRV-02 (MetabolicRecovery stripped),
    /// RSRV-03 (full cycle produces clean pawn).
    /// </summary>
    public static class ResurrectionSurvivalTests
    {
        public static TestSuiteResult RunAll(Verse.Map map)
        {
            var result = new TestSuiteResult(0, 0, new List<string>());

            result.RunTest("ResurrectionSurvival", "BrainRegrowthDoesNotKillAfterResurrection",
                () => BrainRegrowthDoesNotKillAfterResurrection(map));

            result.RunTest("ResurrectionSurvival", "MetabolicRecoveryStrippedAfterResurrection",
                () => MetabolicRecoveryStrippedAfterResurrection(map));

            result.RunTest("ResurrectionSurvival", "FullResurrectionCycleProducesCleanPawn",
                () => FullResurrectionCycleProducesCleanPawn(map));

            return result;
        }

        /// <summary>
        /// RSRV-01: A stale EternalRegrowing_Hediff targeting the brain at severity 0 (stage 0,
        /// partEfficiencyOffset -1.0) would collapse consciousness and immediately re-kill the pawn
        /// if not removed before swap-back. Sanitization must strip it.
        /// </summary>
        private static void BrainRegrowthDoesNotKillAfterResurrection(Verse.Map map)
        {
            // 1. Spawn and kill
            var pawn = TestPawnFactory.SpawnEternalPawn(map);
            TestAssert.IsNotNull(pawn, "Pawn should be spawned");

            pawn.health.AddHediff(HediffDefOf.Cut, pawn.RaceProps.body.corePart).Severity = 1f;
            TestPawnFactory.KillPawn(pawn);
            TestAssert.IsTrue(pawn.Dead, "Pawn should be dead");

            // 2. Get registered corpse data and start healing
            var container = EternalServiceContainer.Instance;
            var corpseData = container.CorpseManager?.GetCorpseData(pawn);
            TestAssert.IsNotNull(corpseData, "Corpse should be registered with EternalCorpseManager");
            TestAssert.IsTrue(corpseData.PreCalculatedHealingQueue?.Count > 0,
                "RSRV-01: Brain sanitization fixture requires captured injury work");
            var savedSet = pawn.health.hediffSet;

            TestAssert.IsTrue(container.CorpseHealingProcessor?.StartCorpseHealing(corpseData) ?? false,
                "StartCorpseHealing must succeed before injecting stale state");
            TestAssert.IsTrue(pawn.Dead && corpseData.IsHealingActive,
                "RSRV-01: Nonempty fixture must remain in actual dead-pawn healing");

            // 3. Inject a stale brain regrowth hediff into the dead pawn's hediffset.
            //    Severity 0 puts it in stage 0 (partEfficiencyOffset -1.0), which makes the brain
            //    non-functional — if this reaches the living pawn it triggers instant re-death.
            //    Direct list add is safe here: pawn is dead so CheckForStateChange is not called.
            var brainPart = pawn.RaceProps?.body?.AllParts?
                .FirstOrDefault(p => p.def.defName == "Brain");

            TestAssert.Available(brainPart, "Brain body part for stale regrowth injection");
            TestAssert.Available(EternalDefOf.Eternal_Regrowing, "Regrowth definition");
            var regrowthHediff = HediffMaker.MakeHediff(EternalDefOf.Eternal_Regrowing, pawn, brainPart)
                as EternalRegrowing_Hediff;
            TestAssert.IsNotNull(regrowthHediff, "Regrowth def must create the actual regrowth class");
            regrowthHediff.Severity = 0.0f;
            regrowthHediff.forPart = brainPart;
            regrowthHediff.partMaxHp = 10f;
            TestAssert.IsTrue(regrowthHediff.Part == brainPart && regrowthHediff.forPart == brainPart
                && regrowthHediff.Severity == 0f && regrowthHediff.CurStage.partEfficiencyOffset <= -1f,
                "RSRV-01: Injected hediff must target the real brain at its nonfunctional initial stage");

            // Ordinary active regrowth blocks resurrection and can complete through progression.
            // Insert this stale saved-set instance at the real swap boundary instead, so only
            // sanitization can remove it before the saved anatomy returns to the living pawn.
            using (var observation = new BrainSanitizationObservation(pawn, savedSet, regrowthHediff))
            {
                int elapsed = TickAdvancer.AdvanceUntil(
                    () => !pawn.Dead,
                    maxTicks: 100000,
                    timeoutMessage: "RSRV-01: Pawn did not resurrect within 100000 ticks");
                TestAssert.IsTrue(elapsed > 0, "RSRV-01: Actual injury healing must advance before resurrection");
                TestAssert.IsTrue(observation.Injected, "RSRV-01: Real resurrection boundary must receive the stale brain instance");
                TestAssert.AreEqual(1, observation.Resurrections, "RSRV-01: Brain sanitization must avoid resurrection/death retry loops");
            }

            // 5. Pawn survived — consciousness was not collapsed by the stale brain hediff
            TestAssert.IsFalse(pawn.Dead, "RSRV-01: Pawn should be alive after resurrection");
            TestAssert.IsTrue(ReferenceEquals(savedSet, pawn.health.hediffSet),
                "RSRV-01: Real saved anatomy tracker must return after the native swap");
            TestAssert.IsFalse(pawn.health.hediffSet.hediffs.Any(hediff => ReferenceEquals(hediff, regrowthHediff)),
                "RSRV-01: Sanitization must remove the exact injected brain instance before another tick can progress it");

            // 6. Sanitization removed the stale regrowth hediff before swap-back
            bool hasStaleRegrowth = pawn.health.hediffSet.hediffs
                .OfType<EternalRegrowing_Hediff>()
                .Any();
            TestAssert.IsFalse(hasStaleRegrowth,
                "RSRV-01: No EternalRegrowing_Hediff should remain after resurrection (sanitization must strip all)");


        }

        private sealed class BrainSanitizationObservation : IDisposable
        {
            private static BrainSanitizationObservation active;
            private readonly Harmony harmony = new Harmony("Eternal.Debug.BrainSanitization");
            private readonly MethodBase utility;
            private readonly Pawn pawn;
            private readonly HediffSet savedSet;
            private readonly EternalRegrowing_Hediff stale;
            public bool Injected { get; private set; }
            public int Resurrections { get; private set; }

            public BrainSanitizationObservation(Pawn pawn, HediffSet savedSet, EternalRegrowing_Hediff stale)
            {
                TestAssert.IsNull(active, "Brain sanitization observation cannot nest");
                this.pawn = pawn;
                this.savedSet = savedSet;
                this.stale = stale;
                utility = typeof(ResurrectionUtility).GetMethods(BindingFlags.Static | BindingFlags.Public)
                    .Single(method => method.Name == "TryResurrect" && method.GetParameters().Length == 2
                        && method.GetParameters()[0].ParameterType == typeof(Pawn));
                active = this;
                try { harmony.Patch(utility, prefix: new HarmonyMethod(typeof(BrainSanitizationObservation), nameof(Inject))); }
                catch { Dispose(); throw; }
            }

            private static void Inject(Pawn __0)
            {
                var observation = active;
                if (observation == null || __0 != observation.pawn)
                {
                    return;
                }

                observation.Resurrections++;
                TestAssert.IsTrue(__0.Dead && !ReferenceEquals(__0.health.hediffSet, observation.savedSet),
                    "RSRV-01: Injection must run inside the real dead-pawn clean-tracker swap");
                TestAssert.IsFalse(observation.savedSet.hediffs.Contains(observation.stale),
                    "RSRV-01: Stale brain instance must not be removed by normal regrowth before injection");
                observation.savedSet.hediffs.Add(observation.stale);
                TestAssert.IsTrue(observation.savedSet.hediffs.Contains(observation.stale),
                    "RSRV-01: Native resurrection boundary must actually receive the stale saved brain state");
                observation.Injected = true;
            }

            public void Dispose()
            {
                try
                {
                    if (utility != null)
                    {
                        harmony.Unpatch(utility, HarmonyPatchType.All, harmony.Id);
                    }
                }
                finally { active = null; }
            }
        }

        /// <summary>
        /// RSRV-02: MetabolicRecovery is added to corpses during StartCorpseHealing for debt
        /// visibility. Sanitization removes that exact stale instance; living debt may then
        /// create a fresh display derived from the resource manager.
        /// </summary>
        private static void MetabolicRecoveryStrippedAfterResurrection(Verse.Map map)
        {
            // 1. Spawn and kill
            var pawn = TestPawnFactory.SpawnEternalPawn(map);
            TestAssert.IsNotNull(pawn, "Pawn should be spawned");

            // Keep the fixture in actual corpse healing instead of the empty-queue immediate
            // resurrection path, which previously finished before stale-state injection.
            pawn.health.AddHediff(HediffDefOf.Cut, pawn.RaceProps.body.corePart).Severity = 1f;
            TestPawnFactory.KillPawn(pawn);
            TestAssert.IsTrue(pawn.Dead, "Pawn should be dead");

            // 2. Get corpse data and start healing
            var container = EternalServiceContainer.Instance;
            var corpseData = container.CorpseManager?.GetCorpseData(pawn);
            TestAssert.IsNotNull(corpseData, "Corpse should be registered with EternalCorpseManager");

            // 3. Inject MetabolicRecovery into dead pawn's hediffset.
            //    This simulates a stale instance that could survive to the living pawn.
            //    Direct list add is safe: pawn is dead.
            TestAssert.Available(EternalDefOf.Eternal_MetabolicRecovery, "Metabolic Recovery definition");
            var injectedRecovery = HediffMaker.MakeHediff(EternalDefOf.Eternal_MetabolicRecovery, pawn);
            injectedRecovery.Severity = 0.5f;
            pawn.health.hediffSet.hediffs.Add(injectedRecovery);
            TestAssert.IsTrue(pawn.health.hediffSet.hediffs.Contains(injectedRecovery),
                "Required Metabolic Recovery instance must be present before resurrection");
            TestAssert.IsTrue(pawn.Dead && corpseData.PreCalculatedHealingQueue?.Count > 0,
                "RSRV-02: Stale display must be injected into a dead pawn with captured injury work");
            TestAssert.IsTrue(container.CorpseHealingProcessor?.StartCorpseHealing(corpseData) ?? false,
                "RSRV-02: Actual healing must start after stale-state injection");
            TestAssert.IsTrue(pawn.Dead && corpseData.IsHealingActive,
                "RSRV-02: Nonempty fixture must remain dead until real healing ticks complete");

            // 4. Advance until resurrected
            TickAdvancer.AdvanceUntil(
                () => !pawn.Dead,
                maxTicks: 100000,
                timeoutMessage: "RSRV-02: Pawn did not resurrect within 100000 ticks");

            // 5. Pawn survived
            TestAssert.IsFalse(pawn.Dead, "RSRV-02: Pawn should be alive after resurrection");

            // Check identity before another tick can clean up stale state independently.
            TestAssert.IsFalse(pawn.health.hediffSet.hediffs.Any(hediff => ReferenceEquals(hediff, injectedRecovery)),
                "RSRV-02: Sanitization must remove the exact injected corpse display before swap-back");

            // A paid healing episode legitimately creates a new living display. Observe the
            // real tick-driven synchronization and validate it against current debt/capacity.
            int syncTicks = Math.Max(Eternal_Mod.settings?.normalTickRate ?? SettingsDefaults.NormalTickRate,
                Eternal_Mod.settings?.rareTickRate ?? SettingsDefaults.RareTickRate) + 1;
            TickAdvancer.AdvanceTicks(syncTicks);
            var displays = pawn.health.hediffSet.hediffs.Where(hediff =>
                hediff.def == EternalDefOf.Eternal_MetabolicRecovery).ToArray();
            TestAssert.IsFalse(displays.Any(hediff => ReferenceEquals(hediff, injectedRecovery)),
                "RSRV-02: Derived display recreation must never reuse the injected stale instance");
            float debt = container.FoodDebtSystem.GetDebt(pawn);
            if (debt <= 0f)
            {
                TestAssert.AreEqual(0, displays.Length, "RSRV-02: Debt-free living pawn must have no recovery display");
            }
            else
            {
                TestAssert.AreEqual(1, displays.Length, "RSRV-02: Positive living debt must have exactly one fresh display");
                TestAssert.IsTrue(displays[0] is MetabolicRecovery_Hediff,
                    "RSRV-02: Fresh display must use the actual debt-driven hediff class");
                float capacity = container.FoodDebtSystem.GetMaxCapacity(pawn);
                TestAssert.IsTrue(capacity > 0f, "RSRV-02: Positive debt display requires a valid capacity");
                float expectedSeverity = Math.Max(0.01f, Math.Min(1f, debt / capacity));
                TestAssert.IsTrue(Math.Abs(displays[0].Severity - expectedSeverity) < 0.00001f,
                    "RSRV-02: Fresh display severity must follow current debt/capacity with its active clamp");
                TestAssert.IsFalse(displays[0].ShouldRemove, "RSRV-02: Fresh positive-debt display must remain active");
            }
            TestAssert.IsFalse(pawn.Dead, "RSRV-02: Pawn must survive subsequent live debt-display synchronization");


        }

        /// <summary>
        /// RSRV-03: The full resurrection cycle with no injected stale state should produce a
        /// living pawn with Eternal_Essence present and no residual regrowth or MetabolicRecovery.
        /// </summary>
        private static void FullResurrectionCycleProducesCleanPawn(Verse.Map map)
        {
            // 1. Spawn and kill
            var pawn = TestPawnFactory.SpawnEternalPawn(map);
            TestAssert.IsNotNull(pawn, "Pawn should be spawned");

            TestPawnFactory.KillPawn(pawn);
            TestAssert.IsTrue(pawn.Dead, "Pawn should be dead");

            // 2. Get corpse data and start healing — no injection, normal flow
            var container = EternalServiceContainer.Instance;
            var corpseData = container.CorpseManager?.GetCorpseData(pawn);
            TestAssert.IsNotNull(corpseData, "Corpse should be registered with EternalCorpseManager");

            bool healingStarted = container.CorpseHealingProcessor?.StartCorpseHealing(corpseData) ?? false;
            TestAssert.IsTrue(healingStarted, "RSRV-03: StartCorpseHealing should succeed");

            // 3. Advance until resurrected
            TickAdvancer.AdvanceUntil(
                () => !pawn.Dead,
                maxTicks: 100000,
                timeoutMessage: "RSRV-03: Pawn did not resurrect within 100000 ticks");

            // 4. Pawn survived
            TestAssert.IsFalse(pawn.Dead, "RSRV-03: Pawn should be alive after resurrection");

            // 5. No stale regrowth hediffs remain
            bool hasStaleRegrowth = pawn.health.hediffSet.hediffs
                .OfType<EternalRegrowing_Hediff>()
                .Any();
            TestAssert.IsFalse(hasStaleRegrowth,
                "RSRV-03: No EternalRegrowing_Hediff should be present on the living pawn");

            // 6. MetabolicRecovery was stripped
            var metRecovery = pawn.health.hediffSet.GetFirstHediffOfDef(EternalDefOf.Eternal_MetabolicRecovery);
            TestAssert.IsNull(metRecovery,
                "RSRV-03: Eternal_MetabolicRecovery must be absent from living pawn");

            // 7. Eternal_Essence survived the HediffSet swap
            var essence = pawn.health.hediffSet.GetFirstHediffOfDef(EternalDefOf.Eternal_Essence);
            TestAssert.IsNotNull(essence,
                "RSRV-03: Eternal_Essence must be present after resurrection (HediffSet swap must preserve it)");


        }


    }
}

#endif
