/*
 * Relative Path: Eternal/Source/Eternal/Healing/EternalCorpseHealingProcessor.cs
 * Creation Date: 09-11-2025
 * Last Edit: 13-10-2026
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
 *              11-07: Parallel healing model (parity with living pawns): per-item full rate, no shared-pool
 *                     division; regrowth debt is effort-based like the living path; fallback only starts
 *                     regrowth (primary path heals it next pass with HP-scaled progression).
 *              12-07: Resurrection debt is uncapped (AddResurrectionDebt raises the baseline); permanent
 *                     death on debt removed — resurrection always completes and transfers full debt.
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
 *              26-03: Added SanitizeHediffSetBeforeRestore — removes stale regrowth hediffs, MetabolicRecovery,
 *                     and residual injuries from saved HediffSet BEFORE swap-back (RSRV-01/02/03).
 *              14-07: Both TryResurrect calls wrapped with Mark/UnmarkExpectedDestruction so the
 *                     Corpse_Destroy_Patch anti-vaporize guard lets resurrection consume the
 *                     still-tracked corpse (UnregisterCorpse runs AFTER TryResurrect).
 *              13-10: Shared healed/immediate resurrection lifecycle; arm swap rollback before assignments.
 *              Gate 1: Persisted corpse queues are hydrated after service rewiring, indexed active
 *                     iteration waits for hydration, and completed queues compact in linear time.
 */

