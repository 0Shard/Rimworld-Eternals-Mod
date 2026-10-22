/*
 * Relative Path: Eternal/Source/Eternal/Healing/EternalHediffSeverityTracker.cs
 * Creation Date: 12-11-2025
 * Last Edit: 16-07-2026
 * Author: 0Shard
 * Description: Tracks per-hediff healing history to detect hediffs protected from reaching
 *              their removal floor. History identity includes persisted Hediff.loadID so
 *              duplicate same-def/same-part instances remain independent. Legacy history is
 *              migrated only for one live candidate; ambiguous rows are discarded safely.
 *              Reconciliation stores scalar keys and attempts only, never Hediff references.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Eternal.Infrastructure;
using Eternal.Utils;

namespace Eternal
{
    /// <summary>
    /// Tracks healing attempts during live hediff processing and identifies stuck hediffs.
    /// </summary>
    public class EternalHediffSeverityTracker : IExposable
    {
        private const float SEVERITY_THRESHOLD = 0.01f;
        private const int MAX_ATTEMPTS_TO_TRACK = 5;
        private const int REQUIRED_STUCK_ATTEMPTS = 3;

        private readonly Dictionary<HealingDictionaryKey, List<HealingAttempt>> healingHistory
            = new Dictionary<HealingDictionaryKey, List<HealingAttempt>>();
        private readonly Dictionary<HealingDictionaryKey, List<HealingAttempt>> legacyHistory
            = new Dictionary<HealingDictionaryKey, List<HealingAttempt>>();

        private sealed class HealingAttempt
        {
            public float SeverityBefore;
            public float SeverityAfter;
            public float HealingApplied;
            public int TickRecorded;

            public float SeverityChange => SeverityBefore - SeverityAfter;
        }

        public EternalHediffSeverityTracker()
        {
            EternalLogger.Info("EternalHediffSeverityTracker initialized");
        }

        /// <summary>
        /// Records a healing attempt for a current hediff instance.
        /// </summary>
        public void RecordHealingAttempt(
            Pawn pawn,
            Hediff hediff,
            float severityBefore,
            float severityAfter,
            float healingApplied)
        {
            if (pawn == null || hediff?.def == null)
            {
                return;
            }

            var key = new HealingDictionaryKey(pawn, hediff);
            if (!healingHistory.TryGetValue(key, out var attempts))
            {
                attempts = new List<HealingAttempt>();
                healingHistory[key] = attempts;
            }

            attempts.Add(new HealingAttempt
            {
                SeverityBefore = severityBefore,
                SeverityAfter = severityAfter,
                HealingApplied = healingApplied,
                TickRecorded = Find.TickManager?.TicksGame ?? 0
            });

            if (attempts.Count > MAX_ATTEMPTS_TO_TRACK)
            {
                attempts.RemoveAt(0);
            }
        }

        /// <summary>
        /// Determines whether a hediff is pinned at its minimum severity after repeated attempts.
        /// </summary>
        public bool IsHediffStuck(Pawn pawn, Hediff hediff)
        {
            if (pawn == null || hediff?.def == null)
            {
                return false;
            }

            float severityFloor = Math.Max(0f, hediff.def.minSeverity);
            if (hediff.Severity > severityFloor + SEVERITY_THRESHOLD)
            {
                return false;
            }

            if (!healingHistory.TryGetValue(new HealingDictionaryKey(pawn, hediff), out var attempts)
                || attempts.Count < REQUIRED_STUCK_ATTEMPTS)
            {
                return false;
            }

            int stuckCount = 0;
            for (int i = attempts.Count - 1;
                 i >= Math.Max(0, attempts.Count - REQUIRED_STUCK_ATTEMPTS);
                 i--)
            {
                var attempt = attempts[i];
                if (HediffProgressPolicy.IsFailedWrite(attempt.SeverityBefore, attempt.SeverityAfter,
                    attempt.HealingApplied, severityFloor))
                {
                    stuckCount++;
                }
                else
                {
                    break;
                }
            }

            if (stuckCount >= REQUIRED_STUCK_ATTEMPTS)
            {
                EternalLogger.Info($"Detected stuck hediff after {stuckCount} attempts: "
                    + $"{hediff.def.defName} (Severity: {hediff.Severity:F4})");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Removes history keys that are not present in the current live HediffSet. Legacy rows
        /// are resolved only when one serialized row maps to one current candidate.
        /// </summary>
        public void ReconcileLiveHealth(
            Pawn pawn,
            IEnumerable<Hediff> currentHediffs,
            ISet<HealingDictionaryKey> currentKeys)
        {
            if (pawn == null)
            {
                return;
            }

            var liveHediffs = new List<Hediff>();
            if (currentHediffs != null)
            {
                foreach (var hediff in currentHediffs)
                {
                    try
                    {
                        if (hediff?.def != null)
                        {
                            liveHediffs.Add(hediff);
                        }
                    }
                    catch (Exception ex)
                    {
                        EternalLogger.Error($"Failed to inspect hediff during live history reconciliation for "
                            + $"pawn {pawn.thingIDNumber}: {ex.Message}");
                    }
                }
            }

            ReconcileLiveHealthCore(pawn, liveHediffs, currentKeys, null);
        }

        internal void ReconcileValidatedHealth(Pawn pawn, LiveHealthReconciliationSnapshot snapshot)
        {
            ReconcileLiveHealthCore(pawn, snapshot.Hediffs, snapshot.CurrentKeys, snapshot);
        }

        private void ReconcileLiveHealthCore(
            Pawn pawn,
            IList<Hediff> liveHediffs,
            ISet<HealingDictionaryKey> currentKeys,
            LiveHealthReconciliationSnapshot snapshot)
        {
            MigrateLegacyEntries(pawn, liveHediffs, snapshot);

            var staleKeys = new List<HealingDictionaryKey>();
            foreach (var key in healingHistory.Keys)
            {
                if (key.PawnThingIDNumber == pawn.thingIDNumber
                    && (currentKeys == null || !currentKeys.Contains(key)))
                {
                    staleKeys.Add(key);
                }
            }

            foreach (var staleKey in staleKeys)
            {
                healingHistory.Remove(staleKey);
            }
        }

        /// <summary>
        /// Clears tracking data for a specific pawn.
        /// </summary>
        public void ClearPawnTracking(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            ClearPawnTrackingById(pawn.thingIDNumber);
        }

        /// <summary>
        /// Clears tracking data for a pawn identified by its persistent integer ID.
        /// </summary>
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

            var legacyKeysToRemove = new List<HealingDictionaryKey>();
            foreach (var key in legacyHistory.Keys)
            {
                if (key.PawnThingIDNumber == pawnId)
                {
                    legacyKeysToRemove.Add(key);
                }
            }

            foreach (var key in legacyKeysToRemove)
            {
                legacyHistory.Remove(key);
            }
        }

        /// <summary>
        /// Clears tracking data for one exact hediff instance.
        /// </summary>
        public void ClearHediffTracking(Pawn pawn, Hediff hediff)
        {
            if (pawn == null || hediff == null)
            {
                return;
            }

            healingHistory.Remove(new HealingDictionaryKey(pawn, hediff));
        }

        public void ClearAllTracking()
        {
            healingHistory.Clear();
            legacyHistory.Clear();
            EternalLogger.Info("Cleared all hediff severity tracking");
        }

        public int TrackedCount => healingHistory.Count + LegacyEntryCount();

        /// <summary>
        /// Removes old attempt lists. Live reconciliation remains the authoritative removal path
        /// for hediff replacement and direct HediffSet list manipulation.
        /// </summary>
        public void PerformPeriodicCleanup(int maxAge = 60000)
        {
            int currentTick = Find.TickManager?.TicksGame ?? 0;
            var keysToRemove = new List<HealingDictionaryKey>();

            foreach (var pair in healingHistory)
            {
                if (pair.Value.Count == 0)
                {
                    keysToRemove.Add(pair.Key);
                    continue;
                }

                var lastAttempt = pair.Value[pair.Value.Count - 1];
                if (currentTick - lastAttempt.TickRecorded > maxAge)
                {
                    keysToRemove.Add(pair.Key);
                }
            }

            foreach (var key in keysToRemove)
            {
                healingHistory.Remove(key);
            }
        }

        #region Serialization

        /// <summary>
        /// Persists attempt history with the instance-aware key. Rows without an ID remain
        /// explicitly ambiguous until live reconciliation resolves or discards them.
        /// </summary>
        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                var pawnIds = new List<int>();
                var defNames = new List<string>();
                var partLabels = new List<string>();
                var hediffLoadIds = new List<int>();
                var severityBefore = new List<float>();
                var severityAfter = new List<float>();
                var healingApplied = new List<float>();
                var tickRecorded = new List<int>();

                foreach (var pair in healingHistory)
                {
                    AddSerializedAttempts(pawnIds, defNames, partLabels, hediffLoadIds,
                        severityBefore, severityAfter, healingApplied, tickRecorded,
                        pair.Key.PawnThingIDNumber, pair.Key.HediffDefName,
                        pair.Key.BodyPartLabel, pair.Key.HediffLoadID, pair.Value);
                }

                foreach (var pair in legacyHistory)
                {
                    AddSerializedAttempts(pawnIds, defNames, partLabels, hediffLoadIds,
                        severityBefore, severityAfter, healingApplied, tickRecorded,
                        pair.Key.PawnThingIDNumber, pair.Key.HediffDefName,
                        pair.Key.BodyPartLabel, -1, pair.Value);
                }

                Scribe_Collections.Look(ref pawnIds, "history_pawnIds", LookMode.Value);
                Scribe_Collections.Look(ref defNames, "history_defNames", LookMode.Value);
                Scribe_Collections.Look(ref partLabels, "history_partLabels", LookMode.Value);
                Scribe_Collections.Look(ref hediffLoadIds, "history_hediffLoadIds", LookMode.Value);
                Scribe_Collections.Look(ref severityBefore, "history_severityBefore", LookMode.Value);
                Scribe_Collections.Look(ref severityAfter, "history_severityAfter", LookMode.Value);
                Scribe_Collections.Look(ref healingApplied, "history_healingApplied", LookMode.Value);
                Scribe_Collections.Look(ref tickRecorded, "history_tickRecorded", LookMode.Value);
                return;
            }

            if (Scribe.mode != LoadSaveMode.LoadingVars)
            {
                return;
            }

            List<int> loadedPawnIds = null;
            List<string> loadedDefNames = null;
            List<string> loadedPartLabels = null;
            List<int> loadedHediffLoadIds = null;
            List<float> loadedSeverityBefore = null;
            List<float> loadedSeverityAfter = null;
            List<float> loadedHealingApplied = null;
            List<int> loadedTickRecorded = null;

            Scribe_Collections.Look(ref loadedPawnIds, "history_pawnIds", LookMode.Value);
            Scribe_Collections.Look(ref loadedDefNames, "history_defNames", LookMode.Value);
            Scribe_Collections.Look(ref loadedPartLabels, "history_partLabels", LookMode.Value);
            Scribe_Collections.Look(ref loadedHediffLoadIds, "history_hediffLoadIds", LookMode.Value);
            Scribe_Collections.Look(ref loadedSeverityBefore, "history_severityBefore", LookMode.Value);
            Scribe_Collections.Look(ref loadedSeverityAfter, "history_severityAfter", LookMode.Value);
            Scribe_Collections.Look(ref loadedHealingApplied, "history_healingApplied", LookMode.Value);
            Scribe_Collections.Look(ref loadedTickRecorded, "history_tickRecorded", LookMode.Value);

            healingHistory.Clear();
            legacyHistory.Clear();

            bool scalarListsValid = ListsMatch(
                loadedPawnIds,
                loadedDefNames,
                loadedPartLabels,
                loadedSeverityBefore,
                loadedSeverityAfter,
                loadedHealingApplied,
                loadedTickRecorded);
            bool identityListsValid = scalarListsValid
                && loadedHediffLoadIds != null
                && loadedHediffLoadIds.Count == loadedPawnIds.Count;

            if (!scalarListsValid)
            {
                return;
            }

            for (int i = 0; i < loadedPawnIds.Count; i++)
            {
                var attempt = new HealingAttempt
                {
                    SeverityBefore = loadedSeverityBefore[i],
                    SeverityAfter = loadedSeverityAfter[i],
                    HealingApplied = loadedHealingApplied[i],
                    TickRecorded = loadedTickRecorded[i]
                };

                if (identityListsValid && loadedHediffLoadIds[i] >= 0)
                {
                    var key = new HealingDictionaryKey(
                        loadedPawnIds[i],
                        loadedDefNames[i],
                        loadedPartLabels[i],
                        loadedHediffLoadIds[i]);
                    AddAttempt(healingHistory, key, attempt);
                }
                else
                {
                    var key = new HealingDictionaryKey(
                        loadedPawnIds[i], loadedDefNames[i], loadedPartLabels[i]);
                    AddAttempt(legacyHistory, key, attempt);
                }
            }
        }

        #endregion

        private void MigrateLegacyEntries(
            Pawn pawn,
            IList<Hediff> liveHediffs,
            LiveHealthReconciliationSnapshot snapshot)
        {
            if (legacyHistory.Count == 0)
            {
                return;
            }

            var candidatesByLegacyKey = snapshot?.LegacyCandidates ?? LegacyHealingCandidateLookup.BuildUniqueLookup(
                liveHediffs.Select(hediff => new HealingDictionaryKey(pawn, hediff)),
                key => new HealingDictionaryKey(key.PawnThingIDNumber, key.HediffDefName, key.BodyPartLabel));

            var legacyKeysToRemove = new List<HealingDictionaryKey>();
            foreach (var pair in legacyHistory)
            {
                if (pair.Key.PawnThingIDNumber != pawn.thingIDNumber)
                {
                    continue;
                }

                legacyKeysToRemove.Add(pair.Key);
                if (pair.Value.Count == 0
                    || !candidatesByLegacyKey.TryGetValue(pair.Key, out var candidate)
                    || !candidate.HasValue)
                {
                    continue;
                }

                var liveKey = candidate.Value;
                if (!healingHistory.ContainsKey(liveKey))
                {
                    healingHistory[liveKey] = pair.Value;
                }
            }

            foreach (var key in legacyKeysToRemove)
            {
                legacyHistory.Remove(key);
            }
        }

        private static void AddAttempt(
            IDictionary<HealingDictionaryKey, List<HealingAttempt>> target,
            HealingDictionaryKey key,
            HealingAttempt attempt)
        {
            if (!target.TryGetValue(key, out var attempts))
            {
                attempts = new List<HealingAttempt>();
                target[key] = attempts;
            }
            attempts.Add(attempt);
        }

        private static void AddSerializedAttempts(
            ICollection<int> pawnIds,
            ICollection<string> defNames,
            ICollection<string> partLabels,
            ICollection<int> hediffLoadIds,
            ICollection<float> severityBefore,
            ICollection<float> severityAfter,
            ICollection<float> healingApplied,
            ICollection<int> tickRecorded,
            int pawnId,
            string defName,
            string partLabel,
            int hediffLoadId,
            IEnumerable<HealingAttempt> attempts)
        {
            foreach (var attempt in attempts)
            {
                pawnIds.Add(pawnId);
                defNames.Add(defName);
                partLabels.Add(partLabel);
                hediffLoadIds.Add(hediffLoadId);
                severityBefore.Add(attempt.SeverityBefore);
                severityAfter.Add(attempt.SeverityAfter);
                healingApplied.Add(attempt.HealingApplied);
                tickRecorded.Add(attempt.TickRecorded);
            }
        }

        private static bool ListsMatch(
            IList<int> pawnIds,
            IList<string> defNames,
            IList<string> partLabels,
            IList<float> severityBefore,
            IList<float> severityAfter,
            IList<float> healingApplied,
            IList<int> tickRecorded)
        {
            return pawnIds != null
                && defNames != null
                && partLabels != null
                && severityBefore != null
                && severityAfter != null
                && healingApplied != null
                && tickRecorded != null
                && pawnIds.Count == defNames.Count
                && pawnIds.Count == partLabels.Count
                && pawnIds.Count == severityBefore.Count
                && pawnIds.Count == severityAfter.Count
                && pawnIds.Count == healingApplied.Count
                && pawnIds.Count == tickRecorded.Count;
        }

        private int LegacyEntryCount()
        {
            int count = 0;
            foreach (var attempts in legacyHistory.Values)
            {
                count += attempts.Count;
            }

            return count;
        }
    }

    internal static class HediffProgressPolicy
    {
        internal static bool IsFailedWrite(float before, float after, float applied, float floor)
            => applied > 0f && !float.IsNaN(before) && !float.IsNaN(after)
                && (after >= before || after <= floor);
    }
}
