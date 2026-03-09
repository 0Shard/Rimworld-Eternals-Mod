// Relative Path: Eternal/Source/Eternal/Resources/DebtRepaymentProcessor.cs
// Creation Date: 29-12-2025
// Last Edit: 29-12-2025
// Author: 0Shard
// Description: Tick-based gradual debt repayment by draining food bar.
//              Creates a "leaky food bar" effect when pawn has debt.
//              Drain rate scales with debt level: higher debt = faster drain.

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
    /// - Drain rate scales with debt level (higher debt = faster drain)
    /// - Min/max drain rate thresholds from settings
    /// - Called from TickOrchestrator during rare tick processing
    ///
    /// Formula: drainRate = minRate + (debtRatio × (maxRate - minRate))
    /// </remarks>
    public class DebtRepaymentProcessor
    {
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
        /// Calculates drain rate based on debt level.
        /// Higher debt = faster drain rate.
        /// </summary>
        /// <param name="pawn">The pawn to calculate for</param>
        /// <param name="debt">Current debt amount</param>
        /// <returns>Drain rate per tick</returns>
        /// <remarks>
        /// Formula: drainRate = minRate + (debtRatio × (maxRate - minRate))
        ///
        /// Examples (with default min=0.0001, max=0.001):
        /// - 0% debt:   0.0001/tick (min rate)
        /// - 50% debt:  0.00055/tick (midpoint)
        /// - 100% debt: 0.001/tick (max rate)
        /// </remarks>
        private float CalculateDrainRate(Pawn pawn, float debt)
        {
            float maxDebt = _debtSystem.GetMaxCapacity(pawn);
            if (maxDebt <= 0f)
                return _settings.MinDebtDrainRate;

            float debtRatio = Math.Min(1f, debt / maxDebt);
            float minRate = _settings.MinDebtDrainRate;
            float maxRate = _settings.MaxDebtDrainRate;

            return minRate + (debtRatio * (maxRate - minRate));
        }
    }
}
