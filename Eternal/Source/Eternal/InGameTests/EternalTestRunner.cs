// Relative Path: Eternal/Source/Eternal/InGameTests/EternalTestRunner.cs
// Creation Date: 24-02-2026
// Last Edit: 16-07-2026
// Author: 0Shard
// Description: Self-contained in-game test runner for Eternal mod integration tests.
//              Invoked via dev-mode DebugAction buttons. Discovers and runs all test suites,
//              tracks pass/fail counts, and posts a summary letter to the player.
//              13-03: Added per-suite DebugAction buttons, Stopwatch timing, named-suite
//              failure grouping, and 3 new v1.0.1 suites (consciousness, mood, elixir/cap).
//              Gated behind #if DEBUG to exclude from Release builds.
//              26-03: Added ResurrectionSurvival suite (RSRV-01/02/03).
//              10-07: Added HealingGate suite (threshold bypass + minSeverity floor removal).

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
        /// Dev-mode button entry point. Runs all registered test suites sequentially.
        /// </summary>
        [DebugAction("Eternal", "Tests: Run All", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void RunAllTests()
        {
            RunAllTestsWithResults();
        }

        public static IReadOnlyList<(string Name, TestSuiteResult Result)> RunAllTestsWithResults()
        {
            var map = Find.CurrentMap;
            if (map == null)
            {
                Log.Error("[EternalTests] No current map found. Load a save first.");
                return Array.Empty<(string Name, TestSuiteResult Result)>();
            }

            Log.Message("[EternalTests] ========================================");
            Log.Message("[EternalTests] Starting Eternal In-Game Test Suite");
            Log.Message("[EternalTests] ========================================");

            var suiteResults = new List<(string Name, TestSuiteResult Result)>();

            RunSuite("ResurrectionCycle", () => ResurrectionCycleTests.RunAll(map), suiteResults);
            RunSuite("HediffSwap", () => HediffSwapTests.RunAll(map), suiteResults);
            RunSuite("CaravanDeath", () => CaravanDeathTests.RunAll(map), suiteResults);
            RunSuite("MapProtection", () => MapProtectionTests.RunAll(map), suiteResults);
            RunSuite("MapRemovalFailure", () => FailureScenarios.MapRemovalFailureTests.RunAll(map), suiteResults);
            RunSuite("RecoverySiteFailure", () => FailureScenarios.RecoverySiteFailureTests.RunAll(map), suiteResults);
            RunSuite("SoloDeathFailure", () => FailureScenarios.SoloDeathFailureTests.RunAll(map), suiteResults);
            RunSuite("DebtPersistenceFailure", () => FailureScenarios.DebtPersistenceFailureTests.RunAll(map), suiteResults);
            RunSuite("Regrowth", () => RegrowthTests.RunAll(map), suiteResults);
            RunSuite("ModernRegrowth", () => ModernRegrowthTests.RunAll(map), suiteResults);
            RunSuite("WorkPriority", () => WorkPriorityTests.RunAll(map), suiteResults);
            RunSuite("ConsciousnessBuff", () => ConsciousnessBuffTests.RunAll(map), suiteResults);
            RunSuite("MoodBuff", () => MoodBuffTests.RunAll(map), suiteResults);
            RunSuite("ElixirPopCap", () => ElixirPopulationCapTests.RunAll(map), suiteResults);
            RunSuite("ResurrectionSurvival", () => ResurrectionSurvivalTests.RunAll(map), suiteResults);
            RunSuite("HealingGate", () => HealingGateTests.RunAll(map), suiteResults);
            RunSuite("ReactiveHealth", () => ReactiveHealthTests.RunAll(map), suiteResults);
            RunSuite("MaintainabilityBoundary", () => MaintainabilityBoundaryTests.RunAll(map), suiteResults);

            int totalPassed = suiteResults.Sum(r => r.Result.Passed);
            int totalFailed = suiteResults.Sum(r => r.Result.Failed);
            int totalUnavailable = suiteResults.Sum(r => r.Result.Unavailable);
            long totalMs = suiteResults.Sum(r => r.Result.ElapsedMs);

            Log.Message("[EternalTests] ========================================");
            Log.Message($"[EternalTests] RESULTS: {totalPassed} passed, {totalFailed} failed, {totalUnavailable} unavailable ({totalMs}ms total)");
            Log.Message("[EternalTests] ========================================");

            PostSummaryLetter(suiteResults, totalMs);
            return suiteResults;
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

        [DebugAction("Eternal", "Tests: ModernRegrowth", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void RunModernRegrowthSuite()
        {
            var map = Find.CurrentMap;
            if (map == null) { Log.Error("[EternalTests] No current map."); return; }
            RunSingleSuite("ModernRegrowth", () => ModernRegrowthTests.RunAll(map));
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

        [DebugAction("Eternal Tests", "Run: ResurrectionSurvival", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void RunResurrectionSurvivalOnly()
        {
            var map = Find.CurrentMap;
            if (map == null) { Log.Error("[EternalTests] No current map."); return; }
            RunSingleSuite("ResurrectionSurvival", () => ResurrectionSurvivalTests.RunAll(map));
        }

        [DebugAction("Eternal Tests", "Run: ReactiveHealth", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void RunReactiveHealthOnly()
        {
            var map = Find.CurrentMap;
            if (map == null) { Log.Error("[EternalTests] No current map."); return; }
            RunSingleSuite("ReactiveHealth", () => ReactiveHealthTests.RunAll(map));
        }

        // Opt-in: the normal resurrection check advances the active game's ticks.
        [DebugAction("Eternal Tests", "Run: RJW Compatibility (opt-in)", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void RunRjwCompatibilityOnly()
        {
            var map = Find.CurrentMap;
            if (map == null) { Log.Error("[EternalTests] No current map."); return; }
            RunSingleSuite("RjwCompatibility", () => RjwCompatibilityTests.RunAll(map));
        }

        // ─── Internal Helpers ────────────────────────────────────────────────

        [DebugAction("Eternal", "Tests: MaintainabilityBoundary", allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void RunMaintainabilityBoundarySuite()
        {
            var map = Find.CurrentMap;
            RunSingleSuite("MaintainabilityBoundary", () => MaintainabilityBoundaryTests.RunAll(map));
        }

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
                Log.Message($"[EternalTests] Suite {suiteName}: {result.Passed} passed, {result.Failed} failed, {result.Unavailable} unavailable ({result.ElapsedMs}ms)");
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
            int totalUnavailable = suiteResults.Sum(r => r.Result.Unavailable);

            var letterDef = totalFailed > 0 ? LetterDefOf.ThreatBig : LetterDefOf.NeutralEvent;
            string title = totalFailed > 0
                ? $"Eternal Tests: {totalFailed} FAILED"
                : totalUnavailable > 0 ? $"Eternal Tests: {totalUnavailable} UNAVAILABLE"
                : $"Eternal Tests: All {totalPassed} Passed";

            var body = new System.Text.StringBuilder();
            body.AppendLine("Eternal In-Game Test Results");
            body.AppendLine();

            // Per-suite summary
            foreach (var (name, result) in suiteResults)
            {
                string statusIcon = result.Failed > 0 ? "[FAIL]" : result.Unavailable > 0 ? "[UNAVAILABLE]" : "[OK]  ";
                body.AppendLine($"{statusIcon} {name}: {result.Passed} passed, {result.Failed} failed, {result.Unavailable} unavailable ({result.ElapsedMs}ms)");
            }

            body.AppendLine();
            body.AppendLine($"Total: {totalPassed} passed, {totalFailed} failed, {totalUnavailable} unavailable ({totalMs}ms)");

            foreach (var suite in suiteResults)
            {
                foreach (var reason in suite.Result.UnavailableReasons)
                {
                    body.AppendLine($"  - UNAVAILABLE: {reason}");
                }
            }

            // Grouped failures
            bool hasFailures = suiteResults.Any(r => r.Result.Failures?.Count > 0);
            if (hasFailures)
            {
                body.AppendLine();
                body.AppendLine("--- Failures ---");
                foreach (var (name, result) in suiteResults)
                {
                    if (result.Failures == null || result.Failures.Count == 0)
                    {
                        continue;
                    }

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
        public int Unavailable;
        public List<string> Failures;
        public List<string> UnavailableReasons;
        /// <summary>Wall-clock milliseconds for the suite. Set by the runner after execution.</summary>
        public long ElapsedMs;

        public TestSuiteResult(int passed, int failed, List<string> failures)
        {
            Passed = passed;
            Failed = failed;
            Unavailable = 0;
            Failures = failures ?? new List<string>();
            UnavailableReasons = new List<string>();
            ElapsedMs = 0;
        }

        public void RunTest(string suite, string name, Action test)
        {
            Log.Message($"[EternalTests] Running: {name}");
            Exception failure = null;
            try
            {
                test();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                try { TestPawnFactory.CleanupAll(); }
                catch (Exception cleanup)
                {
                    failure = failure == null ? cleanup : new AggregateException(failure, cleanup);
                }
            }

            if (failure is TestUnavailableException)
            {
                Unavailable++;
                UnavailableReasons.Add($"[{suite}] {name}: {failure.Message}");
                Log.Warning($"[EternalTests] UNAVAILABLE: {name} -- {failure.Message}");
            }
            else if (failure != null)
            {
                Failed++;
                Failures.Add($"[{suite}] {name}: {failure.Message}");
                Log.Error($"[EternalTests] FAILED: {name} -- {failure}");
            }
            else
            {
                Passed++;
                Log.Message($"[EternalTests] PASSED: {name}");
            }
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
            {
                throw new TestFailedException($"AssertTrue failed: {message}");
            }
        }

        public static void IsFalse(bool condition, string message)
        {
            if (condition)
            {
                throw new TestFailedException($"AssertFalse failed: {message}");
            }
        }

        public static void AreEqual<T>(T expected, T actual, string message)
        {
            if (expected is float expectedFloat)
            {
                Finite(expectedFloat, message);
            }

            if (actual is float actualFloat)
            {
                Finite(actualFloat, message);
            }

            if (expected is double expectedDouble && (double.IsNaN(expectedDouble) || double.IsInfinity(expectedDouble)))
            {
                throw new TestFailedException($"Nonfinite expected value. {message}");
            }

            if (actual is double actualDouble && (double.IsNaN(actualDouble) || double.IsInfinity(actualDouble)))
            {
                throw new TestFailedException($"Nonfinite actual value. {message}");
            }

            if (!EqualityComparer<T>.Default.Equals(expected, actual))
            {
                throw new TestFailedException($"AssertEqual failed: expected [{expected}] but got [{actual}]. {message}");
            }
        }

        public static void IsNotNull(object obj, string message)
        {
            if (obj == null)
            {
                throw new TestFailedException($"AssertNotNull failed: {message}");
            }
        }

        public static void IsNull(object obj, string message)
        {
            if (obj != null)
            {
                throw new TestFailedException($"AssertNull failed: expected null but got [{obj}]. {message}");
            }
        }

        /// <summary>Throws if actual &lt;= threshold.</summary>
        public static void GreaterThan(float actual, float threshold, string message)
        {
            Finite(actual, message);
            Finite(threshold, message);
            if (actual <= threshold)
            {
                throw new TestFailedException($"AssertGreaterThan failed: {actual} is not > {threshold}. {message}");
            }
        }

        /// <summary>Throws if actual &gt; threshold.</summary>
        public static void LessThanOrEqual(float actual, float threshold, string message)
        {
            Finite(actual, message);
            Finite(threshold, message);
            if (actual > threshold)
            {
                throw new TestFailedException($"AssertLessThanOrEqual failed: {actual} is not <= {threshold}. {message}");
            }
        }

        /// <summary>Throws if actual is outside [min, max].</summary>
        public static void InRange(float actual, float min, float max, string message)
        {
            Finite(actual, message);
            Finite(min, message);
            Finite(max, message);
            IsTrue(min <= max, $"Invalid assertion range. {message}");
            if (actual < min || actual > max)
            {
                throw new TestFailedException($"AssertInRange failed: {actual} is not in [{min}, {max}]. {message}");
            }
        }

        public static void Finite(float value, string message)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new TestFailedException($"Nonfinite value [{value}]. {message}");
            }
        }

        public static void Available(object prerequisite, string name)
        {
            if (prerequisite == null)
            {
                throw new TestUnavailableException($"Required host prerequisite: {name}");
            }
        }
    }

    public class TestUnavailableException : Exception
    {
        public TestUnavailableException(string message) : base(message) { }
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
