// Relative Path: Eternal/Source/Eternal.Tests/TestData.cs
// Creation Date: 24-02-2026
// Last Edit: 16-07-2026
// Author: 0Shard
// Description: Single source of truth for all expected constants used in tests.
//              Values must match their production counterparts in SettingsDefaults,
//              UnifiedHediffHealingCalculator, CriticalPartConstants, and EternalExceptionCategory.

using Eternal;
using Eternal.Exceptions;

namespace Eternal.Tests
{
    /// <summary>
    /// Centralised expected values for all test assertions.
    /// No raw literals in test bodies — reference these constants instead.
    /// </summary>
    public static class TestData
    {
        // -----------------------------------------------------------------
        // Stage multipliers — must match UnifiedHediffHealingCalculator switch
        // -----------------------------------------------------------------
        public const float StageMultiplierStage0 = 1.0f;
        public const float StageMultiplierStage1 = 0.8f;
        public const float StageMultiplierStage2 = 0.6f;
        public const float StageMultiplierStage3 = 0.4f;
        public const float StageMultiplierStage4Plus = 0.2f;

        // -----------------------------------------------------------------
        // Settings defaults — must match SettingsDefaults
        // -----------------------------------------------------------------
        public const float DefaultBaseHealingRate = SettingsDefaults.BaseHealingRate;
        public const float DefaultMaxDebtMultiplier = SettingsDefaults.MaxDebtMultiplier;
        public const float DefaultNutritionCostMultiplier = SettingsDefaults.NutritionCostMultiplier;
        public const float DefaultSeverityToNutritionRatio = SettingsDefaults.SeverityToNutritionRatio;

        // Legacy snapshot contracts remain until Gate 4 removes that snapshot surface.
        public const bool DefaultModEnabled = SettingsDefaults.LegacySnapshotModEnabled;
        public const bool DefaultDebugMode = SettingsDefaults.DebugMode;
        public const int DefaultLoggingLevel = SettingsDefaults.LoggingLevel;
        public const bool DefaultShowRegrowthEffects = SettingsDefaults.LegacySnapshotShowRegrowthEffects;
        public const bool DefaultShowRegrowthProgress = SettingsDefaults.ShowEternalPowerLabel;
        public const bool DefaultPauseOnResourceDepletion = SettingsDefaults.LegacySnapshotPauseOnResourceDepletion;
        public const float DefaultMinimumNutritionThreshold = SettingsDefaults.LegacySnapshotMinimumNutritionThreshold;
        public const bool DefaultAllowResourceBorrowing = SettingsDefaults.LegacySnapshotAllowResourceBorrowing;
        public const float DefaultFoodDrainThreshold = SettingsDefaults.FoodDrainThreshold;
        public const float DefaultDebtRepaymentDays = SettingsDefaults.DebtRepaymentDays;
        public const int DefaultNormalTickRate = SettingsDefaults.NormalTickRate;
        public const int DefaultRareTickRate = SettingsDefaults.RareTickRate;
        public const int DefaultTraitCheckInterval = SettingsDefaults.TraitCheckInterval;
        public const int DefaultCorpseCheckInterval = SettingsDefaults.CorpseCheckInterval;
        public const int DefaultMapCheckInterval = SettingsDefaults.MapCheckInterval;
        public const bool DefaultEnableIndividualHediffControl = SettingsDefaults.LegacySnapshotIndividualHediffControl;
        public const bool DefaultAutoHealEnabled = SettingsDefaults.LegacySnapshotAutoHealEnabled;
        public const bool DefaultEnableMapAnchors = SettingsDefaults.EnableMapAnchors;
        public const int DefaultAnchorGracePeriodTicks = SettingsDefaults.AnchorGracePeriodTicks;

        // -----------------------------------------------------------------
        // Critical part sequence — must match CriticalPartConstants.RegrowthSequence
        // -----------------------------------------------------------------
        public static readonly string[] CriticalPartSequence = { "Neck", "Head", "Skull", "Brain" };

        // -----------------------------------------------------------------
        // Floating-point comparison tolerance
        // -----------------------------------------------------------------
        public const float FloatTolerance = 0.0001f;

        /// <summary>Precision digits for xUnit Assert.Equal(expected, actual, precision).</summary>
        public const int FloatPrecision = 4;

        // -----------------------------------------------------------------
        // Body size defaults for IPawnData mocks
        // -----------------------------------------------------------------
        public const float DefaultBodySize = 1.0f;

        // -----------------------------------------------------------------
        // Exception categories — severity classification
        // Error-level: DataInconsistency, GameStateInvalid, InternalError, HediffSwap, Resurrection, CorpseTracking
        // Warning-level: CompatibilityFailure, ConfigurationError, Regrowth, Snapshot, MapProtection
        // -----------------------------------------------------------------
        public static readonly EternalExceptionCategory[] ErrorLevelCategories =
        {
            EternalExceptionCategory.DataInconsistency,
            EternalExceptionCategory.GameStateInvalid,
            EternalExceptionCategory.InternalError,
            EternalExceptionCategory.HediffSwap,
            EternalExceptionCategory.Resurrection,
            EternalExceptionCategory.CorpseTracking,
        };

        public static readonly EternalExceptionCategory[] WarningLevelCategories =
        {
            EternalExceptionCategory.CompatibilityFailure,
            EternalExceptionCategory.ConfigurationError,
            EternalExceptionCategory.Regrowth,
            EternalExceptionCategory.Snapshot,
            EternalExceptionCategory.MapProtection,
        };

        /// <summary>Total number of enum values in EternalExceptionCategory.</summary>
        public const int ExceptionCategoryCount = 11;
    }
}
