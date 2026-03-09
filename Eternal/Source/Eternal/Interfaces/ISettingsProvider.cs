// Relative Path: Eternal/Source/Eternal/Interfaces/ISettingsProvider.cs
// Creation Date: 29-12-2025
// Last Edit: 04-03-2026
// Author: 0Shard
// Description: Abstraction for accessing Eternal mod settings. Enables constructor injection
//              and eliminates direct static dependencies on Eternal_Mod.settings.

namespace Eternal.Interfaces
{
    /// <summary>
    /// Provides read-only access to Eternal mod settings.
    /// Abstracts away static settings access for better testability and dependency injection.
    /// </summary>
    public interface ISettingsProvider
    {
        #region General Settings

        /// <summary>
        /// Whether the mod is enabled.
        /// </summary>
        bool ModEnabled { get; }

        /// <summary>
        /// Whether debug mode is active.
        /// </summary>
        bool DebugMode { get; }

        /// <summary>
        /// Logging level (0=Error, 1=Warning, 2=Info, 3=Debug).
        /// </summary>
        int LoggingLevel { get; }

        #endregion

        #region Healing Settings

        /// <summary>
        /// Base healing rate applied to all hediffs (unless overridden per-hediff).
        /// Range: 0.001 - 0.1 (default 0.01)
        /// </summary>
        float BaseHealingRate { get; }

        /// <summary>
        /// Whether to show regrowth visual effects.
        /// </summary>
        bool ShowRegrowthEffects { get; }

        /// <summary>
        /// Whether to show regrowth progress indicators.
        /// </summary>
        bool ShowRegrowthProgress { get; }

        #endregion

        #region Resource Settings

        /// <summary>
        /// Multiplier for nutrition costs during healing.
        /// </summary>
        float NutritionCostMultiplier { get; }

        /// <summary>
        /// Whether to pause healing when resources are depleted.
        /// </summary>
        bool PauseOnResourceDepletion { get; }

        /// <summary>
        /// Minimum nutrition threshold before healing pauses.
        /// </summary>
        float MinimumNutritionThreshold { get; }

        /// <summary>
        /// Whether pawns can borrow resources (accumulate debt).
        /// </summary>
        bool AllowResourceBorrowing { get; }

        #endregion

        #region Food Debt Settings

        /// <summary>
        /// Maximum debt as a multiplier of pawn's nutrition capacity.
        /// Default: 5.0 (5× nutrition capacity)
        /// </summary>
        float MaxDebtMultiplier { get; }

        /// <summary>
        /// Food level threshold below which healing costs go to debt instead of draining food.
        /// Default: 0.15 (15% = UrgentlyHungry level)
        /// </summary>
        float FoodDrainThreshold { get; }

        /// <summary>
        /// Minimum food drain rate per tick when debt is low (0% debt).
        /// </summary>
        float MinDebtDrainRate { get; }

        /// <summary>
        /// Maximum food drain rate per tick when debt is high (100% debt).
        /// </summary>
        float MaxDebtDrainRate { get; }

        /// <summary>
        /// Ratio for converting severity healed to nutrition cost.
        /// Default: 0.004f (250:1 ratio: 250 severity = 1 nutrition)
        /// </summary>
        float SeverityToNutritionRatio { get; }

        /// <summary>
        /// Maximum hunger rate multiplier applied at full food debt via Metabolic Recovery hediff.
        /// Default: 2.0 (2× normal hunger at 100% debt)
        /// </summary>
        float HungerMultiplierCap { get; }

        /// <summary>
        /// Whether the hunger rate boost from the Metabolic Recovery hediff is enabled.
        /// </summary>
        bool HungerBoostEnabled { get; }

        #endregion

        #region Performance Settings

        /// <summary>
        /// Tick rate for normal healing (injuries).
        /// </summary>
        int NormalTickRate { get; }

        /// <summary>
        /// Tick rate for rare processing (scars, regrowth, corpse healing).
        /// </summary>
        int RareTickRate { get; }

        /// <summary>
        /// Interval for trait-hediff consistency checks.
        /// </summary>
        int TraitCheckInterval { get; }

        /// <summary>
        /// Interval for corpse preservation checks.
        /// </summary>
        int CorpseCheckInterval { get; }

        /// <summary>
        /// Interval for map protection checks.
        /// </summary>
        int MapCheckInterval { get; }

        #endregion

        #region Advanced Settings

        /// <summary>
        /// Whether individual hediff control is enabled.
        /// </summary>
        bool EnableIndividualHediffControl { get; }

        /// <summary>
        /// Whether auto-healing is enabled.
        /// </summary>
        bool AutoHealEnabled { get; }

        /// <summary>
        /// Current healing order preference.
        /// </summary>
        HealingOrder HealingOrder { get; }

        #endregion

        #region Map Protection Settings

        /// <summary>
        /// Whether map anchors are enabled.
        /// </summary>
        bool EnableMapAnchors { get; }

        /// <summary>
        /// Grace period in ticks for anchor cleanup.
        /// </summary>
        int AnchorGracePeriodTicks { get; }

        #endregion
    }
}
