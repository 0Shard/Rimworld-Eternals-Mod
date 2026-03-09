/*
 * Relative Path: Eternal/Source/Eternal/Constants/RegrowthConstants.cs
 * Creation Date: 07-01-2026
 * Last Edit: 07-01-2026
 * Author: 0Shard
 * Description: Constants for the 4-phase biological regrowth system.
 *              Centralizes phase thresholds used across regrowth calculations.
 */

namespace Eternal.Constants
{
    /// <summary>
    /// Constants for the biological 4-phase regrowth system.
    /// Severity thresholds map to regrowth phases.
    /// </summary>
    public static class RegrowthConstants
    {
        /// <summary>
        /// Severity threshold marking end of InitialFormation phase (0-25%).
        /// </summary>
        public const float PHASE_INITIAL_FORMATION = 0.25f;

        /// <summary>
        /// Severity threshold marking end of TissueDevelopment phase (25-50%).
        /// </summary>
        public const float PHASE_TISSUE_DEVELOPMENT = 0.50f;

        /// <summary>
        /// Severity threshold marking end of NerveIntegration phase (50-75%).
        /// </summary>
        public const float PHASE_NERVE_INTEGRATION = 0.75f;

        /// <summary>
        /// Severity threshold marking completion (100%).
        /// </summary>
        public const float PHASE_COMPLETE = 1.0f;

        /// <summary>
        /// Duration of each phase as a fraction of total severity (25%).
        /// </summary>
        public const float PHASE_DURATION = 0.25f;
    }
}
