// Relative Path: Eternal/Source/Eternal/InGameTests/TestSuites/ResurrectionCycleTests.cs
// Creation Date: 24-02-2026
// Last Edit: 24-02-2026
// Author: 0Shard
// Description: Full resurrection cycle integration test. Spawns an Eternal pawn, kills it,
//              starts corpse healing, advances ticks until resurrection completes, and verifies
//              the pawn is alive and spawned on the map.

#if DEBUG

using System;
using System.Collections.Generic;
using Verse;
using Eternal.DI;
using Eternal.InGameTests.Helpers;

namespace Eternal.InGameTests.TestSuites
{
    /// <summary>
    /// Tests the complete resurrection cycle: spawn -> kill -> heal -> resurrect -> verify alive.
    /// </summary>
    public static class ResurrectionCycleTests
    {
        public static TestSuiteResult RunAll(Verse.Map map)
        {
            int passed = 0;
            int failed = 0;
            var failures = new List<string>();

            RunTest("FullResurrectionCycle", () => FullResurrectionCycle(map), ref passed, ref failed, failures);

            return new TestSuiteResult(passed, failed, failures);
        }

        private static void FullResurrectionCycle(Verse.Map map)
        {
            // 1. Spawn Eternal pawn
            var pawn = TestPawnFactory.SpawnEternalPawn(map);
            TestAssert.IsNotNull(pawn, "Pawn should be spawned");
            TestAssert.IsFalse(pawn.Dead, "Pawn should be alive after spawn");

            // 2. Verify Eternal trait and hediff
            TestAssert.IsNotNull(
                pawn.story?.traits?.GetTrait(EternalDefOf.Eternal_GeneticMarker),
                "Pawn should have Eternal_GeneticMarker trait");

            bool hasEssence = false;
            if (pawn.health?.hediffSet != null)
            {
                foreach (var hediff in pawn.health.hediffSet.hediffs)
                {
                    if (hediff.def == EternalDefOf.Eternal_Essence)
                    {
                        hasEssence = true;
                        break;
                    }
                }
            }
            TestAssert.IsTrue(hasEssence, "Pawn should have Eternal_Essence hediff after trait assignment");

            // 3. Kill the pawn
            TestPawnFactory.KillPawn(pawn);
            TestAssert.IsTrue(pawn.Dead, "Pawn should be dead after Kill()");

            // 4. Verify corpse is registered with the corpse manager
            var container = EternalServiceContainer.Instance;
            TestAssert.IsNotNull(container, "EternalServiceContainer should be initialized");

            var corpseData = container.CorpseManager?.GetCorpseData(pawn);
            TestAssert.IsNotNull(corpseData, "Corpse should be registered with EternalCorpseManager");

            // 5. Start corpse healing
            bool healingStarted = container.CorpseHealingProcessor?.StartCorpseHealing(corpseData) ?? false;
            TestAssert.IsTrue(healingStarted, "StartCorpseHealing should succeed");

            // 6. Advance ticks until resurrection completes
            // Resurrection happens automatically during tick processing when healing is complete.
            // We advance ticks and check if the pawn becomes alive.
            int ticksElapsed = TickAdvancer.AdvanceUntil(
                () => !pawn.Dead,
                maxTicks: 100000,
                timeoutMessage: "Pawn did not resurrect within 100000 ticks");

            Log.Message($"[EternalTests] Pawn resurrected after {ticksElapsed} ticks");

            // 7. Verify post-resurrection state
            TestAssert.IsFalse(pawn.Dead, "Pawn should be alive after resurrection");
            TestAssert.IsTrue(pawn.Spawned, "Pawn should be spawned on map after resurrection");
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
                failures.Add($"[ResurrectionCycle] {name}: {ex.Message}");
                Log.Error($"[EternalTests] FAILED: {name} -- {ex.Message}");
            }
            catch (Exception ex)
            {
                failed++;
                failures.Add($"[ResurrectionCycle] {name}: {ex.Message}");
                Log.Error($"[EternalTests] ERROR: {name} -- {ex}");
            }
        }
    }
}

#endif
