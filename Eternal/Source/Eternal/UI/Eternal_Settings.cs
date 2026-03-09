// Relative Path: Eternal/Source/Eternal/UI/Eternal_Settings.cs
// Creation Date: 01-01-2025
// Last Edit: 04-03-2026
// Author: 0Shard
// Description: Settings data class for Eternal mod configuration. Contains all
//              mod-specific settings and user preferences. UI drawing is delegated
//              to SettingsDrawer, validation to SettingsValidator.
//              Hediff settings now use separate XML file storage with auto-migration.
//              CreateSnapshot() produces an ImmutableSettingsSnapshot for hot-path consumers (PERF-03).

using System;
using UnityEngine;
using Verse;
using Eternal.Infrastructure;
using Eternal.UI.Settings;
using Eternal.Settings;

namespace Eternal
{
    /// <summary>
    /// Default values for all Eternal mod settings.
    /// Used for initialization and per-section reset functionality.
    /// </summary>
    public static class SettingsDefaults
    {
        // General
        public const bool ModEnabled = true;
        public const bool DebugMode = false;
        public const int LoggingLevel = 1;

        // Healing
        /// <summary>
        /// Base healing rate: severity reduction per tick.
        /// Default 1.8f provides fast healing speed for living pawns.
        /// Range: 0.01 - 3.0
        /// </summary>
        public const float BaseHealingRate = 1.8f;
        public const bool ShowRegrowthEffects = true;
        public const bool ShowRegrowthProgress = true;

        // Resources
        public const float NutritionCostMultiplier = 1.0f;
        public const bool PauseOnResourceDepletion = true;
        public const float MinimumNutritionThreshold = 0.1f;
        public const bool AllowResourceBorrowing = false;

        // Food Debt
        public const float MaxDebtMultiplier = 5.0f;
        public const float FoodDrainThreshold = 0.15f;     // Stop instant drain at 15% (UrgentlyHungry)
        public const float MinDebtDrainRate = 0.0001f;     // Drain/tick at 0% debt
        public const float MaxDebtDrainRate = 0.001f;      // Drain/tick at 100% debt
        public const float SeverityToNutritionRatio = 0.004f; // 250:1 ratio (250 severity = 1 nutrition)

        /// <summary>
        /// Maximum hunger rate multiplier applied at full food debt.
        /// Default: 2.0x — at 100% debt the pawn eats twice as fast, repaying debt sooner.
        /// The XML HediffStage hungerRateFactor delivers this cap; this setting documents it.
        /// </summary>
        public const float HungerMultiplierCap = 2.0f;

        /// <summary>
        /// Whether the hunger rate boost from Metabolic Recovery is enabled.
        /// When false, pawns still accumulate debt but the hediff is suppressed.
        /// </summary>
        public const bool HungerBoostEnabled = true;

        /// <summary>
        /// Computes display ratio string from SeverityToNutritionRatio.
        /// Example: 0.004f -> "250 : 1"
        /// </summary>
        public static string GetSeverityToNutritionRatioDisplay()
        {
            int ratioValue = (int)Math.Round(1.0f / SeverityToNutritionRatio);
            return $"{ratioValue} : 1";
        }

        // Performance
        public const int NormalTickRate = 60;
        public const int RareTickRate = 250;
        public const int TraitCheckInterval = 5000;
        public const int CorpseCheckInterval = 1000;
        public const int MapCheckInterval = 5000;

        /// <summary>
        /// Interval in ticks for sweeping stale healing history entries.
        /// Default: 300000 ticks = ~5 in-game days. Range: 60000 (1 day) to 900000 (15 days).
        /// </summary>
        public const int HealingHistorySweepInterval = 300000;

        // Advanced Hediff
        public const bool EnableIndividualHediffControl = true;
        public const bool AutoHealEnabled = true;

        // Map Protection
        public const bool EnableMapAnchors = true;
        public const int AnchorGracePeriodTicks = 300;
        public const bool EnableRoofCollapseProtection = true;
    }

