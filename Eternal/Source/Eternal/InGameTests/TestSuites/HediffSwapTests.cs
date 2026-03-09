// Relative Path: Eternal/Source/Eternal/InGameTests/TestSuites/HediffSwapTests.cs
// Creation Date: 24-02-2026
// Last Edit: 24-02-2026
// Author: 0Shard
// Description: HediffSet swap round-trip test (TEST-04). Verifies that Eternal_Essence hediff
//              survives the ResurrectionUtility.TryResurrect() call via the Immortals pattern
//              (save HediffSet before, restore after).

#if DEBUG

using System;
using System.Collections.Generic;
using Verse;
using Eternal.DI;
using Eternal.InGameTests.Helpers;

namespace Eternal.InGameTests.TestSuites
{
    /// <summary>
    /// Validates that the HediffSet swap pattern preserves Eternal_Essence through resurrection.
    /// This is the core TEST-04 requirement: the Immortals pattern must not lose custom hediffs.
    /// </summary>
    public static class HediffSwapTests
    {
        public static TestSuiteResult RunAll(Verse.Map map)
        {
            int passed = 0;
            int failed = 0;
            var failures = new List<string>();

            RunTest("EternalEssenceSurvivesResurrection", () => EternalEssenceSurvivesResurrection(map),
                ref passed, ref failed, failures);

            return new TestSuiteResult(passed, failed, failures);
        }

        private static void EternalEssenceSurvivesResurrection(Verse.Map map)
        {
            // 1. Spawn and verify initial state
            var pawn = TestPawnFactory.SpawnEternalPawn(map);
            TestAssert.IsNotNull(pawn, "Pawn should be spawned");
            TestAssert.IsTrue(HasEternalEssence(pawn), "Pawn should have Eternal_Essence before death");

            // Record the hediff count for comparison
            int hediffCountBeforeDeath = pawn.health?.hediffSet?.hediffs?.Count ?? 0;

            // 2. Kill -> register -> heal -> resurrect
            TestPawnFactory.KillPawn(pawn);
            TestAssert.IsTrue(pawn.Dead, "Pawn should be dead");

            var container = EternalServiceContainer.Instance;
            var corpseData = container.CorpseManager?.GetCorpseData(pawn);
            TestAssert.IsNotNull(corpseData, "Corpse should be registered");

            container.CorpseHealingProcessor?.StartCorpseHealing(corpseData);

            // Advance until alive
            TickAdvancer.AdvanceUntil(
                () => !pawn.Dead,
                maxTicks: 100000,
                timeoutMessage: "Pawn did not resurrect for HediffSwap test");

            // 3. THE KEY ASSERTION: Eternal_Essence must still be present
            TestAssert.IsFalse(pawn.Dead, "Pawn should be alive");
            TestAssert.IsTrue(HasEternalEssence(pawn),
                "Eternal_Essence MUST survive resurrection (HediffSet swap pattern)");

            // 4. Verify the pawn also has its trait still
            TestAssert.IsNotNull(
                pawn.story?.traits?.GetTrait(EternalDefOf.Eternal_GeneticMarker),
                "Eternal_GeneticMarker trait should survive resurrection");
        }

        private static bool HasEternalEssence(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null)
                return false;

            foreach (var hediff in pawn.health.hediffSet.hediffs)
            {
                if (hediff.def == EternalDefOf.Eternal_Essence)
                    return true;
            }
            return false;
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
                failures.Add($"[HediffSwap] {name}: {ex.Message}");
                Log.Error($"[EternalTests] FAILED: {name} -- {ex.Message}");
            }
            catch (Exception ex)
            {
                failed++;
                failures.Add($"[HediffSwap] {name}: {ex.Message}");
                Log.Error($"[EternalTests] ERROR: {name} -- {ex}");
            }
        }
    }
}

#endif
