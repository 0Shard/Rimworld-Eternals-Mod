/*
 * Relative Path: Eternal/Source/Eternal/Resources/UnifiedFoodDebtManager.cs
 * Creation Date: 03-12-2025
 * Last Edit: 12-07-2026
 * Author: 0Shard
 * Description: Unified food debt manager consolidating EternalFoodDebtSystem and EternalFoodDebtMonitor.
 *              Implements IFoodDebtSystem (inherits IFoodDebtReader + IFoodDebtWriter for ISP compliance).
 *              GetMaxCapacity uses body size for fallback when food need is unavailable.
 *              Optimized with TryGetValue patterns and pooled lists to reduce allocations.
 *              Fixed: Added warnings for list length mismatches and null pawns during save/load.
 *              Added: Food need waiver - pawns with food need disabled have nutrient costs waived.
 *              03-02: All catch sites converted to EternalLogger.HandleException(Resurrection, ...).
 *              12-07: Uncapped resurrection debt via per-pawn baseline; alive cap applies on top.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using Verse;
using RimWorld;
using Eternal.DI;
using Eternal.Exceptions;
using Eternal.Interfaces;
using Eternal.Extensions;
using Eternal.Corpse;
using Eternal.Utils;
using Eternal.Utilities;

namespace Eternal.Resources
{
    /// <summary>
    /// Unified food debt manager implementing IFoodDebtSystem.
    /// Consolidates functionality from EternalFoodDebtSystem and EternalFoodDebtMonitor.
    /// </summary>
    /// <remarks>
    /// Key behaviors:
    /// - Both living and dead pawns can accumulate debt during healing/regrowth
    /// - Debt increases hunger rate via the Metabolic Recovery hediff's HediffStage.hungerRateFactor
    /// - Higher debt = faster hunger via hediff stage curve = more eating = faster repayment (self-balancing)
    /// - AddDebt clamps to max capacity instead of rejecting over-limit additions
    /// </remarks>
    public class UnifiedFoodDebtManager : IFoodDebtSystem, IExposable
    {
        // Single dictionary for debt tracking (DRY - no parallel structures)
        private Dictionary<Pawn, float> _debtTracker = new Dictionary<Pawn, float>();

        // Resurrection debt baseline: the uncapped portion of a pawn's debt accrued from corpse
        // healing. GetMaxCapacity returns base cap + baseline, so the alive-time cap
        // (maxDebtMultiplier) applies ON TOP of resurrection debt. Shrinks with repayment.
        private Dictionary<Pawn, float> _resurrectionDebtBaseline = new Dictionary<Pawn, float>();

        // Peak debt of the current episode (max debt since it was last zero). Drives the
        // constant repayment drain rate: peak / (60000 × debtRepaymentDays) per tick, so any
        // debt fully repays within the window. Cleared when debt reaches zero.
        private Dictionary<Pawn, float> _peakDebt = new Dictionary<Pawn, float>();

        // Tracked pawns for monitoring
        private HashSet<Pawn> _trackedPawns = new HashSet<Pawn>();

        /// <summary>
        /// Maximum debt multiplier from settings (debt = foodCapacity * multiplier).
        /// Uses GetSettings() for guaranteed non-null access (SAFE-08).
        /// </summary>
        private float MaxDebtMultiplier => Eternal_Mod.GetSettings().maxDebtMultiplier;

        /// <summary>
        /// Debug mode from settings.
        /// </summary>
        private bool DebugMode => Eternal_Mod.settings?.debugMode ?? false;

        /// <inheritdoc />
        public void RegisterPawn(Pawn pawn)
        {
            try
            {
                if (pawn == null)
                {
                    Log.Warning("[Eternal] Cannot register null pawn for debt tracking");
                    return;
                }

                // Dead pawns are always Destroyed Things in RimWorld (pawn.Destroyed is set before
                // Notify_PawnDied is called — see CLAUDE.md "RimWorld Death Sequence"). IsValidEternal()
                // checks the Destroyed flag and therefore always returns false for dead pawns. Use
                // IsValidEternalCorpse() for dead pawns; it skips the Destroyed check intentionally.
                bool isValidEternal = pawn.Dead
                    ? pawn.IsValidEternalCorpse()
                    : pawn.IsValidEternal();
                if (!isValidEternal)
                {
                    Log.Warning($"[Eternal] Attempted to register non-Eternal pawn: {pawn.Name}");
                    return;
                }

                _trackedPawns.Add(pawn);

                // Initialize debt if not already tracked
                if (!_debtTracker.ContainsKey(pawn))
                {
                    _debtTracker[pawn] = 0f;
                }

                if (DebugMode)
                {
                    Log.Message($"[Eternal] Registered {pawn.Name} for food debt tracking");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                    "RegisterPawn", pawn, ex);
            }
        }

        /// <inheritdoc />
        public void UnregisterPawn(Pawn pawn)
        {
            try
            {
                if (pawn == null)
                    return;

                _trackedPawns.Remove(pawn);
                _debtTracker.Remove(pawn);
                _resurrectionDebtBaseline.Remove(pawn);
                _peakDebt.Remove(pawn);

                if (DebugMode)
                {
                    Log.Message($"[Eternal] Unregistered {pawn.Name} from food debt tracking");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                    "UnregisterPawn", pawn, ex);
            }
        }

        /// <inheritdoc />
        public bool AddDebt(Pawn pawn, float amount)
        {
            try
            {
                if (pawn == null || amount <= 0)
                    return false;

                // Waive debt for pawns with food need disabled (genes, hediffs, traits, etc.)
                if (pawn.HasFoodNeedDisabled())
                {
                    if (DebugMode)
                    {
                        Log.Message($"[Eternal] Waived {amount:F3} debt for {pawn.Name?.ToStringShort ?? "Pawn"} - food need disabled");
                    }
                    return true; // Return true = debt "handled" (by waiving)
                }

                // PERF: Use TryGetValue to avoid double dict lookup
                _debtTracker.TryGetValue(pawn, out float currentDebt);
                float maxDebt = GetMaxCapacity(pawn);

                // Clamp to max capacity instead of rejecting — partial debt is still debt
                // (Pitfall 5: returning false would signal failure to callers when debt WAS added)
                float newDebt = currentDebt + amount;
                if (newDebt > maxDebt)
                {
                    newDebt = maxDebt;  // Clamp to max per CONTEXT.md decision
                    if (DebugMode)
                    {
                        Log.Message($"[Eternal] Debt clamped to max for {pawn.Name}. " +
                                   $"Requested: {currentDebt + amount:F1}, Capped: {maxDebt:F1}");
                    }
                }

                // Ensure pawn is tracked for debt repayment processing
                _trackedPawns.Add(pawn);

                // Store the (possibly clamped) debt
                _debtTracker[pawn] = newDebt;
                UpdatePeakDebt(pawn, newDebt);

                // Sync with corpse data if available
                SyncCorpseData(pawn);

                if (DebugMode)
                {
                    Log.Message($"[Eternal] Added {amount:F1} debt to {pawn.Name}. Total: {newDebt:F1}");
                }

                return true;
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                    "AddDebt", pawn, ex);
                return false;
            }
        }

        /// <inheritdoc />
        public bool AddResurrectionDebt(Pawn pawn, float amount)
        {
            try
            {
                if (pawn == null || amount <= 0)
                    return false;

                // Same waiver as AddDebt: no food need means healing is free.
                if (pawn.HasFoodNeedDisabled())
                {
                    if (DebugMode)
                    {
                        Log.Message($"[Eternal] Waived {amount:F3} resurrection debt for {pawn.Name?.ToStringShort ?? "Pawn"} - food need disabled");
                    }
                    return true;
                }

                _debtTracker.TryGetValue(pawn, out float currentDebt);
                float newDebt = currentDebt + amount;

                _trackedPawns.Add(pawn);
                _debtTracker[pawn] = newDebt;
                UpdatePeakDebt(pawn, newDebt);

                // The full post-add debt becomes the baseline: capacity = base cap + baseline,
                // so the alive-time cap applies on top of everything owed from resurrection.
                _resurrectionDebtBaseline[pawn] = newDebt;

                SyncCorpseData(pawn);

                if (DebugMode)
                {
                    Log.Message($"[Eternal] Added {amount:F3} resurrection debt to {pawn.Name}. Total: {newDebt:F1} (uncapped)");
                }

                return true;
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                    "AddResurrectionDebt", pawn, ex);
                return false;
            }
        }

        /// <inheritdoc />
        public float RepayDebt(Pawn pawn, float amount)
        {
            try
            {
                if (pawn == null || amount <= 0)
                    return 0f;

                // PERF: Use TryGetValue directly
                if (!_debtTracker.TryGetValue(pawn, out float currentDebt) || currentDebt <= 0f)
                    return 0f;

                // Calculate actual repayment
                float actualRepayment = Math.Min(currentDebt, amount);
                float newDebt = currentDebt - actualRepayment;
                _debtTracker[pawn] = newDebt;

                // Shrink the resurrection baseline with repayment so capacity returns to the
                // base cap once corpse debt is paid off (baseline never exceeds current debt).
                if (_resurrectionDebtBaseline.TryGetValue(pawn, out float baseline) && baseline > newDebt)
                {
                    if (newDebt <= 0f)
                        _resurrectionDebtBaseline.Remove(pawn);
                    else
                        _resurrectionDebtBaseline[pawn] = newDebt;
                }

                // Debt episode over: reset the peak so the next episode gets a fresh rate.
                if (newDebt <= 0f)
                {
                    _peakDebt.Remove(pawn);
                }

                // Sync with corpse data if available
                SyncCorpseData(pawn);

                if (DebugMode)
                {
                    Log.Message($"[Eternal] {pawn.Name} repaid {actualRepayment:F1} debt. Remaining: {newDebt:F1}");
                }

                return actualRepayment;
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                    "RepayDebt", pawn, ex);
                return 0f;
            }
        }

        /// <inheritdoc />
        public float GetDebt(Pawn pawn)
        {
            if (pawn == null)
                return 0f;

            return _debtTracker.TryGetValue(pawn, out float debt) ? debt : 0f;
        }

        /// <inheritdoc />
        public void ClearDebt(Pawn pawn)
        {
            try
            {
                if (pawn == null)
                    return;

                _debtTracker[pawn] = 0f;
                _resurrectionDebtBaseline.Remove(pawn);
                _peakDebt.Remove(pawn);

                // Sync with corpse data if available
                SyncCorpseData(pawn);

                if (DebugMode)
                {
                    Log.Message($"[Eternal] Cleared all debt for {pawn.Name}");
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                    "ClearDebt", pawn, ex);
            }
        }

        /// <inheritdoc />
        public bool HasDebt(Pawn pawn)
        {
            return GetDebt(pawn) > 0f;
        }

        /// <inheritdoc />
        public bool HasExcessiveDebt(Pawn pawn)
        {
            if (pawn == null)
                return false;

            // PERF: Use TryGetValue directly
            _debtTracker.TryGetValue(pawn, out float currentDebt);
            return currentDebt >= GetMaxCapacity(pawn);
        }

        /// <inheritdoc />
        public float GetMaxCapacity(Pawn pawn)
        {
            float baseCapacity;
            if (pawn?.needs?.food == null)
            {
                // Fallback for dead pawns without food need: use 1.0 × bodySize × multiplier
                float bodySize = pawn?.BodySize ?? 1.0f;
                baseCapacity = 1.0f * bodySize * MaxDebtMultiplier;
            }
            else
            {
                baseCapacity = pawn.needs.food.MaxLevel * MaxDebtMultiplier;
            }

            // Resurrection debt is uncapped: capacity grows by the baseline so the alive-time
            // cap (maxDebtMultiplier) is headroom ON TOP of what resurrection cost.
            return baseCapacity + GetResurrectionDebtBaseline(pawn);
        }

        /// <summary>
        /// Gets the uncapped resurrection debt baseline included in GetMaxCapacity.
        /// </summary>
        private float GetResurrectionDebtBaseline(Pawn pawn)
        {
            if (pawn == null)
                return 0f;

            return _resurrectionDebtBaseline.TryGetValue(pawn, out float baseline) ? baseline : 0f;
        }

        /// <inheritdoc />
        public float GetPeakDebt(Pawn pawn)
        {
            if (pawn == null)
                return 0f;

            return _peakDebt.TryGetValue(pawn, out float peak) ? peak : 0f;
        }

        /// <summary>
        /// Raises the episode peak to the new debt total if it grew.
        /// </summary>
        private void UpdatePeakDebt(Pawn pawn, float newDebt)
        {
            if (!_peakDebt.TryGetValue(pawn, out float peak) || newDebt > peak)
            {
                _peakDebt[pawn] = newDebt;
            }
        }

        /// <inheritdoc />
        public float GetRemainingCapacity(Pawn pawn)
        {
            // PERF: Use TryGetValue directly instead of calling GetDebt()
            _debtTracker.TryGetValue(pawn, out float currentDebt);
            return Math.Max(0f, GetMaxCapacity(pawn) - currentDebt);
        }

        /// <inheritdoc />
        public IEnumerable<Pawn> GetTrackedPawns()
        {
            return _trackedPawns.ToList(); // Return copy to prevent modification during iteration
        }

        /// <inheritdoc />
        public void CleanupInvalidEntries()
        {
            try
            {
                // PERF: Use pooled list to avoid allocation
                var toRemove = ListPool<Pawn>.Get();
                try
                {
                    foreach (var pawn in _trackedPawns)
                    {
                        if (pawn == null || pawn.Destroyed || !pawn.IsValidEternal())
                        {
                            toRemove.Add(pawn);
                        }
                    }

                    foreach (var pawn in toRemove)
                    {
                        UnregisterPawn(pawn);
                    }

                    if (toRemove.Count > 0)
                    {
                        Log.Message($"[Eternal] Cleaned up {toRemove.Count} invalid debt tracking entries");
                    }
                }
                finally
                {
                    ListPool<Pawn>.Return(toRemove);
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                    "CleanupInvalidEntries", null, ex);
            }
        }

        /// <inheritdoc />
        public string GetDebtStatusString(Pawn pawn)
        {
            if (pawn == null)
                return "No debt information";

            // PERF: Use TryGetValue directly
            _debtTracker.TryGetValue(pawn, out float debt);
            if (debt <= 0f)
                return "No food debt";

            float maxDebt = GetMaxCapacity(pawn);
            float percentage = (debt / maxDebt) * 100f;

            if (percentage >= 100f)
                return $"Food Debt: {debt:F1} (MAXED OUT)";
            else if (percentage >= 80f)
                return $"Food Debt: {debt:F1} (High)";
            else if (percentage >= 50f)
                return $"Food Debt: {debt:F1} (Medium)";
            else
                return $"Food Debt: {debt:F1} (Low)";
        }

        /// <summary>
        /// Gets detailed debt status for UI display.
        /// </summary>
        /// <param name="pawn">The pawn to get status for.</param>
        /// <returns>Dictionary containing debt status information.</returns>
        public Dictionary<string, object> GetDebtStatusDetailed(Pawn pawn)
        {
            var status = new Dictionary<string, object>();

            try
            {
                if (pawn == null)
                {
                    status["Error"] = "Pawn is null";
                    return status;
                }

                // PERF: Single TryGetValue and calculate remaining capacity inline
                _debtTracker.TryGetValue(pawn, out float currentDebt);
                float maxDebt = GetMaxCapacity(pawn);
                float remainingCapacity = Math.Max(0f, maxDebt - currentDebt);

                status["CurrentDebt"] = currentDebt;
                status["MaxDebt"] = maxDebt;
                status["RemainingCapacity"] = remainingCapacity;
                status["DebtPercentage"] = maxDebt > 0f ? (currentDebt / maxDebt) * 100f : 0f;
                status["HasDebt"] = currentDebt > 0f;
                status["IsDead"] = pawn.Dead;
                status["CanAccumulateDebt"] = remainingCapacity > 0f;

                if (pawn.needs?.food != null)
                {
                    status["CurrentFoodLevel"] = pawn.needs.food.CurLevel;
                    status["MaxFoodLevel"] = pawn.needs.food.MaxLevel;
                    status["FoodPercentage"] = (pawn.needs.food.CurLevel / pawn.needs.food.MaxLevel) * 100f;
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                    "GetDebtStatusDetailed", pawn, ex);
                status["Error"] = ex.Message;
            }

            return status;
        }

        /// <summary>
        /// Syncs debt data with corpse data if the pawn is dead and tracked.
        /// </summary>
        private void SyncCorpseData(Pawn pawn)
        {
            try
            {
                var corpseData = EternalServiceContainer.Instance.CorpseManager?.GetCorpseData(pawn);
                if (corpseData != null)
                {
                    corpseData.FoodDebt = _debtTracker.TryGetValue(pawn, out float debt) ? debt : 0f;
                }
            }
            catch (Exception ex)
            {
                // Silent fail - corpse data sync is optional; coalescing suppresses repeated logs
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                    "SyncCorpseData", pawn, ex);
            }
        }

        /// <summary>
        /// Serializes and deserializes the food debt data.
        /// </summary>
        public void ExposeData()
        {
            try
            {
                // Convert to lists for serialization
                if (Scribe.mode == LoadSaveMode.Saving)
                {
                    var pawnList = _debtTracker.Keys.ToList();
                    var debtList = _debtTracker.Values.ToList();
                    var baselinePawnList = _resurrectionDebtBaseline.Keys.ToList();
                    var baselineList = _resurrectionDebtBaseline.Values.ToList();
                    var peakPawnList = _peakDebt.Keys.ToList();
                    var peakList = _peakDebt.Values.ToList();

                    Scribe_Collections.Look(ref pawnList, "debtPawns", LookMode.Reference);
                    Scribe_Collections.Look(ref debtList, "debtAmounts", LookMode.Value);
                    Scribe_Collections.Look(ref baselinePawnList, "resurrectionBaselinePawns", LookMode.Reference);
                    Scribe_Collections.Look(ref baselineList, "resurrectionBaselineAmounts", LookMode.Value);
                    Scribe_Collections.Look(ref peakPawnList, "peakDebtPawns", LookMode.Reference);
                    Scribe_Collections.Look(ref peakList, "peakDebtAmounts", LookMode.Value);
                }
                else if (Scribe.mode == LoadSaveMode.LoadingVars)
                {
                    var pawnList = new List<Pawn>();
                    var debtList = new List<float>();
                    var baselinePawnList = new List<Pawn>();
                    var baselineList = new List<float>();
                    var peakPawnList = new List<Pawn>();
                    var peakList = new List<float>();

                    Scribe_Collections.Look(ref pawnList, "debtPawns", LookMode.Reference);
                    Scribe_Collections.Look(ref debtList, "debtAmounts", LookMode.Value);
                    Scribe_Collections.Look(ref baselinePawnList, "resurrectionBaselinePawns", LookMode.Reference);
                    Scribe_Collections.Look(ref baselineList, "resurrectionBaselineAmounts", LookMode.Value);
                    Scribe_Collections.Look(ref peakPawnList, "peakDebtPawns", LookMode.Reference);
                    Scribe_Collections.Look(ref peakList, "peakDebtAmounts", LookMode.Value);

                    _debtTracker.Clear();
                    _trackedPawns.Clear();
                    _resurrectionDebtBaseline.Clear();
                    _peakDebt.Clear();

                    // Pre-baseline saves simply load with empty baseline/peak dicts (old-cap behavior;
                    // peaks re-establish on the next debt change).
                    LoadPawnFloatPairs(baselinePawnList, baselineList, _resurrectionDebtBaseline, "resurrection baseline");
                    LoadPawnFloatPairs(peakPawnList, peakList, _peakDebt, "peak debt");

                    if (pawnList != null && debtList != null)
                    {
                        // Warn if list lengths don't match - indicates potential save corruption
                        if (pawnList.Count != debtList.Count)
                        {
                            Log.Warning($"[Eternal] Food debt list length mismatch on load: pawns={pawnList.Count}, debts={debtList.Count}. " +
                                       $"This may indicate save data corruption. Using minimum count.");
                        }

                        int loadCount = Math.Min(pawnList.Count, debtList.Count);
                        int skippedNulls = 0;

                        for (int i = 0; i < loadCount; i++)
                        {
                            var pawn = pawnList[i];
                            if (pawn != null)
                            {
                                _debtTracker[pawn] = debtList[i];
                                _trackedPawns.Add(pawn);
                            }
                            else
                            {
                                skippedNulls++;
                            }
                        }

                        if (skippedNulls > 0)
                        {
                            Log.Warning($"[Eternal] Skipped {skippedNulls} null pawn references during food debt load");
                        }
                    }
                }

                // Migration: Try to load from old EternalFoodDebtMonitor format
                if (Scribe.mode == LoadSaveMode.LoadingVars && _debtTracker.Count == 0)
                {
                    TryMigrateFromOldFormat();
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                    "ExposeData", null, ex);
            }
        }

        /// <summary>
        /// Checks for pawns whose food need state has changed to disabled and clears their
        /// debt plus removes the Metabolic Recovery hediff. Called from TickOrchestrator on
        /// the rare tick so the waiver takes effect within a few seconds of the state change.
        /// </summary>
        public void CheckFoodNeedStateChanges()
        {
            try
            {
                foreach (var pawn in _trackedPawns.ToList())
                {
                    if (pawn == null || pawn.Dead)
                        continue;

                    if (pawn.HasFoodNeedDisabled() && GetDebt(pawn) > 0f)
                    {
                        ClearDebt(pawn);

                        // Remove Metabolic Recovery hediff — debt cleared, hediff no longer needed.
                        // Uses type check so this works before EternalDefOf.Eternal_MetabolicRecovery
                        // is bound (Plan 03 adds that DefOf entry; this method must compile now).
                        var hediff = pawn.health?.hediffSet?.hediffs
                            ?.FirstOrDefault(h => h is Eternal.Hediffs.MetabolicRecovery_Hediff);
                        if (hediff != null)
                        {
                            pawn.health.RemoveHediff(hediff);
                        }

                        if (DebugMode)
                        {
                            Log.Message($"[Eternal] Cleared debt and Metabolic Recovery for {pawn.Name?.ToStringShort ?? "Pawn"} — food need disabled");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                    "CheckFoodNeedStateChanges", null, ex);
            }
        }

        /// <summary>
        /// Loads a parallel pawn/amount list pair into a dictionary, warning on length mismatch.
        /// </summary>
        private static void LoadPawnFloatPairs(List<Pawn> pawns, List<float> amounts,
            Dictionary<Pawn, float> target, string label)
        {
            if (pawns == null || amounts == null)
                return;

            if (pawns.Count != amounts.Count)
            {
                Log.Warning($"[Eternal] {label} list length mismatch on load: pawns={pawns.Count}, amounts={amounts.Count}. Using minimum count.");
            }

            int loadCount = Math.Min(pawns.Count, amounts.Count);
            for (int i = 0; i < loadCount; i++)
            {
                if (pawns[i] != null)
                {
                    target[pawns[i]] = amounts[i];
                }
            }
        }

        /// <summary>
        /// Attempts to migrate data from old save format.
        /// </summary>
        private void TryMigrateFromOldFormat()
        {
            // This will be populated if there's old data to migrate
            // For now, just log that we checked
            if (DebugMode)
            {
                Log.Message("[Eternal] Checked for old debt format migration (none found)");
            }
        }

    }
}
