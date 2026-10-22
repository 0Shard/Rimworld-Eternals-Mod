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
using Eternal.Extensions;

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
        public const float MinDrainNutritionPerDay = FoodDebtPolicy.MinDrainNutritionPerDay;

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
            // Native casket suspension freezes metabolism without disabling the food need.
            // Return before either the waiver or any episode mutation can run.
            if (pawn.Suspended)
            {
                return;
            }
            if (pawn.HasFoodNeedDisabled())
            {
                _debtSystem.ClearDebt(pawn);
                return;
            }
            if (_debtSystem.TryCloseUnpayableTerminalDust(pawn))
            {
                return;
            }
            float debt = _debtSystem.GetDebt(pawn);
            if (debt <= 0f)
            {
                return;
            }

            var food = pawn.needs?.food;
            if (food == null)
            {
                return;
            }

            // Check if food is above threshold
            float threshold = food.MaxLevel * _settings.FoodDrainThreshold;
            if (food.CurLevel <= threshold)
            {
                return;
            }

            // Calculate drain rate based on debt level
            float drainRate = CalculateDrainRate(pawn, debt);

            // Scale drain rate by rare tick interval
            // (drainRate is per-tick, but we're called every rareTickRate ticks)
            float toDrain = FoodDebtPolicy.Drain(debt, drainRate, _settings.RareTickRate,
                food.CurLevel, food.MaxLevel, _settings.FoodDrainThreshold);

            if (toDrain > 0f)
            {
                float beforeFood = food.CurLevel;
                food.CurLevel = FoodDebtPolicy.FoodAfterPayment(beforeFood, toDrain, debt, threshold);
                toDrain = Math.Max(0f, beforeFood - food.CurLevel);
                float repaid = _debtSystem.RepayDebt(pawn, toDrain);
                food.CurLevel += Math.Max(0f, toDrain - repaid);
                _debtSystem.TryCloseUnpayableTerminalDust(pawn);

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
            return FoodDebtPolicy.DrainRate(debt, _debtSystem.GetPeakDebt(pawn), _settings.DebtRepaymentDays);
        }
    }
}
