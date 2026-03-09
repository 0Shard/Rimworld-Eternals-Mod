/*
 * Relative Path: Eternal/Source/Eternal/Healing/HediffHealingThresholdTracker.cs
 * Creation Date: 29-12-2025
 * Last Edit: 19-02-2026
 * Author: 0Shard
 * Description: Tracks per-hediff-instance healing activation thresholds for debuff hediffs.
 *              When a debuff (infection, disease, blood loss, etc.) is added to a living Eternal
 *              pawn, a random threshold (1-99% of maxSeverity) is generated. The healing system
 *              will not heal the hediff until its severity reaches this threshold.
 *              Once reached, healing continues even if severity drops below threshold.
 *              Uses lazy registration: if no threshold found, one is created on-the-fly.
 *
 *              PERF-04: Replaced string dictionary keys with HealingDictionaryKey composite struct.
 *              The key now uses HediffDefName+BodyPartLabel (stable across sessions) instead of
 *              hediff.loadID (session-scoped, reassigned on every load).
 *              ExposeData() uses parallel-list decomposition because readonly struct fields
 *              cannot be passed as ref to Scribe_Values (see research Pitfall 3).
 */

using System.Collections.Generic;
using Verse;
using Eternal.Infrastructure;

namespace Eternal.Healing
{
    /// <summary>
    /// Tracks healing activation thresholds for debuff hediff instances.
    /// Each debuff on a living Eternal pawn gets a random threshold that must be reached
    /// before the Eternal healing system will start healing it.
    /// </summary>
    /// <remarks>
    /// Key design decisions:
    /// - Per-instance tracking using HealingDictionaryKey (pawnId + defName + partLabel)
    /// - "Latch" pattern: once threshold is reached, hasBeenReached stays true
    /// - Null-safe: returns true (heal normally) if parameters are invalid
    /// - Save/load via parallel-list decomposition (5 parallel lists, zipped on load)
    /// </remarks>
    public class HediffHealingThresholdTracker : IExposable
    {
        private Dictionary<HealingDictionaryKey, ThresholdEntry> _thresholds = new Dictionary<HealingDictionaryKey, ThresholdEntry>();

        /// <summary>
        /// Entry storing both the threshold value and whether it has been reached.
        /// IExposable is NOT used here for dict-key serialization — the key is decomposed separately.
        /// </summary>
        private class ThresholdEntry
        {
            public float threshold;
            public bool hasBeenReached;

            public ThresholdEntry()
            {
                // Default constructor for reconstruction during parallel-list deserialization
            }

            public ThresholdEntry(float threshold)
            {
                this.threshold = threshold;
                this.hasBeenReached = false;
            }
        }

        /// <summary>
        /// Registers a healing threshold for a specific hediff instance.
        /// Called by Harmony patch when a debuff hediff is added to a living Eternal pawn.
        /// </summary>
        /// <param name="pawn">The pawn with the hediff</param>
        /// <param name="hediff">The hediff instance</param>
        /// <param name="threshold">The severity threshold (0.01-0.99 * maxSeverity)</param>
        public void RegisterThreshold(Pawn pawn, Hediff hediff, float threshold)
        {
            if (pawn == null || hediff == null)
                return;

            var key = new HealingDictionaryKey(pawn, hediff);

            // Don't overwrite existing threshold (hediff might be re-added after save/load)
            if (_thresholds.ContainsKey(key))
                return;

            _thresholds[key] = new ThresholdEntry(threshold);

            if (Eternal_Mod.settings?.debugMode == true)
            {
                Log.Message($"[Eternal] Registered healing threshold {threshold:F2} for {hediff.def.defName} on {pawn.Name}");
            }
        }

        /// <summary>
        /// Checks if a hediff has reached its healing threshold.
        /// Returns true if: (1) no threshold registered, OR (2) threshold already reached, OR (3) current severity >= threshold.
        /// Once threshold is reached, it stays reached (latch pattern).
        /// </summary>
        /// <param name="pawn">The pawn with the hediff</param>
        /// <param name="hediff">The hediff to check</param>
        /// <returns>True if healing should proceed, false if waiting for threshold</returns>
        public bool HasReachedThreshold(Pawn pawn, Hediff hediff)
        {
            if (pawn == null || hediff == null)
                return true; // No valid parameters = heal normally

            var key = new HealingDictionaryKey(pawn, hediff);

            if (!_thresholds.TryGetValue(key, out var entry))
            {
                // Lazy registration: register threshold on-the-fly
                // Handles hediffs that existed before patch was applied, or edge cases
                // where the AddHediff patch didn't fire (e.g., hediffs added via scripts)
                float maxSev = hediff.def.maxSeverity;
                // Handle infinity, NaN, negative, zero, AND effectively infinite values
                if (float.IsInfinity(maxSev) || float.IsNaN(maxSev) || maxSev <= 0f || maxSev > 100f)
                    maxSev = 1.0f;
                float lazyThreshold = Rand.Range(0.01f, 0.99f) * maxSev;

                _thresholds[key] = new ThresholdEntry(lazyThreshold);

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Lazy-registered threshold {lazyThreshold:F2} for {hediff.def.defName} on {pawn.Name}");
                }

                // Check if current severity already meets the new threshold
                if (hediff.Severity >= lazyThreshold)
                {
                    _thresholds[key].hasBeenReached = true;
                    return true;
                }
                return false; // Wait for threshold to be reached
            }

