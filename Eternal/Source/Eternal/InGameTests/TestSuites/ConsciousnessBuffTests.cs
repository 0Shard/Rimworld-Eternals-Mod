// Relative Path: Eternal/Source/Eternal/InGameTests/TestSuites/ConsciousnessBuffTests.cs
// Creation Date: 13-03-2026
// Last Edit: 14-03-2026
// Author: 0Shard
// Description: In-game E2E tests for the consciousness buff toggle (v1.0.1).
//              Verifies that Eternal_Hediff.CurStage correctly applies a consciousness
//              postFactor when enabled and leaves baseline capacity unchanged when disabled.
//              All settings mutations are scoped via SettingsScope for clean restore.

#if DEBUG

using System;
using System.Collections.Generic;
using Verse;
using RimWorld;
using Eternal.InGameTests.Helpers;

namespace Eternal.InGameTests.TestSuites
{
    /// <summary>
    /// Tests the consciousness buff toggle introduced in v1.0.1.
    /// Validates that CurStage override on Eternal_Hediff applies or suppresses the
    /// consciousness postFactor in response to settings changes.
    /// </summary>
    public static class ConsciousnessBuffTests
    {
        public static TestSuiteResult RunAll(Verse.Map map)
        {
            int passed = 0;
            int failed = 0;
            var failures = new List<string>();

            try
            {
                RunTest("ConsciousnessBuffAppliesWhenEnabled",
                    () => ConsciousnessBuffAppliesWhenEnabled(map), ref passed, ref failed, failures);
                RunTest("ConsciousnessBuffDisabledNoModifier",
                    () => ConsciousnessBuffDisabledNoModifier(map), ref passed, ref failed, failures);
                RunTest("ConsciousnessMultiplierChangesLive",
                    () => ConsciousnessMultiplierChangesLive(map), ref passed, ref failed, failures);
                RunTest("ConsciousnessFloorEnforcement",
                    () => ConsciousnessFloorEnforcement(map), ref passed, ref failed, failures);
            }
            finally
            {
                TestPawnFactory.CleanupAll();
            }

            return new TestSuiteResult(passed, failed, failures);
        }

        // ─── Test Bodies ────────────────────────────────────────────────────

        private static void ConsciousnessBuffAppliesWhenEnabled(Verse.Map map)
        {
            using (new SettingsScope())
            {
                // Arrange: enable buff at 2× multiplier
                Eternal_Mod.settings.consciousnessBuffEnabled = true;
                Eternal_Mod.settings.consciousnessMultiplier  = 2.0f;

                var pawn = TestPawnFactory.SpawnEternalPawn(map);
                TestAssert.IsNotNull(pawn, "Eternal pawn should spawn");
                TestAssert.IsFalse(pawn.Dead, "Pawn should be alive");

                // Let RimWorld recalculate capacity caches (normalTickRate = 60)
                TickAdvancer.AdvanceTicks(60);

                float consciousness = pawn.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness);
                Log.Message($"[EternalTests] ConsciousnessBuffAppliesWhenEnabled: consciousness = {consciousness}");

                // With postFactor = 2.0, a healthy pawn at 1.0 base → 2.0 effective
                TestAssert.GreaterThan(consciousness, 1.0f,
                    $"Expected consciousness > 1.0 with 2x buff enabled, got {consciousness}");
            }
        }

