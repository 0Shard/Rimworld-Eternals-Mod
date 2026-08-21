/*
 * Relative Path: Eternal/Source/Eternal/Healing/EternalHediffHealer.cs
 * Creation Date: 09-11-2025
 * Last Edit: 16-07-2026
 *              BUGFIX: Fixed food cost calculation to use actual severity healed instead of scaled healingAmount.
 *              Previously, food cost was multiplied by severityScaling (body part HP, e.g., 30 for torso),
 *              causing food to drain ~30x faster than intended. Now uses 250:1 ratio on actual severity reduced.
 *              PERF-04: Replaced local HealingProgressKey struct with HealingDictionaryKey from Infrastructure.
 *              Gate 2: healing progress and severity history use persisted Hediff.loadID identity;
 *              legacy instance-ambiguous rows are migrated only through live reconciliation.
 *              GetPawnHealingProgress uses scalar key data and never retains Hediff references.
 * Author: 0Shard
 * Description: Orchestrates hediff healing for Eternal pawns.
 *              Delegates to HediffHealingConfig for settings and TypeSpecificHealing for type-specific logic.
 *              Uses IHediffHealingCalculator for per-hediff rate, stage-based multipliers, and the debuff rate factor.
 *              11-07: maxSeverity scaling + 0.1x auto multiplier replaced by DEBUFF_RATE_FACTOR (Immortals parity).
 *              Accumulates food debt proportional to healing performed via IDebtAccumulator.
 *              Simplified healing check: only canHeal matters (enabled is always true for visibility).
 *              PERF-04: Uses HealingDictionaryKey struct keys to avoid string allocations in hot paths.
 *              05-02: Added SweepStaleEntries for periodic bounded cleanup of orphaned healing history entries (SAFE-07).
 */

using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Eternal.DI;
using Eternal.Extensions;
using Eternal.Exceptions;
using Eternal.Healing;
using Eternal.Infrastructure;
using Eternal.Interfaces;
using Eternal.Utils;
using Eternal.Utilities;

namespace Eternal
{
    /// <summary>
    /// Orchestrates hediff healing for Eternal pawns.
    /// Coordinates between settings, priority, and type-specific healing.
    /// </summary>
    public class EternalHediffHealer
    {
        // BUGFIX: Replaced cached field with dynamic property to ensure settings changes are picked up
        // private readonly EternalHediffManager hediffManager; // REMOVED - was causing stale reference bug

        // Gate 2: state keys contain only persistent scalar identity, never Hediff references.
        private readonly Dictionary<HealingDictionaryKey, float> healingProgress;
        private readonly Dictionary<LegacyProgressKey, List<float>> legacyHealingProgress
            = new Dictionary<LegacyProgressKey, List<float>>();
        private readonly EternalHediffSeverityTracker severityTracker;

        private readonly struct LegacyProgressKey : IEquatable<LegacyProgressKey>
        {
            public readonly int PawnThingIDNumber;
            public readonly string HediffDefName;
            public readonly string BodyPartLabel;

            public LegacyProgressKey(int pawnId, string defName, string partLabel)
            {
                PawnThingIDNumber = pawnId;
                HediffDefName = defName ?? string.Empty;
                BodyPartLabel = partLabel ?? string.Empty;
            }

            public LegacyProgressKey(Pawn pawn, Hediff hediff)
                : this(pawn.thingIDNumber, hediff.def.defName, hediff.Part?.Label)
            {
            }

            public bool Equals(LegacyProgressKey other)
            {
                return PawnThingIDNumber == other.PawnThingIDNumber
                    && HediffDefName == other.HediffDefName
                    && BodyPartLabel == other.BodyPartLabel;
            }

            public override bool Equals(object obj)
            {
                return obj is LegacyProgressKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = PawnThingIDNumber * 397;
                    hash ^= HediffDefName != null ? HediffDefName.GetHashCode() : 0;
                    return (hash * 397) ^ (BodyPartLabel != null ? BodyPartLabel.GetHashCode() : 0);
                }
            }
        }

