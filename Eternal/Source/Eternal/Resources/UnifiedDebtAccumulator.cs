// Relative Path: Eternal/Source/Eternal/Resources/UnifiedDebtAccumulator.cs
// Creation Date: 29-12-2025
// Last Edit: 13-01-2026
// Author: 0Shard
// Description: Unified debt accumulator that consolidates the two debt patterns:
//              - Living pawns: uses system 5× max capacity
//              - Corpse resurrection: additional 2× per-resurrection cap

using System;
using Verse;
using Eternal.Interfaces;

namespace Eternal.Resources
{
    /// <summary>
    /// Unified debt accumulator for healing operations.
    /// Provides two modes:
    /// - Standard debt (AddDebt): uses system's 5× max capacity for living pawns
    /// - Resurrection-capped debt (AddDebtWithResurrectionCap): additional 2× per-resurrection cap
    /// </summary>
    public class UnifiedDebtAccumulator : IDebtAccumulator
    {
        private readonly IFoodDebtSystem _debtSystem;

        /// <summary>
        /// Creates a new unified debt accumulator.
        /// </summary>
        /// <param name="debtSystem">The food debt system to use for actual debt tracking</param>
        public UnifiedDebtAccumulator(IFoodDebtSystem debtSystem)
        {
            _debtSystem = debtSystem ?? throw new ArgumentNullException(nameof(debtSystem));
        }

        /// <inheritdoc/>
        public bool AddDebt(Pawn pawn, float amount)
        {
            if (pawn == null || amount <= 0f)
                return false;

            // Uses the system's internal 5× max capacity check
            return _debtSystem.AddDebt(pawn, amount);
        }

        /// <inheritdoc/>
        public float AddDebtWithResurrectionCap(Pawn pawn, float amount, float resurrectionCap)
        {
            if (pawn == null || amount <= 0f)
                return 0f;

            float currentDebt = _debtSystem.GetDebt(pawn);
            float remainingCapacity = resurrectionCap - currentDebt;

            if (remainingCapacity <= 0f)
            {
                // Already at resurrection cap, don't add more debt
                return 0f;
            }

            // Cap the amount to the remaining resurrection capacity
            float cappedAmount = Math.Min(amount, remainingCapacity);

            // Also respect the system's 5× max capacity
            if (_debtSystem.AddDebt(pawn, cappedAmount))
            {
                return cappedAmount;
            }

            // System rejected the debt (at 5× capacity)
            return 0f;
        }
    }
}
