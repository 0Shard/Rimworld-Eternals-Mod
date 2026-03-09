/*
 * Relative Path: Eternal/Source/Eternal/Healing/EternalHediffSeverityTracker.cs
 * Creation Date: 12-11-2025
 * Last Edit: 21-02-2026
 * Author: 0Shard
 * Description: Tracks hediff severity changes to detect "stuck" hediffs that RimWorld protects from
 *              being fully removed. When detected, these hediffs are forcibly removed.
 *              PERF-04: Replaced string dictionary keys with HealingDictionaryKey composite struct
 *              to eliminate per-tick string allocation. ClearPawnTracking now uses integer comparison
 *              instead of string.StartsWith prefix scanning.
 *              05-02: Added ClearPawnTrackingById(int) overload for sweep callers that hold only a pawn ID.
 */

using System;
using System.Collections.Generic;
using Verse;
using Eternal.Infrastructure;
using Eternal.Utils;

namespace Eternal
{
    /// <summary>
    /// Tracks hediff severity changes during healing to detect hediffs that are protected by RimWorld
    /// and cannot naturally reach zero severity. When detected, these hediffs are forcibly removed.
    /// </summary>
    public class EternalHediffSeverityTracker
    {
        // Hardcoded thresholds (no settings as requested)
        private const float SEVERITY_THRESHOLD = 0.01f;
        private const float MIN_CHANGE_THRESHOLD = 0.0001f;
        private const int MAX_ATTEMPTS_TO_TRACK = 5;
        private const int REQUIRED_STUCK_ATTEMPTS = 3; // Must be stuck 3 times before forcing removal

        // PERF-04: Struct key instead of string to avoid per-tick allocation
        private Dictionary<HealingDictionaryKey, List<HealingAttempt>> healingHistory;

        /// <summary>
        /// Represents a single healing attempt on a hediff.
        /// </summary>
        private class HealingAttempt
        {
            public float SeverityBefore { get; set; }
            public float SeverityAfter { get; set; }
            public float HealingApplied { get; set; }
            public int TickRecorded { get; set; }

            public float SeverityChange => SeverityBefore - SeverityAfter;
        }

        /// <summary>
        /// Initializes a new instance of the severity tracker.
        /// </summary>
        public EternalHediffSeverityTracker()
        {
            healingHistory = new Dictionary<HealingDictionaryKey, List<HealingAttempt>>();
            EternalLogger.Info("EternalHediffSeverityTracker initialized");
        }

        /// <summary>
        /// Records a healing attempt for a hediff.
        /// </summary>
        /// <param name="pawn">The pawn being healed</param>
        /// <param name="hediff">The hediff being healed</param>
        /// <param name="severityBefore">Severity before healing was applied</param>
        /// <param name="severityAfter">Severity after healing was applied</param>
        /// <param name="healingApplied">Amount of healing that was attempted</param>
        public void RecordHealingAttempt(Pawn pawn, Hediff hediff, float severityBefore, float severityAfter, float healingApplied)
        {
            if (pawn == null || hediff == null)
                return;

            var key = new HealingDictionaryKey(pawn, hediff);

            // Initialize tracking list if needed
            if (!healingHistory.ContainsKey(key))
            {
                healingHistory[key] = new List<HealingAttempt>();
            }

            var attempts = healingHistory[key];

            // Add new attempt
            attempts.Add(new HealingAttempt
            {
                SeverityBefore = severityBefore,
                SeverityAfter = severityAfter,
                HealingApplied = healingApplied,
                TickRecorded = Find.TickManager.TicksGame
            });

            // Keep only last N attempts (sliding window)
            if (attempts.Count > MAX_ATTEMPTS_TO_TRACK)
            {
                attempts.RemoveAt(0);
            }
        }