        /// <summary>
        /// Gets the hediff manager dynamically from current settings.
        /// This ensures we always use the latest settings, even if modified during gameplay.
        /// Uses GetSettings() for guaranteed non-null access (SAFE-08).
        /// </summary>
        private EternalHediffManager HediffManager => Eternal_Mod.GetSettings().hediffManager;

        /// <summary>
        /// Gets the hediff healing calculator from the service container.
        /// Provides per-hediff rates, stage-based multipliers, and the debuff rate factor.
        /// </summary>
        private IHediffHealingCalculator HediffCalculator => EternalServiceContainer.Instance.HediffHealingCalculator;

        /// <summary>
        /// Gets the debt accumulator from the service container.
        /// Uses unified debt accumulator for consistent debt handling.
        /// </summary>
        private IDebtAccumulator DebtAccumulator => EternalServiceContainer.Instance.DebtAccumulator;

        /// <summary>
        /// Gets the food cost processor from the service container.
        /// Handles instant food drain and debt accumulation.
        /// </summary>
        private IFoodCostProcessor FoodCostProcessor => EternalServiceContainer.Instance.FoodCostProcessor;

        /// <summary>
        /// Initializes a new instance of EternalHediffHealer.
        /// </summary>
        public EternalHediffHealer()
        {
            // BUGFIX: Removed hediffManager initialization - now using dynamic property accessor
            // hediffManager = Eternal_Mod.settings?.hediffManager ?? new EternalHediffManager();

            // PERF-04: HealingDictionaryKey struct — no string allocation per tick
            healingProgress = new Dictionary<HealingDictionaryKey, float>();
            severityTracker = new EternalHediffSeverityTracker();

            EternalLogger.Info("EternalHediffHealer initialized");
        }

        #region Main Processing

        /// <summary>
        /// Processes hediff healing for a pawn.
        /// All eligible hediffs heal in parallel, with nutrition scaling the heal rate.
        /// </summary>
        public void ProcessHediffHealing(Pawn pawn)
        {
            if (pawn == null || pawn.health?.hediffSet == null)
                return;

            var healingItems = GetHealingItems(pawn);
            if (healingItems.Count == 0)
                return;

            ProcessHealingParallel(pawn, healingItems);
        }

        /// <summary>
        /// Gets all healing items for a pawn based on settings and eligibility rules.
        /// </summary>
        private List<HealingItem> GetHealingItems(Pawn pawn)
        {
            var healingItems = new List<HealingItem>();
            var allHediffs = pawn.health.hediffSet.hediffs;

            foreach (var hediff in allHediffs)
            {
                try
                {
                    if (hediff?.def == null)
                        continue;

                    // Never process the Eternal essence itself
                    if (hediff.def == EternalDefOf.Eternal_Essence)
                        continue;

                    // Never process the Metabolic Recovery hediff — its severity is driven
                    // by the debt tracker, not the healing system (single source of truth).
                    // Checked by type (09-02) AND by def (09-03 after DefOf binding is live).
                    if (hediff is Eternal.Hediffs.MetabolicRecovery_Hediff)
                        continue;
                    if (EternalDefOf.Eternal_MetabolicRecovery != null
                        && hediff.def == EternalDefOf.Eternal_MetabolicRecovery)
                        continue;

                    var setting = GetOrCreateSetting(hediff);

                    if (!HediffHealingConfig.ShouldHealByDefault(hediff, setting, pawn.Dead))
                        continue;

                    // Simplified: only check canHeal (enabled is now always true for visibility)
                    if (setting == null || !setting.canHeal)
                        continue;

                    var healingItem = EternalHealingPriority.CreateHealingItem(hediff, pawn);
                    if (healingItem != null)
                        healingItems.Add(healingItem);
                }
                catch (Exception ex)
                {
                    EternalLogger.HandleException(
                        EternalExceptionCategory.Resurrection,
                        "GetHealingItems.Hediff",
                        pawn,
                        ex);
                }
            }

            return healingItems;
        }

