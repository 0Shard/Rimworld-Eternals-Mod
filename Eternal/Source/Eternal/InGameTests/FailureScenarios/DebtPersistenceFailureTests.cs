// Author: 0Shard
// Description: Native periodic cleanup must retain dead Eternal debt episodes and still prune invalid living owners.
#if DEBUG
using System.Collections.Generic;
using System.Linq;
using Eternal.DI;
using Eternal.InGameTests.Helpers;
using Eternal.InGameTests.Performance;
using Eternal.Resources;
using HarmonyLib;
using Verse;

namespace Eternal.InGameTests.FailureScenarios
{
    public static class DebtPersistenceFailureTests
    {
        public static TestSuiteResult RunAll(Verse.Map map)
        {
            var result = new TestSuiteResult(0, 0, new List<string>());
            result.RunTest("DebtPersistence", "PeriodicCleanupRetainsDeadResurrectionDebtAndPrunesInvalidOwner", RetainDeadDebt);
            return result;
        }

        private static void RetainDeadDebt()
        {
            using (var maps = new TestMapScope())
            {
                var map = maps.Create(50);
                var dead = maps.SpawnEternalPawn(map);
                var living = maps.SpawnEternalPawn(map);
                var invalid = maps.SpawnNonEternalPawn(map);
                TestPawnFactory.KillPawn(dead);
                TestAssert.IsTrue(dead.Dead && dead.Destroyed, "The actual native corpse pawn must be a destroyed Thing");
                TestAssert.IsNotNull(EternalServiceContainer.Instance.CorpseManager.GetCorpseData(dead),
                    "The native death hook must own the dead test pawn");
                var debt = EternalServiceContainer.Instance.FoodDebtSystem;
                TestAssert.IsTrue(debt.AddResurrectionDebt(dead, 0.25f), "Dead Eternal must accumulate actual resurrection debt");
                TestAssert.IsTrue(debt.AddResurrectionDebt(living, 0.5f), "Living control must accumulate its independent episode");
                TestAssert.IsTrue(debt.AddDebt(invalid, 0.25f), "Invalid-owner fixture must enter the debt dictionary before cleanup");
                TestAssert.AreEqual(0.25f, debt.GetDebt(invalid), "Invalid-owner fixture must carry real debt");
                var baseline = (Dictionary<Pawn, NutritionAmount>)AccessTools.Field(debt.GetType(), "_resurrectionDebtBaseline").GetValue(debt);
                var deadBaseline = baseline[dead];
                var livingBaseline = baseline[living];
                var deadCorpseData = EternalServiceContainer.Instance.CorpseManager.GetCorpseData(dead);

                debt.CleanupInvalidEntries();

                TestAssert.AreEqual(0.25f, debt.GetDebt(dead), "Periodic cleanup must retain the dead owner's exact debt");
                TestAssert.AreEqual(0.25f, debt.GetPeakDebt(dead), "Periodic cleanup must retain the dead owner's episode peak");
                TestAssert.AreEqual(NutritionAmount.FromFloat(0.25f), deadBaseline, "The dead fixture starts with an exact baseline");
                TestAssert.AreEqual(deadBaseline, baseline[dead], "Periodic cleanup must retain the uncapped resurrection baseline");
                TestAssert.AreEqual(1, debt.GetTrackedPawns().Count(pawn => ReferenceEquals(pawn, dead)),
                    "The dead owner must remain registered exactly once");
                TestAssert.AreEqual(0.25f, deadCorpseData.FoodDebt, "Unified debt and corpse display debt must remain synchronized");
                TestAssert.AreEqual(0.5f, debt.GetDebt(living), "Another living owner's debt must be unaffected");
                TestAssert.AreEqual(0.5f, debt.GetPeakDebt(living), "Another living owner's peak must be unaffected");
                TestAssert.AreEqual(NutritionAmount.FromFloat(0.5f), livingBaseline, "The living fixture starts with an exact baseline");
                TestAssert.AreEqual(livingBaseline, baseline[living], "Another living owner's baseline must be unaffected");
                TestAssert.AreEqual(0f, debt.GetDebt(invalid), "Invalid living owners must still be pruned");
                TestAssert.IsFalse(debt.GetTrackedPawns().Contains(invalid), "Cleanup must remove invalid living-owner monitoring");
            }
        }
    }
}
#endif
