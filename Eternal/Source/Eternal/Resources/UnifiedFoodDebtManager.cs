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
using Eternal.DI;
using Eternal.Exceptions;
using Eternal.Interfaces;
using Eternal.Extensions;
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
        private Dictionary<Pawn, NutritionAmount> _debtTracker = new Dictionary<Pawn, NutritionAmount>();

        // Resurrection debt baseline: the uncapped portion of a pawn's debt accrued from corpse
        // healing. GetMaxCapacity returns base cap + baseline, so the alive-time cap
        // (maxDebtMultiplier) applies ON TOP of resurrection debt. Shrinks with repayment.
        private Dictionary<Pawn, NutritionAmount> _resurrectionDebtBaseline = new Dictionary<Pawn, NutritionAmount>();

        // Peak debt of the current episode (max debt since it was last zero). Drives the
        // constant repayment drain rate: peak / (60000 × debtRepaymentDays) per tick, so any
        // debt fully repays within the window. Cleared when debt reaches zero.
        private Dictionary<Pawn, NutritionAmount> _peakDebt = new Dictionary<Pawn, NutritionAmount>();

        // Tracked pawns for monitoring
        private HashSet<Pawn> _trackedPawns = new HashSet<Pawn>();

        // Reference lists must survive LoadingVars until Scribe resolves their pawn IDs.
        // These are temporary buffers; the existing parallel-list XML schema stays unchanged.
        private List<Pawn> scribedDebtPawns;
        private List<float> scribedDebtAmounts;
        private List<Pawn> scribedBaselinePawns;
        private List<float> scribedBaselineAmounts;
        private List<Pawn> scribedPeakPawns;
        private List<float> scribedPeakAmounts;
        private List<string> scribedDebtExact;
        private List<string> scribedBaselineExact;
        private List<string> scribedPeakExact;

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
                if (!IsValidDebtPawn(pawn))
                {
                    Log.Warning($"[Eternal] Attempted to register non-Eternal pawn: {pawn.Name}");
                    return;
                }

                EnsureDebtRecord(pawn);
                _trackedPawns.Add(pawn);
                SyncCorpseData(pawn);

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
                {
                    return;
                }

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

        private void EnsureDebtRecord(Pawn pawn)
        {
            float legacy = _debtTracker.ContainsKey(pawn) ? 0f :
                EternalServiceContainer.Instance.CorpseManager?.GetCorpseData(pawn)?.FoodDebt ?? 0f;
            FoodDebtPolicy.EnsureDebtRecord(pawn, _debtTracker, _resurrectionDebtBaseline, _peakDebt,
                legacy, pawn.HasFoodNeedDisabled());
        }

        /// <inheritdoc />
        public bool AddDebt(Pawn pawn, float amount)
            => AddDebtCore(pawn, amount, false) >= 0f;

        public float AddDebtAndGetAccepted(Pawn pawn, float amount)
            => Math.Max(0f, AddDebtCore(pawn, amount, false));

        private float AddDebtCore(Pawn pawn, float amount, bool resurrection)
        {
            try
            {
                if (pawn == null || !NutritionAmount.IsSupported(amount) || amount <= 0f)
                {
                    return -1f;
                }

                // Waive debt for pawns with food need disabled (genes, hediffs, traits, etc.)
                if (pawn.HasFoodNeedDisabled())
                {
                    if (DebugMode)
                    {
                        Log.Message($"[Eternal] Waived {amount:F3} debt for {pawn.Name?.ToStringShort ?? "Pawn"} - food need disabled");
                    }
                    return 0f;
                }

                EnsureDebtRecord(pawn);
                _debtTracker.TryGetValue(pawn, out var newDebt);
                float accepted;
                if (resurrection)
                {
                    newDebt += NutritionAmount.FromFloat(amount);
                    accepted = amount;
                    _resurrectionDebtBaseline[pawn] = newDebt;
                }
                else
                {
                    accepted = FoodDebtPolicy.AddExact(ref newDebt, GetExactCapacity(pawn), amount);
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

                return accepted;
            }
            catch (Exception ex)
            {
                EternalLogger.HandleException(EternalExceptionCategory.Resurrection,
                    "AddDebtCore", pawn, ex);
                return -1f;
            }
        }

        /// <inheritdoc />
        public bool AddResurrectionDebt(Pawn pawn, float amount)
            => AddDebtCore(pawn, amount, true) >= 0f;

        /// <inheritdoc />
        public float RepayDebt(Pawn pawn, float amount)
        {
            try
            {
                if (pawn == null || !NutritionAmount.IsSupported(amount) || amount <= 0f)
                {
                    return 0f;
                }

                // PERF: Use TryGetValue directly
                if (!_debtTracker.TryGetValue(pawn, out var newDebt) || newDebt.IsZero)
                {
                    return 0f;
                }

                // Calculate actual repayment
                float actualRepayment = FoodDebtPolicy.RepayExact(ref newDebt, amount);
                _debtTracker[pawn] = newDebt;

                // Shrink the resurrection baseline with repayment so capacity returns to the
                // base cap once corpse debt is paid off (baseline never exceeds current debt).
                if (_resurrectionDebtBaseline.TryGetValue(pawn, out var baseline))
                {
                    if (newDebt.IsZero)
                    {
                        _resurrectionDebtBaseline.Remove(pawn);
                    }
                    else
                    {
                        _resurrectionDebtBaseline[pawn] = NutritionAmount.Min(baseline, newDebt);
                    }
                }

                // Debt episode over: reset the peak so the next episode gets a fresh rate.
                if (newDebt.IsZero)
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
        public bool TryCloseUnpayableTerminalDust(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.Destroyed || pawn.Suspended
                || !IsValidDebtPawn(pawn) || pawn.HasFoodNeedDisabled())
            {
                return false;
            }
            var food = pawn.needs?.food;
            if (food == null || !NutritionAmount.IsSupported(food.CurLevel)
                || food.CurLevel > food.MaxLevel || !_debtTracker.TryGetValue(pawn, out var debt)
                || !FoodDebtPolicy.TryCloseTerminalDust(ref debt, food.MaxLevel, Eternal_Mod.GetSettings().foodDrainThreshold))
            {
                return false;
            }
            // A terminal adjustment is distinct from RepayDebt: no food was consumed.
            _debtTracker[pawn] = debt;
            _resurrectionDebtBaseline.Remove(pawn);
            _peakDebt.Remove(pawn);
            SyncCorpseData(pawn);
            return true;
        }

        /// <inheritdoc />
        public float GetDebt(Pawn pawn)
        {
            if (pawn == null)
            {
                return 0f;
            }

            return _debtTracker.TryGetValue(pawn, out var debt) ? debt.ToFloat() : 0f;
        }

        /// <inheritdoc />
        public void ClearDebt(Pawn pawn)
        {
            try
            {
                if (pawn == null)
                {
                    return;
                }

                _debtTracker[pawn] = default;
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
            {
                return false;
            }

            // PERF: Use TryGetValue directly
            return GetRemainingCapacity(pawn) <= 0f;
        }

        /// <inheritdoc />
        public float GetMaxCapacity(Pawn pawn)
            => GetExactCapacity(pawn).ToFloat();

        private NutritionAmount GetExactCapacity(Pawn pawn)
        {
            float foodMaxOrBodySize;
            if (pawn?.needs?.food == null)
            {
                // Fallback for dead pawns without food need: use 1.0 × bodySize × multiplier
                float bodySize = pawn?.BodySize ?? 1.0f;
                foodMaxOrBodySize = bodySize;
            }
            else
            {
                foodMaxOrBodySize = pawn.needs.food.MaxLevel;
            }

            // Resurrection debt is uncapped: capacity grows by the baseline so the alive-time
            // cap (maxDebtMultiplier) is headroom ON TOP of what resurrection cost.
            return NutritionAmount.Product(foodMaxOrBodySize, MaxDebtMultiplier) + GetResurrectionDebtBaseline(pawn);
        }

        /// <summary>
        /// Gets the uncapped resurrection debt baseline included in GetMaxCapacity.
        /// </summary>
        private NutritionAmount GetResurrectionDebtBaseline(Pawn pawn)
        {
            if (pawn == null)
            {
                return default;
            }

            return _resurrectionDebtBaseline.TryGetValue(pawn, out var baseline) ? baseline : default;
        }

        /// <inheritdoc />
        public float GetPeakDebt(Pawn pawn)
        {
            if (pawn == null)
            {
                return 0f;
            }

            return _peakDebt.TryGetValue(pawn, out var peak) ? peak.ToFloat() : 0f;
        }

        /// <summary>
        /// Raises the episode peak to the new debt total if it grew.
        /// </summary>
        private void UpdatePeakDebt(Pawn pawn, NutritionAmount newDebt)
        {
            _peakDebt.TryGetValue(pawn, out var peak);
            _peakDebt[pawn] = NutritionAmount.Max(peak, newDebt);
        }

        /// <inheritdoc />
        public float GetRemainingCapacity(Pawn pawn)
        {
            if (pawn == null)
            {
                return 0f;
            }
            _debtTracker.TryGetValue(pawn, out var currentDebt);
            return NutritionAmount.Difference(GetExactCapacity(pawn), currentDebt).ToFloat();
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
                        if (!IsValidDebtPawn(pawn))
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
            {
                return "No debt information";
            }

            // PERF: Use TryGetValue directly
            float debt = GetDebt(pawn);
            if (debt <= 0f)
            {
                return "No food debt";
            }

            float maxDebt = GetMaxCapacity(pawn);
            float percentage = (debt / maxDebt) * 100f;

            if (percentage >= 100f)
            {
                return $"Food Debt: {debt:F1} (MAXED OUT)";
            }
            else if (percentage >= 80f)
            {
                return $"Food Debt: {debt:F1} (High)";
            }
            else if (percentage >= 50f)
            {
                return $"Food Debt: {debt:F1} (Medium)";
            }
            else
            {
                return $"Food Debt: {debt:F1} (Low)";
            }
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
                float currentDebt = GetDebt(pawn);
                float maxDebt = GetMaxCapacity(pawn);
                float remainingCapacity = GetRemainingCapacity(pawn);

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
                    corpseData.FoodDebt = GetDebt(pawn);
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
                if (Scribe.mode == LoadSaveMode.Saving)
                {
                    scribedDebtPawns = _debtTracker.Keys.ToList();
                    scribedDebtAmounts = _debtTracker.Values.Select(amount => amount.ToFloat()).ToList();
                    scribedDebtExact = _debtTracker.Values.Select(amount => amount.Serialize()).ToList();
                    scribedBaselinePawns = _resurrectionDebtBaseline.Keys.ToList();
                    scribedBaselineAmounts = _resurrectionDebtBaseline.Values.Select(amount => amount.ToFloat()).ToList();
                    scribedBaselineExact = _resurrectionDebtBaseline.Values.Select(amount => amount.Serialize()).ToList();
                    scribedPeakPawns = _peakDebt.Keys.ToList();
                    scribedPeakAmounts = _peakDebt.Values.Select(amount => amount.ToFloat()).ToList();
                    scribedPeakExact = _peakDebt.Values.Select(amount => amount.Serialize()).ToList();
                }
                else if (Scribe.mode == LoadSaveMode.LoadingVars)
                {
                    ClearScribeBuffers();
                    _debtTracker.Clear();
                    _trackedPawns.Clear();
                    _resurrectionDebtBaseline.Clear();
                    _peakDebt.Clear();
                }

                // Dead pawns are Destroyed Things, but remain deep-saved inside their corpse.
                // Explicitly retain those references and expose the same buffers in every phase.
                Scribe_Collections.Look(ref scribedDebtPawns, "debtPawns", saveDestroyedThings: true, lookMode: LookMode.Reference);
                Scribe_Collections.Look(ref scribedDebtAmounts, "debtAmounts", LookMode.Value);
                Scribe_Collections.Look(ref scribedBaselinePawns, "resurrectionBaselinePawns", saveDestroyedThings: true, lookMode: LookMode.Reference);
                Scribe_Collections.Look(ref scribedBaselineAmounts, "resurrectionBaselineAmounts", LookMode.Value);
                Scribe_Collections.Look(ref scribedPeakPawns, "peakDebtPawns", saveDestroyedThings: true, lookMode: LookMode.Reference);
                Scribe_Collections.Look(ref scribedPeakAmounts, "peakDebtAmounts", LookMode.Value);
                Scribe_Collections.Look(ref scribedDebtExact, "debtExactUnits", LookMode.Value);
                Scribe_Collections.Look(ref scribedBaselineExact, "resurrectionBaselineExactUnits", LookMode.Value);
                Scribe_Collections.Look(ref scribedPeakExact, "peakDebtExactUnits", LookMode.Value);

                if (Scribe.mode == LoadSaveMode.PostLoadInit)
                {
                    // Missing baseline/peak nodes in older saves retain their empty-dictionary
                    // defaults. Null-written legacy pawn rows cannot be associated safely.
                    LoadPawnFloatPairs(scribedBaselinePawns, scribedBaselineAmounts, scribedBaselineExact, _resurrectionDebtBaseline, "resurrection baseline");
                    LoadPawnFloatPairs(scribedPeakPawns, scribedPeakAmounts, scribedPeakExact, _peakDebt, "peak debt");
                    LoadPawnFloatPairs(scribedDebtPawns, scribedDebtAmounts, scribedDebtExact, _debtTracker, "food debt", _trackedPawns);
                    ClearScribeBuffers();
                }
                else if (Scribe.mode == LoadSaveMode.Saving)
                {
                    ClearScribeBuffers();
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
                    {
                        continue;
                    }

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
        private static void LoadPawnFloatPairs(List<Pawn> pawns, List<float> amounts, List<string> exactAmounts,
            Dictionary<Pawn, NutritionAmount> target, string label, ISet<Pawn> trackedPawns = null)
        {
            if (pawns == null || amounts == null)
            {
                return;
            }

            if (pawns.Count != amounts.Count)
            {
                Log.Warning($"[Eternal] {label} list length mismatch on load: pawns={pawns.Count}, amounts={amounts.Count}. Using minimum count.");
            }

            int loadCount = Math.Min(pawns.Count, amounts.Count);
            int skippedNulls = 0;
            for (int i = 0; i < loadCount; i++)
            {
                if (pawns[i] != null)
                {
                    string exact = exactAmounts != null && i < exactAmounts.Count ? exactAmounts[i] : null;
                    NutritionAmount amount;
                    if (exact != null && NutritionAmount.TryParse(exact, out amount))
                    {
                        target[pawns[i]] = amount;
                    }
                    else
                    {
                        if (exact != null)
                        {
                            Log.Warning($"[Eternal] Invalid exact {label} row; restoring its legacy float amount.");
                        }
                        if (!NutritionAmount.IsSupported(amounts[i]))
                        {
                            Log.Warning($"[Eternal] Invalid legacy {label} row; no finite nonnegative amount can be restored.");
                            continue;
                        }
                        target[pawns[i]] = NutritionAmount.FromFloat(amounts[i]);
                    }
                    trackedPawns?.Add(pawns[i]);
                }
                else
                {
                    skippedNulls++;
                }
            }
            if (skippedNulls > 0)
            {
                Log.Warning($"[Eternal] Skipped {skippedNulls} unresolved/null pawn references during {label} load; "
                    + "their saved amounts have no recoverable pawn identity.");
            }
        }

        private static bool IsValidDebtPawn(Pawn pawn)
            => pawn != null && (pawn.Dead ? pawn.IsValidEternalCorpse() : pawn.IsValidEternal());

        private void ClearScribeBuffers()
        {
            scribedDebtPawns = null;
            scribedDebtAmounts = null;
            scribedBaselinePawns = null;
            scribedBaselineAmounts = null;
            scribedPeakPawns = null;
            scribedPeakAmounts = null;
            scribedDebtExact = null;
            scribedBaselineExact = null;
            scribedPeakExact = null;
        }

    }
}
