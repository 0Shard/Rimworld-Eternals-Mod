// Relative Path: Eternal/Source/Eternal/InGameTests/TestSuites/WorkPriorityTests.cs
// Creation Date: 24-02-2026
// Last Edit: 14-10-2026
// Author: 0Shard
// Description: Work priority preservation test. Sets specific work priorities before death,
//              then verifies they are restored after resurrection via PawnAssignmentSnapshot.

#if DEBUG

using System.Collections.Generic;
using System.Linq;
using Verse;
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
            var result = new TestSuiteResult(0, 0, new List<string>());

            result.RunTest("WorkPriority", "WorkPrioritiesRestoredAfterResurrection",
                () => WorkPrioritiesRestoredAfterResurrection(map));

            return result;
        }

        private static void WorkPrioritiesRestoredAfterResurrection(Verse.Map map)
        {
            var playSettings = Find.PlaySettings;
            TestAssert.Available(playSettings, "Native work-mode settings");
            bool previousManualMode = playSettings.useWorkPriorities;
            try
            {
                playSettings.useWorkPriorities = true;
                CheckWorkPrioritiesRestoredAfterResurrection(map);
            }
            finally { playSettings.useWorkPriorities = previousManualMode; }
        }

        private static void CheckWorkPrioritiesRestoredAfterResurrection(Verse.Map map)
        {
            // 1. Spawn Eternal pawn
            var pawn = TestPawnFactory.SpawnEternalPawn(map);
            TestAssert.IsNotNull(pawn, "Pawn should be spawned");

            // 2. Enable manual priorities and set specific values
            TestAssert.IsNotNull(pawn.workSettings, "Work settings required before and after resurrection");

            pawn.workSettings.EnableAndInitialize();

            // Generated backstories can disable named jobs. Exercise actual supported work
            // instead of asking native SetPriority to accept an incapable pawn's assignment.
            var workTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading
                .Where(work => !pawn.WorkTypeIsDisabled(work))
                .OrderBy(work => work.defName, System.StringComparer.Ordinal)
                .Take(2).ToArray();
            TestAssert.AreEqual(2, workTypes.Length, "Fixture requires two enabled native work types");
            var firstWork = workTypes[0];
            var secondWork = workTypes[1];

            // Set recognizable priorities
            pawn.workSettings.SetPriority(firstWork, 1);
            pawn.workSettings.SetPriority(secondWork, 2);

            int firstPriorityBefore = pawn.workSettings.GetPriority(firstWork);
            int secondPriorityBefore = pawn.workSettings.GetPriority(secondWork);

            Log.Message($"[EternalTests] Set priorities — {firstWork.defName}: {firstPriorityBefore}, {secondWork.defName}: {secondPriorityBefore}");

            TestAssert.AreEqual(1, firstPriorityBefore, firstWork.defName + " setup priority");
            TestAssert.AreEqual(2, secondPriorityBefore, secondWork.defName + " setup priority");

            // 3. Kill -> heal -> resurrect
            TestPawnFactory.KillPawn(pawn);
            TestAssert.IsTrue(pawn.Dead, "Pawn should be dead");

            var container = EternalServiceContainer.Instance;
            var corpseData = container.CorpseManager?.GetCorpseData(pawn);
            TestAssert.IsNotNull(corpseData, "Corpse should be tracked");

            TestAssert.IsTrue(container.CorpseHealingProcessor?.StartCorpseHealing(corpseData) ?? false,
                "WorkPriority StartCorpseHealing must succeed");

            TickAdvancer.AdvanceUntil(
                () => !pawn.Dead,
                maxTicks: 100000,
                timeoutMessage: "Pawn did not resurrect for WorkPriority test");

            // 4. Verify work priorities restored
            TestAssert.IsFalse(pawn.Dead, "Pawn should be alive");

            TestAssert.IsNotNull(pawn.workSettings, "Work settings required before and after resurrection");

            int firstPriorityAfter = pawn.workSettings.GetPriority(firstWork);
            int secondPriorityAfter = pawn.workSettings.GetPriority(secondWork);

            Log.Message($"[EternalTests] Post-resurrection priorities — {firstWork.defName}: {firstPriorityAfter}, {secondWork.defName}: {secondPriorityAfter}");

            TestAssert.AreEqual(1, firstPriorityAfter,
                $"{firstWork.defName} priority should be restored (expected {firstPriorityBefore}, got {firstPriorityAfter})");
            TestAssert.AreEqual(2, secondPriorityAfter,
                $"{secondWork.defName} priority should be restored (expected {secondPriorityBefore}, got {secondPriorityAfter})");
        }


    }
}

#endif
