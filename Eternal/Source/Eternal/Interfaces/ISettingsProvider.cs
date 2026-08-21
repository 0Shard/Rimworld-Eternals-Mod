// Relative Path: Eternal/Source/Eternal/Interfaces/ISettingsProvider.cs
// Creation Date: 29-12-2025
// Last Edit: 16-07-2026
// Author: 0Shard
// Description: Read-only settings boundary for runtime services. It exposes only active
//              controls and keeps removed UI toggles out of dependency-injected consumers.

namespace Eternal.Interfaces
{
    /// <summary>
    /// Provides read-only access to active Eternal settings.
    /// </summary>
    public interface ISettingsProvider
    {
        #region General Settings

        /// <summary>
        /// Debug mode derived from the logging-level choice.
        /// </summary>
        bool DebugMode { get; }

        /// <summary>
        /// Logging level (0=Errors only, 1=Warnings, 2=Info, 3=Debug).
        /// </summary>
        int LoggingLevel { get; }

        #endregion

        #region Healing Settings

        /// <summary>
        /// Base healing rate applied to all hediffs unless overridden per-hediff.
        /// </summary>
        float BaseHealingRate { get; }

        /// <summary>
        /// Whether the Essence hediff displays the Eternal Power label.
        /// </summary>
        bool ShowEternalPowerLabel { get; }

        #endregion

        #region Nutrition Settings

        /// <summary>
        /// Global multiplier for every healing nutrition cost.
        /// </summary>
        float NutritionCostMultiplier { get; }

        #endregion

        #region Food Debt Settings

        /// <summary>
        /// Maximum debt as a multiplier of pawn nutrition capacity.
        /// </summary>
        float MaxDebtMultiplier { get; }

        /// <summary>
        /// Food level threshold below which costs become debt.
        /// </summary>
        float FoodDrainThreshold { get; }

        /// <summary>
        /// In-game days over which a debt episode repays through food-bar drain.
        /// </summary>
        float DebtRepaymentDays { get; }

        #endregion

        #region Performance Settings

        int NormalTickRate { get; }
        int RareTickRate { get; }
        int TraitCheckInterval { get; }
        int CorpseCheckInterval { get; }
        int MapCheckInterval { get; }
        int HealingHistorySweepInterval { get; }

        #endregion

        #region Map Protection Settings

        bool EnableMapAnchors { get; }
        int AnchorGracePeriodTicks { get; }

        #endregion
    }
}
