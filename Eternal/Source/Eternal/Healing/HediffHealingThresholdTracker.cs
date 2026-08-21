/*
 * Relative Path: Eternal/Source/Eternal/Healing/HediffHealingThresholdTracker.cs
 * Creation Date: 29-12-2025
 * Last Edit: 16-07-2026
 * Author: 0Shard
 * Description: Tracks per-hediff-instance healing activation thresholds for debuff hediffs.
 *              Threshold identity includes RimWorld's persisted Hediff.loadID so duplicate
 *              same-def/same-part instances never share activation state. Legacy saves that
 *              lack instance identity are migrated only when exactly one live hediff matches;
 *              ambiguous legacy entries are discarded and current state is registered fresh.
 *              State is reconciled from the current HediffSet and never retains Hediff references.
 */

using System;
using System.Collections.Generic;
using Verse;
using Eternal.Infrastructure;
using Eternal.Exceptions;
using Eternal.Utils;

namespace Eternal.Healing
{
    /// <summary>
    /// Tracks healing activation thresholds for debuff hediff instances.
    /// Each debuff on a living Eternal pawn gets a random threshold that must be reached
    /// before the Eternal healing system will start healing it.
    /// </summary>
    public class HediffHealingThresholdTracker : IExposable
    {
        private readonly Dictionary<HealingDictionaryKey, ThresholdEntry> _thresholds
            = new Dictionary<HealingDictionaryKey, ThresholdEntry>();
        private readonly Dictionary<LegacyThresholdKey, List<ThresholdEntry>> _legacyThresholds
            = new Dictionary<LegacyThresholdKey, List<ThresholdEntry>>();

        /// <summary>
        /// Entry storing both the threshold value and whether it has been reached.
        /// </summary>
        private sealed class ThresholdEntry
        {
            public float threshold;
            public bool hasBeenReached;

            public ThresholdEntry() { }

            public ThresholdEntry(float threshold)
            {
                this.threshold = threshold;
            }
        }

        /// <summary>
        /// Identity used only while migrating pre-Gate-2 state. It intentionally has no
        /// instance ID, so it must never be used to select one of several live hediffs.
        /// </summary>
        private readonly struct LegacyThresholdKey : IEquatable<LegacyThresholdKey>
        {
            public readonly int PawnThingIDNumber;
            public readonly string HediffDefName;
            public readonly string BodyPartLabel;

            public LegacyThresholdKey(int pawnId, string defName, string partLabel)
            {
                PawnThingIDNumber = pawnId;
                HediffDefName = defName ?? string.Empty;
                BodyPartLabel = partLabel ?? string.Empty;
            }

            public LegacyThresholdKey(Pawn pawn, Hediff hediff)
                : this(pawn.thingIDNumber, hediff.def.defName, hediff.Part?.Label)
            {
            }

            public bool Equals(LegacyThresholdKey other)
            {
                return PawnThingIDNumber == other.PawnThingIDNumber
                    && HediffDefName == other.HediffDefName
                    && BodyPartLabel == other.BodyPartLabel;
            }

            public override bool Equals(object obj)
            {
                return obj is LegacyThresholdKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = PawnThingIDNumber * 397;
                    hash ^= HediffDefName != null ? HediffDefName.GetHashCode() : 0;
                    hash = (hash * 397) ^ (BodyPartLabel != null ? BodyPartLabel.GetHashCode() : 0);
                    return hash;
                }
            }
        }

        /// <summary>
        /// Registers a threshold for a specific hediff instance. Existing state is preserved.
        /// </summary>
        public void RegisterThreshold(Pawn pawn, Hediff hediff, float threshold)
        {
            if (pawn == null || hediff?.def == null)
                return;

            var key = new HealingDictionaryKey(pawn, hediff);
            if (_thresholds.ContainsKey(key))
                return;

            _thresholds[key] = new ThresholdEntry(threshold);
        }

        /// <summary>
        /// Registers a fresh threshold only for the supplied current hediff instance.
        /// </summary>
        public void RegisterThresholdIfMissing(Pawn pawn, Hediff hediff)
        {
            if (pawn == null || hediff?.def == null)
                return;

            var key = new HealingDictionaryKey(pawn, hediff);
            if (_thresholds.ContainsKey(key))
                return;

            float maxSeverity = hediff.def.maxSeverity;
            if (float.IsInfinity(maxSeverity) || float.IsNaN(maxSeverity)
                || maxSeverity <= 0f || maxSeverity > 100f)
            {
                maxSeverity = 1f;
            }

            RegisterThreshold(pawn, hediff, Rand.Range(0.01f, 0.99f) * maxSeverity);
        }

