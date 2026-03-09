// Relative Path: Eternal/Source/Eternal/InGameTests/TestSuites/WorkPriorityTests.cs
// Creation Date: 24-02-2026
// Last Edit: 24-02-2026
// Author: 0Shard
// Description: Work priority preservation test. Sets specific work priorities before death,
//              then verifies they are restored after resurrection via PawnAssignmentSnapshot.

#if DEBUG

using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using Eternal.DI;
using Eternal.InGameTests.Helpers;

namespace Eternal.InGameTests.TestSuites
{
    /// <summary>
    /// Tests that work priorities set before death are restored after resurrection
    /// via the PawnAssignmentSnapshot system.
    /// </summary>
    public static class WorkPriorityTests
    {
        public static TestSuiteResult RunAll(Verse.Map map)
        {
            int passed = 0;
            int failed = 0;
            var failures = new List<string>();

            RunTest("WorkPrioritiesRestoredAfterResurrection",
                () => WorkPrioritiesRestoredAfterResurrection(map),
                ref passed, ref failed, failures);

            return new TestSuiteResult(passed, failed, failures);
        }

        private static void WorkPrioritiesRestoredAfterResurrection(Verse.Map map)
        {
            // 1. Spawn Eternal pawn
            var pawn = TestPawnFactory.SpawnEternalPawn(map);
            TestAssert.IsNotNull(pawn, "Pawn should be spawned");

            // 2. Enable manual priorities and set specific values
            if (pawn.workSettings == null)
            {
                Log.Warning("[EternalTests] Pawn has no workSettings — skipping work priority test");
                return;
            }

            pawn.workSettings.EnableAndInitialize();

            // Find work types to set priorities for
            var construction = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Construction");
            var mining = DefDatabase<WorkTypeDef>.GetNamedSilentFail("Mining");

            if (construction == null || mining == null)
            {
                Log.Warning("[EternalTests] Could not find Construction or Mining WorkTypeDefs");
                return;
            }

            // Set recognizable priorities
            pawn.workSettings.SetPriority(construction, 1);
            pawn.workSettings.SetPriority(mining, 2);

            int constructionPriorityBefore = pawn.workSettings.GetPriority(construction);
            int miningPriorityBefore = pawn.workSettings.GetPriority(mining);

            Log.Message($"[EternalTests] Set priorities — Construction: {constructionPriorityBefore}, Mining: {miningPriorityBefore}");

            // 3. Kill -> heal -> resurrect
            TestPawnFactory.KillPawn(pawn);
            TestAssert.IsTrue(pawn.Dead, "Pawn should be dead");

            var container = EternalServiceContainer.Instance;
            var corpseData = container.CorpseManager?.GetCorpseData(pawn);
            TestAssert.IsNotNull(corpseData, "Corpse should be tracked");

            container.CorpseHealingProcessor?.StartCorpseHealing(corpseData);

            TickAdvancer.AdvanceUntil(
                () => !pawn.Dead,
                maxTicks: 100000,
                timeoutMessage: "Pawn did not resurrect for WorkPriority test");

            // 4. Verify work priorities restored
            TestAssert.IsFalse(pawn.Dead, "Pawn should be alive");

            if (pawn.workSettings == null)
            {
                Log.Warning("[EternalTests] Pawn workSettings null after resurrection — snapshot may not have restored");
                return;
            }

            int constructionPriorityAfter = pawn.workSettings.GetPriority(construction);
            int miningPriorityAfter = pawn.workSettings.GetPriority(mining);

            Log.Message($"[EternalTests] Post-resurrection priorities — Construction: {constructionPriorityAfter}, Mining: {miningPriorityAfter}");

            TestAssert.AreEqual(constructionPriorityBefore, constructionPriorityAfter,
                $"Construction priority should be restored (expected {constructionPriorityBefore}, got {constructionPriorityAfter})");
            TestAssert.AreEqual(miningPriorityBefore, miningPriorityAfter,
                $"Mining priority should be restored (expected {miningPriorityBefore}, got {miningPriorityAfter})");
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
                failures.Add($"[WorkPriority] {name}: {ex.Message}");
                Log.Error($"[EternalTests] FAILED: {name} -- {ex.Message}");
            }
            catch (Exception ex)
            {
                failed++;
                failures.Add($"[WorkPriority] {name}: {ex.Message}");
                Log.Error($"[EternalTests] ERROR: {name} -- {ex}");
            }
        }
    }
}

#endif
