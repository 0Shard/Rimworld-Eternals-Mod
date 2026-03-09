// Relative Path: Eternal/Source/Eternal/InGameTests/TestSuites/MapProtectionTests.cs
// Creation Date: 24-02-2026
// Last Edit: 24-02-2026
// Author: 0Shard
// Description: Map protection tests. Validates that dying on the current map registers the
//              corpse for tracking. Full temp-map teleportation test is impractical without
//              quest generation; those paths are documented for manual testing.

#if DEBUG

using System;
using System.Collections.Generic;
using Verse;
using Eternal.DI;
using Eternal.InGameTests.Helpers;

namespace Eternal.InGameTests.TestSuites
{
    /// <summary>
    /// Tests map protection basics: corpse tracking on death, corpse presence on map.
    /// Temp-map teleportation requires quest site generation and is documented for manual test.
    /// </summary>
    public static class MapProtectionTests
    {
        public static TestSuiteResult RunAll(Verse.Map map)
        {
            int passed = 0;
            int failed = 0;
            var failures = new List<string>();

            RunTest("CorpseTrackedOnDeath", () => CorpseTrackedOnDeath(map),
                ref passed, ref failed, failures);
            RunTest("CorpseExistsOnMap", () => CorpseExistsOnMap(map),
                ref passed, ref failed, failures);

            return new TestSuiteResult(passed, failed, failures);
        }

        private static void CorpseTrackedOnDeath(Verse.Map map)
        {
            var pawn = TestPawnFactory.SpawnEternalPawn(map);
            TestPawnFactory.KillPawn(pawn);
            TestAssert.IsTrue(pawn.Dead, "Pawn should be dead");

            var container = EternalServiceContainer.Instance;
            var corpseData = container.CorpseManager?.GetCorpseData(pawn);
            TestAssert.IsNotNull(corpseData, "Dead Eternal pawn should be tracked by CorpseManager");
        }

        private static void CorpseExistsOnMap(Verse.Map map)
        {
            var pawn = TestPawnFactory.SpawnEternalPawn(map);
            TestPawnFactory.KillPawn(pawn);

            var corpse = TestPawnFactory.FindCorpseForPawn(pawn, map);
            TestAssert.IsNotNull(corpse, "Corpse should exist on the map after death");
            TestAssert.IsTrue(corpse.Spawned, "Corpse should be spawned on map");
        }

        // LIMITATION: Full temp-map protection test requires:
        // 1. Generate a temporary map (quest encounter, caravan ambush)
        // 2. Spawn Eternal pawn on temp map
        // 3. Kill pawn on temp map
        // 4. Trigger map closure event
        // 5. Verify corpse is teleported to colony map
        // This requires quest generation infrastructure beyond test scope.

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
                failures.Add($"[MapProtection] {name}: {ex.Message}");
                Log.Error($"[EternalTests] FAILED: {name} -- {ex.Message}");
            }
            catch (Exception ex)
            {
                failed++;
                failures.Add($"[MapProtection] {name}: {ex.Message}");
                Log.Error($"[EternalTests] ERROR: {name} -- {ex}");
            }
        }
    }
}

#endif