    /// <summary>
    /// Settings data class for Eternal mod configuration.
    /// Contains all mod-specific settings and user preferences.
    /// UI drawing is handled by <see cref="SettingsDrawer"/>.
    /// Validation is handled by <see cref="SettingsValidator"/>.
    /// </summary>
    public class Eternal_Settings : ModSettings
    {
        /// <summary>
        /// Static instance for easy access to settings.
        /// </summary>
        public static Eternal_Settings instance;

        private SettingsDrawer drawer;
        private bool hediffSettingsInitialized = false;

        public Eternal_Settings()
        {
            instance = this;
        }

        /// <summary>
        /// Initializes hediff settings from XML after ExposeData has loaded any old settings.
        /// Called once per session, handles migration if needed.
        /// </summary>
        public void InitializeHediffSettings()
        {
            if (hediffSettingsInitialized)
                return;

            hediffSettingsInitialized = true;

            // Check if migration is needed (old ModSettings data exists but no XML file)
            if (HediffSettingsMigrator.NeedsMigration(hediffManager?.Store))
            {
                HediffSettingsMigrator.Migrate(hediffManager.Store);
            }

            // Load settings from XML file (applies any saved customizations)
            hediffManager?.Store?.LoadFromXml();
        }

        /// <summary>
        /// Saves hediff settings to XML file.
        /// Call this when the settings window is closed or when game is saved.
        /// </summary>
        public void SaveHediffSettings()
        {
            hediffManager?.Store?.SaveToXml();
        }

        #region General Settings

        public bool modEnabled = true;
        public bool debugMode = false;
        public int loggingLevel = 1; // 0=Error, 1=Warning, 2=Info, 3=Debug

        #endregion

        #region Healing Settings

        /// <summary>
        /// Base healing rate applied to all hediffs (unless overridden per-hediff).
        /// Range: 0.01 - 3.0 (default 1.8)
        /// UI displays as ratio format: 250 : 1 (severity : nutrition)
        /// </summary>
        public float baseHealingRate = 1.8f;

        public bool showRegrowthEffects = true;
        public bool showRegrowthProgress = true;

        #endregion

        #region Resource Settings

        public float nutritionCostMultiplier = 1.0f;
        public bool pauseOnResourceDepletion = true;
        public float minimumNutritionThreshold = 0.1f;
        public bool allowResourceBorrowing = false;

        #endregion

        #region Food Debt Settings

        /// <summary>
        /// Maximum debt as a multiplier of pawn's nutrition capacity.
        /// Default: 5.0 (5× nutrition capacity)
        /// </summary>
        public float maxDebtMultiplier = 5.0f;

        /// <summary>
        /// Food level threshold below which healing costs go to debt instead of draining food.
        /// Default: 0.15 (15% = UrgentlyHungry level)
        /// </summary>
        public float foodDrainThreshold = 0.15f;

        /// <summary>
        /// Minimum food drain rate per tick when debt is low.
        /// At 0% debt, food drains at this rate.
        /// </summary>
        public float minDebtDrainRate = 0.0001f;

        /// <summary>
        /// Maximum food drain rate per tick when debt is high.
        /// At 100% debt, food drains at this rate.
        /// </summary>
        public float maxDebtDrainRate = 0.001f;

        /// <summary>
        /// Ratio for converting severity healed to nutrition cost.
        /// Default: 0.004f (250:1 ratio: 250 severity = 1 nutrition)
        /// Range: 0.001 - 0.1 (1000:1 to 10:1)
        /// </summary>
        public float severityToNutritionRatio = 0.004f;

        /// <summary>
        /// Maximum hunger rate multiplier applied at full food debt via Metabolic Recovery hediff.
        /// Default: 2.0 (2× normal hunger at 100% debt)
        /// </summary>
        public float hungerMultiplierCap = SettingsDefaults.HungerMultiplierCap;