        /// <summary>
        /// Checks whether a hediff has reached its threshold. Missing state is lazily created
        /// as a fallback for old callers; the normal path creates it during reconciliation.
        /// </summary>
        public bool HasReachedThreshold(Pawn pawn, Hediff hediff)
        {
            if (pawn == null || hediff?.def == null)
                return true;

            var key = new HealingDictionaryKey(pawn, hediff);
            if (!_thresholds.TryGetValue(key, out var entry))
            {
                RegisterThresholdIfMissing(pawn, hediff);
                if (!_thresholds.TryGetValue(key, out entry))
                    return true;

                if (hediff.Severity >= entry.threshold)
                    entry.hasBeenReached = true;
            }

            if (entry.hasBeenReached)
                return true;

            if (hediff.Severity >= entry.threshold)
            {
                entry.hasBeenReached = true;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Read-only threshold lookup for UI display. This never creates state.
        /// </summary>
        public bool TryGetThreshold(Pawn pawn, Hediff hediff, out float threshold, out bool reached)
        {
            threshold = 0f;
            reached = false;

            if (pawn == null || hediff?.def == null)
                return false;

            var key = new HealingDictionaryKey(pawn, hediff);
            if (!_thresholds.TryGetValue(key, out var entry))
                return false;

            threshold = entry.threshold;
            reached = entry.hasBeenReached;
            return true;
        }

        /// <summary>
        /// Removes the threshold for one exact hediff instance.
        /// </summary>
        public void RemoveThreshold(Pawn pawn, Hediff hediff)
        {
            if (pawn == null || hediff == null)
                return;

            _thresholds.Remove(new HealingDictionaryKey(pawn, hediff));
        }

        /// <summary>
        /// Reconciles threshold state against the current live HediffSet. Legacy state is
        /// transferred only for a single exact candidate; duplicates or missing candidates are
        /// discarded, after which eligible current instances receive fresh thresholds.
        /// </summary>
        public void ReconcileLiveHealth(
            Pawn pawn,
            IEnumerable<Hediff> currentHediffs,
            ISet<HealingDictionaryKey> eligibleKeys)
        {
            if (pawn == null)
                return;

            var liveHediffs = new List<Hediff>();
            var currentKeys = new HashSet<HealingDictionaryKey>();

            if (currentHediffs != null)
            {
                foreach (var hediff in currentHediffs)
                {
                    try
                    {
                        if (hediff?.def == null)
                            continue;

                        liveHediffs.Add(hediff);
                        currentKeys.Add(new HealingDictionaryKey(pawn, hediff));
                    }
                    catch (Exception ex)
                    {
                        EternalLogger.HandleException(
                            EternalExceptionCategory.Resurrection,
                            "ReconcileLiveHealth.ThresholdInput",
                            pawn,
                            ex);
                    }
                }
            }

            MigrateLegacyEntries(pawn, liveHediffs, eligibleKeys);

            var staleKeys = new List<HealingDictionaryKey>();
            foreach (var key in _thresholds.Keys)
            {
                if (key.PawnThingIDNumber == pawn.thingIDNumber
                    && (!currentKeys.Contains(key)
                        || eligibleKeys == null
                        || !eligibleKeys.Contains(key)))
                {
                    staleKeys.Add(key);
                }
            }

            foreach (var staleKey in staleKeys)
                _thresholds.Remove(staleKey);

            foreach (var hediff in liveHediffs)
            {
                try
                {
                    var key = new HealingDictionaryKey(pawn, hediff);
                    if (eligibleKeys != null && eligibleKeys.Contains(key))
                        RegisterThresholdIfMissing(pawn, hediff);
                }
                catch (Exception ex)
                {
                    EternalLogger.HandleException(
                        EternalExceptionCategory.Resurrection,
                        "ReconcileLiveHealth.ThresholdHediff",
                        pawn,
                        ex);
                }
            }
        }

        /// <summary>
        /// Clears all threshold state for a pawn. Used when its health set is unavailable.
        /// </summary>
        public void ClearPawnThresholds(Pawn pawn)
        {
            if (pawn == null)
                return;

            int pawnId = pawn.thingIDNumber;
            var keysToRemove = new List<HealingDictionaryKey>();
            foreach (var key in _thresholds.Keys)
            {
                if (key.PawnThingIDNumber == pawnId)
                    keysToRemove.Add(key);
            }

            foreach (var key in keysToRemove)
                _thresholds.Remove(key);

            var legacyKeysToRemove = new List<LegacyThresholdKey>();
            foreach (var key in _legacyThresholds.Keys)
            {
                if (key.PawnThingIDNumber == pawnId)
                    legacyKeysToRemove.Add(key);
            }

            foreach (var key in legacyKeysToRemove)
                _legacyThresholds.Remove(key);
        }

        /// <summary>
        /// Gets the count of live threshold entries. Legacy rows are included until the next
        /// pawn reconciliation so load diagnostics can see unprocessed state.
        /// </summary>
        public int TrackedCount
        {
            get
            {
                int legacyCount = 0;
                foreach (var entries in _legacyThresholds.Values)
                    legacyCount += entries.Count;
                return _thresholds.Count + legacyCount;
            }
        }

        private void MigrateLegacyEntries(
            Pawn pawn,
            IList<Hediff> liveHediffs,
            ISet<HealingDictionaryKey> eligibleKeys)
        {
            var candidatesByLegacyKey = new Dictionary<LegacyThresholdKey, List<Hediff>>();
            foreach (var hediff in liveHediffs)
            {
                var legacyKey = new LegacyThresholdKey(pawn, hediff);
                if (!candidatesByLegacyKey.TryGetValue(legacyKey, out var candidates))
                {
                    candidates = new List<Hediff>();
                    candidatesByLegacyKey[legacyKey] = candidates;
                }
                candidates.Add(hediff);
            }

            var keysToRemove = new List<LegacyThresholdKey>();
            foreach (var legacyPair in _legacyThresholds)
            {
                if (legacyPair.Key.PawnThingIDNumber != pawn.thingIDNumber)
                    continue;

                keysToRemove.Add(legacyPair.Key);

                // Exactly one serialized row and exactly one live candidate is the only safe
                // migration. Every other shape is intentionally reset rather than guessed.
                if (legacyPair.Value.Count != 1
                    || !candidatesByLegacyKey.TryGetValue(legacyPair.Key, out var candidates)
                    || candidates.Count != 1)
                {
                    continue;
                }

                var liveHediff = candidates[0];
                var liveKey = new HealingDictionaryKey(pawn, liveHediff);
                if (eligibleKeys != null && eligibleKeys.Contains(liveKey)
                    && !_thresholds.ContainsKey(liveKey))
                {
                    _thresholds[liveKey] = legacyPair.Value[0];
                }
            }

            foreach (var key in keysToRemove)
                _legacyThresholds.Remove(key);
        }

        #region Serialization

        /// <summary>
        /// Serializes instance-aware threshold state. Legacy rows are written with a negative
        /// load ID until a live reconciliation can resolve them safely.
        /// </summary>
        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                var pawnIds = new List<int>();
                var defNames = new List<string>();
                var partLabels = new List<string>();
                var hediffLoadIds = new List<int>();
                var thresholdValues = new List<float>();
                var reachedFlags = new List<bool>();

                foreach (var pair in _thresholds)
                {
                    AddSerializedEntry(pawnIds, defNames, partLabels, hediffLoadIds,
                        thresholdValues, reachedFlags, pair.Key.PawnThingIDNumber,
                        pair.Key.HediffDefName, pair.Key.BodyPartLabel, pair.Key.HediffLoadID,
                        pair.Value);
                }

                foreach (var pair in _legacyThresholds)
                {
                    foreach (var entry in pair.Value)
                    {
                        AddSerializedEntry(pawnIds, defNames, partLabels, hediffLoadIds,
                            thresholdValues, reachedFlags, pair.Key.PawnThingIDNumber,
                            pair.Key.HediffDefName, pair.Key.BodyPartLabel, -1, entry);
                    }
                }

                Scribe_Collections.Look(ref pawnIds, "threshold_pawnIds", LookMode.Value);
                Scribe_Collections.Look(ref defNames, "threshold_defNames", LookMode.Value);
                Scribe_Collections.Look(ref partLabels, "threshold_partLabels", LookMode.Value);
                Scribe_Collections.Look(ref hediffLoadIds, "threshold_hediffLoadIds", LookMode.Value);
                Scribe_Collections.Look(ref thresholdValues, "threshold_values", LookMode.Value);
                Scribe_Collections.Look(ref reachedFlags, "threshold_reached", LookMode.Value);
                return;
            }

            if (Scribe.mode != LoadSaveMode.LoadingVars)
                return;

            List<int> loadedPawnIds = null;
            List<string> loadedDefNames = null;
            List<string> loadedPartLabels = null;
            List<int> loadedHediffLoadIds = null;
            List<float> loadedThresholdValues = null;
            List<bool> loadedReachedFlags = null;

            Scribe_Collections.Look(ref loadedPawnIds, "threshold_pawnIds", LookMode.Value);
            Scribe_Collections.Look(ref loadedDefNames, "threshold_defNames", LookMode.Value);
            Scribe_Collections.Look(ref loadedPartLabels, "threshold_partLabels", LookMode.Value);
            Scribe_Collections.Look(ref loadedHediffLoadIds, "threshold_hediffLoadIds", LookMode.Value);
            Scribe_Collections.Look(ref loadedThresholdValues, "threshold_values", LookMode.Value);
            Scribe_Collections.Look(ref loadedReachedFlags, "threshold_reached", LookMode.Value);

            _thresholds.Clear();
            _legacyThresholds.Clear();

            bool oldListsValid = ListsMatch(
                loadedPawnIds,
                loadedDefNames,
                loadedPartLabels,
                loadedThresholdValues,
                loadedReachedFlags);
            bool identityListsValid = oldListsValid
                && loadedHediffLoadIds != null
                && loadedHediffLoadIds.Count == loadedPawnIds.Count;

            if (!oldListsValid)
            {
                if (loadedPawnIds != null)
                {
                    Log.Warning("[Eternal] Threshold state lists were invalid during load; resetting threshold state.");
                }
                return;
            }

            for (int i = 0; i < loadedPawnIds.Count; i++)
            {
                if (identityListsValid && loadedHediffLoadIds[i] >= 0)
                {
                    var key = new HealingDictionaryKey(
                        loadedPawnIds[i], loadedDefNames[i], loadedPartLabels[i], loadedHediffLoadIds[i]);
                    _thresholds[key] = new ThresholdEntry(loadedThresholdValues[i])
                    {
                        hasBeenReached = loadedReachedFlags[i]
                    };
                }
                else
                {
                    AddLegacyEntry(
                        new LegacyThresholdKey(
                            loadedPawnIds[i], loadedDefNames[i], loadedPartLabels[i]),
                        new ThresholdEntry(loadedThresholdValues[i])
                        {
                            hasBeenReached = loadedReachedFlags[i]
                        });
                }
            }
        }

