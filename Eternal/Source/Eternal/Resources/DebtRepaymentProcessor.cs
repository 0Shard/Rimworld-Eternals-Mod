// Relative Path: Eternal/Source/Eternal/Resources/DebtRepaymentProcessor.cs
// Creation Date: 29-12-2025
// Last Edit: 13-07-2026
// Author: 0Shard
// Description: Tick-based gradual debt repayment by draining food bar.
//              Creates a "leaky food bar" effect when pawn has debt.
//              Drain rate is CONSTANT per debt episode: peakDebt / (60000 × debtRepaymentDays)
//              per tick, so any debt fully repays within the window when food is available.
//              13-07: Drain rate floored at MinDrainNutritionPerDay so tiny episodes/tails
//              clear in minutes instead of always taking the full repayment window.

using System;
using System.Linq;
using Verse;
using Eternal.Interfaces;

namespace Eternal.Resources
{
    /// <summary>
    /// Processes tick-based debt repayment by gradually draining food bar.
    /// Creates a "leaky food bar" effect when pawn has debt.
    /// </summary>
    /// <remarks>
    /// Behavior:
    /// - When food bar is above threshold AND pawn has debt → drain food bar
    /// - Drain rate is constant per debt episode so repayment completes in a fixed window
    /// - Called from TickOrchestrator during rare tick processing
    ///
    /// Formula: drainRate = peakDebt / (60000 × debtRepaymentDays) per tick
    /// </remarks>
    public class DebtRepaymentProcessor
    {
        // Floor for the episode drain rate: without it a residual crumb of debt (its own
        // tiny "episode") drains at a rate sized for that crumb and takes the FULL
        // repayment window — the hediff sits at "0% debt" for a day. ~1 nutrition/day is
        // below natural hunger, so the floor is imperceptible on the food bar.
        public const float MinDrainNutritionPerDay = 1.0f;

        private readonly ISettingsProvider _settings;
        private readonly IFoodDebtSystem _debtSystem;

        /// <summary>
        /// Creates a new debt repayment processor.
        /// </summary>
        /// <param name="settings">Settings provider for drain rate thresholds</param>
        /// <param name="debtSystem">Food debt system for debt tracking</param>
        public DebtRepaymentProcessor(ISettingsProvider settings, IFoodDebtSystem debtSystem)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _debtSystem = debtSystem ?? throw new ArgumentNullException(nameof(debtSystem));
        }

        /// <summary>
        /// Processes debt repayment for all tracked pawns.
        /// Called from TickOrchestrator on rare ticks.
        /// </summary>
        public void ProcessDebtRepayment()
        {
            var trackedPawns = _debtSystem.GetTrackedPawns()
                .Where(p => p != null && !p.Dead && !p.Destroyed)
                .ToList();

            foreach (var pawn in trackedPawns)
            {
                ProcessPawnDebtRepayment(pawn);
            }
        }

        /// <summary>
        /// Processes debt repayment for a single pawn.
        /// </summary>
        /// <param name="pawn">The pawn to process</param>
        private void ProcessPawnDebtRepayment(Pawn pawn)
        {
            float debt = _debtSystem.GetDebt(pawn);
            if (debt <= 0f)
                return;

            var food = pawn.needs?.food;
            if (food == null)
                return;

            // Check if food is above threshold
            float threshold = food.MaxLevel * _settings.FoodDrainThreshold;
            if (food.CurLevel <= threshold)
                return;

            // Calculate drain rate based on debt level
            float drainRate = CalculateDrainRate(pawn, debt);

            // Scale drain rate by rare tick interval
            // (drainRate is per-tick, but we're called every rareTickRate ticks)
            float scaledDrainRate = drainRate * _settings.RareTickRate;

            // Calculate amount to drain this tick
            float drainable = food.CurLevel - threshold;
            float toDrain = Math.Min(scaledDrainRate, drainable);
            toDrain = Math.Min(toDrain, debt); // Don't drain more than debt

            if (toDrain > 0f)
            {
                food.CurLevel -= toDrain;
                float repaid = _debtSystem.RepayDebt(pawn, toDrain);

                if (_settings.DebugMode)
                {
                    float remainingDebt = _debtSystem.GetDebt(pawn);
                    Log.Message($"[Eternal] {pawn.Name?.ToStringShort ?? "Pawn"} debt repayment: " +
                        $"drained {toDrain:F4} (rate: {drainRate:F6}/tick), " +
                        $"repaid {repaid:F4}, remaining debt: {remainingDebt:F2}");
                }
            }
        }

        /// <summary>
        /// Calculates the constant drain rate for the pawn's current debt episode.
        /// </summary>
        /// <param name="pawn">The pawn to calculate for</param>
        /// <param name="debt">Current debt amount</param>
        /// <returns>Drain rate per tick</returns>
        /// <remarks>
        /// Formula: drainRate = peakDebt / (60000 × debtRepaymentDays)
        ///
        /// The rate derives from the episode's PEAK debt (not the remaining debt) so it stays
        /// constant as debt shrinks — a proportional rate would decay exponentially and never
        /// finish. Any debt therefore fully repays within debtRepaymentDays of food availability.
        /// </remarks>
        private float CalculateDrainRate(Pawn pawn, float debt)
        {
            // Peak can lag behind debt if it was never recorded (e.g. pre-update save) — use max.
            float peakDebt = Math.Max(_debtSystem.GetPeakDebt(pawn), debt);
            float repaymentTicks = 60000f * Math.Max(_settings.DebtRepaymentDays, 0.01f);

            // Floor so small episodes finish quickly; ProcessPawnDebtRepayment already clamps
            // the drained amount to the remaining debt and the 15% food floor.
            return Math.Max(peakDebt / repaymentTicks, MinDrainNutritionPerDay / 60000f);
        }
    }
}
