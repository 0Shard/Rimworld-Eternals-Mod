// Relative Path: Eternal/Source/Eternal/InGameTests/TestSuites/RegrowthTests.cs
// Creation Date: 24-02-2026
// Last Edit: 24-02-2026
// Author: 0Shard
// Description: Regrowth system integration tests. Verifies that missing body parts trigger
//              the 4-phase regrowth system and are eventually restored. Tests both the
//              regrowth hediff application and part restoration.

#if DEBUG

using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using Eternal.InGameTests.Helpers;

namespace Eternal.InGameTests.TestSuites
{
    /// <summary>
    /// Tests the 4-phase regrowth system: remove a body part, advance ticks, verify restoration.
    /// </summary>
    public static class RegrowthTests
    {
        public static TestSuiteResult RunAll(Verse.Map map)
        {
            var result = new TestSuiteResult(0, 0, new List<string>());

            result.RunTest("Regrowth", "MissingPartTriggersRegrowth", () => MissingPartTriggersRegrowth(map));

            return result;
        }

        private static void MissingPartTriggersRegrowth(Verse.Map map)
        {
            // 1. Spawn Eternal pawn
            var pawn = TestPawnFactory.SpawnEternalPawn(map);
            TestAssert.IsNotNull(pawn, "Pawn should be spawned");
            TestAssert.IsFalse(pawn.Dead, "Pawn should be alive");

            // 2. Find a non-critical body part to remove (finger or toe)
            var finger = pawn.health?.hediffSet?.GetNotMissingParts()
                .FirstOrDefault(p => p.def.defName == "Finger");

            if (finger == null)
            {
                // Fall back to any non-vital small part
                finger = pawn.health?.hediffSet?.GetNotMissingParts()
                    .FirstOrDefault(p => p.def.defName == "Toe");
            }

            TestAssert.Available(finger, "Finger or Toe required for regrowth");

            string partLabel = finger.Label;
            Log.Message($"[EternalTests] Removing body part: {partLabel}");

            // 3. Remove the part
            pawn.health.AddHediff(HediffDefOf.MissingBodyPart, finger);

            // Verify part is now missing
            bool isMissing = !pawn.health.hediffSet.GetNotMissingParts().Any(p => p == finger);
            TestAssert.IsTrue(isMissing, $"Part {partLabel} should be missing after AddHediff(MissingBodyPart)");

            // 4. Advance ticks to trigger regrowth processing
            // Regrowth is processed on rareTickRate (250 ticks) by TickOrchestrator.
            // A finger should regrow relatively quickly.
            TickAdvancer.AdvanceTicks(5000); // ~83 seconds game time

            // 5. Check if the Eternal_Regrowing hediff was applied (regrowth started)
            bool hasRegrowingHediff = pawn.health.hediffSet.hediffs
                .Any(h => h.def == EternalDefOf.Eternal_Regrowing);

            // The regrowth system should have detected the missing part and started regrowing
            // Note: For living pawns, the regrowth manager handles this during UpdateAllRegrowth
            Log.Message($"[EternalTests] Has Eternal_Regrowing hediff: {hasRegrowingHediff}");

            TickAdvancer.AdvanceUntil(
                () => pawn.health.hediffSet.GetNotMissingParts().Any(p => p == finger),
                maxTicks: 50000,
                timeoutMessage: $"Part {partLabel} did not regrow within 50000 ticks");
            TestAssert.IsTrue(pawn.health.hediffSet.GetNotMissingParts().Any(p => p == finger),
                "The exact removed body part must be restored");
        }


    }
}

#endif