        private static bool ListsMatch(
            IList<int> pawnIds,
            IList<string> defNames,
            IList<string> partLabels,
            IList<float> thresholdValues,
            IList<bool> reachedFlags)
        {
            return pawnIds != null
                && defNames != null
                && partLabels != null
                && thresholdValues != null
                && reachedFlags != null
                && pawnIds.Count == defNames.Count
                && pawnIds.Count == partLabels.Count
                && pawnIds.Count == thresholdValues.Count
                && pawnIds.Count == reachedFlags.Count;
        }

        private void AddLegacyEntry(LegacyThresholdKey key, ThresholdEntry entry)
        {
            if (!_legacyThresholds.TryGetValue(key, out var entries))
            {
                entries = new List<ThresholdEntry>();
                _legacyThresholds[key] = entries;
            }
            entries.Add(entry);
        }

        private static void AddSerializedEntry(
            ICollection<int> pawnIds,
            ICollection<string> defNames,
            ICollection<string> partLabels,
            ICollection<int> hediffLoadIds,
            ICollection<float> thresholdValues,
            ICollection<bool> reachedFlags,
            int pawnId,
            string defName,
            string partLabel,
            int hediffLoadId,
            ThresholdEntry entry)
        {
            pawnIds.Add(pawnId);
            defNames.Add(defName);
            partLabels.Add(partLabel);
            hediffLoadIds.Add(hediffLoadId);
            thresholdValues.Add(entry.threshold);
            reachedFlags.Add(entry.hasBeenReached);
        }

        #endregion
    }
}