        /// <summary>
        /// Determines if a hediff is "stuck" at a minimum severity and should be forcibly removed.
        /// A hediff is considered stuck if:
        /// - Current severity is <= 0.01 (very low)
        /// - Last 3 consecutive healing attempts resulted in no change or minimal change (less than 0.0001)
        /// </summary>
        /// <param name="pawn">The pawn with the hediff</param>
        /// <param name="hediff">The hediff to check</param>
        /// <returns>True if hediff is stuck and should be forcibly removed</returns>
        public bool IsHediffStuck(Pawn pawn, Hediff hediff)
        {
            if (pawn == null || hediff == null)
                return false;

            // Check if severity is below threshold
            if (hediff.Severity > SEVERITY_THRESHOLD)
                return false;

            var key = new HealingDictionaryKey(pawn, hediff);

            // Check if we have tracking history
            if (!healingHistory.ContainsKey(key))
                return false;

            var attempts = healingHistory[key];

            // Need at least REQUIRED_STUCK_ATTEMPTS to determine if truly stuck
            if (attempts.Count < REQUIRED_STUCK_ATTEMPTS)
                return false;

            // Check the last N attempts to see if hediff is consistently stuck
            int stuckCount = 0;
            for (int i = attempts.Count - 1; i >= Math.Max(0, attempts.Count - REQUIRED_STUCK_ATTEMPTS); i--)
            {
                var attempt = attempts[i];

                // Check if this attempt shows the hediff is stuck
                // (healing was applied but severity didn't decrease meaningfully)
                if (attempt.HealingApplied > 0f && attempt.SeverityChange < MIN_CHANGE_THRESHOLD)
                {
                    stuckCount++;
                }
                else
                {
                    // If any recent attempt was NOT stuck, hediff is not considered stuck
                    break;
                }
            }

            // Hediff is stuck if all last N attempts were stuck
            if (stuckCount >= REQUIRED_STUCK_ATTEMPTS)
            {
                EternalLogger.Info($"Detected stuck hediff after {stuckCount} attempts: {hediff.def.defName} " +
                    $"(Severity: {hediff.Severity:F4}, Last Change: {attempts[attempts.Count - 1].SeverityChange:F4})");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Clears tracking data for a specific pawn.
        /// PERF-04: Uses integer PawnThingIDNumber comparison instead of string.StartsWith prefix scan.
        /// </summary>
        /// <param name="pawn">The pawn to clear tracking for</param>
        public void ClearPawnTracking(Pawn pawn)
        {
            if (pawn == null)
                return;

            int pawnId = pawn.thingIDNumber;
            var keysToRemove = new List<HealingDictionaryKey>();

            foreach (var key in healingHistory.Keys)
            {
                if (key.PawnThingIDNumber == pawnId)
                {
                    keysToRemove.Add(key);
                }
            }

            foreach (var key in keysToRemove)
            {
                healingHistory.Remove(key);
            }

            if (keysToRemove.Count > 0)
            {
                EternalLogger.Debug($"Cleared tracking for pawn {pawnId}: {keysToRemove.Count} hediffs");
            }
        }

        /// <summary>
        /// Clears tracking data for a pawn identified by its integer ID.
        /// Used by sweep callers that hold only a pawn ID, not a live Pawn reference.
        /// </summary>
        /// <param name="pawnId">The ThingIDNumber of the pawn to clear tracking for</param>
        public void ClearPawnTrackingById(int pawnId)
        {
            var keysToRemove = new List<HealingDictionaryKey>();

            foreach (var key in healingHistory.Keys)
            {
                if (key.PawnThingIDNumber == pawnId)
                {
                    keysToRemove.Add(key);
                }
            }

            foreach (var key in keysToRemove)
            {
                healingHistory.Remove(key);
            }

            if (keysToRemove.Count > 0)
            {
                EternalLogger.Debug($"Cleared severity tracking for stale pawn ID {pawnId}: {keysToRemove.Count} entries");
            }
        }

        /// <summary>
        /// Clears tracking data for a specific hediff on a pawn.
        /// </summary>
        /// <param name="pawn">The pawn</param>
        /// <param name="hediff">The hediff to clear tracking for</param>
        public void ClearHediffTracking(Pawn pawn, Hediff hediff)
        {
            if (pawn == null || hediff == null)
                return;

            var key = new HealingDictionaryKey(pawn, hediff);
            healingHistory.Remove(key);
        }

        /// <summary>
        /// Clears all tracking data.
        /// </summary>
        public void ClearAllTracking()
        {
            int count = healingHistory.Count;
            healingHistory.Clear();
            EternalLogger.Info($"Cleared all hediff severity tracking ({count} entries)");
        }

        /// <summary>
        /// Performs periodic cleanup of old tracking entries.
        /// Removes entries for hediffs that haven't been updated in a long time.
        /// PERF-04: Iterates struct keys — no string allocation during cleanup.
        /// </summary>
        /// <param name="maxAge">Maximum age in ticks before an entry is considered stale</param>
        public void PerformPeriodicCleanup(int maxAge = 60000) // Default: 1 in-game day
        {
            int currentTick = Find.TickManager.TicksGame;
            var keysToRemove = new List<HealingDictionaryKey>();

            foreach (var kvp in healingHistory)
            {
                if (kvp.Value.Count == 0)
                {
                    keysToRemove.Add(kvp.Key);
                    continue;
                }

                // Check if the last attempt is too old
                var lastAttempt = kvp.Value[kvp.Value.Count - 1];
                if (currentTick - lastAttempt.TickRecorded > maxAge)
                {
                    keysToRemove.Add(kvp.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                healingHistory.Remove(key);
            }

            if (keysToRemove.Count > 0)
            {
                EternalLogger.Debug($"Periodic cleanup: Removed {keysToRemove.Count} stale tracking entries");
            }
        }
    }
}