            // Already reached before? Keep healing (even if severity dropped from healing)
            if (entry.hasBeenReached)
                return true;

            // Check if we've NOW reached the threshold
            if (hediff.Severity >= entry.threshold)
            {
                entry.hasBeenReached = true; // Latch: never goes back to false

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Hediff {hediff.def.defName} on {pawn.Name} reached threshold {entry.threshold:F2}, healing will begin");
                }

                return true;
            }

            return false; // Not yet reached threshold - don't heal yet
        }

        /// <summary>
        /// Removes the threshold entry for a hediff (called when hediff is removed).
        /// </summary>
        /// <param name="pawn">The pawn that had the hediff</param>
        /// <param name="hediff">The hediff being removed</param>
        public void RemoveThreshold(Pawn pawn, Hediff hediff)
        {
            if (pawn == null || hediff == null)
                return;

            var key = new HealingDictionaryKey(pawn, hediff);

            if (_thresholds.Remove(key))
            {
                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Removed healing threshold for {hediff.def.defName} on {pawn.Name}");
                }
            }
        }

        /// <summary>
        /// Gets the count of tracked thresholds (for debugging).
        /// </summary>
        public int TrackedCount => _thresholds.Count;

        /// <summary>
        /// Serializes/deserializes threshold data for save/load using parallel-list decomposition.
        /// HealingDictionaryKey is a readonly struct and cannot be passed as ref to Scribe_Values,
        /// so the dictionary is decomposed into 5 parallel lists (pawnIds, defNames, partLabels,
        /// thresholdValues, reachedFlags) and reconstructed on load.
        /// </summary>
        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                // Decompose dictionary into parallel lists for serialization
                var pawnIds = new List<int>(_thresholds.Count);
                var defNames = new List<string>(_thresholds.Count);
                var partLabels = new List<string>(_thresholds.Count);
                var thresholdValues = new List<float>(_thresholds.Count);
                var reachedFlags = new List<bool>(_thresholds.Count);

                foreach (var kvp in _thresholds)
                {
                    pawnIds.Add(kvp.Key.PawnThingIDNumber);
                    defNames.Add(kvp.Key.HediffDefName);
                    partLabels.Add(kvp.Key.BodyPartLabel);
                    thresholdValues.Add(kvp.Value.threshold);
                    reachedFlags.Add(kvp.Value.hasBeenReached);
                }

                Scribe_Collections.Look(ref pawnIds, "threshold_pawnIds", LookMode.Value);
                Scribe_Collections.Look(ref defNames, "threshold_defNames", LookMode.Value);
                Scribe_Collections.Look(ref partLabels, "threshold_partLabels", LookMode.Value);
                Scribe_Collections.Look(ref thresholdValues, "threshold_values", LookMode.Value);
                Scribe_Collections.Look(ref reachedFlags, "threshold_reached", LookMode.Value);
            }
            else if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                List<int> pawnIds = null;
                List<string> defNames = null;
                List<string> partLabels = null;
                List<float> thresholdValues = null;
                List<bool> reachedFlags = null;

                Scribe_Collections.Look(ref pawnIds, "threshold_pawnIds", LookMode.Value);
                Scribe_Collections.Look(ref defNames, "threshold_defNames", LookMode.Value);
                Scribe_Collections.Look(ref partLabels, "threshold_partLabels", LookMode.Value);
                Scribe_Collections.Look(ref thresholdValues, "threshold_values", LookMode.Value);
                Scribe_Collections.Look(ref reachedFlags, "threshold_reached", LookMode.Value);

                // Reconstruct dictionary — guard against null lists or length mismatches
                _thresholds = new Dictionary<HealingDictionaryKey, ThresholdEntry>();

                bool listsValid = pawnIds != null
                    && defNames != null
                    && partLabels != null
                    && thresholdValues != null
                    && reachedFlags != null
                    && pawnIds.Count == defNames.Count
                    && pawnIds.Count == partLabels.Count
                    && pawnIds.Count == thresholdValues.Count
                    && pawnIds.Count == reachedFlags.Count;

                if (listsValid)
                {
                    for (int i = 0; i < pawnIds.Count; i++)
                    {
                        var key = new HealingDictionaryKey(pawnIds[i], defNames[i], partLabels[i]);
                        _thresholds[key] = new ThresholdEntry(thresholdValues[i])
                        {
                            hasBeenReached = reachedFlags[i]
                        };
                    }
                }
                else if (pawnIds != null)
                {
                    // Something went wrong with partial data — start clean
                    Log.Warning("[Eternal] HediffHealingThresholdTracker: parallel lists null or length mismatch during load. Resetting thresholds.");
                }
            }
        }
    }
}