        /// <summary>
        /// Whether the hunger rate boost from the Metabolic Recovery hediff is enabled.
        /// When false, pawns still accumulate debt but the hunger acceleration is suppressed.
        /// </summary>
        public bool hungerBoostEnabled = SettingsDefaults.HungerBoostEnabled;

        #endregion

        #region Performance Settings

        public int normalTickRate = 60;
        public int rareTickRate = 250;
        public int traitCheckInterval = 5000;
        public int corpseCheckInterval = 1000;
        public int mapCheckInterval = 5000;

        /// <summary>
        /// Interval in ticks for sweeping stale healing history entries.
        /// Range: 60000 (1 day) to 900000 (15 days). Default: 300000 (~5 days).
        /// </summary>
        public int healingHistorySweepInterval = 300000;

        #endregion


        #region Advanced Hediff Settings

        public EternalHediffManager hediffManager = new EternalHediffManager();
        public bool autoHealEnabled = true;
        public HealingOrder healingOrder = HealingOrder.CheapestFirst;
        public bool enableIndividualHediffControl = true;

        #endregion

        #region Map Protection Settings

        public string mapProtectionAction = "teleport";

        #endregion

        #region Map Protection Settings (Anchors)

        public bool enableMapAnchors = true;
        public int anchorGracePeriodTicks = 300;
        public bool enableRoofCollapseProtection = true;

        #endregion

        #region Per-Section Reset Methods

        /// <summary>
        /// Resets General settings to defaults.
        /// </summary>
        public void ResetGeneralSettings()
        {
            modEnabled = SettingsDefaults.ModEnabled;
            debugMode = SettingsDefaults.DebugMode;
            loggingLevel = SettingsDefaults.LoggingLevel;
        }

        /// <summary>
        /// Resets Healing settings to defaults.
        /// </summary>
        public void ResetHealingSettings()
        {
            baseHealingRate = SettingsDefaults.BaseHealingRate;
            showRegrowthEffects = SettingsDefaults.ShowRegrowthEffects;
            showRegrowthProgress = SettingsDefaults.ShowRegrowthProgress;
        }

        /// <summary>
        /// Resets Resource settings to defaults.
        /// </summary>
        public void ResetResourceSettings()
        {
            nutritionCostMultiplier = SettingsDefaults.NutritionCostMultiplier;
            pauseOnResourceDepletion = SettingsDefaults.PauseOnResourceDepletion;
            minimumNutritionThreshold = SettingsDefaults.MinimumNutritionThreshold;
            allowResourceBorrowing = SettingsDefaults.AllowResourceBorrowing;
        }

        /// <summary>
        /// Resets Food Debt settings to defaults.
        /// </summary>
        public void ResetFoodDebtSettings()
        {
            maxDebtMultiplier = SettingsDefaults.MaxDebtMultiplier;
            foodDrainThreshold = SettingsDefaults.FoodDrainThreshold;
            minDebtDrainRate = SettingsDefaults.MinDebtDrainRate;
            maxDebtDrainRate = SettingsDefaults.MaxDebtDrainRate;
            severityToNutritionRatio = SettingsDefaults.SeverityToNutritionRatio;
            hungerMultiplierCap = SettingsDefaults.HungerMultiplierCap;
            hungerBoostEnabled = SettingsDefaults.HungerBoostEnabled;
        }

        /// <summary>
        /// Resets Performance settings to defaults.
        /// </summary>
        public void ResetPerformanceSettings()
        {
            normalTickRate = SettingsDefaults.NormalTickRate;
            rareTickRate = SettingsDefaults.RareTickRate;
            traitCheckInterval = SettingsDefaults.TraitCheckInterval;
            corpseCheckInterval = SettingsDefaults.CorpseCheckInterval;
            mapCheckInterval = SettingsDefaults.MapCheckInterval;
            healingHistorySweepInterval = SettingsDefaults.HealingHistorySweepInterval;
        }

        /// <summary>
        /// Resets Advanced Hediff settings to defaults.
        /// </summary>
        public void ResetAdvancedHediffSettings()
        {
            enableIndividualHediffControl = SettingsDefaults.EnableIndividualHediffControl;
            autoHealEnabled = SettingsDefaults.AutoHealEnabled;
        }

