// Relative Path: Eternal/Source/Eternal.Tests/TestData.cs
// Creation Date: 24-02-2026
// Last Edit: 24-02-2026
// Author: 0Shard
// Description: Single source of truth for all expected constants used in tests.
//              Values must match their production counterparts in SettingsDefaults,
//              UnifiedHediffHealingCalculator, CriticalPartConstants, and EternalExceptionCategory.

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
        public const float DefaultBaseHealingRate = 1.8f;
        public const float DefaultMaxDebtMultiplier = 5.0f;
        public const float DefaultNutritionCostMultiplier = 1.0f;
        public const float DefaultSeverityToNutritionRatio = 0.004f; // 250:1

        public const bool DefaultModEnabled = true;
        public const bool DefaultDebugMode = false;
        public const int DefaultLoggingLevel = 1;
        public const bool DefaultShowRegrowthEffects = true;
        public const bool DefaultShowRegrowthProgress = true;
        public const bool DefaultPauseOnResourceDepletion = true;
        public const float DefaultMinimumNutritionThreshold = 0.1f;
        public const bool DefaultAllowResourceBorrowing = false;
        public const float DefaultFoodDrainThreshold = 0.15f;
        public const float DefaultMinDebtDrainRate = 0.0001f;
        public const float DefaultMaxDebtDrainRate = 0.001f;
        public const int DefaultNormalTickRate = 60;
        public const int DefaultRareTickRate = 250;
        public const int DefaultTraitCheckInterval = 5000;
        public const int DefaultCorpseCheckInterval = 1000;
        public const int DefaultMapCheckInterval = 5000;
        public const bool DefaultEnableIndividualHediffControl = true;
        public const bool DefaultAutoHealEnabled = true;
        public const bool DefaultEnableMapAnchors = true;
        public const int DefaultAnchorGracePeriodTicks = 300;

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
