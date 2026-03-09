// Relative Path: Eternal/Source/Eternal/Hediffs/MetabolicRecovery_Hediff.cs
// Creation Date: 04-03-2026
// Last Edit: 04-03-2026
// Author: 0Shard
// Description: Custom hediff class for the Metabolic Recovery hediff applied to Eternal pawns
//              when they accumulate food debt during healing. Syncs severity from the food
//              debt tracker (single source of truth), overrides ShouldRemove to defer cleanup
//              to the debt system, and drives hunger rate via XML-defined HediffStage factors.

using Verse;
using Eternal.DI;

namespace Eternal.Hediffs
{
    /// <summary>
    /// Custom hediff that represents an Eternal pawn's metabolic recovery state.
    /// Severity is driven by the food debt ratio (0–1), sourced from the food debt tracker.
    ///
    /// Design notes:
    /// - Severity is NOT persisted independently — it is recalculated from the debt tracker
    ///   on save/load to keep the debt tracker as the single source of truth.
    /// - ShouldRemove returns true only when debt is zero AND the pawn is alive; on dead
    ///   pawns the hediff remains visible until resurrection clears the debt.
    /// - Hunger rate boost is handled by XML HediffStage hungerRateFactor — the C# class
    ///   does not need to override hunger rate directly.
    /// </summary>
    public class MetabolicRecovery_Hediff : HediffWithComps
    {
        // Tick counter for throttling SyncSeverityFromDebt calls
        private int _syncTickCounter = 0;

        // Number of ticks between severity syncs (matches normalTickRate default)
        private const int SyncInterval = 60;

        // Clamp floor prevents severity == 0 auto-removal when debt > 0 (Pitfall 7)
        private const float MinActiveSeverity = 0.01f;

        /// <summary>
        /// Returns true when the debt is cleared and the pawn is alive, signalling that the
        /// hediff can be removed by RimWorld's hediff cleanup logic.
        /// On dead pawns the hediff is preserved — the corpse healing system will clear debt
        /// and trigger removal as part of the resurrection process.
        /// </summary>
        public override bool ShouldRemove
        {
            get
            {
                // Keep hediff on dead pawns until debt is cleared via resurrection flow
                if (pawn == null || pawn.Dead)
                    return false;

                var foodDebtSystem = EternalServiceContainer.Instance?.FoodDebtSystem;
                if (foodDebtSystem == null)
                    return false;

                return !foodDebtSystem.HasDebt(pawn);
            }
        }

        /// <summary>
        /// Returns a stage-contextual label showing the current debt percentage.
        /// </summary>
        public override string SeverityLabel
        {
            get
            {
                if (pawn == null)
                    return base.SeverityLabel;

                var foodDebtSystem = EternalServiceContainer.Instance?.FoodDebtSystem;
                if (foodDebtSystem == null)
                    return base.SeverityLabel;

                float debt = foodDebtSystem.GetDebt(pawn);
                float maxDebt = foodDebtSystem.GetMaxCapacity(pawn);

                if (maxDebt <= 0f)
                    return base.SeverityLabel;

                float pct = (debt / maxDebt) * 100f;
                return $"{pct:F0}% debt";
            }
        }

        /// <summary>
        /// Syncs this hediff's severity with the current food debt ratio.
        /// Severity = debt / maxDebt, clamped to [MinActiveSeverity, 1.0] when debt > 0.
        /// This keeps the hediff stage (and therefore hunger rate) in lockstep with the
        /// debt tracker, which is the single source of truth.
        /// </summary>
        public void SyncSeverityFromDebt()
        {
            if (pawn == null)
                return;

            var foodDebtSystem = EternalServiceContainer.Instance?.FoodDebtSystem;
            if (foodDebtSystem == null)
                return;

            float debt = foodDebtSystem.GetDebt(pawn);
            float maxDebt = foodDebtSystem.GetMaxCapacity(pawn);

            if (debt <= 0f)
            {
                // Debt is zero on a living pawn — let ShouldRemove handle cleanup.
                // Do NOT set severity to 0 here; that triggers immediate auto-removal
                // before ShouldRemove is evaluated and can cause race conditions.
                return;
            }

            if (maxDebt <= 0f)
                return;

            float ratio = debt / maxDebt;
            // Clamp to [MinActiveSeverity, 1.0] — severity 0 causes auto-removal (Pitfall 7)
            Severity = UnityEngine.Mathf.Clamp(ratio, MinActiveSeverity, 1.0f);
        }

        /// <summary>
        /// Ticks each game tick. Syncs severity from debt every SyncInterval ticks.
        /// </summary>
        public override void PostTick()
        {
            base.PostTick();

            _syncTickCounter++;
            if (_syncTickCounter >= SyncInterval)
            {
                _syncTickCounter = 0;
                SyncSeverityFromDebt();
            }
        }

        /// <summary>
        /// Saves/loads hediff state. Severity is intentionally NOT persisted separately —
        /// it will be recalculated from the debt tracker on next tick after load.
        /// The tick counter resets to 0, triggering a sync on the first SyncInterval boundary.
        /// </summary>
        public override void ExposeData()
        {
            base.ExposeData();

            // Severity is recalculated from debt tracker — do not persist it independently.
            // On load, the first tick cycle will call SyncSeverityFromDebt() and correct severity.
            // We reset the counter so the first sync happens promptly.
            if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                _syncTickCounter = SyncInterval - 1; // Sync on the very next tick
            }
        }
    }
}
