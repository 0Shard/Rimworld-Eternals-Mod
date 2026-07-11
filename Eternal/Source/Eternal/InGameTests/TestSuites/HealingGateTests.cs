// Relative Path: Eternal/Source/Eternal/InGameTests/TestSuites/HealingGateTests.cs
// Creation Date: 10-07-2026
// Last Edit: 11-07-2026
// Author: 0Shard
// Description: In-game tests for the healing eligibility gate and activation threshold.
//              canHeal decides IF a hediff heals; the random activation threshold decides
//              WHEN, for RISING staged debuffs only. Naturally-decaying debuffs (toxic
//              buildup) bypass the threshold — they can never rise to reach it (field bug).
//              Also covers minSeverity-floored hediffs (fibrous mechanites, floor 0.001)
//              that could never reach the Severity <= 0 removal check.
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
                RunTest("RisingDebuffBelowThresholdDoesNotHeal",
                    () => RisingDebuffBelowThresholdDoesNotHeal(map), ref passed, ref failed, failures);
                RunTest("RisingDebuffAboveThresholdHeals",
                    () => RisingDebuffAboveThresholdHeals(map), ref passed, ref failed, failures);
                RunTest("ThresholdLatchKeepsHealingBelowThreshold",
                    () => ThresholdLatchKeepsHealingBelowThreshold(map), ref passed, ref failed, failures);
                RunTest("NoThresholdSettingBypassesThreshold",
                    () => NoThresholdSettingBypassesThreshold(map), ref passed, ref failed, failures);
                RunTest("DebuffHealAmountMatchesImmortalsParity",
                    () => DebuffHealAmountMatchesImmortalsParity(map), ref passed, ref failed, failures);
            }
            finally
            {
                TestPawnFactory.CleanupAll();
            }

            return new TestSuiteResult(passed, failed, failures);
        }

        /// <summary>
        /// Toxic buildup at severity 0.3 with canHeal enabled must lose severity on the very
        /// first healing pass. Toxic buildup naturally DECAYS (severityPerDayNotImmune -0.08),
        /// so it can never rise to an activation threshold — HasNaturallyDecayingSeverity
        /// bypasses the gate (old field bug: a threshold rolled above 0.3 blocked it forever).
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

        /// <summary>
        /// Flu (rising Immunizable disease) at severity 0.2 with a registered threshold of 0.6
        /// must NOT heal — the activation threshold gates rising staged debuffs even when the
        /// user enabled canHeal (old bug: any setting object bypassed the threshold entirely).
        /// </summary>
        private static void RisingDebuffBelowThresholdDoesNotHeal(Verse.Map map)
        {
            var pawn = TestPawnFactory.SpawnEternalPawn(map);
            TestAssert.IsNotNull(pawn, "Pawn should be spawned");

            WithThresholdedHediff(pawn, "Flu", initialSeverity: 0.2f, threshold: 0.6f, noThreshold: false, body: hediff =>
            {
                float severityBefore = hediff.Severity;
                ProcessHealingOnce(pawn);

                var remaining = pawn.health.hediffSet.GetFirstHediffOfDef(hediff.def);
                TestAssert.IsNotNull(remaining, "Below-threshold flu must not be removed");
                TestAssert.InRange(remaining.Severity, severityBefore - 0.0001f, severityBefore + 0.0001f,
                    $"Flu below its 0.6 threshold must not heal (before: {severityBefore:F3}, after: {remaining.Severity:F3})");
            });
        }

        /// <summary>
        /// The same flu at severity 0.7 (above its 0.6 threshold) must heal on the first pass.
        /// </summary>
        private static void RisingDebuffAboveThresholdHeals(Verse.Map map)
        {
            var pawn = TestPawnFactory.SpawnEternalPawn(map);
            TestAssert.IsNotNull(pawn, "Pawn should be spawned");

            WithThresholdedHediff(pawn, "Flu", initialSeverity: 0.7f, threshold: 0.6f, noThreshold: false, body: hediff =>
            {
                float severityBefore = hediff.Severity;
                ProcessHealingOnce(pawn);

                var remaining = pawn.health.hediffSet.GetFirstHediffOfDef(hediff.def);
                bool healed = remaining == null || remaining.Severity < severityBefore;
                TestAssert.IsTrue(healed,
                    $"Flu above its 0.6 threshold must heal (before: {severityBefore:F3}, " +
                    $"after: {remaining?.Severity.ToString("F3") ?? "removed"})");
            });
        }

        /// <summary>
        /// Once the threshold latch has fired, healing must continue even after severity drops
        /// back below the threshold (otherwise healing would stall exactly at the threshold).
        /// </summary>
        private static void ThresholdLatchKeepsHealingBelowThreshold(Verse.Map map)
        {
            var pawn = TestPawnFactory.SpawnEternalPawn(map);
            TestAssert.IsNotNull(pawn, "Pawn should be spawned");

            WithThresholdedHediff(pawn, "Flu", initialSeverity: 0.7f, threshold: 0.6f, noThreshold: false, body: hediff =>
            {
                ProcessHealingOnce(pawn); // fires the latch (0.7 >= 0.6)

                hediff.Severity = 0.3f;   // drop well below the threshold
                float severityBefore = hediff.Severity;
                ProcessHealingOnce(pawn);

                var remaining = pawn.health.hediffSet.GetFirstHediffOfDef(hediff.def);
                bool healed = remaining == null || remaining.Severity < severityBefore;
                TestAssert.IsTrue(healed,
                    $"Latched flu must keep healing below the threshold (before: {severityBefore:F3}, " +
                    $"after: {remaining?.Severity.ToString("F3") ?? "removed"})");
            });
        }

        /// <summary>
        /// The "Instant Healing" (noThreshold) setting must bypass the gate: flu at severity
        /// 0.2 with a 0.6 threshold heals immediately when noThreshold is enabled.
        /// </summary>
        private static void NoThresholdSettingBypassesThreshold(Verse.Map map)
        {
            var pawn = TestPawnFactory.SpawnEternalPawn(map);
            TestAssert.IsNotNull(pawn, "Pawn should be spawned");

            WithThresholdedHediff(pawn, "Flu", initialSeverity: 0.2f, threshold: 0.6f, noThreshold: true, body: hediff =>
            {
                float severityBefore = hediff.Severity;
                ProcessHealingOnce(pawn);

                var remaining = pawn.health.hediffSet.GetFirstHediffOfDef(hediff.def);
                bool healed = remaining == null || remaining.Severity < severityBefore;
                TestAssert.IsTrue(healed,
                    $"noThreshold flu must heal below its threshold (before: {severityBefore:F3}, " +
                    $"after: {remaining?.Severity.ToString("F3") ?? "removed"})");
            });
        }

        /// <summary>
        /// One healing pass on an above-threshold disease must reduce severity by
        /// baseHealingRate x stageMult x bodySize x DEBUFF_RATE_FACTOR (0.01) — the Immortals
        /// parity rate. The old formula (maxSeverity scaling + 0.1x auto multiplier) healed
        /// ~0.1 per pass; the fixed rate is ~0.01, so the 0.03 ceiling separates them cleanly.
        /// </summary>
        private static void DebuffHealAmountMatchesImmortalsParity(Verse.Map map)
        {
            var pawn = TestPawnFactory.SpawnEternalPawn(map);
            TestAssert.IsNotNull(pawn, "Pawn should be spawned");

            WithThresholdedHediff(pawn, "Flu", initialSeverity: 0.7f, threshold: 0.6f, noThreshold: false, body: hediff =>
            {
                // Expected from live values: the seam under test is the debuff rate factor
                // replacing the old maxSeverity scaling + 0.1x auto multiplier (~10x larger).
                var setting = Eternal_Mod.GetSettings().hediffManager.GetHediffSetting(hediff.def.defName);
                var calculator = EternalServiceContainer.Instance.HediffHealingCalculator;
                float expectedPerPass = setting.GetEffectiveHealingRate()
                    * calculator.GetStageMultiplier(hediff)
                    * pawn.BodySize
                    * Healing.UnifiedHediffHealingCalculator.DEBUFF_RATE_FACTOR;

                float severityBefore = hediff.Severity;
                ProcessHealingOnce(pawn);

                var remaining = pawn.health.hediffSet.GetFirstHediffOfDef(hediff.def);
                TestAssert.IsNotNull(remaining, "One pass must not fully heal a 0.7-severity flu");
                float severityHealed = severityBefore - remaining.Severity;
                TestAssert.InRange(severityHealed, expectedPerPass * 0.75f, expectedPerPass * 1.25f,
                    $"Disease heal per pass must match the Immortals parity rate " +
                    $"(healed {severityHealed:F4}, expected {expectedPerPass:F4})");
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

        /// <summary>
        /// Adds the hediff with a DETERMINISTIC activation threshold. The AddHediff patch rolls
        /// a random threshold for eligible hediffs, so canHeal stays false during AddHediff
        /// (patch skips ineligible hediffs), the fixed threshold is registered manually, and
        /// only then is canHeal enabled. Restores setting state and removes hediff + threshold.
        /// </summary>
        private static void WithThresholdedHediff(Pawn pawn, string hediffDefName, float initialSeverity,
            float threshold, bool noThreshold, Action<Hediff> body)
        {
            var hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(hediffDefName);
            TestAssert.IsNotNull(hediffDef, $"{hediffDefName} def should exist");

            var setting = Eternal_Mod.GetSettings().hediffManager.GetHediffSetting(hediffDefName);
            TestAssert.IsNotNull(setting, $"{hediffDefName} setting should exist");
            bool originalCanHeal = setting.canHeal;
            bool originalNoThreshold = setting.noThreshold;
            setting.canHeal = false; // keep the AddHediff patch from rolling a random threshold
            setting.noThreshold = false;

            var hediff = HediffMaker.MakeHediff(hediffDef, pawn);
            hediff.Severity = initialSeverity;
            pawn.health.AddHediff(hediff);

            var tracker = EternalServiceContainer.Instance?.ThresholdTracker;
            TestAssert.IsNotNull(tracker, "ThresholdTracker should be available via the service container");
            tracker.RegisterThreshold(pawn, hediff, threshold);

            setting.canHeal = true;
            setting.noThreshold = noThreshold;

            try
            {
                body(hediff);
            }
            finally
            {
                setting.canHeal = originalCanHeal;
                setting.noThreshold = originalNoThreshold;
                tracker.RemoveThreshold(pawn, hediff);
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
