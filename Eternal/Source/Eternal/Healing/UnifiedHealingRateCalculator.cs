// Relative Path: Eternal/Source/Eternal/Healing/UnifiedHealingRateCalculator.cs
// Creation Date: 29-12-2025
// Last Edit: 21-02-2026
// Author: 0Shard
// Description: Unified healing rate calculator using a single configurable rate from settings.
//              All healing contexts (living pawns, corpses, regrowth) use the same rate.

using Verse;
using Eternal.Interfaces;

namespace Eternal.Healing
{
    /// <summary>
    /// Unified healing rate calculator.
    /// Uses a single configurable rate from settings for all healing contexts:
    /// - Living pawn healing (injuries, diseases)
    /// - Corpse healing (resurrection)
    /// - Scar healing
    /// - Body part regrowth
    /// </summary>
    public class UnifiedHealingRateCalculator : IHealingRateCalculator
    {
        private readonly ISettingsProvider _settings;

        /// <summary>
        /// Creates a new unified healing rate calculator.
        /// </summary>
        /// <param name="settings">Settings provider for configuration values</param>
        public UnifiedHealingRateCalculator(ISettingsProvider settings)
        {
            _settings = settings;
        }

        /// <inheritdoc/>
        public float CalculateHealingPerTick(Pawn pawn)
        {
            float bodySize = GetBodySizeScaling(pawn);
            // Use settings rate directly - no additional multiplier
            float healingRate = _settings.BaseHealingRate;
            return healingRate * bodySize;
        }

        /// <inheritdoc/>
        public float GetBodySizeScaling(Pawn pawn)
        {
            return pawn?.BodySize ?? 1.0f;
        }

    }
}