        private static void ConsciousnessBuffDisabledNoModifier(Verse.Map map)
        {
            using (new SettingsScope())
            {
                // Arrange: buff disabled
                Eternal_Mod.settings.consciousnessBuffEnabled = false;

                var pawn = TestPawnFactory.SpawnEternalPawn(map);
                TestAssert.IsNotNull(pawn, "Eternal pawn should spawn");

                TickAdvancer.AdvanceTicks(60);

                float consciousness = pawn.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness);
                Log.Message($"[EternalTests] ConsciousnessBuffDisabledNoModifier: consciousness = {consciousness}");

                // Disabled buff → no postFactor applied → healthy pawn stays at or below 1.0
                TestAssert.LessThanOrEqual(consciousness, 1.0f,
                    $"Expected consciousness <= 1.0 with buff disabled, got {consciousness}");
            }
        }

        private static void ConsciousnessMultiplierChangesLive(Verse.Map map)
        {
            using (new SettingsScope())
            {
                // Arrange: start at 3x
                Eternal_Mod.settings.consciousnessBuffEnabled = true;
                Eternal_Mod.settings.consciousnessMultiplier  = 3.0f;

                var pawn = TestPawnFactory.SpawnEternalPawn(map);
                TestAssert.IsNotNull(pawn, "Eternal pawn should spawn");

                TickAdvancer.AdvanceTicks(60);
                float firstRead = pawn.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness);
                Log.Message($"[EternalTests] ConsciousnessMultiplierChangesLive: first read = {firstRead}");

                // Live update: drop to 1.5x
                Eternal_Mod.settings.consciousnessMultiplier = 1.5f;
                // Force RimWorld to recalculate capacity levels — changing the multiplier updates
                // CurStage's return value but PawnCapacitiesHandler caches the computed level.
                // DirtyCache() is the canonical invalidation path:
                //   HediffSet.DirtyCache() → capacities.Notify_CapacityLevelsDirty() → CapacityLevelState.Uncached
                // Do NOT call DirtyCache() from inside Eternal_Hediff.CurStage — that would cause
                // infinite recursion (DirtyCache → GetLevel → CalculateCapacityLevel → CurStage → loop).
                pawn.health.hediffSet.DirtyCache();
                TickAdvancer.AdvanceTicks(60);

                float secondRead = pawn.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness);
                Log.Message($"[EternalTests] ConsciousnessMultiplierChangesLive: second read = {secondRead}");

                // Both reads should be above 1.0 (buff active), but 3x > 1.5x
                TestAssert.GreaterThan(firstRead, 1.0f,
                    $"First read (3x) should be > 1.0, got {firstRead}");
                TestAssert.GreaterThan(secondRead, 1.0f,
                    $"Second read (1.5x) should be > 1.0, got {secondRead}");
                TestAssert.IsTrue(firstRead > secondRead,
                    $"3x ({firstRead}) should produce higher consciousness than 1.5x ({secondRead})");
            }
        }

        private static void ConsciousnessFloorEnforcement(Verse.Map map)
        {
            using (new SettingsScope())
            {
                // Arrange: multiplier below floor (floor is 1.0 in CurStage override)
                Eternal_Mod.settings.consciousnessBuffEnabled = true;
                Eternal_Mod.settings.consciousnessMultiplier  = 0.5f;  // below floor

                var pawn = TestPawnFactory.SpawnEternalPawn(map);
                TestAssert.IsNotNull(pawn, "Eternal pawn should spawn");

                TickAdvancer.AdvanceTicks(60);

                float consciousness = pawn.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness);
                Log.Message($"[EternalTests] ConsciousnessFloorEnforcement: consciousness = {consciousness}");

                // Floor clamps 0.5 → 1.0, so postFactor = 1.0 → same as baseline.
                // A healthy pawn at baseline is 1.0, so we expect >= 1.0.
                TestAssert.InRange(consciousness, 0.99f, 1.01f,
                    $"Expected consciousness ~1.0 when floor clamps 0.5x multiplier, got {consciousness}");
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
                failures.Add($"[ConsciousnessBuff] {name}: {ex.Message}");
                Log.Error($"[EternalTests] FAILED: {name} -- {ex.Message}");
            }
            catch (Exception ex)
            {
                failed++;
                failures.Add($"[ConsciousnessBuff] {name}: {ex.Message}");
                Log.Error($"[EternalTests] ERROR: {name} -- {ex}");
            }
        }
    }
}

#endif