        /// <summary>
        /// Resets Map Protection settings to defaults.
        /// </summary>
        public void ResetMapProtectionSettings()
        {
            enableMapAnchors = SettingsDefaults.EnableMapAnchors;
            anchorGracePeriodTicks = SettingsDefaults.AnchorGracePeriodTicks;
            enableRoofCollapseProtection = SettingsDefaults.EnableRoofCollapseProtection;
        }

        #endregion

        #region Snapshot

        /// <summary>
        /// Creates an immutable snapshot of all current settings for hot-path consumers.
        /// Capture this once per tick batch to avoid repeated null-checks and property
        /// lookups on the hot path (PERF-03). The returned struct is stack-allocated.
        /// </summary>
        /// <returns>A complete, immutable copy of all settings values.</returns>
        public ImmutableSettingsSnapshot CreateSnapshot()
        {
            return new ImmutableSettingsSnapshot
            {
                General = new ImmutableSettingsSnapshot.GeneralSection
                {
                    ModEnabled  = modEnabled,
                    DebugMode   = debugMode,
                    LoggingLevel = loggingLevel,
                },
                Healing = new ImmutableSettingsSnapshot.HealingSection
                {
                    BaseRate    = baseHealingRate,
                    ShowEffects = showRegrowthEffects,
                    ShowProgress = showRegrowthProgress,
                },
                Resource = new ImmutableSettingsSnapshot.ResourceSection
                {
                    NutritionCostMultiplier  = nutritionCostMultiplier,
                    PauseOnResourceDepletion = pauseOnResourceDepletion,
                    MinimumNutritionThreshold = minimumNutritionThreshold,
                    AllowResourceBorrowing   = allowResourceBorrowing,
                },
                FoodDebt = new ImmutableSettingsSnapshot.FoodDebtSection
                {
                    MaxDebtMultiplier       = maxDebtMultiplier,
                    FoodDrainThreshold      = foodDrainThreshold,
                    MinDebtDrainRate        = minDebtDrainRate,
                    MaxDebtDrainRate        = maxDebtDrainRate,
                    SeverityToNutritionRatio = severityToNutritionRatio,
                    HungerMultiplierCap     = hungerMultiplierCap,
                    HungerBoostEnabled      = hungerBoostEnabled,
                },
                Perf = new ImmutableSettingsSnapshot.PerfSection
                {
                    NormalTickRate              = normalTickRate,
                    RareTickRate                = rareTickRate,
                    TraitCheckInterval          = traitCheckInterval,
                    CorpseCheckInterval         = corpseCheckInterval,
                    MapCheckInterval            = mapCheckInterval,
                    HealingHistorySweepInterval = healingHistorySweepInterval,
                },
                AdvancedHediff = new ImmutableSettingsSnapshot.AdvancedHediffSection
                {
                    AutoHealEnabled              = autoHealEnabled,
                    HealingOrder                 = healingOrder,
                    EnableIndividualHediffControl = enableIndividualHediffControl,
                },
                Map = new ImmutableSettingsSnapshot.MapSection
                {
                    MapProtectionAction        = mapProtectionAction,
                    EnableMapAnchors           = enableMapAnchors,
                    AnchorGracePeriodTicks     = anchorGracePeriodTicks,
                    EnableRoofCollapseProtection = enableRoofCollapseProtection,
                },
            };
        }

        #endregion

        #region UI

        /// <summary>
        /// Draws the settings window. Delegates to SettingsDrawer.
        /// </summary>
        public void DoWindowContents(Rect inRect)
        {
            if (drawer == null)
            {
                drawer = new SettingsDrawer(this);
            }
            drawer.DoWindowContents(inRect);
        }

        #endregion

        #region Serialization

