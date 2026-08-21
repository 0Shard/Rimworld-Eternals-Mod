// Relative Path: Eternal/Source/Eternal/InGameTests/TestSuites/MapProtectionTests.cs
// Creation Date: 24-02-2026
// Last Edit: 16-07-2026
// Author: 0Shard
// Description: Map protection tests. Validates that dying on the current map registers the
//              corpse for tracking. Gate 1 also verifies index equivalence, manager-owned
//              relocation, post-load hydration, and unspawned-holder rollback contracts.

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
            RunTest("CorpseMapIndexMatchesScan", () => CorpseMapIndexMatchesScan(map),
                ref passed, ref failed, failures);
            RunTest("ManagerRelocationCommitsAtomically", () => ManagerRelocationCommitsAtomically(map),
                ref passed, ref failed, failures);
            RunTest("FailedRelocationRestoresUnspawnedHolder", () => FailedRelocationRestoresUnspawnedHolder(map),
                ref passed, ref failed, failures);
            RunTest("ActiveCorpseHydrationHasExplicitOwner", ActiveCorpseHydrationHasExplicitOwner,
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

        private static void CorpseMapIndexMatchesScan(Verse.Map map)
        {
            var corpseManager = EternalServiceContainer.Instance.CorpseManager;
            var indexedPawns = new HashSet<Pawn>(corpseManager.GetCorpsesOnMap(map)
                .Where(corpseData => corpseData?.OriginalPawn != null)
                .Select(corpseData => corpseData.OriginalPawn));
            var scannedPawns = new HashSet<Pawn>(corpseManager.GetAllCorpses()
                .Where(corpseData => corpseData?.OriginalPawn != null
                    && corpseData.Corpse != null
                    && !corpseData.Corpse.Destroyed
                    && (corpseData.CurrentMap == map || corpseData.Corpse.Map == map))
                .Select(corpseData => corpseData.OriginalPawn));

            TestAssert.AreEqual(scannedPawns.Count, indexedPawns.Count,
                "Indexed and authoritative corpse scans should have equal cardinality");
            foreach (var pawn in scannedPawns)
            {
                TestAssert.IsTrue(indexedPawns.Contains(pawn),
                    "Every scanned corpse should be present in the map index");
            }
        }

        private static void ManagerRelocationCommitsAtomically(Verse.Map map)
        {
            var pawn = TestPawnFactory.SpawnEternalPawn(map);
            TestPawnFactory.KillPawn(pawn);

            var corpseManager = EternalServiceContainer.Instance.CorpseManager;
            var corpseData = corpseManager.GetCorpseData(pawn);
            TestAssert.IsNotNull(corpseData, "Relocation test corpse should be tracked");

            IntVec3 targetPosition;
            bool foundTarget = CellFinder.TryFindRandomCellNear(
                corpseData.Position,
                map,
                20,
                cell => cell.Standable(map) && cell != corpseData.Position,
                out targetPosition);
            TestAssert.IsTrue(foundTarget, "Relocation test needs a distinct standable target cell");

            TestAssert.IsTrue(corpseManager.TryRelocateCorpse(pawn, map, targetPosition),
                "Manager should commit a valid corpse relocation");
            TestAssert.AreEqual(targetPosition, corpseData.Position,
                "Entry position should change only after physical relocation succeeds");
            TestAssert.IsTrue(corpseManager.GetCorpsesOnMap(map).Any(indexedEntry =>
                    indexedEntry.OriginalPawn == pawn),
                "Relocated corpse should remain indexed on its target map");

            var committedPosition = corpseData.Position;
            TestAssert.IsFalse(corpseManager.TryRelocateCorpse(pawn, null, IntVec3.Invalid),
                "Invalid relocation target should fail without changing ownership");
            TestAssert.AreEqual(committedPosition, corpseData.Position,
                "Failed relocation must preserve the last committed entry position");
        }

        private static void FailedRelocationRestoresUnspawnedHolder(Verse.Map map)
        {
            var pawn = TestPawnFactory.SpawnEternalPawn(map);
            TestPawnFactory.KillPawn(pawn);

            var corpseManager = EternalServiceContainer.Instance.CorpseManager;
            var corpseData = corpseManager.GetCorpseData(pawn);
            var corpse = corpseData?.Corpse;
            TestAssert.IsNotNull(corpse, "Holder rollback test corpse should be tracked");

            var sourceHolder = new TestThingHolder();
            bool transferredToHolder = corpseManager.TryReleaseCorpseToUnspawnedOwner(
                pawn,
                () =>
                {
                    if (corpse.Spawned)
                    {
                        corpse.DeSpawn(DestroyMode.WillReplace);
                    }

                    return sourceHolder.GetDirectlyHeldThings().TryAdd(
                        corpse,
                        canMergeWithExistingStacks: false);
                });
            TestAssert.IsTrue(transferredToHolder,
                "Test corpse should first be transferred into the unspawned holder");

            bool failedRelocation = corpseManager.TryReleaseCorpseToUnspawnedOwner(
                pawn,
                () =>
                {
                    sourceHolder.GetDirectlyHeldThings().Remove(corpse);
                    return false;
                });

            TestAssert.IsFalse(failedRelocation,
                "A failed unspawned relocation should report failure");
            TestAssert.IsTrue(sourceHolder.GetDirectlyHeldThings().Contains(corpse),
                "The exact original ThingOwner should regain the corpse after failure");
            TestAssert.IsNull(corpseData.CurrentMap,
                "Failed relocation must not invent a map owner for an unspawned corpse");
        }

        private static void ActiveCorpseHydrationHasExplicitOwner()
        {
            var corpseProcessor = EternalServiceContainer.Instance.CorpseHealingProcessor;
            TestAssert.IsNotNull(corpseProcessor, "Corpse healing processor should be registered");

            corpseProcessor.RebuildActiveHealingCorpses();
            TestAssert.IsTrue(corpseProcessor.ActiveHealingCorpsesHydrated,
                "Active corpse ownership should be explicitly hydrated before indexed iteration");
        }

        private sealed class TestThingHolder : IThingHolder
        {
            private readonly ThingOwner<Thing> innerContainer;

            public TestThingHolder()
            {
                innerContainer = new ThingOwner<Thing>(this);
            }

            public IThingHolder ParentHolder => null;

            public ThingOwner GetDirectlyHeldThings()
            {
                return innerContainer;
            }

            public void GetChildHolders(List<IThingHolder> outChildren)
            {
            }
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
