// file path: Eternal/Source/Eternal/Interfaces/IEternalServices.cs
// Author Name: 0Shard
// Date Created: 03-12-2025
// Description: Facade interface for all Eternal services. Provides unified access point for testing and service location.

using Eternal.Corpse;
using Eternal.Healing;

namespace Eternal.Interfaces
{
    /// <summary>
    /// Facade interface for all Eternal mod services.
    /// Provides a unified access point for service location and enables testing through mocking.
    /// </summary>
    /// <remarks>
    /// Usage patterns:
    /// - Production: Access via Eternal_Component.Current (implements IEternalServices)
    /// - Testing: Create mock implementations for unit testing
    ///
    /// Service lifecycle:
    /// - All services are initialized when Eternal_Component is created (game load)
    /// - Services remain active for the lifetime of the game session
    /// - Services are cleaned up when the game session ends
    /// </remarks>
    public interface IEternalServices
    {
        /// <summary>
        /// Gets the food debt tracking system.
        /// Manages debt accumulation during healing and repayment through food consumption.
        /// </summary>
        IFoodDebtSystem FoodDebtSystem { get; }

        /// <summary>
        /// Gets the healing processor for living pawns.
        /// Handles injury healing, scar removal, and regrowth coordination.
        /// </summary>
        EternalHealingProcessor HealingProcessor { get; }

        /// <summary>
        /// Gets the corpse manager.
        /// Tracks all Eternal corpses globally and manages their lifecycle.
        /// </summary>
        EternalCorpseManager CorpseManager { get; }

        /// <summary>
        /// Gets the corpse healing processor.
        /// Handles healing and resurrection of dead Eternal pawns.
        /// </summary>
        EternalCorpseHealingProcessor CorpseHealingProcessor { get; }

        /// <summary>
        /// Gets the corpse preservation system.
        /// Prevents Eternal corpses from rotting or being destroyed.
        /// </summary>
        EternalCorpsePreservation CorpsePreservation { get; }

        /// <summary>
        /// Gets the map protection system.
        /// Prevents maps containing Eternal corpses from being removed.
        /// </summary>
        EternalMapProtection MapProtection { get; }

        /// <summary>
        /// Gets the regrowth manager.
        /// Coordinates limb regrowth across all Eternal pawns.
        /// </summary>
        EternalRegrowthManager RegrowthManager { get; }
    }
}
