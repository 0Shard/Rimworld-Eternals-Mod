// Relative Path: Eternal/Source/Eternal/InGameTests/TestSuites/MoodBuffTests.cs
// Creation Date: 13-03-2026
// Last Edit: 13-03-2026
// Author: 0Shard
// Description: In-game E2E tests for the mood buff toggle (v1.0.1).
//              Verifies that the Eternal_MoodBuff thought appears for Eternal pawns when
//              enabled, disappears when disabled, and never appears on non-Eternal pawns.
//              Uses ThoughtWorker_EternalMoodBuff via the pawn's normal needs/thoughts path.

#if DEBUG

using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using Eternal.InGameTests.Helpers;

namespace Eternal.InGameTests.TestSuites
{
    /// <summary>
    /// Tests the mood buff toggle introduced in v1.0.1.
    /// Validates that ThoughtWorker_EternalMoodBuff returns Active/Inactive correctly
    /// and that the thought appears or is suppressed in the pawn's mood thought list.
    /// </summary>
    public static class MoodBuffTests
    {
        public static TestSuiteResult RunAll(Verse.Map map)
        {
            int passed = 0;
            int failed = 0;
            var failures = new List<string>();

            try
            {
                RunTest("MoodBuffPresentWhenEnabled",
                    () => MoodBuffPresentWhenEnabled(map), ref passed, ref failed, failures);
                RunTest("MoodBuffAbsentWhenDisabled",
                    () => MoodBuffAbsentWhenDisabled(map), ref passed, ref failed, failures);
                RunTest("MoodBuffNotOnNonEternal",
                    () => MoodBuffNotOnNonEternal(map), ref passed, ref failed, failures);
            }
            finally
            {
                TestPawnFactory.CleanupAll();
            }

            return new TestSuiteResult(passed, failed, failures);
        }

        // ─── Test Bodies ────────────────────────────────────────────────────

        private static void MoodBuffPresentWhenEnabled(Verse.Map map)
        {
            using (new SettingsScope())
            {
                Eternal_Mod.settings.moodBuffEnabled = true;
                Eternal_Mod.settings.moodBuffValue   = 15;

                var pawn = TestPawnFactory.SpawnEternalPawn(map);
                TestAssert.IsNotNull(pawn, "Eternal pawn should spawn");

                // Let ThoughtWorker evaluation run (needs.mood is updated on tick)
                TickAdvancer.AdvanceTicks(120);

                var thoughts = new List<Thought>();
                pawn.needs?.mood?.thoughts?.GetAllMoodThoughts(thoughts);

                bool hasBuff = thoughts.Any(t => t.def == EternalDefOf.Eternal_MoodBuff);
                Log.Message($"[EternalTests] MoodBuffPresentWhenEnabled: hasBuff = {hasBuff}, thought count = {thoughts.Count}");

                TestAssert.IsTrue(hasBuff,
                    "Expected Eternal_MoodBuff thought to be present when moodBuffEnabled = true");
            }
        }

        private static void MoodBuffAbsentWhenDisabled(Verse.Map map)
        {
            using (new SettingsScope())
            {
                Eternal_Mod.settings.moodBuffEnabled = false;

                var pawn = TestPawnFactory.SpawnEternalPawn(map);
                TestAssert.IsNotNull(pawn, "Eternal pawn should spawn");

                TickAdvancer.AdvanceTicks(120);

                var thoughts = new List<Thought>();
                pawn.needs?.mood?.thoughts?.GetAllMoodThoughts(thoughts);

                bool hasBuff = thoughts.Any(t => t.def == EternalDefOf.Eternal_MoodBuff);
                Log.Message($"[EternalTests] MoodBuffAbsentWhenDisabled: hasBuff = {hasBuff}, thought count = {thoughts.Count}");

                TestAssert.IsFalse(hasBuff,
                    "Expected Eternal_MoodBuff thought to be absent when moodBuffEnabled = false");
            }
        }

        private static void MoodBuffNotOnNonEternal(Verse.Map map)
        {
            using (new SettingsScope())
            {
                Eternal_Mod.settings.moodBuffEnabled = true;

                // Non-Eternal pawn — should never receive the mood buff
                var pawn = TestPawnFactory.SpawnNonEternalPawn(map);
                TestAssert.IsNotNull(pawn, "Non-Eternal pawn should spawn");

                TickAdvancer.AdvanceTicks(120);

                var thoughts = new List<Thought>();
                pawn.needs?.mood?.thoughts?.GetAllMoodThoughts(thoughts);

                bool hasBuff = thoughts.Any(t => t.def == EternalDefOf.Eternal_MoodBuff);
                Log.Message($"[EternalTests] MoodBuffNotOnNonEternal: hasBuff = {hasBuff}");

                TestAssert.IsFalse(hasBuff,
                    "Non-Eternal pawn must not receive Eternal_MoodBuff thought");
            }
        }

        // ─── RunTest helper (copied from ResurrectionCycleTests — intentional, per design) ───

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
                failures.Add($"[MoodBuff] {name}: {ex.Message}");
                Log.Error($"[EternalTests] FAILED: {name} -- {ex.Message}");
            }
            catch (Exception ex)
            {
                failed++;
                failures.Add($"[MoodBuff] {name}: {ex.Message}");
                Log.Error($"[EternalTests] ERROR: {name} -- {ex}");
            }
        }
    }
}

#endif