        /// <summary>
        /// Saves and loads settings data.
        /// Hediff settings are now stored in a separate XML file for easier management.
        /// </summary>
        public override void ExposeData()
        {
            base.ExposeData();

            // General settings
            Scribe_Values.Look(ref modEnabled, "modEnabled", true);
            Scribe_Values.Look(ref debugMode, "debugMode", false);
            Scribe_Values.Look(ref loggingLevel, "loggingLevel", 1);

            // Healing settings
            Scribe_Values.Look(ref baseHealingRate, "baseHealingRate", 1.8f);
            Scribe_Values.Look(ref showRegrowthEffects, "showRegrowthEffects", true);
            Scribe_Values.Look(ref showRegrowthProgress, "showRegrowthProgress", true);

            // Resource settings
            Scribe_Values.Look(ref nutritionCostMultiplier, "nutritionCostMultiplier", 1.0f);
            Scribe_Values.Look(ref pauseOnResourceDepletion, "pauseOnResourceDepletion", true);
            Scribe_Values.Look(ref minimumNutritionThreshold, "minimumNutritionThreshold", 0.1f);
            Scribe_Values.Look(ref allowResourceBorrowing, "allowResourceBorrowing", false);

            // Food debt settings
            Scribe_Values.Look(ref maxDebtMultiplier, "maxDebtMultiplier", 5.0f);
            Scribe_Values.Look(ref foodDrainThreshold, "foodDrainThreshold", 0.15f);
            Scribe_Values.Look(ref minDebtDrainRate, "minDebtDrainRate", 0.0001f);
            Scribe_Values.Look(ref maxDebtDrainRate, "maxDebtDrainRate", 0.001f);
            Scribe_Values.Look(ref severityToNutritionRatio, "severityToNutritionRatio", 0.004f);
            Scribe_Values.Look(ref hungerMultiplierCap, "hungerMultiplierCap", SettingsDefaults.HungerMultiplierCap);
            Scribe_Values.Look(ref hungerBoostEnabled, "hungerBoostEnabled", SettingsDefaults.HungerBoostEnabled);

            // Performance settings
            Scribe_Values.Look(ref normalTickRate, "normalTickRate", 60);
            Scribe_Values.Look(ref rareTickRate, "rareTickRate", 250);
            Scribe_Values.Look(ref traitCheckInterval, "traitCheckInterval", 5000);
            Scribe_Values.Look(ref corpseCheckInterval, "corpseCheckInterval", 1000);
            Scribe_Values.Look(ref mapCheckInterval, "mapCheckInterval", 5000);
            Scribe_Values.Look(ref healingHistorySweepInterval, "healingHistorySweepInterval", SettingsDefaults.HealingHistorySweepInterval);

            // Advanced hediff settings
            // Still load hediffManager for migration purposes (reads old saved data)
            Scribe_Deep.Look(ref hediffManager, "hediffManager");
            Scribe_Values.Look(ref autoHealEnabled, "autoHealEnabled", true);
            Scribe_Values.Look(ref healingOrder, "healingOrder", HealingOrder.CheapestFirst);
            Scribe_Values.Look(ref enableIndividualHediffControl, "enableIndividualHediffControl", true);

            // Map protection settings
            Scribe_Values.Look(ref mapProtectionAction, "mapProtectionAction", "teleport");

            // Map protection settings (anchors)
            Scribe_Values.Look(ref enableMapAnchors, "enableMapAnchors", true);
            Scribe_Values.Look(ref anchorGracePeriodTicks, "anchorGracePeriodTicks", 300);
            Scribe_Values.Look(ref enableRoofCollapseProtection, "enableRoofCollapseProtection", true);

            // After loading, initialize hediff settings from XML
            if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // Defer initialization until after all loading is complete
                LongEventHandler.ExecuteWhenFinished(InitializeHediffSettings);
            }

            // NOTE: Hediff settings are saved in Eternal_Mod.WriteSettings() AFTER base.WriteSettings()
            // completes, to avoid nested Scribe context conflicts (SafeSaver manages its own Scribe lifecycle)
        }

        #endregion
    }
}
