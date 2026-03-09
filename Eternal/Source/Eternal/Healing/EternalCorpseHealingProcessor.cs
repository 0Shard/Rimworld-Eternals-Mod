/*
 * Relative Path: Eternal/Source/Eternal/Healing/EternalCorpseHealingProcessor.cs
 * Creation Date: 09-11-2025
 * Last Edit: 04-03-2026
 * Author: 0Shard
 * Description: Processes healing and regrowth on Eternal corpses, accumulating debt and managing resurrection completion.
 *              Integrates with EternalRegrowthState for proper 4-phase body part regrowth.
 *              Restores PawnAssignmentSnapshot after resurrection for work priority/policy preservation.
 *              Now uses unified IHealingRateCalculator and IDebtAccumulator for consistency.
 *              PERF-01: Replaced ListPool<T>.Get()/Return() with pre-allocated instance-level buffers.
 *              Eliminates per-tick lock contention (ListPool uses a lock on every Get/Return) and
 *              heap allocations from new List<Pawn>() in CleanupInvalidCorpses().
 *              Fixed: Regrowth debt formula changed to proportional (was using 0.01f throttle).
 *              Fixed: GetActiveHealingCorpses now validates OriginalPawn is not null.
 *              Split into ProcessCorpseInjuryHealing (normalTickRate) and ProcessCorpseRegrowth (rareTickRate)
 *              for healing speed parity with living pawns.
 *              Enhanced: IsHealingComplete now verifies actual pawn state (missing parts, regrowth status).
 *              Fixed: Added Part sync after resurrection (Immortals pattern) - hediff.Part = hediff.forPart.
 *              Fixed: Fallback regrowth now heals actual hediff.Severity, not HealingItem.Severity.
 *              03-02: All catch sites converted to EternalLogger.HandleException with correct categories.
 *              03-04: CompleteResurrection and ResurrectImmediately use try/finally + swapActive flag.
 *                     AttemptHediffRestore helper: retry once then re-kill — no hybrid-state pawn possible.
 *              24-02: WorkTab compatibility fix in CompleteResurrection and ResurrectImmediately:
 *                     TryResurrect is now wrapped in try/catch. If WorkTab's SetPriority transpiler throws
 *                     IndexOutOfRangeException inside EnableAndInitialize, but pawn.Dead==false (resurrection
 *                     already completed — Notify_Resurrected sets healthState=Mobile BEFORE EnableAndInitialize),
 *                     the exception is swallowed with a warning and post-work continues. This eliminates the
 *                     AttemptHediffRestore false-alarm on every resurrection with WorkTab loaded.
 *              SAFE-04: FindCaravanContainingCorpse uses CaravanId fast-path before linear scan.
 *              SAFE-05: CompleteResurrection and ResurrectImmediately transfer food debt after resurrection;
 *                       debt exceeding 5× max nutrition triggers permanent death with red letter + full cleanup.
 *              RC5-FIX: All pawn.Name.Named("PAWN") calls changed to pawn.Named("PAWN") so GrammarResolverSimple
 *                       receives the Pawn object and correctly generates PAWN_nameFull, PAWN_pronoun, etc.
 *                       Passing pawn.Name (a Name object) caused grammar token resolution to fail silently.
 *              09-03: Apply Metabolic Recovery hediff to corpse during StartCorpseHealing for debt visibility.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using Eternal.Corpse;
using Eternal.DI;
using Eternal.Exceptions;
using Eternal.Interfaces;
using Eternal.Resources;
using Eternal.Models;
using Eternal.Utils;
using Eternal.Utilities;
using RimWorld.Planet;

// Type alias for backwards compatibility
using EternalCorpseData = Eternal.Models.CorpseTrackingEntry;

namespace Eternal.Healing
{
    /// <summary>
    /// Processes healing and regrowth on Eternal corpses.
    /// Manages debt accumulation during corpse healing and handles resurrection completion.
    /// Uses unified IHealingRateCalculator and IDebtAccumulator for consistent healing logic.
    /// </summary>
    public class EternalCorpseHealingProcessor
    {
        private readonly EternalResurrectionCalculator resurrectionCalculator;
        private readonly HashSet<Pawn> activeHealingCorpses = new HashSet<Pawn>();

        // PERF-01: Pre-allocated buffers — reused every call, zero allocation after construction.
        // Sized conservatively for typical Eternal counts; List<T> auto-grows if exceeded (rare).
        // RimWorld game logic is single-threaded — no locks required.
        private readonly List<EternalCorpseData> _corpseBuffer      = new List<EternalCorpseData>(4);
        private readonly List<Pawn>              _toRemoveBuffer    = new List<Pawn>(4);
        private readonly List<HealingItem>       _nonRegrowthBuffer = new List<HealingItem>(16);
        private readonly List<HealingItem>       _regrowthBuffer    = new List<HealingItem>(8);
        private readonly List<HealingItem>       _completedBuffer   = new List<HealingItem>(16);
        private readonly List<Pawn>              _cleanupBuffer     = new List<Pawn>(4);

        // Unified services (injected via service container)
        private IHealingRateCalculator RateCalculator => EternalServiceContainer.Instance.RateCalculator;
        private IDebtAccumulator DebtAccumulator => EternalServiceContainer.Instance.DebtAccumulator;
        private IPartRestorer PartRestorer => EternalServiceContainer.Instance.PartRestorer;
        private IFoodDebtSystem DebtSystem => EternalServiceContainer.Instance.FoodDebtSystem;
        private EternalCorpseManager CorpseManager => EternalServiceContainer.Instance.CorpseManager;
        private IFoodCostProcessor FoodCostProcessor => EternalServiceContainer.Instance.FoodCostProcessor;

        public EternalCorpseHealingProcessor()
        {
            resurrectionCalculator = new EternalResurrectionCalculator();
        }

        /// <summary>
        /// Processes all healing for active Eternal corpses (both injuries and regrowth).
        /// This is the legacy combined method - calls both injury healing and regrowth.
        /// Kept for backwards compatibility.
        /// </summary>
        public void ProcessCorpseHealing()
        {
            ProcessCorpseInjuryHealing();
            ProcessCorpseRegrowth();
        }

        /// <summary>
        /// Processes injury/disease healing for all active Eternal corpses.
        /// Called at normalTickRate (60 ticks) for parity with living pawn injury healing.
        /// Handles hediffs like injuries, diseases, and scars - NOT body part regrowth.
        /// PERF: Uses pooled lists to eliminate allocations per tick.
        /// </summary>
        public void ProcessCorpseInjuryHealing()
        {
            // PERF-01: Use pre-allocated instance buffers — zero allocation on the hot path.
            _corpseBuffer.Clear();
            _toRemoveBuffer.Clear();

            try
            {
                foreach (var corpseData in GetActiveHealingCorpses())
                {
                    _corpseBuffer.Add(corpseData);
                }

                foreach (var corpseData in _corpseBuffer)
                {
                    try
                    {
                        ProcessCorpseInjuryTick(corpseData);

                        // Check if healing is complete (no more items to heal)
                        if (IsHealingComplete(corpseData))
                        {
                            CompleteResurrection(corpseData);
                            _toRemoveBuffer.Add(corpseData.OriginalPawn);
                        }
                    }
                    catch (Exception ex)
                    {
                        EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                            "ProcessCorpseInjuryTick", corpseData.OriginalPawn, ex);
                        _toRemoveBuffer.Add(corpseData.OriginalPawn);
                    }
                }

                foreach (var pawn in _toRemoveBuffer)
                {
                    activeHealingCorpses.Remove(pawn);
                }

                CleanupInvalidCorpses();
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                    "ProcessCorpseInjuryHealing", null, ex);
            }
        }

        /// <summary>
        /// Processes body part regrowth for all active Eternal corpses.
        /// Called at rareTickRate (250 ticks) matching living pawn regrowth rate.
        /// Handles 4-phase body part regrowth system only.
        /// PERF: Uses pooled lists to eliminate allocations per tick.
        /// </summary>
        public void ProcessCorpseRegrowth()
        {
            // PERF-01: Use pre-allocated instance buffers — zero allocation on the hot path.
            // Safe to share _corpseBuffer/_toRemoveBuffer with ProcessCorpseInjuryHealing because
            // these methods run on different tick intervals (normalTickRate vs rareTickRate).
            _corpseBuffer.Clear();
            _toRemoveBuffer.Clear();

            try
            {
                foreach (var corpseData in GetActiveHealingCorpses())
                {
                    _corpseBuffer.Add(corpseData);
                }

                foreach (var corpseData in _corpseBuffer)
                {
                    try
                    {
                        ProcessCorpseRegrowthTick(corpseData);

                        // Check if healing is complete (no more items to heal)
                        if (IsHealingComplete(corpseData))
                        {
                            CompleteResurrection(corpseData);
                            _toRemoveBuffer.Add(corpseData.OriginalPawn);
                        }
                    }
                    catch (Exception ex)
                    {
                        EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                            "ProcessCorpseRegrowthTick", corpseData.OriginalPawn, ex);
                        _toRemoveBuffer.Add(corpseData.OriginalPawn);
                    }
                }

                foreach (var pawn in _toRemoveBuffer)
                {
                    activeHealingCorpses.Remove(pawn);
                }

                CleanupInvalidCorpses();
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                    "ProcessCorpseRegrowth", null, ex);
            }
        }

        /// <summary>
        /// Starts healing process for a corpse.
        /// </summary>
        /// <param name="corpseData">The corpse data to start healing for</param>
        /// <returns>True if healing started successfully</returns>
        public bool StartCorpseHealing(EternalCorpseData corpseData)
        {
            try
            {
                if (corpseData == null || corpseData.OriginalPawn == null)
                {
                    Log.Warning("[Eternal] Cannot start healing - corpse data or pawn is null");
                    return false;
                }

                if (corpseData.IsHealingActive)
                {
                    Log.Warning($"[Eternal] Healing already active for {corpseData.OriginalPawn.Name}");
                    return false;
                }

                // Calculate healing queue if not already calculated
                if (corpseData.HealingQueue == null || corpseData.HealingQueue.Count == 0)
                {
                    // Use pre-calculated healing queue if available (captured at death before RimWorld removes injuries)
                    if (corpseData.PreCalculatedHealingQueue != null && corpseData.PreCalculatedHealingQueue.Count > 0)
                    {
                        corpseData.HealingQueue = new List<HealingItem>(corpseData.PreCalculatedHealingQueue);

                        if (Eternal_Mod.settings?.debugMode == true)
                        {
                            Log.Message($"[Eternal] Using pre-calculated healing queue for {corpseData.OriginalPawn.Name}: {corpseData.HealingQueue.Count} items");
                        }
                    }
                    else
                    {
                        // Fallback: calculate now (may miss injuries if RimWorld already removed them)
                        corpseData.HealingQueue = resurrectionCalculator.CalculateHealingQueue(corpseData.OriginalPawn);

                        if (Eternal_Mod.settings?.debugMode == true)
                        {
                            Log.Warning($"[Eternal] No pre-calculated queue for {corpseData.OriginalPawn.Name}, calculating now: {corpseData.HealingQueue?.Count ?? 0} items (injuries may be missing)");
                        }
                    }

                    // Use capped cost to prevent excessive debt (max 2.0 × pawn nutrition)
                    corpseData.TotalHealingCost = resurrectionCalculator.CalculateTotalCostCapped(
                        corpseData.HealingQueue, corpseData.OriginalPawn);
                }

                if (corpseData.HealingQueue.Count == 0)
                {
                    // Nothing to heal - resurrect immediately
                    ResurrectImmediately(corpseData);
                    return true;
                }

                // Start healing
                corpseData.IsHealingActive = true;
                corpseData.HealingStartTick = Find.TickManager.TicksGame;
                corpseData.HealingProgress = 0f;

                // Register for debt monitoring (dead pawn)
                DebtSystem.RegisterPawn(corpseData.OriginalPawn);

                // Apply Metabolic Recovery hediff to corpse so debt is visible during healing.
                // Uses same approach as Eternal_Regrowing which is successfully added to corpses.
                // This hedge is excluded from the healing system — severity is debt-driven only.
                try
                {
                    var pawnRef = corpseData.OriginalPawn;
                    if (pawnRef.health?.hediffSet != null
                        && EternalDefOf.Eternal_MetabolicRecovery != null
                        && pawnRef.health.hediffSet.GetFirstHediffOfDef(EternalDefOf.Eternal_MetabolicRecovery) == null)
                    {
                        var metabolicHediff = HediffMaker.MakeHediff(EternalDefOf.Eternal_MetabolicRecovery, pawnRef);
                        pawnRef.health.AddHediff(metabolicHediff);
                    }
                }
                catch (Exception ex)
                {
                    EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                        "AddMetabolicRecoveryToCorpse", corpseData.OriginalPawn, ex);
                }

                // Initialize 4-phase regrowth system for missing body parts
                var regrowthManager = Eternal_Component.Current?.RegrowthManager;
                if (regrowthManager != null)
                {
                    // Check if there are any regrowth items (missing body parts)
                    bool hasRegrowthItems = corpseData.HealingQueue.Any(item => item.Type == HealingType.Regrowth);
                    if (hasRegrowthItems)
                    {
                        regrowthManager.StartRegrowth(corpseData.OriginalPawn);
                        if (Eternal_Mod.settings?.debugMode == true)
                        {
                            Log.Message($"[Eternal] Initialized 4-phase regrowth system for {corpseData.OriginalPawn.Name}");
                        }
                    }
                }

                // Add to active healing tracking
                activeHealingCorpses.Add(corpseData.OriginalPawn);

                Log.Message($"[Eternal] Started corpse healing for {corpseData.OriginalPawn.Name} - {corpseData.HealingQueue.Count} items");

                return true;
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                    "StartCorpseHealing", corpseData?.OriginalPawn, ex);
                return false;
            }
        }

        /// <summary>
        /// Processes a single injury healing tick for a corpse.
        /// Handles non-regrowth items only (injuries, diseases, scars).
        /// Called at normalTickRate (60 ticks) for parity with living pawn injury healing.
        /// PERF: Uses pooled lists to eliminate allocations.
        /// </summary>
        /// <param name="corpseData">The corpse data to process.</param>
        private void ProcessCorpseInjuryTick(EternalCorpseData corpseData)
        {
            if (corpseData?.HealingQueue == null || corpseData.HealingQueue.Count == 0)
                return;

            float totalHeal = CalculateHealingPerTick(corpseData.OriginalPawn);

            // Get resurrection cost cap for this pawn (2.0 × nutrition capacity)
            float resurrectionCostCap = resurrectionCalculator.GetResurrectionCostCap(corpseData.OriginalPawn);

            // Check if debt has reached resurrection cap - stop healing if maxed out
            float currentDebt = DebtSystem?.GetDebt(corpseData.OriginalPawn) ?? 0f;
            if (currentDebt >= resurrectionCostCap)
            {
                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Corpse injury healing paused for {corpseData.OriginalPawn?.Name}: debt at resurrection cap ({currentDebt:F1}/{resurrectionCostCap:F1})");
                }
                return;
            }

            // PERF-01: Use pre-allocated instance buffers — zero allocation on the hot path.
            _nonRegrowthBuffer.Clear();
            _completedBuffer.Clear();

            // Collect non-regrowth items only (injuries, diseases, scars)
            foreach (var item in corpseData.HealingQueue)
            {
                if (item == null) continue;

                if (item.Type != HealingType.Regrowth && item.Severity > 0.01f)
                {
                    _nonRegrowthBuffer.Add(item);
                }
            }

            if (_nonRegrowthBuffer.Count == 0)
                return;

            // Count regrowth items for heal distribution (they get a share even though not processed here)
            int regrowthCount = 0;
            foreach (var item in corpseData.HealingQueue)
            {
                if (item != null && item.Type == HealingType.Regrowth)
                    regrowthCount++;
            }

            // Distribute healing across all items (injuries get processed, regrowth share is skipped)
            float perItemHeal = totalHeal / (_nonRegrowthBuffer.Count + (regrowthCount > 0 ? 1 : 0));

            foreach (var item in _nonRegrowthBuffer)
            {
                float before = item.Severity;
                float healAmount = Math.Min(perItemHeal, before);
                if (healAmount <= 0f)
                    continue;

                item.Severity -= healAmount;

                // Debt accumulation proportional to healing performed
                float fraction = before > 0f ? (healAmount / before) : 1f;
                float debtCost = fraction * item.EnergyCost;
                AddDebtCapped(corpseData.OriginalPawn, debtCost, resurrectionCostCap);

                if (item.Severity <= 0.01f)
                {
                    _completedBuffer.Add(item);
                    ApplyHealingEffect(corpseData, item);
                }
            }

            corpseData.FoodDebt = DebtSystem.GetDebt(corpseData.OriginalPawn);

            foreach (var completed in _completedBuffer)
            {
                corpseData.HealingQueue.Remove(completed);
            }

            UpdateHealingProgress(corpseData);
        }

        /// <summary>
        /// Processes a single regrowth tick for a corpse.
        /// Handles regrowth items only (missing body parts via 4-phase system).
        /// Called at rareTickRate (250 ticks) matching living pawn regrowth rate.
        /// PERF: Uses pooled lists to eliminate allocations.
        /// </summary>
        /// <param name="corpseData">The corpse data to process.</param>
        private void ProcessCorpseRegrowthTick(EternalCorpseData corpseData)
        {
            if (corpseData?.HealingQueue == null || corpseData.HealingQueue.Count == 0)
                return;

            float totalHeal = CalculateHealingPerTick(corpseData.OriginalPawn);

            // Get resurrection cost cap for this pawn (2.0 × nutrition capacity)
            float resurrectionCostCap = resurrectionCalculator.GetResurrectionCostCap(corpseData.OriginalPawn);

            // Check if debt has reached resurrection cap - stop healing if maxed out
            float currentDebt = DebtSystem?.GetDebt(corpseData.OriginalPawn) ?? 0f;
            if (currentDebt >= resurrectionCostCap)
            {
                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Corpse regrowth paused for {corpseData.OriginalPawn?.Name}: debt at resurrection cap ({currentDebt:F1}/{resurrectionCostCap:F1})");
                }
                return;
            }

            // PERF-01: Use pre-allocated instance buffers — zero allocation on the hot path.
            // Safe to share _completedBuffer with ProcessCorpseInjuryTick because these methods
            // run on different tick intervals (normalTickRate vs rareTickRate) — never concurrent.
            _regrowthBuffer.Clear();
            _completedBuffer.Clear();

            // Collect regrowth items only
            foreach (var item in corpseData.HealingQueue)
            {
                if (item != null && item.Type == HealingType.Regrowth)
                {
                    _regrowthBuffer.Add(item);
                }
            }

            if (_regrowthBuffer.Count == 0)
                return;

            // Count non-regrowth items for heal distribution
            int nonRegrowthCount = 0;
            foreach (var item in corpseData.HealingQueue)
            {
                if (item != null && item.Type != HealingType.Regrowth && item.Severity > 0.01f)
                    nonRegrowthCount++;
            }

            var regrowthManager = Eternal_Component.Current?.RegrowthManager;
            var regrowthState = regrowthManager?.GetRegrowthState(corpseData.OriginalPawn);

            if (regrowthState != null)
            {
                // Calculate healing amount for regrowth (shares pool with injuries)
                float regrowthHeal = totalHeal / (nonRegrowthCount + 1);

                // Apply healing to the regrowth system (handles 4-phase progression internally)
                regrowthState.ApplyHealing(regrowthHeal);

                // Accumulate debt for regrowth healing, capped at resurrection cost limit
                float totalRegrowthCost = 0f;
                foreach (var item in _regrowthBuffer)
                {
                    totalRegrowthCost += item.EnergyCost;
                }
                float regrowthDebtPerTick = regrowthHeal * totalRegrowthCost;
                AddDebtCapped(corpseData.OriginalPawn, regrowthDebtPerTick, resurrectionCostCap);

                // Check if regrowth is complete (all body parts restored)
                if (regrowthState.IsRegrowthComplete())
                {
                    foreach (var item in _regrowthBuffer)
                    {
                        _completedBuffer.Add(item);
                    }

                    regrowthManager.RemoveCompletedRegrowth(corpseData.OriginalPawn);

                    if (Eternal_Mod.settings?.debugMode == true)
                    {
                        Log.Message($"[Eternal] 4-phase regrowth complete for {corpseData.OriginalPawn.Name}");
                    }
                }
            }
            else
            {
                // Fallback: regrowthState is null, meaning no regrowing hediffs exist.
                // This shouldn't happen if StartCorpseHealing() ran correctly.
                // Try to recover by starting regrowth (adding hediffs) and healing them directly.

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Warning($"[Eternal] Corpse regrowth fallback triggered for {corpseData.OriginalPawn?.Name}: " +
                               $"No regrowing hediffs found but {_regrowthBuffer.Count} regrowth items in queue. Attempting recovery.");
                }

                // Try to start regrowth (adds hediffs if missing)
                regrowthManager?.StartRegrowth(corpseData.OriginalPawn);

                // Now try to get the regrowing hediffs directly and heal them
                var pawnRegrowingHediffs = corpseData.OriginalPawn?.health?.hediffSet?.hediffs
                    .OfType<EternalRegrowing_Hediff>()
                    .ToList();

                if (pawnRegrowingHediffs != null && pawnRegrowingHediffs.Count > 0)
                {
                    // Heal the actual hediffs on the pawn (not HealingItem.Severity)
                    float perHediffHeal = totalHeal / pawnRegrowingHediffs.Count;
                    foreach (var hediff in pawnRegrowingHediffs)
                    {
                        if (hediff == null || hediff.Severity >= 1.0f)
                            continue;

                        float beforeSeverity = hediff.Severity;
                        hediff.Severity += perHediffHeal;

                        // Accumulate debt for regrowth healing
                        float totalRegrowthCost = 0f;
                        foreach (var item in _regrowthBuffer)
                        {
                            totalRegrowthCost += item.EnergyCost;
                        }
                        float debtPerHeal = (perHediffHeal / pawnRegrowingHediffs.Count) * totalRegrowthCost;
                        AddDebtCapped(corpseData.OriginalPawn, debtPerHeal, resurrectionCostCap);

                        if (Eternal_Mod.settings?.debugMode == true)
                        {
                            Log.Message($"[Eternal] Fallback healed regrowth hediff: " +
                                       $"{hediff.forPart?.Label ?? "unknown"} {beforeSeverity:F4} -> {hediff.Severity:F4}");
                        }

                        // Check for completion
                        if (hediff.Severity >= 1.0f)
                        {
                            // Mark corresponding HealingItems as complete
                            foreach (var item in _regrowthBuffer)
                            {
                                _completedBuffer.Add(item);
                            }
                            regrowthManager?.RemoveCompletedRegrowth(corpseData.OriginalPawn);
                        }
                    }
                }
                else
                {
                    // No hediffs could be created - log error but don't crash
                    Log.Error($"[Eternal] Failed to create regrowing hediffs for {corpseData.OriginalPawn?.Name}. " +
                             $"Marking {_regrowthBuffer.Count} regrowth items as complete to prevent infinite loop.");
                    foreach (var item in _regrowthBuffer)
                    {
                        _completedBuffer.Add(item);
                    }
                }
            }

            corpseData.FoodDebt = DebtSystem.GetDebt(corpseData.OriginalPawn);

            foreach (var completed in _completedBuffer)
            {
                corpseData.HealingQueue.Remove(completed);
            }

            UpdateHealingProgress(corpseData);
        }

        /// <summary>
        /// Calculates the amount of healing per tick using the unified calculator.
        /// Delegates to IHealingRateCalculator for consistent healing logic across the codebase.
        /// </summary>
        /// <param name="pawn">The pawn being healed (for body size scaling)</param>
        /// <returns>Healing amount per tick</returns>
        private float CalculateHealingPerTick(Pawn pawn)
        {
            // Use unified calculator for consistent healing across all contexts
            return RateCalculator?.CalculateHealingPerTick(pawn) ?? 0.00001f;
        }

        /// <summary>
        /// Adds debt to a pawn, capped at the resurrection cost limit.
        /// Delegates to IDebtAccumulator for consistent debt logic.
        /// </summary>
        /// <param name="pawn">The pawn to add debt to</param>
        /// <param name="amount">The amount of debt to add</param>
        /// <param name="costCap">The maximum allowed debt (resurrection cost cap)</param>
        private void AddDebtCapped(Pawn pawn, float amount, float costCap)
        {
            // Use unified debt accumulator with resurrection cap
            DebtAccumulator?.AddDebtWithResurrectionCap(pawn, amount, costCap);
        }

        /// <summary>
        /// Applies the effect of a completed healing item.
        /// </summary>
        /// <param name="corpseData">The corpse data</param>
        /// <param name="healingItem">The completed healing item</param>
        private void ApplyHealingEffect(EternalCorpseData corpseData, HealingItem healingItem)
        {
            try
            {
                if (healingItem.Type == HealingType.Regrowth && healingItem.Hediff is Hediff_MissingPart missingPart)
                {
                    // For regrowth, we'll handle this during resurrection
                    // Missing parts will be restored when the pawn is resurrected
                    return;
                }

                // For other healing types, the effects are applied during resurrection
                // The healing queue completion indicates readiness for resurrection

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Completed healing {healingItem.Type} for {corpseData.OriginalPawn.Name}");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                    "ApplyHealingEffect", corpseData?.OriginalPawn, ex);
            }
        }

        /// <summary>
        /// Updates healing progress for a corpse.
        /// </summary>
        /// <param name="corpseData">The corpse data to update</param>
        private void UpdateHealingProgress(EternalCorpseData corpseData)
        {
            try
            {
                if (corpseData.HealingQueue == null)
                    return;

                // Calculate progress based on remaining items
                int totalItems = corpseData.HealingQueue.Count + (corpseData.TotalHealingCost > 0 ? 1 : 0); // Account for completed items
                int remainingItems = corpseData.HealingQueue.Count;

                if (totalItems > 0)
                {
                    corpseData.HealingProgress = 1f - ((float)remainingItems / totalItems);
                }
                else
                {
                    corpseData.HealingProgress = 1f;
                }

                // Update corpse component progress
                var component = corpseData.Corpse?.GetComp<EternalCorpseComponent>();
                if (component != null)
                {
                    // Trigger progress update
                    component.CompTick();
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                    "UpdateHealingProgress", corpseData?.OriginalPawn, ex);
            }
        }

        /// <summary>
        /// Checks if healing is complete for a corpse.
        /// Verifies both queue emptiness AND actual pawn state.
        /// </summary>
        /// <param name="corpseData">The corpse data to check</param>
        /// <returns>True if healing is complete and pawn is ready for resurrection</returns>
        private bool IsHealingComplete(EternalCorpseData corpseData)
        {
            // Basic queue check
            if (corpseData?.HealingQueue == null)
                return true;

            if (corpseData.HealingQueue.Count > 0)
                return false;

            // Verify pawn state
            var pawn = corpseData.OriginalPawn;
            if (pawn?.health?.hediffSet == null)
                return false;

            // Verify no missing body parts remain
            var missingParts = pawn.health.hediffSet.GetMissingPartsCommonAncestors();
            if (missingParts != null && missingParts.Any())
            {
                if (Eternal_Mod.settings?.debugMode == true)
                {
                    var partNames = string.Join(", ", missingParts.Select(p => p.Part?.def?.defName ?? "unknown"));
                    Log.Message($"[Eternal] IsHealingComplete: {pawn.Name} still has missing parts: {partNames}");
                }
                return false;
            }

            // Verify regrowth system confirms completion
            var regrowthManager = EternalServiceContainer.Instance?.RegrowthManager;
            if (regrowthManager != null && regrowthManager.IsPawnInRegrowth(pawn))
            {
                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] IsHealingComplete: {pawn.Name} still has active regrowth");
                }
                return false;
            }

            return true;
        }

        /// <summary>
        /// Validates that resurrection can proceed safely.
        /// </summary>
        private bool ValidatePreResurrection(Pawn pawn, EternalCorpseData corpseData)
        {
            // Check pawn is actually dead
            if (!pawn.Dead)
            {
                Log.Warning($"[Eternal] Skipping resurrection - {pawn.Name} is already alive");
                CleanupCorpseData(corpseData);
                return false;
            }

            // Check corpse exists and not destroyed
            if (corpseData.Corpse == null || corpseData.Corpse.Destroyed)
            {
                Log.Error($"[Eternal] Cannot resurrect - corpse for {pawn.Name} is null or destroyed");
                CleanupCorpseData(corpseData);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Cleans up corpse data when resurrection cannot proceed.
        /// </summary>
        private void CleanupCorpseData(EternalCorpseData corpseData)
        {
            if (corpseData?.OriginalPawn != null)
            {
                activeHealingCorpses.Remove(corpseData.OriginalPawn);
                CorpseManager?.UnregisterCorpse(corpseData.OriginalPawn);
            }
        }

        /// <summary>
        /// Finds the caravan containing a corpse, if any.
        /// SAFE-04: Fast path — if corpseData.CaravanId is set, resolves directly via WorldObject.ID lookup
        /// (O(n) over typically &lt;10 caravans) instead of scanning all world objects with AllThings.Contains.
        /// Falls back to the full linear scan if CaravanId is -1 (old saves, non-caravan deaths).
        /// </summary>
        private RimWorld.Planet.Caravan FindCaravanContainingCorpse(Verse.Corpse corpse)
        {
            if (corpse == null || corpse.Tile < 0)
                return null;

            // SAFE-04: Fast path via persisted CaravanId — avoids AllThings.Contains per caravan.
            // GetCorpseData lookup is O(1); Caravans list is typically tiny (<10 entries).
            var corpseOwner = corpse.InnerPawn;
            if (corpseOwner != null)
            {
                var corpseData = CorpseManager?.GetCorpseData(corpseOwner);
                if (corpseData != null && corpseData.CaravanId != -1)
                {
                    var found = Find.WorldObjects.Caravans.FirstOrDefault(c => c.ID == corpseData.CaravanId);
                    if (found != null)
                        return found;
                    // CaravanId set but caravan dissolved — fall through to linear scan below.
                }
            }

            // Fallback: linear scan (old saves, or CaravanId lookup returned null).
            foreach (var worldObject in Find.World.worldObjects.AllWorldObjects)
            {
                if (worldObject is RimWorld.Planet.Caravan caravan && caravan.AllThings.Contains(corpse))
                {
                    return caravan;
                }
            }
            return null;
        }

        /// <summary>
        /// Completes the resurrection process for a fully healed corpse.
        /// Body parts should already be restored by the 4-phase regrowth system.
        /// Uses HediffSet swap pattern from Immortals mod to preserve custom hediffs.
        /// SAFE-03: Atomic swap via try/finally + swapActive flag — HediffSet is always restored
        /// even when third-party Harmony postfixes throw during TryResurrect.
        /// </summary>
        /// <param name="corpseData">The corpse data to resurrect</param>
        private void CompleteResurrection(EternalCorpseData corpseData)
        {
            if (corpseData?.OriginalPawn == null || corpseData.Corpse == null)
            {
                Log.Error("[Eternal] Cannot complete resurrection - corpse data or corpse is null");
                return;
            }

            var pawn = corpseData.OriginalPawn;

            // Pre-resurrection validation
            if (!ValidatePreResurrection(pawn, corpseData))
                return;

            // === SAFE-05: Capture healing debt BEFORE UnregisterPawn (which zeroes the tracker) ===
            // corpseData.FoodDebt is synced by SyncCorpseData() on every AddDebt/RepayDebt call —
            // it is current even after save/load (persisted via Scribe_Values).
            float healingDebt = corpseData.FoodDebt;
            float absoluteLimit = DebtSystem.GetMaxCapacity(pawn);

            // Permanent death check: if accumulated debt exceeds 5× max nutrition the pawn cannot
            // survive another resurrection. Send a red-bordered letter and abort with full cleanup.
            if (healingDebt > absoluteLimit)
            {
                Find.LetterStack.ReceiveLetter(
                    "EternalPermanentDeath".Translate(),
                    "EternalPermanentDeathDesc".Translate(
                        pawn.Named("PAWN"),
                        healingDebt.Named("DEBT"),
                        absoluteLimit.Named("LIMIT")),
                    LetterDefOf.ThreatBig);

                // Full cleanup — leaves no orphaned entries (Pitfall 4 from RESEARCH.md).
                activeHealingCorpses.Remove(pawn);
                CorpseManager?.UnregisterCorpse(pawn);
                DebtSystem.UnregisterPawn(pawn);

                Log.Error($"[Eternal] Permanent death for {pawn.Name}: healing debt {healingDebt:F2} exceeds absolute limit {absoluteLimit:F2}");
                return;
            }

            // === Save HediffSet and ImmunityHandler (Immortals pattern) ===
            // Captured before the try block so the finally block can always access them.
            var savedHediffSet = pawn.health.hediffSet;
            var savedImmunity = pawn.health.immunity;

            // swapActive is true between swap-out and swap-in.
            // If any exception escapes while swapActive, the finally block calls
            // AttemptHediffRestore to guarantee the pawn never stays alive without Eternal hediffs.
            bool swapActive = false;

            // Caravan reference captured before the swap so post-work can use it even if
            // swap-phase code is never reached (ValidatePreResurrection already guards this path).
            RimWorld.Planet.Caravan pawnCaravan = null;

            try
            {
                // === Detect caravan/storage corpses ===
                if (!corpseData.Corpse.Spawned)
                {
                    pawnCaravan = FindCaravanContainingCorpse(corpseData.Corpse);
                    if (Eternal_Mod.settings?.debugMode == true && pawnCaravan != null)
                    {
                        Log.Message($"[Eternal] Detected corpse in caravan: {pawnCaravan.Label}");
                    }
                }

                // === Create clean slate for resurrection ===
                // RimWorld's TryResurrect expects a clean health state.
                pawn.health.hediffSet = new HediffSet(pawn);
                pawn.health.immunity = new ImmunityHandler(pawn);
                swapActive = true; // HediffSet is now swapped out — finally must restore it on any throw

                // Apply healing effects (remove harmful hediffs like injuries and diseases)
                ApplyCompleteHealingEffects(corpseData);

                // Resurrect the pawn.
                // Third-party Harmony transpilers/postfixes (e.g. WorkTab) may throw inside
                // TryResurrect. Critically, Pawn_HealthTracker.Notify_Resurrected sets healthState
                // to Mobile BEFORE EnableAndInitialize is called — so if WorkTab crashes during
                // EnableAndInitialize, the pawn is already alive. We treat that as a soft failure:
                // log a warning, clear swapActive, and continue post-work normally.
                // If the pawn is still dead after the exception, it is a hard failure — let the
                // outer catch and finally handle it (AttemptHediffRestore).
                try
                {
                    ResurrectionUtility.TryResurrect(pawn, null);
                }
                catch (Exception tryResurrectEx)
                {
                    if (!pawn.Dead)
                    {
                        // Resurrection succeeded (pawn is alive) despite the exception.
                        // WorkTab or another mod threw in a Harmony postfix/transpiler inside
                        // TryResurrect after Notify_Resurrected already set healthState=Mobile.
                        // This is safe — log warning and continue post-work normally.
                        Log.Warning($"[Eternal:CompleteResurrection] TryResurrect threw but pawn {pawn.Name} " +
                            $"is alive — treating as success (third-party Harmony exception swallowed). " +
                            $"Exception: {tryResurrectEx.GetType().Name}: {tryResurrectEx.Message}");
                    }
                    else
                    {
                        // Pawn is still dead — hard failure. Re-throw so the outer catch handles it
                        // and the finally block calls AttemptHediffRestore with swapActive=true.
                        throw;
                    }
                }

                // === Restore HediffSet and Immunity ===
                // This preserves Eternal_Essence, food debt tracking, and other custom hediffs.
                pawn.health.hediffSet = savedHediffSet;
                pawn.health.immunity = savedImmunity;
                swapActive = false; // Swap complete — post-work exceptions no longer need atomic rollback

                // === Post-resurrection work ===
                // swapActive is false here: hediffs are restored, so exceptions below are
                // caught by the catch block and logged without triggering AttemptHediffRestore.

                // === Sync Part property on regrowing hediffs (Immortals pattern) ===
                // After resurrection, the Part property must be synced with forPart.
                var regrowingHediffsToSync = pawn.health?.hediffSet?.hediffs
                    .OfType<EternalRegrowing_Hediff>()
                    .ToList();

                if (regrowingHediffsToSync != null)
                {
                    foreach (var regrowthHediff in regrowingHediffsToSync)
                    {
                        if (regrowthHediff.forPart != null && regrowthHediff.Part == null)
                        {
                            regrowthHediff.Part = regrowthHediff.forPart;

                            if (Eternal_Mod.settings?.debugMode == true)
                            {
                                Log.Message($"[Eternal] Synced Part property for regrowing hediff on {regrowthHediff.forPart.Label}");
                            }
                        }
                    }
                }

                // === Re-add to caravan if was in one ===
                if (pawnCaravan != null)
                {
                    // Remove corpse from carrier's inventory first
                    var carrier = CaravanInventoryUtility.GetOwnerOf(pawnCaravan, corpseData.Corpse);
                    carrier?.inventory?.innerContainer?.Remove(corpseData.Corpse);

                    // Add pawn back to caravan
                    pawnCaravan.AddPawn(pawn, false);

                    if (Eternal_Mod.settings?.debugMode == true)
                    {
                        Log.Message($"[Eternal] Re-added {pawn.Name} to caravan {pawnCaravan.Label}");
                    }
                }

                // Restore work priorities, policies, and schedule AFTER resurrection.
                // ResurrectionUtility.TryResurrect calls pawn.workSettings.EnableAndInitialize()
                // which RESETS all work priorities, so we must restore after.
                if (corpseData.AssignmentSnapshot != null)
                {
                    corpseData.AssignmentSnapshot.ApplyTo(pawn);

                    if (Eternal_Mod.settings?.debugMode == true)
                    {
                        Log.Message($"[Eternal] Restored work assignments for resurrected pawn {pawn.Name}");
                    }
                }

                // Note: Body parts are restored progressively by the 4-phase regrowth system
                // (ProcessCorpseInjuryHealing / ProcessCorpseRegrowth), not instantly here.
                // Only do fallback restoration if regrowth system was unavailable.
                var regrowthManager = Eternal_Component.Current?.RegrowthManager;
                if (regrowthManager == null || !regrowthManager.IsPawnInRegrowth(pawn))
                {
                    // Fallback: restore any remaining missing parts if regrowth system wasn't used
                    RestoreMissingBodyParts(pawn, corpseData);
                }

                // Clean up regrowth state if it still exists
                regrowthManager?.RemoveCompletedRegrowth(pawn);

                // Unregister from corpse tracking
                CorpseManager?.UnregisterCorpse(pawn);

                // Unregister from debt monitoring (pawn is now alive).
                // IMPORTANT: UnregisterPawn zeroes the tracker — healingDebt was captured above,
                // before this call, so the transfer below uses the correct pre-unregister value.
                DebtSystem.UnregisterPawn(pawn);

                // Re-register for debt monitoring as living pawn
                DebtSystem.RegisterPawn(pawn);

                // === SAFE-05: Transfer accumulated healing debt to the now-living pawn ===
                // Additive transfer — old unpaid debt (from prior deaths) + new healing debt.
                if (healingDebt > 0f)
                {
                    DebtSystem.AddDebt(pawn, healingDebt);
                }

                // Log completion with transferred debt amount
                Log.Message($"[Eternal] Resurrection completed for {pawn.Name}. Transferred debt: {healingDebt:F2}");

                // Show notification
                Find.LetterStack.ReceiveLetter(
                    "EternalResurrectionComplete".Translate(),
                    "EternalResurrectionCompleteDesc".Translate(pawn.Named("PAWN"), healingDebt.Named("DEBT")),
                    LetterDefOf.PositiveEvent,
                    pawn);
            }
            catch (Exception ex)
            {
                if (!swapActive)
                {
                    // Hediffs already restored — exception occurred in post-work. Log and continue.
                    EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                        "CompleteResurrection.PostWork", pawn, ex);
                }
                // If swapActive is still true, the finally block below calls AttemptHediffRestore.
            }
            finally
            {
                if (swapActive)
                {
                    // Swap was active when the exception escaped — restore hediffs atomically.
                    AttemptHediffRestore(pawn, savedHediffSet, savedImmunity, corpseData);
                }
            }
        }

        /// <summary>
        /// Attempts to restore the saved HediffSet and ImmunityHandler after a swap failure.
        /// Retries once. If both attempts fail, re-kills the pawn so Eternal_Hediff.Notify_PawnDied()
        /// re-registers the corpse and re-enters the resurrection pipeline cleanly.
        /// This ensures no pawn is ever left alive without Eternal hediffs (SAFE-03).
        /// </summary>
        private void AttemptHediffRestore(Pawn pawn, HediffSet savedSet,
            ImmunityHandler savedImmunity, EternalCorpseData corpseData)
        {
            // Attempt 1 — restore the saved HediffSet
            try
            {
                pawn.health.hediffSet = savedSet;
                pawn.health.immunity = savedImmunity;
                EternalLogger.HandleException(EternalExceptionCategory.HediffSwap,
                    "AttemptHediffRestore.RecoveredOnAttempt1", pawn, null);
                return; // Success
            }
            catch (Exception ex1)
            {
                EternalLogger.HandleException(EternalExceptionCategory.HediffSwap,
                    "AttemptHediffRestore.Attempt1Failed", pawn, ex1);
            }

            // Attempt 2 — retry once
            try
            {
                pawn.health.hediffSet = savedSet;
                pawn.health.immunity = savedImmunity;
                EternalLogger.HandleException(EternalExceptionCategory.HediffSwap,
                    "AttemptHediffRestore.RecoveredOnAttempt2", pawn, null);
                return; // Success
            }
            catch (Exception ex2)
            {
                EternalLogger.HandleException(EternalExceptionCategory.HediffSwap,
                    "AttemptHediffRestore.Attempt2Failed.ReKilling", pawn, ex2);
            }

            // Last resort — re-kill pawn to re-enter resurrection pipeline cleanly.
            // Eternal_Hediff.Notify_PawnDied() fires → corpse is re-registered.
            try
            {
                if (!pawn.Dead)
                {
                    pawn.Kill(null);
                    // Eternal_Hediff.Notify_PawnDied() fires → re-registers corpse
                }
                else if (pawn.Corpse != null)
                {
                    // Still dead — register corpse directly so the pipeline can retry
                    var container = EternalServiceContainer.Instance;
                    container?.CorpseManager?.RegisterCorpse(pawn.Corpse, pawn,
                        corpseData?.AssignmentSnapshot,
                        corpseData?.PreCalculatedHealingQueue);
                }
            }
            catch (Exception killEx)
            {
                // Absolute last resort — bare Log.Error intentional here.
                // This fallback must not depend on HandleException's coalescing for visibility.
                Log.Error($"[Eternal:HediffSwap] CRITICAL: Re-kill fallback failed for " +
                    $"{pawn?.Name?.ToStringFull ?? "unknown"}. Pawn may be in hybrid state. " +
                    $"Exception: {killEx.Message}\n{killEx.StackTrace}");
            }
        }

        /// <summary>
        /// Resurrects a pawn immediately when no healing is needed.
        /// Uses HediffSet swap pattern from Immortals mod to preserve custom hediffs.
        /// SAFE-03: Atomic swap via try/finally + swapActive flag — HediffSet is always restored
        /// even when third-party Harmony postfixes throw during TryResurrect.
        /// </summary>
        /// <param name="corpseData">The corpse data to resurrect</param>
        private void ResurrectImmediately(EternalCorpseData corpseData)
        {
            if (corpseData?.OriginalPawn == null || corpseData.Corpse == null)
            {
                Log.Error("[Eternal] Cannot resurrect immediately - corpse data or corpse is null");
                return;
            }

            var pawn = corpseData.OriginalPawn;

            // Pre-resurrection validation
            if (!ValidatePreResurrection(pawn, corpseData))
                return;

            // === SAFE-05: Capture healing debt BEFORE any unregister call that zeroes the tracker ===
            // Even in the immediate-resurrection path (no prior healing), corpseData.FoodDebt may
            // be non-zero if the pawn carried debt from a previous death cycle.
            float healingDebt = corpseData.FoodDebt;
            float absoluteLimit = DebtSystem.GetMaxCapacity(pawn);

            // Permanent death check mirrors CompleteResurrection — immediate resurrections are not
            // exempt from the 5× cap. Debt earned in prior healing cycles accumulates additively.
            if (healingDebt > absoluteLimit)
            {
                Find.LetterStack.ReceiveLetter(
                    "EternalPermanentDeath".Translate(),
                    "EternalPermanentDeathDesc".Translate(
                        pawn.Named("PAWN"),
                        healingDebt.Named("DEBT"),
                        absoluteLimit.Named("LIMIT")),
                    LetterDefOf.ThreatBig);

                // Full cleanup — leaves no orphaned entries (Pitfall 4 from RESEARCH.md).
                activeHealingCorpses.Remove(pawn);
                CorpseManager?.UnregisterCorpse(pawn);
                DebtSystem.UnregisterPawn(pawn);

                Log.Error($"[Eternal] Permanent death (immediate) for {pawn.Name}: healing debt {healingDebt:F2} exceeds absolute limit {absoluteLimit:F2}");
                return;
            }

            // === Save HediffSet and ImmunityHandler (Immortals pattern) ===
            // Captured before the try block so the finally block can always access them.
            var savedHediffSet = pawn.health.hediffSet;
            var savedImmunity = pawn.health.immunity;

            // swapActive is true between swap-out and swap-in.
            // If any exception escapes while swapActive, the finally block calls
            // AttemptHediffRestore to guarantee the pawn never stays alive without Eternal hediffs.
            bool swapActive = false;

            RimWorld.Planet.Caravan pawnCaravan = null;

            try
            {
                // === Detect caravan/storage corpses ===
                if (!corpseData.Corpse.Spawned)
                {
                    pawnCaravan = FindCaravanContainingCorpse(corpseData.Corpse);
                    if (Eternal_Mod.settings?.debugMode == true && pawnCaravan != null)
                    {
                        Log.Message($"[Eternal] Detected corpse in caravan for immediate resurrection: {pawnCaravan.Label}");
                    }
                }

                // === Create clean slate for resurrection ===
                // RimWorld's TryResurrect expects a clean health state.
                pawn.health.hediffSet = new HediffSet(pawn);
                pawn.health.immunity = new ImmunityHandler(pawn);
                swapActive = true; // HediffSet is now swapped out — finally must restore it on any throw

                // Resurrect the pawn.
                // Same WorkTab-compatibility guard as CompleteResurrection:
                // Notify_Resurrected sets healthState=Mobile BEFORE EnableAndInitialize is called,
                // so if WorkTab crashes during EnableAndInitialize the pawn is already alive.
                // Treat that as a soft failure: log warning, clear swapActive, continue post-work.
                try
                {
                    ResurrectionUtility.TryResurrect(pawn, null);
                }
                catch (Exception tryResurrectEx)
                {
                    if (!pawn.Dead)
                    {
                        // Resurrection succeeded (pawn is alive) despite the exception.
                        Log.Warning($"[Eternal:ResurrectImmediately] TryResurrect threw but pawn {pawn.Name} " +
                            $"is alive — treating as success (third-party Harmony exception swallowed). " +
                            $"Exception: {tryResurrectEx.GetType().Name}: {tryResurrectEx.Message}");
                    }
                    else
                    {
                        // Pawn is still dead — hard failure.
                        throw;
                    }
                }

                // === Restore HediffSet and Immunity ===
                // This preserves Eternal_Essence, food debt tracking, and other custom hediffs.
                pawn.health.hediffSet = savedHediffSet;
                pawn.health.immunity = savedImmunity;
                swapActive = false; // Swap complete — post-work exceptions no longer need atomic rollback

                // === Post-resurrection work ===
                // swapActive is false here: hediffs are restored, so exceptions below are
                // caught by the catch block and logged without triggering AttemptHediffRestore.

                // === Sync Part property on regrowing hediffs (Immortals pattern) ===
                // After resurrection, the Part property must be synced with forPart.
                var regrowingHediffsToSync = pawn.health?.hediffSet?.hediffs
                    .OfType<EternalRegrowing_Hediff>()
                    .ToList();

                if (regrowingHediffsToSync != null)
                {
                    foreach (var regrowthHediff in regrowingHediffsToSync)
                    {
                        if (regrowthHediff.forPart != null && regrowthHediff.Part == null)
                        {
                            regrowthHediff.Part = regrowthHediff.forPart;

                            if (Eternal_Mod.settings?.debugMode == true)
                            {
                                Log.Message($"[Eternal] Synced Part property for regrowing hediff on {regrowthHediff.forPart.Label}");
                            }
                        }
                    }
                }

                // === Re-add to caravan if was in one ===
                if (pawnCaravan != null)
                {
                    // Remove corpse from carrier's inventory first
                    var carrier = CaravanInventoryUtility.GetOwnerOf(pawnCaravan, corpseData.Corpse);
                    carrier?.inventory?.innerContainer?.Remove(corpseData.Corpse);

                    // Add pawn back to caravan
                    pawnCaravan.AddPawn(pawn, false);

                    if (Eternal_Mod.settings?.debugMode == true)
                    {
                        Log.Message($"[Eternal] Re-added {pawn.Name} to caravan {pawnCaravan.Label}");
                    }
                }

                // Restore work priorities, policies, and schedule AFTER resurrection.
                // ResurrectionUtility.TryResurrect calls pawn.workSettings.EnableAndInitialize()
                // which RESETS all work priorities, so we must restore after.
                if (corpseData.AssignmentSnapshot != null)
                {
                    corpseData.AssignmentSnapshot.ApplyTo(pawn);

                    if (Eternal_Mod.settings?.debugMode == true)
                    {
                        Log.Message($"[Eternal] Restored work assignments for immediately resurrected pawn {pawn.Name}");
                    }
                }

                // Unregister from corpse tracking
                CorpseManager?.UnregisterCorpse(pawn);

                // Register for debt monitoring as living pawn.
                // Note: UnregisterPawn is NOT called here because ResurrectImmediately runs when there was
                // nothing to heal — the pawn was never registered as a corpse debtor in StartCorpseHealing.
                DebtSystem.RegisterPawn(pawn);

                // === SAFE-05: Transfer any accumulated healing debt to the now-living pawn ===
                if (healingDebt > 0f)
                {
                    DebtSystem.AddDebt(pawn, healingDebt);
                }

                Log.Message($"[Eternal] Immediately resurrected {pawn.Name}. Transferred debt: {healingDebt:F2}");

                // Show notification
                Find.LetterStack.ReceiveLetter(
                    "EternalResurrectionComplete".Translate(),
                    "EternalResurrectionImmediateDesc".Translate(pawn.Named("PAWN")),
                    LetterDefOf.PositiveEvent,
                    pawn);
            }
            catch (Exception ex)
            {
                if (!swapActive)
                {
                    // Hediffs already restored — exception occurred in post-work. Log and continue.
                    EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                        "ResurrectImmediately.PostWork", pawn, ex);
                }
                // If swapActive is still true, the finally block below calls AttemptHediffRestore.
            }
            finally
            {
                if (swapActive)
                {
                    // Swap was active when the exception escaped — restore hediffs atomically.
                    AttemptHediffRestore(pawn, savedHediffSet, savedImmunity, corpseData);
                }
            }
        }

        /// <summary>
        /// Applies complete healing effects to a pawn before resurrection.
        /// </summary>
        /// <param name="corpseData">The corpse data with healing information</param>
        private void ApplyCompleteHealingEffects(EternalCorpseData corpseData)
        {
            try
            {
                var pawn = corpseData.OriginalPawn;
                if (pawn?.health == null)
                    return;

                // Remove all harmful hediffs that were being healed
                var hediffsToRemove = new List<Hediff>();

                foreach (var hediff in pawn.health.hediffSet.hediffs)
                {
                    if (hediff == null || hediff.def == EternalDefOf.Eternal_Essence)
                        continue;

                    if (EternalHealingPriority.IsHarmfulHediff(hediff))
                    {
                        hediffsToRemove.Add(hediff);
                    }
                }

                // Remove the harmful hediffs
                foreach (var hediff in hediffsToRemove)
                {
                    pawn.health.RemoveHediff(hediff);
                }

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Message($"[Eternal] Removed {hediffsToRemove.Count} harmful hediffs from {pawn.Name}");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                    "ApplyCompleteHealingEffects", corpseData?.OriginalPawn, ex);
            }
        }

        /// <summary>
        /// Fallback method to restore missing body parts to a resurrected pawn.
        /// Only used when the 4-phase regrowth system was not available.
        /// Uses UnifiedPartRestorer for consistent restoration logic.
        /// Note: This does NOT call pawn.health.Reset() as that would wipe all hediffs.
        /// </summary>
        /// <param name="pawn">The pawn to restore parts to</param>
        /// <param name="corpseData">The corpse data with regrowth information</param>
        private void RestoreMissingBodyParts(Pawn pawn, EternalCorpseData corpseData)
        {
            try
            {
                if (pawn?.health == null)
                    return;

                // Use unified part restorer if available
                if (PartRestorer != null)
                {
                    PartRestorer.RestoreAllMissingParts(pawn);
                    return;
                }

                // Fallback to direct restoration if service container not available
                var missingParts = pawn.health.hediffSet.GetMissingPartsCommonAncestors().ToList();

                foreach (var missingPart in missingParts)
                {
                    if (missingPart.Part != null)
                    {
                        pawn.health.RestorePart(missingPart.Part);

                        if (Eternal_Mod.settings?.debugMode == true)
                        {
                            Log.Message($"[Eternal] Fallback restored missing body part: {missingPart.Part?.def?.defName}");
                        }
                    }
                }

                // Note: We intentionally do NOT call pawn.health.Reset() here
                // as that would wipe all hediffs including Eternal_Essence.
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                    "RestoreMissingBodyParts", pawn, ex);
            }
        }

        /// <summary>
        /// Gets all corpses currently active in healing.
        /// </summary>
        /// <returns>Collection of active healing corpse data</returns>
        private IEnumerable<EternalCorpseData> GetActiveHealingCorpses()
        {
            return (CorpseManager?.GetAllCorpses() ?? Enumerable.Empty<EternalCorpseData>())
                .Where(corpse => corpse != null && corpse.OriginalPawn != null && corpse.IsHealingActive);
        }

        /// <summary>
        /// Cleans up invalid corpse entries.
        /// </summary>
        private void CleanupInvalidCorpses()
        {
            // PERF-01: Use pre-allocated instance buffer — eliminates per-call heap allocation.
            _cleanupBuffer.Clear();

            foreach (var pawn in activeHealingCorpses)
            {
                var corpseData = CorpseManager?.GetCorpseData(pawn);
                if (corpseData == null || !corpseData.IsHealingActive)
                {
                    _cleanupBuffer.Add(pawn);
                }
            }

            foreach (var pawn in _cleanupBuffer)
            {
                activeHealingCorpses.Remove(pawn);
            }
        }

        /// <summary>
        /// Gets the healing status for a specific corpse.
        /// </summary>
        /// <param name="pawn">The pawn to get status for</param>
        /// <returns>Dictionary containing healing status information</returns>
        public Dictionary<string, object> GetHealingStatus(Pawn pawn)
        {
            var status = new Dictionary<string, object>();

            try
            {
                var corpseData = CorpseManager?.GetCorpseData(pawn);
                if (corpseData == null)
                {
                    status["Error"] = "Corpse data not found";
                    return status;
                }

                status["IsHealingActive"] = corpseData.IsHealingActive;
                status["HealingProgress"] = corpseData.HealingProgress;
                status["ItemsRemaining"] = corpseData.HealingQueue?.Count ?? 0;
                status["TotalCost"] = corpseData.TotalHealingCost;
                status["CurrentDebt"] = corpseData.FoodDebt;
                status["HealingStartTick"] = corpseData.HealingStartTick;

                if (corpseData.HealingStartTick > 0)
                {
                    int elapsedTicks = Find.TickManager.TicksGame - corpseData.HealingStartTick;
                    status["ElapsedTime"] = elapsedTicks / GenDate.TicksPerDay;
                }

                // Add debt status
                status["Debt_Status"] = DebtSystem.GetDebtStatusString(pawn);
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                    "GetHealingStatus", pawn, ex);
                status["Error"] = ex.Message;
            }

            return status;
        }

        /// <summary>
        /// Forces completion of healing for a corpse (debug/emergency use).
        /// </summary>
        /// <param name="pawn">The pawn to complete healing for</param>
        public void ForceCompleteHealing(Pawn pawn)
        {
            try
            {
                var corpseData = CorpseManager?.GetCorpseData(pawn);
                if (corpseData == null || !corpseData.IsHealingActive)
                {
                    Log.Warning($"[Eternal] Cannot force complete healing - no active healing for {pawn.Name}");
                    return;
                }

                // Clear healing queue
                corpseData.HealingQueue.Clear();
                corpseData.HealingProgress = 1f;

                // Complete resurrection
                CompleteResurrection(corpseData);

                Log.Message($"[Eternal] Force completed healing for {pawn.Name}");
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                    "ForceCompleteHealing", pawn, ex);
            }
        }
    }
}
