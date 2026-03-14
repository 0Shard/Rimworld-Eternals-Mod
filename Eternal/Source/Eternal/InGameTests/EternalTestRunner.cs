// Relative Path: Eternal/Source/Eternal/InGameTests/EternalTestRunner.cs
// Creation Date: 24-02-2026
// Last Edit: 13-03-2026
// Author: 0Shard
// Description: Self-contained in-game test runner for Eternal mod integration tests.
//              Invoked via dev-mode DebugAction buttons. Discovers and runs all test suites,
//              tracks pass/fail counts, and posts a summary letter to the player.
//              13-03: Added per-suite DebugAction buttons, Stopwatch timing, named-suite
//              failure grouping, and 3 new v1.0.1 suites (consciousness, mood, elixir/cap).
//              Gated behind #if DEBUG to exclude from Release builds.

#if DEBUG

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Verse;
using RimWorld;
using LudeonTK;
using Eternal.InGameTests.Helpers;
using Eternal.InGameTests.TestSuites;

namespace Eternal.InGameTests
{
    /// <summary>
    /// Orchestrates in-game integration test execution. Accessible via dev-mode debug actions.
    /// Each test suite returns (passed, failed, failures, elapsedMs) and the runner aggregates
    /// results into a per-suite summary letter with timing info.
    /// </summary>
    public static class EternalTestRunner
    {
        // ─── Run All ──────────────────────────────────────────────────────────

