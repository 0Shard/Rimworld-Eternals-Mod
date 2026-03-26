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

using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Eternal.DI;
using Eternal.InGameTests.Helpers;

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
            int passed = 0;
            int failed = 0;
            var failures = new List<string>();

            RunTest("BrainRegrowthDoesNotKillAfterResurrection",
                () => BrainRegrowthDoesNotKillAfterResurrection(map),
                ref passed, ref failed, failures);

            RunTest("MetabolicRecoveryStrippedAfterResurrection",
                () => MetabolicRecoveryStrippedAfterResurrection(map),
                ref passed, ref failed, failures);

            RunTest("FullResurrectionCycleProducesCleanPawn",
                () => FullResurrectionCycleProducesCleanPawn(map),
                ref passed, ref failed, failures);

            return new TestSuiteResult(passed, failed, failures);
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

            TestPawnFactory.KillPawn(pawn);
            TestAssert.IsTrue(pawn.Dead, "Pawn should be dead");

            // 2. Get registered corpse data and start healing
            var container = EternalServiceContainer.Instance;
            var corpseData = container.CorpseManager?.GetCorpseData(pawn);
            TestAssert.IsNotNull(corpseData, "Corpse should be registered with EternalCorpseManager");

            container.CorpseHealingProcessor?.StartCorpseHealing(corpseData);

            // 3. Inject a stale brain regrowth hediff into the dead pawn's hediffset.
            //    Severity 0 puts it in stage 0 (partEfficiencyOffset -1.0), which makes the brain
            //    non-functional — if this reaches the living pawn it triggers instant re-death.
            //    Direct list add is safe here: pawn is dead so CheckForStateChange is not called.
            var brainPart = pawn.RaceProps?.body?.AllParts?
                .FirstOrDefault(p => p.def.defName == "Brain");

            if (brainPart != null && EternalDefOf.Eternal_Regrowing != null)
            {
                var regrowthHediff = HediffMaker.MakeHediff(EternalDefOf.Eternal_Regrowing, pawn, brainPart)
                    as EternalRegrowing_Hediff;

                if (regrowthHediff != null)
                {
                    regrowthHediff.Severity = 0.0f;
                    regrowthHediff.forPart = brainPart;
                    regrowthHediff.partMaxHp = 10f;
                    pawn.health.hediffSet.hediffs.Add(regrowthHediff);
                    Log.Message("[EternalTests][RSRV-01] Injected stale brain regrowth hediff (severity 0) into dead pawn.");
                }
                else
                {
                    Log.Warning("[EternalTests][RSRV-01] Could not cast hediff to EternalRegrowing_Hediff — injection skipped, test may not cover the failure mode.");
                }
            }
            else
            {
                Log.Warning("[EternalTests][RSRV-01] Brain part or Eternal_Regrowing def not found — injection skipped.");
            }

            // 4. Advance until resurrected
            TickAdvancer.AdvanceUntil(
                () => !pawn.Dead,
                maxTicks: 100000,
                timeoutMessage: "RSRV-01: Pawn did not resurrect within 100000 ticks");

            // 5. Pawn survived — consciousness was not collapsed by the stale brain hediff
            TestAssert.IsFalse(pawn.Dead, "RSRV-01: Pawn should be alive after resurrection");

            // 6. Sanitization removed the stale regrowth hediff before swap-back
            bool hasStaleRegrowth = pawn.health.hediffSet.hediffs
                .OfType<EternalRegrowing_Hediff>()
                .Any();
            TestAssert.IsFalse(hasStaleRegrowth,
                "RSRV-01: No EternalRegrowing_Hediff should remain after resurrection (sanitization must strip all)");

            // 7. Cleanup
            if (!pawn.Dead && !pawn.Destroyed)
                pawn.Destroy();
        }

        /// <summary>
        /// RSRV-02: MetabolicRecovery is added to corpses during StartCorpseHealing for debt
        /// visibility. It must not carry through into the living pawn — sanitization strips it.
        /// </summary>
        private static void MetabolicRecoveryStrippedAfterResurrection(Verse.Map map)
        {
            // 1. Spawn and kill
            var pawn = TestPawnFactory.SpawnEternalPawn(map);
            TestAssert.IsNotNull(pawn, "Pawn should be spawned");

            TestPawnFactory.KillPawn(pawn);
            TestAssert.IsTrue(pawn.Dead, "Pawn should be dead");

            // 2. Get corpse data and start healing
            var container = EternalServiceContainer.Instance;
            var corpseData = container.CorpseManager?.GetCorpseData(pawn);
            TestAssert.IsNotNull(corpseData, "Corpse should be registered with EternalCorpseManager");

            container.CorpseHealingProcessor?.StartCorpseHealing(corpseData);

            // 3. Inject MetabolicRecovery into dead pawn's hediffset.
            //    This simulates a stale instance that could survive to the living pawn.
            //    Direct list add is safe: pawn is dead.
            if (EternalDefOf.Eternal_MetabolicRecovery != null)
            {
                var metRecovery = HediffMaker.MakeHediff(EternalDefOf.Eternal_MetabolicRecovery, pawn);
                metRecovery.Severity = 0.5f;
                pawn.health.hediffSet.hediffs.Add(metRecovery);
                Log.Message("[EternalTests][RSRV-02] Injected MetabolicRecovery hediff (severity 0.5) into dead pawn.");
            }
            else
            {
                Log.Warning("[EternalTests][RSRV-02] Eternal_MetabolicRecovery def is null — injection skipped, test may not cover the failure mode.");
            }

            // 4. Advance until resurrected
            TickAdvancer.AdvanceUntil(
                () => !pawn.Dead,
                maxTicks: 100000,
                timeoutMessage: "RSRV-02: Pawn did not resurrect within 100000 ticks");

            // 5. Pawn survived
            TestAssert.IsFalse(pawn.Dead, "RSRV-02: Pawn should be alive after resurrection");

            // 6. MetabolicRecovery was stripped before swap-back
            var remainingMetRecovery = pawn.health.hediffSet.GetFirstHediffOfDef(EternalDefOf.Eternal_MetabolicRecovery);
            TestAssert.IsNull(remainingMetRecovery,
                "RSRV-02: Eternal_MetabolicRecovery must be absent after resurrection (sanitization must strip it)");

            // 7. Cleanup
            if (!pawn.Dead && !pawn.Destroyed)
                pawn.Destroy();
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

            // 8. Cleanup
            if (!pawn.Dead && !pawn.Destroyed)
                pawn.Destroy();
        }

        private static void RunTest(string name, Action test, ref int passed, ref int failed, List<string> failures)
        {
            Log.Message($"[EternalTests] Running: {name}");
            try
            {
                test();
                passed++;
                Log.Message($"[EternalTests] PASSED: {name}");
            }
            catch (TestFailedException ex)
            {
                failed++;
                failures.Add($"[ResurrectionSurvival] {name}: {ex.Message}");
                Log.Error($"[EternalTests] FAILED: {name} -- {ex.Message}");
            }
            catch (Exception ex)
            {
                failed++;
                failures.Add($"[ResurrectionSurvival] {name}: {ex.Message}");
                Log.Error($"[EternalTests] ERROR: {name} -- {ex}");
            }
        }
    }
}

#endif
