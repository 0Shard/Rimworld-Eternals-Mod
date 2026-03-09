// Relative Path: Eternal/Source/Eternal/InGameTests/TestSuites/CaravanDeathTests.cs
// Creation Date: 24-02-2026
// Last Edit: 24-02-2026
// Author: 0Shard
// Description: Caravan death detection and registration tests. Full caravan formation is
//              impractical in automated tests, so this validates the detection API and
//              non-caravan path. Full caravan death flow documented for manual testing.

#if DEBUG

using System;
using System.Collections.Generic;
using Verse;
using Eternal.Caravan;
using Eternal.InGameTests.Helpers;

namespace Eternal.InGameTests.TestSuites
{
    /// <summary>
    /// Tests caravan death detection. Full caravan flow requires manual testing;
    /// automated tests verify the detection API returns correct results for map-spawned pawns.
    /// </summary>
    public static class CaravanDeathTests
    {
        public static TestSuiteResult RunAll(Verse.Map map)
        {
            int passed = 0;
            int failed = 0;
            var failures = new List<string>();

            RunTest("MapPawnIsNotInCaravan", () => MapPawnIsNotInCaravan(map),
                ref passed, ref failed, failures);
            RunTest("DeadMapPawnIsNotInCaravan", () => DeadMapPawnIsNotInCaravan(map),
                ref passed, ref failed, failures);

            return new TestSuiteResult(passed, failed, failures);
        }

        private static void MapPawnIsNotInCaravan(Verse.Map map)
        {
            var pawn = TestPawnFactory.SpawnEternalPawn(map);
            TestAssert.IsNotNull(pawn, "Pawn should be spawned");

            bool inCaravan = EternalCaravanDeathHandler.IsPawnInCaravan(pawn);
            TestAssert.IsFalse(inCaravan, "Map-spawned pawn should NOT be detected as in caravan");
        }

        private static void DeadMapPawnIsNotInCaravan(Verse.Map map)
        {
            var pawn = TestPawnFactory.SpawnEternalPawn(map);
            TestPawnFactory.KillPawn(pawn);
            TestAssert.IsTrue(pawn.Dead, "Pawn should be dead");

            bool inCaravan = EternalCaravanDeathHandler.IsPawnInCaravan(pawn);
            TestAssert.IsFalse(inCaravan, "Dead map pawn should NOT be detected as in caravan");
        }

        // LIMITATION: Full caravan death test requires:
        // 1. Form caravan with CaravanMaker.MakeCaravan()
        // 2. Add Eternal pawn to caravan
        // 3. Kill pawn inside caravan context
        // 4. Verify corpse tracked with caravan ID
        // 5. Resurrect and verify pawn re-added to caravan
        // This is impractical in automated tests due to world tile requirements.
        // Manual test checklist documented in 07-04-SUMMARY.md.

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
                failures.Add($"[CaravanDeath] {name}: {ex.Message}");
                Log.Error($"[EternalTests] FAILED: {name} -- {ex.Message}");
            }
            catch (Exception ex)
            {
                failed++;
                failures.Add($"[CaravanDeath] {name}: {ex.Message}");
                Log.Error($"[EternalTests] ERROR: {name} -- {ex}");
            }
        }
    }
}

#endif