        /// <summary>
        /// Gets or creates a setting for a hediff.
        /// Uses dynamic property to ensure latest settings are always used.
        /// </summary>
        private EternalHediffSetting GetOrCreateSetting(Hediff hediff)
        {
            // BUGFIX: Use dynamic property instead of cached field
            var manager = HediffManager;
            if (manager == null)
                return HediffHealingConfig.CreateDefaultSetting(hediff);

            var existingSetting = manager.GetHediffSetting(hediff.def.defName);
            if (existingSetting != null)
                return existingSetting;

            // Use centralized config for defaults
            return HediffHealingConfig.CreateDefaultSetting(hediff);
        }

        #endregion

        #region Parallel Healing

        /// <summary>
        /// Processes healing for all eligible items in parallel.
        /// All hediffs heal at their configured rate (no nutrition throttling).
        /// </summary>
        private void ProcessHealingParallel(Pawn pawn, List<HealingItem> items)
        {
            foreach (var item in items)
            {
                if (item?.Hediff == null)
                    continue;

                try
                {
                    HealHediff(pawn, item);
                }
                catch (Exception ex)
                {
                    EternalLogger.HandleException(
                        EternalExceptionCategory.Resurrection,
                        "ProcessHealingParallel.Hediff",
                        pawn,
                        ex);
                }
            }
        }

        /// <summary>
        /// Heals a specific hediff based on its setting.
        /// Healing scales with:
        /// - Effective healing rate (per-hediff override OR global baseHealingRate)
        /// - Stage-based multiplier for debuff hediffs (higher severity = slower healing)
        /// - Body size (larger pawns heal faster to compensate for larger body parts)
        /// - Debuff rate factor (staged debuffs heal at DEBUFF_RATE_FACTOR for Immortals parity)
        ///
        /// Food cost is calculated from ACTUAL severity healed and the internal conversion, NOT healingAmount.
        /// </summary>
        private void HealHediff(Pawn pawn, HealingItem item)
        {
            if (pawn == null || item?.Hediff == null)
                return;

            var hediff = item.Hediff;
            var setting = GetOrCreateSetting(hediff);

            // Simplified: only check canHeal (enabled is now always true for visibility)
            if (setting == null || !setting.canHeal)
                return;

            // Calculate healing amount using unified calculator
            // Formula: effectiveRate × stageMultiplier × bodySize × debuffRateFactor
            float healingAmount = HediffCalculator?.CalculateHediffHealing(pawn, hediff, setting) ?? 0f;

            if (healingAmount <= 0f)
                return;

            // Check if we have capacity for debt before healing (prevents free healing at max debt)
            var foodDebtSystem = EternalServiceContainer.Instance.FoodDebtSystem;
            if (foodDebtSystem != null)
            {
                bool canDrainFood = FoodCostProcessor?.CanDrainFood(pawn) ?? false;
                bool hasDebtCapacity = foodDebtSystem.GetRemainingCapacity(pawn) > 0f;

                if (!canDrainFood && !hasDebtCapacity)
                {
                    // Max debt reached and no food to drain - skip healing
                    if (Eternal_Mod.settings?.debugMode == true)
                    {
                        EternalLogger.Debug($"Skipping heal for {pawn.Name?.ToStringShort}: max debt reached");
                    }
                    return;
                }
            }

            // BUGFIX: Capture severity BEFORE healing to calculate actual severity reduced
            float severityBefore = hediff.Severity;

            // Apply type-specific healing via centralized logic
            ApplyHealingWithTracking(pawn, item, healingAmount);

            // Food cost uses actual severity reduced, not healingAmount — type-specific
            // handlers (e.g. scars ×0.5) and severity clamps make them differ.
            float severityHealed = Math.Max(0f, severityBefore - hediff.Severity);

            // Process food cost based on ACTUAL severity healed and the shared internal ratio.
            if (severityHealed > 0f)
            {
                float severityToNutritionRatio = SettingsDefaults.SeverityToNutritionRatio;
                float nutritionCost = severityHealed * severityToNutritionRatio;
                FoodCostProcessor?.ProcessHealingCost(pawn, nutritionCost);

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    EternalLogger.Debug($"Food cost: severityHealed={severityHealed:F4}, nutritionCost={nutritionCost:F6}");
                }
            }

