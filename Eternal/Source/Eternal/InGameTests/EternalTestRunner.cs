// Relative Path: Eternal/Source/Eternal/InGameTests/EternalTestRunner.cs
// Creation Date: 24-02-2026
// Last Edit: 24-02-2026
// Author: 0Shard
// Description: Self-contained in-game test runner for Eternal mod integration tests.
//              Invoked via dev-mode DebugAction button. Discovers and runs all test suites,
//              tracks pass/fail counts, and posts a summary letter to the player.
//              Gated behind #if DEBUG to exclude from Release builds.

#if DEBUG

using System;
using System.Collections.Generic;
using Verse;
using RimWorld;
using LudeonTK;
using Eternal.InGameTests.Helpers;
using Eternal.InGameTests.TestSuites;

namespace Eternal.InGameTests
{
    /// <summary>
    /// Orchestrates in-game integration test execution. Accessible via dev-mode debug actions.
    /// Each test suite returns (passed, failed, failures) and the runner aggregates results
    /// into a summary letter.
    /// </summary>
    public static class EternalTestRunner
    {
        /// <summary>
        /// Dev-mode button entry point. Runs all registered test suites sequentially.
        /// </summary>
        [DebugAction("Eternal", "Run In-Game Tests", allowedGameStates = AllowedGameStates.PlayingOnMap)]
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

            int totalPassed = 0;
            int totalFailed = 0;
            var allFailures = new List<string>();

            // Run each suite and aggregate results
            RunSuite("ResurrectionCycle", () => ResurrectionCycleTests.RunAll(map),
                ref totalPassed, ref totalFailed, allFailures);

            RunSuite("HediffSwap", () => HediffSwapTests.RunAll(map),
                ref totalPassed, ref totalFailed, allFailures);

            RunSuite("CaravanDeath", () => CaravanDeathTests.RunAll(map),
                ref totalPassed, ref totalFailed, allFailures);

            RunSuite("MapProtection", () => MapProtectionTests.RunAll(map),
                ref totalPassed, ref totalFailed, allFailures);

            RunSuite("Regrowth", () => RegrowthTests.RunAll(map),
                ref totalPassed, ref totalFailed, allFailures);

            RunSuite("WorkPriority", () => WorkPriorityTests.RunAll(map),
                ref totalPassed, ref totalFailed, allFailures);

            // Summary
            Log.Message("[EternalTests] ========================================");
            Log.Message($"[EternalTests] RESULTS: {totalPassed} passed, {totalFailed} failed");
            Log.Message("[EternalTests] ========================================");

            // Post summary letter
            PostSummaryLetter(totalPassed, totalFailed, allFailures);
        }

        /// <summary>
        /// Runs a single test suite, catching any unhandled exceptions at the suite level.
        /// </summary>
        private static void RunSuite(
            string suiteName,
            Func<TestSuiteResult> suiteRunner,
            ref int totalPassed,
            ref int totalFailed,
            List<string> allFailures)
        {
            Log.Message($"[EternalTests] --- Suite: {suiteName} ---");

            try
            {
                var result = suiteRunner();
                totalPassed += result.Passed;
                totalFailed += result.Failed;
                allFailures.AddRange(result.Failures);

                Log.Message($"[EternalTests] Suite {suiteName}: {result.Passed} passed, {result.Failed} failed");
            }
            catch (Exception ex)
            {
                totalFailed++;
                string failure = $"[{suiteName}] Suite crashed: {ex.Message}";
                allFailures.Add(failure);
                Log.Error($"[EternalTests] Suite {suiteName} CRASHED: {ex}");
            }
        }

        /// <summary>
        /// Posts a summary letter to the player's letter stack with test results.
        /// </summary>
        private static void PostSummaryLetter(int passed, int failed, List<string> failures)
        {
            var letterDef = failed > 0 ? LetterDefOf.ThreatBig : LetterDefOf.NeutralEvent;
            string title = failed > 0
                ? $"Eternal Tests: {failed} FAILED"
                : $"Eternal Tests: All {passed} Passed";

            string body = $"Eternal In-Game Test Results\n\n" +
                          $"Passed: {passed}\nFailed: {failed}\n\n";

            if (failures.Count > 0)
            {
                body += "Failures:\n";
                foreach (var f in failures)
                {
                    body += $"  - {f}\n";
                }
            }

            try
            {
                Find.LetterStack?.ReceiveLetter(title, body, letterDef);
            }
            catch (Exception ex)
            {
                Log.Error($"[EternalTests] Failed to post summary letter: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// Result from a single test suite execution.
    /// </summary>
    public struct TestSuiteResult
    {
        public int Passed;
        public int Failed;
        public List<string> Failures;

        public TestSuiteResult(int passed, int failed, List<string> failures)
        {
            Passed = passed;
            Failed = failed;
            Failures = failures ?? new List<string>();
        }
    }

    /// <summary>
    /// Simple assertion helpers that throw <see cref="TestFailedException"/> on failure.
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
