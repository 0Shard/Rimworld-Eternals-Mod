// Relative Path: Eternal/Source/Eternal/Resources/UnifiedDebtAccumulator.cs
// Creation Date: 29-12-2025
// Last Edit: 12-07-2026
// Author: 0Shard
// Description: Unified debt accumulator that consolidates the two debt patterns:
//              - Living pawns: capped at system max capacity (alive cap on top of baseline)
//              - Corpse resurrection: uncapped, raises the resurrection debt baseline

using System;
using Verse;
using Eternal.Interfaces;

namespace Eternal.Resources
{
    /// <summary>
    /// Unified debt accumulator for healing operations.
    /// Provides two modes:
    /// - Standard debt (AddDebt): capped at the system's max capacity for living pawns
    /// - Resurrection debt (AddResurrectionDebt): uncapped corpse-healing debt
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

            // Uses the system's internal max capacity check
            return _debtSystem.AddDebt(pawn, amount);
        }

        /// <inheritdoc/>
        public bool AddResurrectionDebt(Pawn pawn, float amount)
        {
            if (pawn == null || amount <= 0f)
                return false;

            return _debtSystem.AddResurrectionDebt(pawn, amount);
        }
    }
}
