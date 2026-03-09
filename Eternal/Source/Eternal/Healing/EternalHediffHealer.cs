/*
 * Relative Path: Eternal/Source/Eternal/Healing/EternalHediffHealer.cs
 * Creation Date: 09-11-2025
 * Last Edit: 04-03-2026
 *              BUGFIX: Fixed food cost calculation to use actual severity healed instead of scaled healingAmount.
 *              Previously, food cost was multiplied by severityScaling (body part HP, e.g., 30 for torso),
 *              causing food to drain ~30x faster than intended. Now uses 250:1 ratio on actual severity reduced.
 *              PERF-04: Replaced local HealingProgressKey struct with HealingDictionaryKey from Infrastructure.
 *              HealingProgressKey removed — it used loadID (session-scoped, unstable across saves).
 *              HealingDictionaryKey uses defName+partLabel (stable) with three-field global uniqueness.
 *              GetPawnHealingProgress simplified: uses key.HediffDefName directly, no loadID lookup map.
 * Author: 0Shard
 * Description: Orchestrates hediff healing for Eternal pawns.
 *              Delegates to HediffHealingConfig for settings and TypeSpecificHealing for type-specific logic.
 *              Uses IHediffHealingCalculator for per-hediff rate, stage-based multipliers, and severity scaling.
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

        // PERF-04: Use HealingDictionaryKey struct (defName+partLabel) instead of HealingProgressKey (loadID)
        private readonly Dictionary<HealingDictionaryKey, float> healingProgress;
        private readonly EternalHediffSeverityTracker severityTracker;

        /// <summary>
        /// Gets the hediff manager dynamically from current settings.
        /// This ensures we always use the latest settings, even if modified during gameplay.
        /// Uses GetSettings() for guaranteed non-null access (SAFE-08).
        /// </summary>
        private EternalHediffManager HediffManager => Eternal_Mod.GetSettings().hediffManager;

        /// <summary>
        /// Gets the hediff healing calculator from the service container.
        /// Provides per-hediff rates, stage-based multipliers, and severity scaling.
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
                if (hediff == null)
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

                // Check eligibility using centralized config
                if (!HediffHealingConfig.ShouldHealByDefault(hediff, setting, pawn.Dead))
                    continue;

                // Simplified: only check canHeal (enabled is now always true for visibility)
                if (setting == null || !setting.canHeal)
                    continue;

                var healingItem = EternalHealingPriority.CreateHealingItem(hediff, pawn);
                if (healingItem != null)
                {
                    healingItems.Add(healingItem);
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
            foreach (var item in items.Where(i => i?.Hediff != null))
            {
                HealHediff(pawn, item);
            }
        }

        /// <summary>
        /// Heals a specific hediff based on its setting.
        /// Healing scales with:
        /// - Effective healing rate (per-hediff override OR global baseHealingRate)
        /// - Stage-based multiplier for debuff hediffs (higher severity = slower healing)
        /// - Body size (larger pawns heal faster to compensate for larger body parts)
        /// - Max severity scaling (injuries scale by body part HP, infections by maxSeverity)
        ///
        /// Food cost is calculated from ACTUAL severity healed (configurable ratio), NOT the scaled healingAmount.
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
            // Formula: effectiveRate × stageMultiplier × bodySize × severityScaling
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

            // BUGFIX: Calculate actual severity reduced (not the scaled healingAmount)
            // healingAmount includes severityScaling (body part HP, e.g., 30 for torso)
            // which was causing food to drain ~30x faster than intended
            float severityHealed = Math.Max(0f, severityBefore - hediff.Severity);

            // Process food cost based on ACTUAL severity healed (configurable ratio, default 250:1)
            if (severityHealed > 0f)
            {
                float severityToNutritionRatio = EternalServiceContainer.Instance?.Settings?.SeverityToNutritionRatio ?? 0.004f;
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
                float severityScaling = HediffCalculator?.GetSeverityScaling(hediff, pawn) ?? 1f;
                EternalLogger.Debug($"Healed {pawn.Name?.ToStringShort ?? "Unknown"}'s {hediff.def.LabelCap} by {healingAmount:F3} " +
                    $"(rate: {effectiveRate:F3} [{rateSource}], stage: {hediff.CurStageIndex} [×{stageMultiplier:F1}], " +
                    $"bodySize: {pawn.BodySize:F2}, severityScale: {severityScaling:F1})");
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

        #region Cleanup

        /// <summary>
        /// Clears healing progress for all pawns.
        /// </summary>
        public void ClearHealingProgress()
        {
            healingProgress.Clear();
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