        /// <summary>
        /// Dev-mode button entry point. Runs all 9 registered test suites sequentially.
        /// </summary>
        [DebugAction("Eternal", "Tests: Run All", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void RunAllTests()
        {
            var map = Find.CurrentMap;
            if (map == null)
            {
                Log.Error("[EternalTests] No current map found. Load a save first.");
                return;
            }

            Log.Message("[EternalTests] ========================================");
            Log.Message("[EternalTests] Starting Eternal In-Game Test Suite");
            Log.Message("[EternalTests] ========================================");

            var suiteResults = new List<(string Name, TestSuiteResult Result)>();

            RunSuite("ResurrectionCycle", () => ResurrectionCycleTests.RunAll(map), suiteResults);
            RunSuite("HediffSwap",        () => HediffSwapTests.RunAll(map),        suiteResults);
            RunSuite("CaravanDeath",       () => CaravanDeathTests.RunAll(map),      suiteResults);
            RunSuite("MapProtection",      () => MapProtectionTests.RunAll(map),     suiteResults);
            RunSuite("Regrowth",           () => RegrowthTests.RunAll(map),          suiteResults);
            RunSuite("WorkPriority",       () => WorkPriorityTests.RunAll(map),      suiteResults);
            RunSuite("ConsciousnessBuff",  () => ConsciousnessBuffTests.RunAll(map), suiteResults);
            RunSuite("MoodBuff",           () => MoodBuffTests.RunAll(map),          suiteResults);
            RunSuite("ElixirPopCap",       () => ElixirPopulationCapTests.RunAll(map), suiteResults);

            int totalPassed = suiteResults.Sum(r => r.Result.Passed);
            int totalFailed = suiteResults.Sum(r => r.Result.Failed);
            long totalMs    = suiteResults.Sum(r => r.Result.ElapsedMs);

            Log.Message("[EternalTests] ========================================");
            Log.Message($"[EternalTests] RESULTS: {totalPassed} passed, {totalFailed} failed ({totalMs}ms total)");
            Log.Message("[EternalTests] ========================================");

            PostSummaryLetter(suiteResults, totalMs);
        }

        // ─── Per-Suite Buttons ────────────────────────────────────────────────

        [DebugAction("Eternal", "Tests: ResurrectionCycle", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void RunResurrectionCycleSuite()
        {
            var map = Find.CurrentMap;
            if (map == null) { Log.Error("[EternalTests] No current map."); return; }
            RunSingleSuite("ResurrectionCycle", () => ResurrectionCycleTests.RunAll(map));
        }

        [DebugAction("Eternal", "Tests: HediffSwap", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void RunHediffSwapSuite()
        {
            var map = Find.CurrentMap;
            if (map == null) { Log.Error("[EternalTests] No current map."); return; }
            RunSingleSuite("HediffSwap", () => HediffSwapTests.RunAll(map));
        }

        [DebugAction("Eternal", "Tests: CaravanDeath", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void RunCaravanDeathSuite()
        {
            var map = Find.CurrentMap;
            if (map == null) { Log.Error("[EternalTests] No current map."); return; }
            RunSingleSuite("CaravanDeath", () => CaravanDeathTests.RunAll(map));
        }

        [DebugAction("Eternal", "Tests: MapProtection", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void RunMapProtectionSuite()
        {
            var map = Find.CurrentMap;
            if (map == null) { Log.Error("[EternalTests] No current map."); return; }
            RunSingleSuite("MapProtection", () => MapProtectionTests.RunAll(map));
        }

        [DebugAction("Eternal", "Tests: Regrowth", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void RunRegrowthSuite()
        {
            var map = Find.CurrentMap;
            if (map == null) { Log.Error("[EternalTests] No current map."); return; }
            RunSingleSuite("Regrowth", () => RegrowthTests.RunAll(map));
        }

        [DebugAction("Eternal", "Tests: WorkPriority", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void RunWorkPrioritySuite()
        {
            var map = Find.CurrentMap;
            if (map == null) { Log.Error("[EternalTests] No current map."); return; }
            RunSingleSuite("WorkPriority", () => WorkPriorityTests.RunAll(map));
        }

        [DebugAction("Eternal", "Tests: ConsciousnessBuff", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void RunConsciousnessBuffSuite()
        {
            var map = Find.CurrentMap;
            if (map == null) { Log.Error("[EternalTests] No current map."); return; }
            RunSingleSuite("ConsciousnessBuff", () => ConsciousnessBuffTests.RunAll(map));
        }

        [DebugAction("Eternal", "Tests: MoodBuff", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void RunMoodBuffSuite()
        {
            var map = Find.CurrentMap;
            if (map == null) { Log.Error("[EternalTests] No current map."); return; }
            RunSingleSuite("MoodBuff", () => MoodBuffTests.RunAll(map));
        }

        [DebugAction("Eternal", "Tests: ElixirPopCap", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void RunElixirPopCapSuite()
        {
            var map = Find.CurrentMap;
            if (map == null) { Log.Error("[EternalTests] No current map."); return; }
            RunSingleSuite("ElixirPopCap", () => ElixirPopulationCapTests.RunAll(map));
        }

        // ─── Internal Helpers ────────────────────────────────────────────────

        /// <summary>
        /// Runs a single suite, wraps timing, posts its own summary letter.
        /// </summary>
        private static void RunSingleSuite(string suiteName, Func<TestSuiteResult> runner)
        {
            var results = new List<(string Name, TestSuiteResult Result)>();
            RunSuite(suiteName, runner, results);

            var r = results[0].Result;
            long totalMs = r.ElapsedMs;
            PostSummaryLetter(results, totalMs);
        }

        /// <summary>
        /// Runs a single suite, records timing, and appends to the results list.
        /// </summary>
        private static void RunSuite(
            string suiteName,
            Func<TestSuiteResult> suiteRunner,
            List<(string Name, TestSuiteResult Result)> results)
        {
            Log.Message($"[EternalTests] --- Suite: {suiteName} ---");

            var sw = Stopwatch.StartNew();
            try
            {
                var result = suiteRunner();
                sw.Stop();
                result.ElapsedMs = sw.ElapsedMilliseconds;
                results.Add((suiteName, result));
                Log.Message($"[EternalTests] Suite {suiteName}: {result.Passed} passed, {result.Failed} failed ({result.ElapsedMs}ms)");
            }
            catch (Exception ex)
            {
                sw.Stop();
                var crashResult = new TestSuiteResult(0, 1, new List<string> { $"Suite crashed: {ex.Message}" });
                crashResult.ElapsedMs = sw.ElapsedMilliseconds;
                results.Add((suiteName, crashResult));
                Log.Error($"[EternalTests] Suite {suiteName} CRASHED: {ex}");
            }
        }

        /// <summary>
        /// Posts a letter with per-suite summary lines, grouped failures, and total timing.
        /// </summary>
        private static void PostSummaryLetter(
            List<(string Name, TestSuiteResult Result)> suiteResults,
            long totalMs)
        {
            int totalPassed = suiteResults.Sum(r => r.Result.Passed);
            int totalFailed = suiteResults.Sum(r => r.Result.Failed);

            var letterDef = totalFailed > 0 ? LetterDefOf.ThreatBig : LetterDefOf.NeutralEvent;
            string title = totalFailed > 0
                ? $"Eternal Tests: {totalFailed} FAILED"
                : $"Eternal Tests: All {totalPassed} Passed";

            var body = new System.Text.StringBuilder();
            body.AppendLine("Eternal In-Game Test Results");
            body.AppendLine();

            // Per-suite summary
            foreach (var (name, result) in suiteResults)
            {
                string statusIcon = result.Failed > 0 ? "[FAIL]" : "[OK]  ";
                body.AppendLine($"{statusIcon} {name}: {result.Passed} passed, {result.Failed} failed ({result.ElapsedMs}ms)");
            }

            body.AppendLine();
            body.AppendLine($"Total: {totalPassed} passed, {totalFailed} failed ({totalMs}ms)");

            // Grouped failures
            bool hasFailures = suiteResults.Any(r => r.Result.Failures?.Count > 0);
            if (hasFailures)
            {
                body.AppendLine();
                body.AppendLine("--- Failures ---");
                foreach (var (name, result) in suiteResults)
                {
                    if (result.Failures == null || result.Failures.Count == 0)
                        continue;

                    body.AppendLine($"[{name}]");
                    foreach (var failure in result.Failures)
                    {
                        body.AppendLine($"  - {failure}");
                    }
                }
            }

            try
            {
                Find.LetterStack?.ReceiveLetter(title, body.ToString(), letterDef);
            }
            catch (Exception ex)
            {
                Log.Error($"[EternalTests] Failed to post summary letter: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Result from a single test suite execution, including per-suite timing.
    /// </summary>
    public struct TestSuiteResult
    {
        public int Passed;
        public int Failed;
        public List<string> Failures;
        /// <summary>Wall-clock milliseconds for the suite. Set by the runner after execution.</summary>
        public long ElapsedMs;

        public TestSuiteResult(int passed, int failed, List<string> failures)
        {
            Passed    = passed;
            Failed    = failed;
            Failures  = failures ?? new List<string>();
            ElapsedMs = 0;
        }
    }

    /// <summary>
    /// Assertion helpers that throw <see cref="TestFailedException"/> on failure.
    /// </summary>
    public static class TestAssert
    {
        public static void IsTrue(bool condition, string message)
        {
            if (!condition)
                throw new TestFailedException($"AssertTrue failed: {message}");
        }

        public static void IsFalse(bool condition, string message)
        {
            if (condition)
                throw new TestFailedException($"AssertFalse failed: {message}");
        }

        public static void AreEqual<T>(T expected, T actual, string message)
        {
            if (!EqualityComparer<T>.Default.Equals(expected, actual))
                throw new TestFailedException($"AssertEqual failed: expected [{expected}] but got [{actual}]. {message}");
        }

        public static void IsNotNull(object obj, string message)
        {
            if (obj == null)
                throw new TestFailedException($"AssertNotNull failed: {message}");
        }

        public static void IsNull(object obj, string message)
        {
            if (obj != null)
                throw new TestFailedException($"AssertNull failed: expected null but got [{obj}]. {message}");
        }

        /// <summary>Throws if actual &lt;= threshold.</summary>
        public static void GreaterThan(float actual, float threshold, string message)
        {
            if (actual <= threshold)
                throw new TestFailedException($"AssertGreaterThan failed: {actual} is not > {threshold}. {message}");
        }

        /// <summary>Throws if actual &gt; threshold.</summary>
        public static void LessThanOrEqual(float actual, float threshold, string message)
        {
            if (actual > threshold)
                throw new TestFailedException($"AssertLessThanOrEqual failed: {actual} is not <= {threshold}. {message}");
        }

        /// <summary>Throws if actual is outside [min, max].</summary>
        public static void InRange(float actual, float min, float max, string message)
        {
            if (actual < min || actual > max)
                throw new TestFailedException($"AssertInRange failed: {actual} is not in [{min}, {max}]. {message}");
        }
    }

    /// <summary>
    /// Exception thrown by <see cref="TestAssert"/> on assertion failure.
    /// </summary>
    public class TestFailedException : Exception
    {
        public TestFailedException(string message) : base(message) { }
    }
}

#endif