using System;
using Eternal.Extensions;
using System.Collections.Generic;
using System.Linq;
using Verse;
using Eternal.Compatibility;
using RimWorld;
using Eternal.Corpse;
using Eternal.DI;
using Eternal.Exceptions;
using Eternal.Interfaces;
using Eternal.Utils;
using RimWorld.Planet;
using Eternal.World;

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
        private bool activeHealingCorpsesHydrated;

        // PERF-01: Pre-allocated buffers — reused every call, zero allocation after construction.
        // Sized conservatively for typical Eternal counts; List<T> auto-grows if exceeded (rare).
        // RimWorld game logic is single-threaded — no locks required.
        private readonly List<EternalCorpseData> _corpseBuffer = new List<EternalCorpseData>(4);
        private readonly List<Pawn> _toRemoveBuffer = new List<Pawn>(4);
        private readonly List<HealingItem> _nonRegrowthBuffer = new List<HealingItem>(16);
        private readonly List<HealingItem> _regrowthBuffer = new List<HealingItem>(8);
        private readonly HashSet<HealingItem> _completedBuffer = new HashSet<HealingItem>();
        private readonly List<Pawn> _cleanupBuffer = new List<Pawn>(4);

        // Unified services (injected via service container)
        private IHealingRateCalculator RateCalculator => EternalServiceContainer.Instance.RateCalculator;
        private IDebtAccumulator DebtAccumulator => EternalServiceContainer.Instance.DebtAccumulator;
        private IPartRestorer PartRestorer => EternalServiceContainer.Instance.PartRestorer;
        private IFoodDebtSystem DebtSystem => EternalServiceContainer.Instance.FoodDebtSystem;
        private EternalCorpseManager CorpseManager => EternalServiceContainer.Instance.CorpseManager;

        public EternalCorpseHealingProcessor()
        {
            resurrectionCalculator = new EternalResurrectionCalculator();
        }

        /// <summary>
        /// Indicates whether the active-corpse index was rebuilt from fully hydrated manager entries.
        /// Until this is true, processing uses the manager scan fallback.
        /// </summary>
        public bool ActiveHealingCorpsesHydrated => activeHealingCorpsesHydrated;

        /// <summary>
        /// Returns the number of entries currently owned by the active-corpse index.
        /// </summary>
        public int ActiveHealingCorpseCount => activeHealingCorpses.Count;

        /// <summary>
        /// Rebuilds active corpse ownership after corpse entries, queues, and service references
        /// have completed post-load initialization. Old saves without an active queue are rebuilt
        /// from the death-time queue or the deterministic resurrection calculator fallback.
        /// </summary>
        public void RebuildActiveHealingCorpses()
        {
            activeHealingCorpses.Clear();
            activeHealingCorpsesHydrated = false;

            if (CorpseManager == null)
            {
                return;
            }

            bool allQueuesHydrated = true;
            try
            {
                foreach (var corpseData in GetManagerScanActiveHealingCorpses())
                {
                    if (corpseData?.OriginalPawn == null)
                    {
                        continue;
                    }

                    activeHealingCorpses.Add(corpseData.OriginalPawn);
                    // Exact manager saves have finished loading. Import a legacy corpse-only
                    // record before any resumed healing pass can refresh its float display.
                    DebtSystem.RegisterPawn(corpseData.OriginalPawn);
                    if (!EnsureHealingQueueHydrated(corpseData))
                    {
                        allQueuesHydrated = false;
                    }
                }

                // The set is trusted only after every active entry was inspected. A failed
                // hydration leaves the scan fallback enabled so it can be retried safely.
                activeHealingCorpsesHydrated = allQueuesHydrated;
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                    "RebuildActiveHealingCorpses", null, ex);
            }
        }

        private bool EnsureHealingQueueHydrated(EternalCorpseData corpseData)
        {
            if (corpseData == null || corpseData.OriginalPawn == null)
            {
                return false;
            }

            if (corpseData.HealingQueueHydrated && corpseData.HealingQueue != null)
            {
                corpseData.RebindHealingQueueReferences();
                ExcludeProtectedQueueItems(corpseData.HealingQueue);
                return true;
            }

            try
            {
                bool hydrated = corpseData.TryHydrateHealingQueue(() =>
                    resurrectionCalculator.CalculateHealingQueue(corpseData.OriginalPawn));
                if (hydrated)
                {
                    ExcludeProtectedQueueItems(corpseData.HealingQueue);
                }
                return hydrated;
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                    "HydrateHealingQueue", corpseData.OriginalPawn, ex);
                return false;
            }
        }

        internal void ReconcileCrashRegrowth(Pawn pawn, List<HealingItem> freshQueue)
        {
            var entry = CorpseManager?.GetCorpseData(pawn);
            if (entry?.IsHealingActive != true || !EnsureHealingQueueHydrated(entry)) { return; }
            foreach (var item in freshQueue.Where(item => item.Type == HealingType.Regrowth))
            {
                if (!entry.HealingQueue.Any(existing => existing.Type == HealingType.Regrowth
                    && (ReferenceEquals(existing.Hediff?.Part, item.Hediff?.Part)
                        || (existing.HediffLoadId >= 0 && existing.HediffLoadId == item.HediffLoadId))))
                { entry.HealingQueue.Add(item); }
            }
            // StartRegrowth preserves existing per-part hediff progress and starts only new
            // eligible parts. Old queue entries, paid debt and assignments remain authoritative.
            Eternal_Component.Current?.RegrowthManager?.StartRegrowth(pawn);
            entry.TotalHealingCost = resurrectionCalculator.CalculateTotalCost(entry.HealingQueue);
            activeHealingCorpses.Add(pawn);
        }

        // Discard policy-excluded work without touching the live hediff. This runs after
        // rebinding and before cost, completion, or execution checks, including old saves.
        internal static void ExcludeProtectedQueueItems(List<HealingItem> queue)
        {
            queue?.RemoveAll(IsPolicyExcludedQueueItem);
        }

        private static bool IsPolicyExcludedQueueItem(HealingItem item)
        {
            if (item == null)
            {
                return false;
            }

            var def = item.Hediff?.def;
            if (def == null && !string.IsNullOrEmpty(item.HediffDefName))
            {
                def = DefDatabase<HediffDef>.GetNamedSilentFail(item.HediffDefName);
            }

            // An old scalar-only entry without a def cannot be identified reliably.
            // Preserve its original fallback rather than guessing from severity or part.
            return RjwHealthPolicy.GetCorpseHealingDecision(def) == false;
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
                            if (IsCompleted(CompleteResurrection(corpseData)))
                            { _toRemoveBuffer.Add(corpseData.OriginalPawn); }
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
                            if (IsCompleted(CompleteResurrection(corpseData)))
                            { _toRemoveBuffer.Add(corpseData.OriginalPawn); }
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
                if (EternalModState.IsDisabled || corpseData == null || corpseData.OriginalPawn == null
                    || !corpseData.OriginalPawn.Dead || !SupportsResurrection(corpseData.Corpse))
                {
                    Log.Warning("[Eternal] Cannot start healing - corpse data or pawn is null");
                    return false;
                }

                if (corpseData.IsHealingActive)
                {
                    Log.Warning($"[Eternal] Healing already active for {corpseData.OriginalPawn.Name}");
                    return false;
                }

                // Calculate or restore the queue before marking the entry active. An empty queue
                // is valid only after hydration has completed; an unhydrated old save must wait.
                if (corpseData.HealingQueue == null)
                {
                    corpseData.HealingQueueHydrated = false;
                }

                if (!EnsureHealingQueueHydrated(corpseData))
                {
                    Log.Warning($"[Eternal] Cannot start healing for {corpseData.OriginalPawn.Name} - healing queue hydration failed");
                    return false;
                }

                corpseData.TotalHealingCost = resurrectionCalculator.CalculateTotalCost(
                    corpseData.HealingQueue);

                if (corpseData.HealingQueue.Count == 0)
                {
                    // Nothing to heal - resurrect immediately
                    return IsCompleted(ResurrectImmediately(corpseData));
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
            if (!EnsureHealingQueueHydrated(corpseData)
                || corpseData.HealingQueue == null
                || corpseData.HealingQueue.Count == 0)
            {
                return;
            }

            float totalHeal = CalculateHealingPerTick(corpseData.OriginalPawn);

            // PERF-01: Use pre-allocated instance buffers — zero allocation on the hot path.
            _nonRegrowthBuffer.Clear();
            _completedBuffer.Clear();

            // Collect non-regrowth items only (injuries, diseases, scars)
            foreach (var item in corpseData.HealingQueue)
            {
                if (item == null)
                {
                    continue;
                }

                float itemSeverity = item.Hediff?.Severity ?? item.Severity;
                if (item.Type != HealingType.Regrowth && itemSeverity > 0f)
                {
                    _nonRegrowthBuffer.Add(item);
                }
            }

            if (_nonRegrowthBuffer.Count == 0)
            {
                return;
            }

            // Parallel model (parity with living pawns): every queue item heals at the full
            // per-pass rate simultaneously — no shared-pool division across the queue.
            float perItemHeal = totalHeal;

            foreach (var item in _nonRegrowthBuffer)
            {
                if (IsPolicyExcludedQueueItem(item))
                {
                    _completedBuffer.Add(item);
                    continue;
                }
                // Read current severity from hediff if available, else from local copy (save/load fallback)
                float currentSeverity = item.Hediff?.Severity ?? item.Severity;
                if (item.Hediff == null && Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Warning($"[Eternal] Stale hediff reference in healing queue for {corpseData.OriginalPawn?.Name} — using local severity fallback");
                }

                var profile = HealingCostProfiles.ForItem(item, Eternal_Mod.GetSettings().nutritionCostMultiplier, corpseData.OriginalPawn.HasFoodNeedDisabled());
                var step = CorpseHealingCostCalculator.CalculateProfileStep(currentSeverity, perItemHeal, profile);
                if (!step.IsValid)
                {
                    continue;
                }

                // Write to real hediff (health tab sees the update) and local copy (save/load persistence)
                if (item.Hediff != null)
                {
                    item.Hediff.Severity = step.RemainingSeverity;
                }

                item.Severity = step.RemainingSeverity;

                // Debt accumulation proportional to healing performed (uncapped resurrection debt)
                DebtAccumulator?.AddResurrectionDebt(corpseData.OriginalPawn, step.NutritionCharge);

                if (step.IsComplete)
                {
                    _completedBuffer.Add(item);
                    ApplyHealingEffect(corpseData, item);
                }
            }

            corpseData.FoodDebt = DebtSystem.GetDebt(corpseData.OriginalPawn);

            CompactCompletedQueue(corpseData.HealingQueue, _completedBuffer);
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
            if (!EnsureHealingQueueHydrated(corpseData)
                || corpseData.HealingQueue == null
                || corpseData.HealingQueue.Count == 0)
            {
                return;
            }

            float totalHeal = CalculateHealingPerTick(corpseData.OriginalPawn);

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
            {
                return;
            }

            var regrowthManager = Eternal_Component.Current?.RegrowthManager;
            var regrowthState = regrowthManager?.GetRegrowthState(corpseData.OriginalPawn);

            if (regrowthState != null)
            {
                // Parallel model (parity with living pawns): regrowth receives the full per-pass
                // healing effort — no shared-pool division against the injury queue.
                float regrowthHeal = totalHeal;

                // Apply healing to the regrowth system (handles 4-phase progression internally)
                // HVIS-02 VERIFIED: ApplyHealing delegates to EternalRegrowthManager.ProgressRegrowth()
                // which writes hediff.Severity += severityIncrease directly — health tab shows progress.
                // Path: regrowthState.ApplyHealing → ProgressRegrowth → EternalRegrowing_Hediff.Severity
                var result = regrowthState.ApplyHealing(regrowthHeal);
                DebtAccumulator?.AddResurrectionDebt(corpseData.OriginalPawn, result.NutritionCharge);

                // Check if regrowth is complete (all body parts restored)
                if (regrowthState.IsRegrowthComplete() && !regrowthManager.IsPawnInRegrowth(corpseData.OriginalPawn) &&
                    !corpseData.OriginalPawn.health.hediffSet.GetMissingPartsCommonAncestors().Any())
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
                // Recover by starting regrowth; the primary path picks the new hediffs up on the
                // next rare tick with the correct HP-scaled progression (healing them directly
                // here would bypass EternalRegrowthManager's rate and completion gating).

                if (Eternal_Mod.settings?.debugMode == true)
                {
                    Log.Warning($"[Eternal] Corpse regrowth fallback triggered for {corpseData.OriginalPawn?.Name}: " +
                               $"No regrowing hediffs found but {_regrowthBuffer.Count} regrowth items in queue. Attempting recovery.");
                }

                regrowthManager?.StartRegrowth(corpseData.OriginalPawn);

                bool recoveryStartedRegrowth = regrowthManager != null
                    && regrowthManager.IsPawnInRegrowth(corpseData.OriginalPawn);

                if (!recoveryStartedRegrowth)
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

            CompactCompletedQueue(corpseData.HealingQueue, _completedBuffer);
            UpdateHealingProgress(corpseData);
        }

        /// <summary>
        /// Removes completed queue entries in one stable write pass. Null entries are also
        /// discarded so a malformed or old queue cannot wait forever on an unprocessable item.
        /// </summary>
        internal static void CompactCompletedQueue(List<HealingItem> healingQueue, ISet<HealingItem> completedItems)
        {
            Eternal.UI.HealingQueueSelector.Compact(healingQueue, completedItems);
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
        /// Applies the effect of a completed healing item.
        /// </summary>
        /// <param name="corpseData">The corpse data</param>
        /// <param name="healingItem">The completed healing item</param>
        private void ApplyHealingEffect(EternalCorpseData corpseData, HealingItem healingItem)
        {
            try
            {
                if (IsPolicyExcludedQueueItem(healingItem))
                {
                    return;
                }

                if (healingItem.Type == HealingType.Regrowth && healingItem.Hediff is Hediff_MissingPart missingPart)
                {
                    // Regrowth: missing parts restored during resurrection, not here
                    return;
                }

                // Remove completed injury hediffs from the pawn's health tab immediately.
                // This prevents ghost entries (injuries showing at original severity after healing completes).
                // Skip if: hediff ref is stale (null), pawn health is gone, or hediff is a MissingPart
                // (MissingParts are restored by PartRestorer during resurrection, not removed here).
                if (healingItem.Hediff != null
                    && corpseData.OriginalPawn?.health?.hediffSet != null
                    && !(healingItem.Hediff is Hediff_MissingPart)
                    && corpseData.OriginalPawn.health.hediffSet.hediffs.Contains(healingItem.Hediff))
                {
                    try
                    {
                        corpseData.OriginalPawn.health.RemoveHediff(healingItem.Hediff);
                    }
                    catch (Exception removeEx)
                    {
                        // Dead pawn hediff removal can trigger unexpected callbacks in
                        // Notify_HediffRemoved that assume living-pawn context
                        EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                            "RemoveHealedHediff", corpseData.OriginalPawn, removeEx);
                    }
                }

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
                {
                    return;
                }

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
            // An unhydrated queue is not evidence that healing finished. This guard prevents
            // old saves from resurrecting immediately or waiting on a permanently empty queue.
            if (corpseData == null || !EnsureHealingQueueHydrated(corpseData))
            {
                return false;
            }

            if (corpseData.HealingQueue == null || corpseData.HealingQueue.Count > 0)
            {
                return false;
            }

            // Verify pawn state
            var pawn = corpseData.OriginalPawn;
            if (pawn?.health?.hediffSet == null)
            {
                return false;
            }

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
            if (corpse == null)
            {
                return null;
            }

            // SAFE-04: Fast path via persisted CaravanId — avoids AllThings.Contains per caravan.
            // GetCorpseData lookup is O(1); Caravans list is typically tiny (<10 entries).
            var corpseOwner = corpse.InnerPawn;
            if (corpseOwner != null)
            {
                var corpseData = CorpseManager?.GetCorpseData(corpseOwner);
                if (corpseData != null && corpseData.CaravanId != -1)
                {
                    var found = Find.WorldObjects.Caravans.FirstOrDefault(c => c.ID == corpseData.CaravanId);
                    if (found != null && found.AllThings.Contains(corpse))
                    {
                        return found;
                    }
                    // CaravanId set but caravan dissolved — fall through to linear scan below.
                }
            }

            // Fallback: linear scan (old saves, or CaravanId lookup returned null).
            foreach (var worldObject in Find.World.worldObjects.AllWorldObjects)
            {
                if (worldObject is RimWorld.Planet.Caravan caravan && caravan.AllThings.Contains(corpse))
                {
                    var entry = CorpseManager?.GetCorpseData(corpse.InnerPawn);
                    if (entry != null) { entry.CaravanId = caravan.ID; }
                    return caravan;
                }
            }
            var stale = CorpseManager?.GetCorpseData(corpse.InnerPawn);
            if (stale != null) { stale.CaravanId = -1; }
            return null;
        }

        internal static bool SupportsResurrection(Verse.Corpse corpse)
            => corpse?.InnerPawn != null && !corpse.Destroyed && !corpse.InnerPawn.Discarded
                && !(ModsConfig.AnomalyActive && corpse is UnnaturalCorpse);

        private enum ResurrectionOutcome { Retry, Rejected, Completed, Recovered }
        private static bool IsCompleted(ResurrectionOutcome outcome)
            => outcome == ResurrectionOutcome.Completed || outcome == ResurrectionOutcome.Recovered;

        private enum ResurrectionMode
        {
            Healed,
            Immediate
        }

        /// <summary>Completes resurrection after the healing and regrowth passes finish.</summary>
        private ResurrectionOutcome CompleteResurrection(EternalCorpseData corpseData)
        {
            return Resurrect(corpseData, ResurrectionMode.Healed);
        }

        /// <summary>Resurrects immediately when the death-time queue has no healing work.</summary>
        private ResurrectionOutcome ResurrectImmediately(EternalCorpseData corpseData)
        {
            return Resurrect(corpseData, ResurrectionMode.Immediate);
        }

        /// <summary>
        /// Preserves saved health through resurrection, then restores caravan membership,
        /// assignments and debt. The mode retains each entry point's registration history.
        /// </summary>
        private ResurrectionOutcome Resurrect(EternalCorpseData corpseData, ResurrectionMode mode)
        {
            bool immediate = mode == ResurrectionMode.Immediate;
            string operation = immediate ? "ResurrectImmediately" : "CompleteResurrection";

            if (corpseData?.OriginalPawn == null || corpseData.Corpse == null)
            {
                Log.Error(immediate
                    ? "[Eternal] Cannot resurrect immediately - corpse data or corpse is null"
                    : "[Eternal] Cannot complete resurrection - corpse data or corpse is null");
                return ResurrectionOutcome.Rejected;
            }

            var pawn = corpseData.OriginalPawn;

            // Recover any legacy-only episode while its corpse entry still exists. The same
            // owner must retain it even if validation cleans up or resurrection/post-work fails.
            DebtSystem.RegisterPawn(pawn);

            // Pre-resurrection validation
            if (!ValidatePreResurrection(pawn, corpseData))
            {
                return ResurrectionOutcome.Rejected;
            }

            if (!SupportsResurrection(corpseData.Corpse))
            {
                corpseData.IsHealingActive = false;
                activeHealingCorpses.Remove(pawn);
                return ResurrectionOutcome.Rejected;
            }
            var sourceCorpse = corpseData.Corpse;
            var sourceMap = sourceCorpse.MapHeld;
            var sourcePosition = sourceCorpse.PositionHeld;
            var sourceSite = Find.WorldObjects.AllWorldObjects.OfType<WorldObject_EternalCrashSite>()
                .FirstOrDefault(site => site.HasPendingCorpse(sourceCorpse));
            // Reserve the existing recovery-site owner before the native call can consume its
            // corpse. A later placement failure must have somewhere real to deep-save the pawn.
            var recoverySite = sourceSite ?? SpaceCrashRescueService.CreateOrGetCrashSite(
                SpaceCrashRescueService.ResolveGroundTile(sourceCorpse.Tile));
            if (recoverySite == null) { return ResurrectionOutcome.Retry; }
            bool partial = false;

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
                        Log.Message(immediate
                            ? $"[Eternal] Detected corpse in caravan for immediate resurrection: {pawnCaravan.Label}"
                            : $"[Eternal] Detected corpse in caravan: {pawnCaravan.Label}");
                    }
                }

                // === Create clean slate for resurrection ===
                // RimWorld's TryResurrect expects a clean health state.
                // Arm rollback before either assignment: constructing the second tracker may throw.
                swapActive = true;
                pawn.health.hediffSet = new HediffSet(pawn);
                pawn.health.immunity = new ImmunityHandler(pawn);

                // Resurrect the pawn.
                // Third-party Harmony transpilers/postfixes (e.g. WorkTab) may throw inside
                // TryResurrect. Critically, Pawn_HealthTracker.Notify_Resurrected sets healthState
                // to Mobile BEFORE EnableAndInitialize is called — so if WorkTab crashes during
                // EnableAndInitialize, the pawn is already alive. Recover its physical/deep
                // ownership before sanitizing saved health or committing completion.
                // If the pawn is still dead after the exception, it is a hard failure — let the
                // outer catch and finally handle it (AttemptHediffRestore).
                // TryResurrect destroys the still-tracked corpse (Vanish, unspawned for caravan
                // corpses) — mark it expected so Corpse_Destroy_Patch lets the destroy through.
                CorpseManager?.MarkExpectedDestruction(corpseData.Corpse);
                bool resurrected;
                try
                {
                    resurrected = ResurrectionUtility.TryResurrect(pawn, null);
                }
                catch (Exception tryResurrectEx)
                {
                    if (!pawn.Dead)
                    {
                        // Becoming alive is only a partial transition; placement below must
                        // establish a persistent owner before this can count as completed.
                        partial = true;
                        resurrected = true;
                        Log.Warning($"[Eternal:{operation}] TryResurrect threw after {pawn.Name} became alive; recovering ownership. " +
                            $"Exception: {tryResurrectEx.GetType().Name}: {tryResurrectEx.Message}");
                    }
                    else
                    {
                        // Pawn is still dead — hard failure. Re-throw so the outer catch handles it
                        // and the finally block calls AttemptHediffRestore with swapActive=true.
                        throw;
                    }
                }
                finally
                {
                    CorpseManager?.UnmarkExpectedDestruction(corpseData.Corpse);
                }
                if (!resurrected || pawn.Dead) { return ResurrectionOutcome.Retry; }
                if (!EnsureResurrectedOwner(pawn, pawnCaravan, sourceMap, sourcePosition, recoverySite))
                { return ResurrectionOutcome.Retry; }
                sourceSite?.ForgetPendingCorpse(sourceCorpse);

                // === Sanitize saved HediffSet BEFORE swap-back (RSRV-01/02/03) ===
                // The savedHediffSet is detached from the pawn during the swap window.
                // Direct list manipulation here is safe — no CheckForStateChange fires because
                // pawn.health.hediffSet still points to the clean HediffSet (not savedHediffSet).
                // This prevents consciousness collapse from stale regrowth hediffs (RSRV-01),
                // strips the purposeless MetabolicRecovery hediff (RSRV-02),
                // and catches any residual injury severity above the lethal threshold (RSRV-03).
                SanitizeHediffSetBeforeRestore(savedHediffSet, pawn);

                // === Restore HediffSet and Immunity ===
                // This preserves Eternal_Essence, food debt tracking, and other custom hediffs.
                pawn.health.hediffSet = savedHediffSet;
                pawn.health.immunity = savedImmunity;
                swapActive = false; // Swap complete — post-work exceptions no longer need atomic rollback

                // Reconcile immediately after the saved HediffSet is attached. This reads and
                // prunes scalar state only; healing remains owned by the scheduled passes.
                Eternal_Component.Instance?.ReconcileLiveHealth(pawn);
                if (partial)
                {
                    // Finish idempotent native state work only after the real saved hediffs
                    // return, so their need waivers and capacities participate in the refresh.
                    pawn.needs?.AddOrRemoveNeedsAsAppropriate();
                    if (pawn.relations != null) { pawn.relations.hidePawnRelations = false; }
                    if (pawn.guest?.IsInteractionEnabled(PrisonerInteractionModeDefOf.Execution) == true)
                    { pawn.guest.SetNoInteraction(); }
                    pawn.Drawer?.renderer?.SetAllGraphicsDirty();
                }

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

                // Restore work priorities, policies, and schedule AFTER resurrection.
                // ResurrectionUtility.TryResurrect calls pawn.workSettings.EnableAndInitialize()
                // which RESETS all work priorities, so we must restore after.
                if (corpseData.AssignmentSnapshot != null)
                {
                    corpseData.AssignmentSnapshot.ApplyTo(pawn);

                    if (Eternal_Mod.settings?.debugMode == true)
                    {
                        Log.Message(immediate
                            ? $"[Eternal] Restored work assignments for immediately resurrected pawn {pawn.Name}"
                            : $"[Eternal] Restored work assignments for resurrected pawn {pawn.Name}");
                    }
                }

                if (!immediate)
                {
                    // Healed resurrection alone performs the regrowth fallback and cleanup.
                    var regrowthManager = Eternal_Component.Current?.RegrowthManager;
                    if (regrowthManager == null || !regrowthManager.IsPawnInRegrowth(pawn))
                    {
                        RestoreMissingBodyParts(pawn, corpseData);
                    }
                    regrowthManager?.RemoveCompletedRegrowth(pawn);
                }

                // The same pawn keeps its exact episode; this float is only a notification.
                float healingDebt = DebtSystem.GetDebt(pawn);
                ReleaseResurrectionTracking(corpseData);

                Log.Message(immediate
                    ? $"[Eternal] Immediately resurrected {pawn.Name}. Retained debt: {healingDebt:F2}"
                    : $"[Eternal] Resurrection completed for {pawn.Name}. Retained debt: {healingDebt:F2}");

                Find.LetterStack.ReceiveLetter(
                    "EternalResurrectionComplete".Translate(),
                    immediate
                        ? "EternalResurrectionImmediateDesc".Translate(pawn.Named("PAWN"))
                        : "EternalResurrectionCompleteDesc".Translate(pawn.Named("PAWN"), healingDebt.Named("DEBT")),
                    LetterDefOf.PositiveEvent,
                    pawn);
                return partial ? ResurrectionOutcome.Recovered : ResurrectionOutcome.Completed;
            }
            catch (Exception ex)
            {
                if (!swapActive)
                {
                    // Hediffs already restored — exception occurred in post-work. Log and continue.
                    EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                        operation + ".PostWork", pawn, ex);
                }
                // If swapActive is still true, the finally block below calls AttemptHediffRestore.
                return ResurrectionOutcome.Retry;
            }
            finally
            {
                if (swapActive)
                {
                    // Swap was active when the exception escaped — restore hediffs atomically.
                    AttemptHediffRestore(pawn, savedHediffSet, savedImmunity, corpseData);
                }
                if (!pawn.Dead)
                {
                    bool owned = EnsureResurrectedOwner(pawn, pawnCaravan, sourceMap, sourcePosition, recoverySite);
                    if (owned) { sourceSite?.ForgetPendingCorpse(sourceCorpse); }
                    // A consumed corpse cannot retain a dead-only retry index. Its pawn is now
                    // persisted by the verified map/caravan/site and controlled through that owner.
                    if (owned) { ReleaseResurrectionTracking(corpseData); }
                }
                if (!recoverySite.HasRecoveryContents && !recoverySite.HasMap)
                { Find.WorldObjects.Remove(recoverySite); }
            }
        }

        private void ReleaseResurrectionTracking(EternalCorpseData entry)
        {
            entry.IsHealingActive = false;
            activeHealingCorpses.Remove(entry.OriginalPawn);
            if (CorpseManager?.IsTracked(entry.OriginalPawn) == true)
            { CorpseManager.UnregisterCorpse(entry.OriginalPawn); }
        }

        private static bool EnsureResurrectedOwner(Pawn pawn, RimWorld.Planet.Caravan caravan,
            Verse.Map map, IntVec3 position, WorldObject_EternalCrashSite recoverySite)
        {
            if (pawn.Dead) { return false; }
            // Site retention completes placement. Retrying a former destination would
            // leave its separate deep-save list owning a pawn also saved by that destination.
            if (recoverySite.Spawned && !recoverySite.Destroyed && recoverySite.HasPendingPawn(pawn)) { return true; }
            if (pawn.Spawned && pawn.Map != null)
            {
                if (pawn.IsWorldPawn()) { Find.WorldPawns.RemovePawn(pawn); }
                return true;
            }
            if (caravan != null && caravan.Spawned)
            {
                try
                {
                    if (!caravan.ContainsPawn(pawn)) { caravan.AddPawn(pawn, false); }
                }
                catch (Exception ex) { Log.Warning("[Eternal] Resurrection caravan placement failed: " + ex.Message); }
                if (caravan.ContainsPawn(pawn))
                {
                    try
                    {
                        // Caravan's pawn ThingOwner uses LookMode.Reference. WorldPawns is
                        // its native deep owner; membership alone cannot make a safe save.
                        if (!pawn.IsWorldPawn()) { Find.WorldPawns.PassToWorld(pawn); }
                    }
                    catch (Exception ex) { Log.Warning("[Eternal] Resurrection world ownership failed: " + ex.Message); }
                    if (pawn.IsWorldPawn()) { return true; }
                    // Failed persistence must release the reference-only membership before
                    // the already-reserved site can adopt this living pawn as its deep owner.
                    caravan.RemovePawn(pawn);
                }
            }
            if (map != null && Find.Maps.Contains(map) && position.IsValid && position.InBounds(map))
            {
                try
                {
                    if (pawn.IsWorldPawn()) { Find.WorldPawns.RemovePawn(pawn); }
                    GenSpawn.Spawn(pawn, position, map);
                    if (pawn.Spawned && pawn.Map == map) { return true; }
                }
                catch (Exception ex) { Log.Warning("[Eternal] Resurrection map placement failed: " + ex.Message); }
                if (pawn.Spawned && pawn.Map == map) { return true; }
            }
            return recoverySite.TryRetainUnownedPawn(pawn);
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
        /// Sanitizes a saved HediffSet before restoring it to a resurrected pawn.
        /// Removes stale regrowth hediffs (RSRV-01), MetabolicRecovery (RSRV-02),
        /// and residual injuries above lethal threshold (RSRV-03).
        /// Must be called BEFORE assigning hediffSet back to pawn.health.hediffSet
        /// to avoid triggering CheckForStateChange during cleanup.
        /// </summary>
        /// <param name="hediffSet">The detached HediffSet to sanitize (not yet assigned to pawn)</param>
        /// <param name="pawn">The pawn being resurrected (used for logging only)</param>
        private void SanitizeHediffSetBeforeRestore(HediffSet hediffSet, Pawn pawn)
        {
            if (hediffSet == null)
            {
                return;
            }

            // Pass 1: Remove stale EternalRegrowing_Hediff instances (RSRV-01).
            // Under normal flow these are all removed before resurrection completes.
            // This guards against edge cases: floating-point precision, save/load desync,
            // mod conflicts, or ResurrectImmediately running before regrowth hediffs were cleaned.
            // A regrowing hediff on brain/skull/head at stage 0-1 has partEfficiencyOffset -1.0,
            // which zeroes consciousness → instant death on the first CheckForStateChange.
            int regrowthRemoved = hediffSet.hediffs.RemoveAll(h => h is EternalRegrowing_Hediff);
            if (regrowthRemoved > 0 && Eternal_Mod.settings?.debugMode == true)
            {
                Log.Message($"[Eternal] SanitizeHediffSet: Removed {regrowthRemoved} stale regrowth hediff(s) from {pawn?.Name}");
            }

            // Pass 2: Remove MetabolicRecovery (RSRV-02).
            // The pawn was dead during healing and didn't eat, so hunger rate acceleration
            // on the corpse is meaningless. After resurrection the debt system re-applies it
            // correctly based on the transferred debt amount.
            int metabolicRemoved = hediffSet.hediffs.RemoveAll(h => h.def == EternalDefOf.Eternal_MetabolicRecovery);
            if (metabolicRemoved > 0 && Eternal_Mod.settings?.debugMode == true)
            {
                Log.Message($"[Eternal] SanitizeHediffSet: Stripped MetabolicRecovery from {pawn?.Name}");
            }

            // Pass 3: Check and remove residual injury hediffs (RSRV-03).
            // Injuries should be fully healed before resurrection completes.
            // Any remaining injury severity is an edge case (floating-point, mod conflict, etc.)
            // that would kill the pawn via ShouldBeDeadFromLethalDamageThreshold().
            float totalInjurySeverity = 0f;
            foreach (var hediff in hediffSet.hediffs)
            {
                if (hediff is Hediff_Injury)
                {
                    totalInjurySeverity += hediff.Severity;
                }
            }
            if (totalInjurySeverity > 0f)
            {
                Log.Warning($"[Eternal] SanitizeHediffSet: Found {totalInjurySeverity:F2} residual injury severity on {pawn?.Name} — removing injuries");
                hediffSet.hediffs.RemoveAll(h => h is Hediff_Injury);
            }

            // Force recalculation of all cached capacity values (part efficiency, consciousness, etc.).
            // Direct list manipulation bypasses RimWorld's normal cache-dirty path, so we
            // must call DirtyCache() explicitly before the hediffSet becomes the pawn's active set.
            hediffSet.DirtyCache();
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
                {
                    return;
                }

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
            if (activeHealingCorpsesHydrated)
            {
                foreach (var pawn in activeHealingCorpses)
                {
                    var indexedCorpse = CorpseManager?.GetCorpseData(pawn);
                    if (indexedCorpse != null && indexedCorpse.IsHealingActive)
                    {
                        yield return indexedCorpse;
                    }
                }

                yield break;
            }

            foreach (var corpseData in GetManagerScanActiveHealingCorpses())
            {
                yield return corpseData;
            }
        }

        private IEnumerable<EternalCorpseData> GetManagerScanActiveHealingCorpses()
        {
            var trackedCorpses = CorpseManager?.GetAllCorpses();
            if (trackedCorpses == null)
            {
                yield break;
            }

            foreach (var corpseData in trackedCorpses)
            {
                if (corpseData != null && corpseData.OriginalPawn != null && corpseData.IsHealingActive)
                {
                    yield return corpseData;
                }
            }
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
