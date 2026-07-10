// Relative Path: Eternal/Source/Eternal/InGameTests/TestSuites/HealingGateTests.cs
// Creation Date: 10-07-2026
// Last Edit: 10-07-2026
// Author: 0Shard
// Description: In-game tests for the healing eligibility gate ("enabled = heals, always").
//              Covers the two field-reported bugs: (1) staged debuffs (toxic buildup) with
//              canHeal enabled were blocked by the random activation threshold; (2) hediffs
//              with def.minSeverity > 0 (fibrous mechanites, floor 0.001) could never reach
//              the Severity <= 0 removal check and sat at the clamp floor forever.
//              These need live Verse types (Pawn/Hediff/DefDatabase), so they run in-game.

#if DEBUG

using System;
using System.Collections.Generic;
using Verse;
using Eternal.DI;
using Eternal.InGameTests.Helpers;

namespace Eternal.InGameTests.TestSuites
{
    /// <summary>
    /// Validates that user-enabled hediffs heal immediately (threshold bypass), disabled
    /// hediffs do not heal, and minSeverity-floored hediffs are removed instead of sticking.
    /// </summary>
    public static class HealingGateTests
    {
        public static TestSuiteResult RunAll(Verse.Map map)
        {
            int passed = 0;
            int failed = 0;
            var failures = new List<string>();

            try
            {
                RunTest("UserEnabledStagedDebuffHealsImmediately",
                    () => UserEnabledStagedDebuffHealsImmediately(map), ref passed, ref failed, failures);
                RunTest("DisabledStagedDebuffDoesNotHeal",
                    () => DisabledStagedDebuffDoesNotHeal(map), ref passed, ref failed, failures);
                RunTest("MinSeverityFlooredHediffIsRemoved",
                    () => MinSeverityFlooredHediffIsRemoved(map), ref passed, ref failed, failures);
            }
            finally
            {
                TestPawnFactory.CleanupAll();
            }

            return new TestSuiteResult(passed, failed, failures);
        }

        /// <summary>
        /// Toxic buildup at severity 0.3 with canHeal enabled must lose severity on the very
        /// first healing pass — no waiting for a random activation threshold (old bug: a
        /// threshold rolled above 0.3 silently blocked healing forever).
        /// </summary>
        private static void UserEnabledStagedDebuffHealsImmediately(Verse.Map map)
        {
            var pawn = TestPawnFactory.SpawnEternalPawn(map);
            TestAssert.IsNotNull(pawn, "Pawn should be spawned");

            WithHediffAndCanHeal(pawn, "ToxicBuildup", initialSeverity: 0.3f, canHeal: true, body: hediff =>
            {
                float severityBefore = hediff.Severity;
                ProcessHealingOnce(pawn);

                var remaining = pawn.health.hediffSet.GetFirstHediffOfDef(hediff.def);
                bool healedOrRemoved = remaining == null || remaining.Severity < severityBefore;
                TestAssert.IsTrue(healedOrRemoved,
                    $"Enabled toxic buildup must heal on the first pass (before: {severityBefore:F3}, " +
                    $"after: {remaining?.Severity.ToString("F3") ?? "removed"})");
            });
        }

        /// <summary>
        /// The same debuff with canHeal disabled must NOT be touched by the healer.
        /// </summary>
        private static void DisabledStagedDebuffDoesNotHeal(Verse.Map map)
        {
            var pawn = TestPawnFactory.SpawnEternalPawn(map);
            TestAssert.IsNotNull(pawn, "Pawn should be spawned");

            WithHediffAndCanHeal(pawn, "ToxicBuildup", initialSeverity: 0.3f, canHeal: false, body: hediff =>
            {
                float severityBefore = hediff.Severity;
                ProcessHealingOnce(pawn);

                var remaining = pawn.health.hediffSet.GetFirstHediffOfDef(hediff.def);
                TestAssert.IsNotNull(remaining, "Disabled hediff must not be removed");
                TestAssert.InRange(remaining.Severity, severityBefore - 0.0001f, severityBefore + 0.0001f,
                    "Disabled hediff severity must be untouched by the Eternal healer");
            });
        }

        /// <summary>
        /// Fibrous mechanites (def.minSeverity 0.001) at low severity must be REMOVED by
        /// healing, not clamped at the floor (old bug: Severity setter clamps to minSeverity,
        /// so the "Severity &lt;= 0 → remove" check never fired).
        /// </summary>
        private static void MinSeverityFlooredHediffIsRemoved(Verse.Map map)
        {
            var pawn = TestPawnFactory.SpawnEternalPawn(map);
            TestAssert.IsNotNull(pawn, "Pawn should be spawned");

            WithHediffAndCanHeal(pawn, "FibrousMechanites", initialSeverity: 0.05f, canHeal: true, body: hediff =>
            {
                // A handful of passes is far more healing than 0.05 severity needs;
                // the old bug kept the hediff alive at 0.001 no matter how many ran.
                for (int i = 0; i < 10 && pawn.health.hediffSet.GetFirstHediffOfDef(hediff.def) != null; i++)
                {
                    ProcessHealingOnce(pawn);
                }

                TestAssert.IsNull(pawn.health.hediffSet.GetFirstHediffOfDef(hediff.def),
                    "Mechanites must be removed at the minSeverity floor, not stuck at 0.001");
            });
        }

        // ─── Helpers ─────────────────────────────────────────────────────────

        /// <summary>
        /// Adds the hediff, forces the named canHeal state, runs the body, then restores the
        /// original canHeal value and removes the hediff so suites stay order-independent.
        /// </summary>
        private static void WithHediffAndCanHeal(Pawn pawn, string hediffDefName, float initialSeverity,
            bool canHeal, Action<Hediff> body)
        {
            var hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(hediffDefName);
            TestAssert.IsNotNull(hediffDef, $"{hediffDefName} def should exist");

            var setting = Eternal_Mod.GetSettings().hediffManager.GetHediffSetting(hediffDefName);
            TestAssert.IsNotNull(setting, $"{hediffDefName} setting should exist");
            bool originalCanHeal = setting.canHeal;
            setting.canHeal = canHeal;

            var hediff = HediffMaker.MakeHediff(hediffDef, pawn);
            hediff.Severity = initialSeverity;
            pawn.health.AddHediff(hediff);

            try
            {
                body(hediff);
            }
            finally
            {
                setting.canHeal = originalCanHeal;
                var leftover = pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef);
                if (leftover != null)
                    pawn.health.RemoveHediff(leftover);
            }
        }

        private static void ProcessHealingOnce(Pawn pawn)
        {
            var hediffHealer = EternalServiceContainer.Instance?.HealingProcessor?.HediffHealer;
            TestAssert.IsNotNull(hediffHealer, "HediffHealer should be available via the service container");
            hediffHealer.ProcessHediffHealing(pawn);
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
                failures.Add($"[HealingGate] {name}: {ex.Message}");
                Log.Error($"[EternalTests] FAILED: {name} -- {ex.Message}");
            }
            catch (Exception ex)
            {
                failed++;
                failures.Add($"[HealingGate] {name}: {ex.Message}");
                Log.Error($"[EternalTests] ERROR: {name} -- {ex}");
            }
        }
    }
}

#endif