            // PERF-04: Track progress using HealingDictionaryKey struct (no string allocation)
            var key = new HealingDictionaryKey(pawn, hediff);
            healingProgress.TryGetValue(key, out float currentProgress);
            healingProgress[key] = currentProgress + healingAmount;

            // Debug logging
            if (Eternal_Mod.settings?.debugMode == true)
            {
                string rateSource = setting.HasCustomHealingRate ? "custom" : "global";
                float effectiveRate = HediffCalculator?.GetEffectiveRate(setting) ?? 0f;
                float stageMultiplier = HediffCalculator?.GetStageMultiplier(hediff) ?? 1f;
                float debuffFactor = hediff.IsDebuffWithStages() ? UnifiedHediffHealingCalculator.DEBUFF_RATE_FACTOR : 1f;
                EternalLogger.Debug($"Healed {pawn.Name?.ToStringShort ?? "Unknown"}'s {hediff.def.LabelCap} by {healingAmount:F3} " +
                    $"(rate: {effectiveRate:F3} [{rateSource}], stage: {hediff.CurStageIndex} [×{stageMultiplier:F1}], " +
                    $"bodySize: {pawn.BodySize:F2}, debuffFactor: {debuffFactor:F2})");
            }
        }

        /// <summary>
        /// Applies healing with severity tracking for stuck hediff detection.
        /// </summary>
        private void ApplyHealingWithTracking(Pawn pawn, HealingItem item, float healingAmount)
        {
            var hediff = item.Hediff;
            float severityBefore = hediff.Severity;

            // Apply type-specific healing
            TypeSpecificHealing.HealByType(pawn, item, healingAmount);

            float severityAfter = hediff.Severity;

            // Track healing attempt
            severityTracker.RecordHealingAttempt(pawn, hediff, severityBefore, severityAfter, healingAmount);

            // Check for stuck hediffs (RimWorld protection)
            if (severityTracker.IsHediffStuck(pawn, hediff))
            {
                pawn.health.RemoveHediff(hediff);
                severityTracker.ClearHediffTracking(pawn, hediff);
                EternalLogger.Info($"Force-removed protected hediff {hediff.def.LabelCap} from {pawn.Name?.ToStringShort ?? "Unknown"}");
            }
        }

        #endregion

        #region Live State Reconciliation

        /// <summary>
        /// Prunes progress and severity history against the current live HediffSet. Legacy
        /// progress rows are migrated only when exactly one current candidate exists; duplicate
        /// candidates discard the old value instead of assigning it arbitrarily.
        /// </summary>
        public void ReconcileLiveHealth(
            Pawn pawn,
            IEnumerable<Hediff> currentHediffs,
            ISet<HealingDictionaryKey> currentKeys)
        {
            if (pawn == null)
                return;

            var liveHediffs = new List<Hediff>();
            if (currentHediffs != null)
            {
                foreach (var hediff in currentHediffs)
                {
                    try
                    {
                        if (hediff?.def != null)
                            liveHediffs.Add(hediff);
                    }
                    catch (Exception ex)
                    {
                        EternalLogger.HandleException(
                            EternalExceptionCategory.Resurrection,
                            "ReconcileLiveHealth.ProgressInput",
                            pawn,
                            ex);
                    }
                }
            }

            MigrateLegacyProgress(pawn, liveHediffs);

            var staleKeys = new List<HealingDictionaryKey>();
            foreach (var key in healingProgress.Keys)
            {
                if (key.PawnThingIDNumber == pawn.thingIDNumber
                    && (currentKeys == null || !currentKeys.Contains(key)))
                {
                    staleKeys.Add(key);
                }
            }

            foreach (var staleKey in staleKeys)
                healingProgress.Remove(staleKey);

            severityTracker.ReconcileLiveHealth(pawn, liveHediffs, currentKeys);
        }

        private void MigrateLegacyProgress(Pawn pawn, IList<Hediff> liveHediffs)
        {
            var candidatesByLegacyKey = new Dictionary<LegacyProgressKey, List<Hediff>>();
            foreach (var hediff in liveHediffs)
            {
                var legacyKey = new LegacyProgressKey(pawn, hediff);
                if (!candidatesByLegacyKey.TryGetValue(legacyKey, out var candidates))
                {
                    candidates = new List<Hediff>();
                    candidatesByLegacyKey[legacyKey] = candidates;
                }
                candidates.Add(hediff);
            }

            var legacyKeysToRemove = new List<LegacyProgressKey>();
            foreach (var pair in legacyHealingProgress)
            {
                if (pair.Key.PawnThingIDNumber != pawn.thingIDNumber)
                    continue;

                legacyKeysToRemove.Add(pair.Key);
                if (pair.Value.Count != 1
                    || !candidatesByLegacyKey.TryGetValue(pair.Key, out var candidates)
                    || candidates.Count != 1)
                {
                    continue;
                }

                var liveKey = new HealingDictionaryKey(pawn, candidates[0]);
                if (!healingProgress.ContainsKey(liveKey))
                    healingProgress[liveKey] = pair.Value[0];
            }

            foreach (var key in legacyKeysToRemove)
                legacyHealingProgress.Remove(key);
        }

        #endregion

        #region Statistics

        /// <summary>
        /// Gets healing statistics for a pawn.
        /// </summary>
        public Dictionary<string, object> GetHealingStatistics(Pawn pawn)
        {
            var stats = new Dictionary<string, object>
            {
                ["Total Hediffs"] = pawn.health?.hediffSet?.hediffs?.Count ?? 0,
                ["Harmful Hediffs"] = pawn.health?.hediffSet?.hediffs?.Count(h => h.def.isBad) ?? 0,
                ["Healing Progress"] = GetPawnHealingProgress(pawn)
            };

            if (pawn.health?.hediffSet != null)
            {
                var criticalHediffs = pawn.health.hediffSet.hediffs
                    .Where(h => h.IsDefaultHarmful() && h.def.isBad)
                    .ToList();

                stats["Critical Harmful Hediffs"] = criticalHediffs.Count;
                stats["Critical Hediff Names"] = criticalHediffs.Select(h => h.def.LabelCap).ToList();
            }

            return stats;
        }

        /// <summary>
        /// Gets healing progress for a pawn.
        /// PERF-04: Uses HealingDictionaryKey.HediffDefName directly — no loadID-to-name lookup map needed.
        /// </summary>
        private Dictionary<string, float> GetPawnHealingProgress(Pawn pawn)
        {
            var progress = new Dictionary<string, float>();
            int pawnId = pawn.thingIDNumber;

            foreach (var kvp in healingProgress)
            {
                if (kvp.Key.PawnThingIDNumber == pawnId)
                {
                    // Key already carries the stable defName — no secondary lookup needed
                    progress[kvp.Key.HediffDefName] = kvp.Value;
                }
            }

            return progress;
        }

        #endregion

        #region Serialization

        /// <summary>
        /// Persists healing progress and severity history with the instance-aware identity.
        /// </summary>
        public void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                var pawnIds = new List<int>();
                var defNames = new List<string>();
                var partLabels = new List<string>();
                var hediffLoadIds = new List<int>();
                var progressValues = new List<float>();

                foreach (var pair in healingProgress)
                {
                    pawnIds.Add(pair.Key.PawnThingIDNumber);
                    defNames.Add(pair.Key.HediffDefName);
                    partLabels.Add(pair.Key.BodyPartLabel);
                    hediffLoadIds.Add(pair.Key.HediffLoadID);
                    progressValues.Add(pair.Value);
                }

                foreach (var pair in legacyHealingProgress)
                {
                    foreach (var progress in pair.Value)
                    {
                        pawnIds.Add(pair.Key.PawnThingIDNumber);
                        defNames.Add(pair.Key.HediffDefName);
                        partLabels.Add(pair.Key.BodyPartLabel);
                        hediffLoadIds.Add(-1);
                        progressValues.Add(progress);
                    }
                }

                Scribe_Collections.Look(ref pawnIds, "healingProgress_pawnIds", LookMode.Value);
                Scribe_Collections.Look(ref defNames, "healingProgress_defNames", LookMode.Value);
                Scribe_Collections.Look(ref partLabels, "healingProgress_partLabels", LookMode.Value);
                Scribe_Collections.Look(ref hediffLoadIds, "healingProgress_hediffLoadIds", LookMode.Value);
                Scribe_Collections.Look(ref progressValues, "healingProgress_values", LookMode.Value);
                severityTracker.ExposeData();
                return;
            }

            if (Scribe.mode != LoadSaveMode.LoadingVars)
                return;

            List<int> loadedPawnIds = null;
            List<string> loadedDefNames = null;
            List<string> loadedPartLabels = null;
            List<int> loadedHediffLoadIds = null;
            List<float> loadedProgressValues = null;

            Scribe_Collections.Look(ref loadedPawnIds, "healingProgress_pawnIds", LookMode.Value);
            Scribe_Collections.Look(ref loadedDefNames, "healingProgress_defNames", LookMode.Value);
            Scribe_Collections.Look(ref loadedPartLabels, "healingProgress_partLabels", LookMode.Value);
            Scribe_Collections.Look(ref loadedHediffLoadIds, "healingProgress_hediffLoadIds", LookMode.Value);
            Scribe_Collections.Look(ref loadedProgressValues, "healingProgress_values", LookMode.Value);

            healingProgress.Clear();
            legacyHealingProgress.Clear();

            bool scalarListsValid = loadedPawnIds != null
                && loadedDefNames != null
                && loadedPartLabels != null
                && loadedProgressValues != null
                && loadedPawnIds.Count == loadedDefNames.Count
                && loadedPawnIds.Count == loadedPartLabels.Count
                && loadedPawnIds.Count == loadedProgressValues.Count;
            bool identityListsValid = scalarListsValid
                && loadedHediffLoadIds != null
                && loadedHediffLoadIds.Count == loadedPawnIds.Count;

            if (scalarListsValid)
            {
                for (int i = 0; i < loadedPawnIds.Count; i++)
                {
                    if (identityListsValid && loadedHediffLoadIds[i] >= 0)
                    {
                        healingProgress[new HealingDictionaryKey(
                            loadedPawnIds[i],
                            loadedDefNames[i],
                            loadedPartLabels[i],
                            loadedHediffLoadIds[i])] = loadedProgressValues[i];
                    }
                    else
                    {
                        var legacyKey = new LegacyProgressKey(
                            loadedPawnIds[i], loadedDefNames[i], loadedPartLabels[i]);
                        if (!legacyHealingProgress.TryGetValue(legacyKey, out var values))
                        {
                            values = new List<float>();
                            legacyHealingProgress[legacyKey] = values;
                        }
                        values.Add(loadedProgressValues[i]);
                    }
                }
            }

            severityTracker.ExposeData();
        }

        #endregion

        #region Cleanup

        /// <summary>
        /// Clears healing progress for all pawns.
        /// </summary>
        public void ClearHealingProgress()
        {
            healingProgress.Clear();
            legacyHealingProgress.Clear();
            severityTracker.ClearAllTracking();
            EternalLogger.Info("EternalHediffHealer healing progress cleared");
        }

        /// <summary>
        /// Clears healing progress for a specific pawn.
        /// PERF-04: Uses integer PawnThingIDNumber comparison instead of string StartsWith.
        /// </summary>
        public void ClearPawnHealingProgress(Pawn pawn)
        {
            if (pawn == null)
                return;

            int pawnId = pawn.thingIDNumber;
            var keysToRemove = new List<HealingDictionaryKey>();

            foreach (var key in healingProgress.Keys)
            {
                if (key.PawnThingIDNumber == pawnId)
                {
                    keysToRemove.Add(key);
                }
            }

            foreach (var key in keysToRemove)
            {
                healingProgress.Remove(key);
            }

            var legacyKeysToRemove = new List<LegacyProgressKey>();
            foreach (var key in legacyHealingProgress.Keys)
            {
                if (key.PawnThingIDNumber == pawnId)
                    legacyKeysToRemove.Add(key);
            }

            foreach (var key in legacyKeysToRemove)
                legacyHealingProgress.Remove(key);

            severityTracker.ClearPawnTracking(pawn);
        }

        /// <summary>
        /// Performs periodic cleanup of old tracking data.
        /// </summary>
        public void PerformPeriodicCleanup()
        {
            severityTracker.PerformPeriodicCleanup();
        }

        /// <summary>
        /// Sweeps stale healing history entries for pawns that are no longer alive on any map
        /// and are not under active corpse healing.
        /// Called periodically by TickOrchestrator (every HealingHistorySweepInterval ticks).
        /// Pitfall 5 guard: corpseIds set prevents removing entries for dead pawns mid-resurrection.
        /// </summary>
        /// <param name="livePawnIds">ThingIDNumbers of all pawns currently spawned on any map</param>
        /// <param name="activeCorpseIds">ThingIDNumbers of pawns whose corpses are actively tracked</param>
        public void SweepStaleEntries(HashSet<int> livePawnIds, HashSet<int> activeCorpseIds)
        {
            if (healingProgress.Count == 0)
                return;

            var staleIds = new HashSet<int>();

            foreach (var key in healingProgress.Keys)
            {
                int id = key.PawnThingIDNumber;
                // Stale = not alive on any map AND not an active corpse being healed
                if (!livePawnIds.Contains(id) && !activeCorpseIds.Contains(id))
                {
                    staleIds.Add(id);
                }
            }

            if (staleIds.Count == 0)
                return;

            // Remove all stale keys from healingProgress
            var keysToRemove = new List<HealingDictionaryKey>();
            foreach (var key in healingProgress.Keys)
            {
                if (staleIds.Contains(key.PawnThingIDNumber))
                {
                    keysToRemove.Add(key);
                }
            }

            foreach (var key in keysToRemove)
            {
                healingProgress.Remove(key);
            }

            // Also clear severity tracker for the same stale pawn IDs
            foreach (int staleId in staleIds)
            {
                severityTracker.ClearPawnTrackingById(staleId);
            }

            Log.Message($"[Eternal] Swept {staleIds.Count} stale healing history entries ({keysToRemove.Count} keys removed)");
        }

        #endregion

        #region Emergency Healing

        /// <summary>
        /// Forces immediate healing of all harmful hediffs for a pawn.
        /// </summary>
        public void EmergencyHealAllHarmfulHediffs(Pawn pawn)
        {
            if (pawn?.health?.hediffSet == null)
                return;

            var harmfulHediffs = pawn.health.hediffSet.hediffs
                .Where(h => h.def.isBad
                         && h.def != EternalDefOf.Eternal_Essence
                         && !(h is Eternal.Hediffs.MetabolicRecovery_Hediff)
                         && (EternalDefOf.Eternal_MetabolicRecovery == null
                             || h.def != EternalDefOf.Eternal_MetabolicRecovery))
                .ToList();

            foreach (var hediff in harmfulHediffs)
            {
                pawn.health.RemoveHediff(hediff);
            }

            EternalLogger.Info($"Emergency healed {harmfulHediffs.Count} harmful hediffs for {pawn.Name?.ToStringShort ?? "Unknown"}");
        }

        #endregion
    }
}
