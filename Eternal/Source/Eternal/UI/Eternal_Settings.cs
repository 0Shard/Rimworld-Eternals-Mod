// Relative Path: Eternal/Source/Eternal/UI/Eternal_Settings.cs
// Creation Date: 01-01-2025
// Last Edit: 16-07-2026
// Author: 0Shard
// Description: Settings data class for Eternal mod configuration. Contains all
//              mod-specific settings and user preferences. UI drawing is delegated
//              to SettingsDrawer, validation to SettingsValidator.
//              Hediff settings now use separate XML file storage with auto-migration.
//              CreateSnapshot() remains a Gate 4 compatibility bridge for now.

using UnityEngine;
using Verse;
using Eternal.Infrastructure;
using Eternal.UI.Settings;
using Eternal.Settings;

namespace Eternal
{
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

        /// <summary>
        /// Logging verbosity. Debug mode is derived when this reaches the Debug choice.
        /// </summary>
        public int loggingLevel = SettingsDefaults.LoggingLevel;

        private bool legacyDebugMode;

        /// <summary>
        /// Derived debug state. Keeping this as a read-only compatibility property lets
        /// existing diagnostic call sites follow the new logging-level source of truth.
        /// </summary>
        public bool DebugMode => loggingLevel >= SettingsDefaults.LoggingLevelDebug;

        // Legacy call sites use this read-only alias; it is never serialized or user-editable.
        public bool debugMode => DebugMode;

        #endregion

        #region Healing Settings

        /// <summary>
        /// Base healing rate applied to all hediffs unless overridden per-hediff.
        /// </summary>
        public float baseHealingRate = SettingsDefaults.BaseHealingRate;
        public bool showEternalPowerLabel = SettingsDefaults.ShowEternalPowerLabel;

        #endregion

        #region Resource Settings

        /// <summary>
        /// Global multiplier applied by the food-cost processor to every healing cost.
        /// </summary>
        public float nutritionCostMultiplier = SettingsDefaults.NutritionCostMultiplier;

        #endregion

        #region Food Debt Settings

        /// <summary>
        /// Maximum debt as a multiplier of pawn's nutrition capacity.
        /// Default: 5.0 (5× nutrition capacity)
        /// </summary>
        public float maxDebtMultiplier = SettingsDefaults.MaxDebtMultiplier;

        /// <summary>
        /// Food level threshold below which healing costs go to debt instead of draining food.
        /// Default: 0.15 (15% = UrgentlyHungry level)
        /// </summary>
        public float foodDrainThreshold = SettingsDefaults.FoodDrainThreshold;

        /// <summary>
        /// In-game days a debt episode takes to fully repay via the food-bar drain.
        /// Default: 1.0 (range 0.25 - 5).
        /// </summary>
        public float debtRepaymentDays = SettingsDefaults.DebtRepaymentDays;

        #endregion

        #region Performance Settings

        public int normalTickRate = SettingsDefaults.NormalTickRate;
        public int rareTickRate = SettingsDefaults.RareTickRate;
        public int traitCheckInterval = SettingsDefaults.TraitCheckInterval;
        public int corpseCheckInterval = SettingsDefaults.CorpseCheckInterval;
        public int mapCheckInterval = SettingsDefaults.MapCheckInterval;

        /// <summary>
        /// Interval in ticks for sweeping stale healing history entries.
        /// Range: 60000 (1 day) to 900000 (15 days). Default: 300000 (~5 days).
        /// </summary>
        public int healingHistorySweepInterval = SettingsDefaults.HealingHistorySweepInterval;

        #endregion


        #region Advanced Hediff Settings

        /// <summary>
        /// Per-hediff settings are always available; the removed individual-control toggle
        /// never represented an independent runtime capability.
        /// </summary>
        public EternalHediffManager hediffManager = new EternalHediffManager();

        #endregion

        #region Map Protection Settings (Anchors)

        public bool enableMapAnchors = SettingsDefaults.EnableMapAnchors;
        public int anchorGracePeriodTicks = SettingsDefaults.AnchorGracePeriodTicks;
        public bool enableRoofCollapseProtection = SettingsDefaults.EnableRoofCollapseProtection;

        #endregion

        #region Effects Settings

        public bool consciousnessBuffEnabled = SettingsDefaults.ConsciousnessBuffEnabled;
        public float consciousnessMultiplier = SettingsDefaults.ConsciousnessMultiplier;
        public bool moodBuffEnabled = SettingsDefaults.MoodBuffEnabled;
        public int moodBuffValue = SettingsDefaults.MoodBuffValue;
        public bool populationCapEnabled = SettingsDefaults.PopulationCapEnabled;
        public int populationCap = SettingsDefaults.PopulationCap;

        #endregion

        #region Per-Section Reset Methods

        /// <summary>
        /// Resets General settings to defaults.
        /// </summary>
        public void ResetGeneralSettings()
        {
            loggingLevel = SettingsDefaults.LoggingLevel;
        }

        /// <summary>
        /// Resets Healing settings to defaults.
        /// </summary>
        public void ResetHealingSettings()
        {
            baseHealingRate = SettingsDefaults.BaseHealingRate;
            showEternalPowerLabel = SettingsDefaults.ShowEternalPowerLabel;
        }

        /// <summary>
        /// Resets Resource settings to defaults.
        /// </summary>
        public void ResetResourceSettings()
        {
            nutritionCostMultiplier = SettingsDefaults.NutritionCostMultiplier;
        }

        /// <summary>
        /// Resets Food Debt settings to defaults.
        /// </summary>
        public void ResetFoodDebtSettings()
        {
            maxDebtMultiplier = SettingsDefaults.MaxDebtMultiplier;
            foodDrainThreshold = SettingsDefaults.FoodDrainThreshold;
            debtRepaymentDays = SettingsDefaults.DebtRepaymentDays;
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
        /// Resets Map Protection settings to defaults.
        /// </summary>
        public void ResetMapProtectionSettings()
        {
            enableMapAnchors = SettingsDefaults.EnableMapAnchors;
            anchorGracePeriodTicks = SettingsDefaults.AnchorGracePeriodTicks;
            enableRoofCollapseProtection = SettingsDefaults.EnableRoofCollapseProtection;
        }

        /// <summary>
        /// Resets Consciousness Buff settings to defaults.
        /// </summary>
        public void ResetConsciousnessBuffSettings()
        {
            consciousnessBuffEnabled = SettingsDefaults.ConsciousnessBuffEnabled;
            consciousnessMultiplier = SettingsDefaults.ConsciousnessMultiplier;
        }

        /// <summary>
        /// Resets Mood Buff settings to defaults.
        /// </summary>
        public void ResetMoodBuffSettings()
        {
            moodBuffEnabled = SettingsDefaults.MoodBuffEnabled;
            moodBuffValue = SettingsDefaults.MoodBuffValue;
        }

        /// <summary>
        /// Resets Population Cap settings to defaults.
        /// </summary>
        public void ResetPopulationCapSettings()
        {
            populationCapEnabled = SettingsDefaults.PopulationCapEnabled;
            populationCap = SettingsDefaults.PopulationCap;
        }

        /// <summary>
        /// Resets all Effects settings to defaults (consciousness buff, mood buff, population cap).
        /// </summary>
        public void ResetEffectsSettings()
        {
            ResetConsciousnessBuffSettings();
            ResetMoodBuffSettings();
            ResetPopulationCapSettings();
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
                    ModEnabled  = SettingsDefaults.LegacySnapshotModEnabled,
                    DebugMode   = DebugMode,
                    LoggingLevel = loggingLevel,
                },
                Healing = new ImmutableSettingsSnapshot.HealingSection
                {
                    BaseRate    = baseHealingRate,
                    ShowEffects = SettingsDefaults.LegacySnapshotShowRegrowthEffects,
                    ShowProgress = showEternalPowerLabel,
                },
                Resource = new ImmutableSettingsSnapshot.ResourceSection
                {
                    NutritionCostMultiplier  = nutritionCostMultiplier,
                    PauseOnResourceDepletion = SettingsDefaults.LegacySnapshotPauseOnResourceDepletion,
                    MinimumNutritionThreshold = SettingsDefaults.LegacySnapshotMinimumNutritionThreshold,
                    AllowResourceBorrowing   = SettingsDefaults.LegacySnapshotAllowResourceBorrowing,
                },
                FoodDebt = new ImmutableSettingsSnapshot.FoodDebtSection
                {
                    MaxDebtMultiplier       = maxDebtMultiplier,
                    FoodDrainThreshold      = foodDrainThreshold,
                    DebtRepaymentDays       = debtRepaymentDays,
                    SeverityToNutritionRatio = SettingsDefaults.SeverityToNutritionRatio,
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
                    AutoHealEnabled              = SettingsDefaults.LegacySnapshotAutoHealEnabled,
                    HealingOrder                 = SettingsDefaults.LegacySnapshotHealingOrder,
                    EnableIndividualHediffControl = SettingsDefaults.LegacySnapshotIndividualHediffControl,
                },
                Map = new ImmutableSettingsSnapshot.MapSection
                {
                    MapProtectionAction        = SettingsDefaults.LegacySnapshotMapProtectionAction,
                    EnableMapAnchors           = enableMapAnchors,
                    AnchorGracePeriodTicks     = anchorGracePeriodTicks,
                    EnableRoofCollapseProtection = enableRoofCollapseProtection,
                },
                Effects = new ImmutableSettingsSnapshot.EffectsSection
                {
                    ConsciousnessBuffEnabled = consciousnessBuffEnabled,
                    ConsciousnessMultiplier  = consciousnessMultiplier,
                    MoodBuffEnabled          = moodBuffEnabled,
                    MoodBuffValue            = moodBuffValue,
                    PopulationCapEnabled     = populationCapEnabled,
                    PopulationCap            = populationCap,
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

            // The old debug key is read only during load. It is deliberately not written again;
            // after migration DebugMode is derived from loggingLevel.
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                Scribe_Values.Look(ref legacyDebugMode, "debugMode", false);
            }

            // General settings
            Scribe_Values.Look(ref loggingLevel, "loggingLevel", SettingsDefaults.LoggingLevel);

            // Healing settings. Keep the old key so existing saves retain the label preference.
            Scribe_Values.Look(ref baseHealingRate, "baseHealingRate", SettingsDefaults.BaseHealingRate);
            Scribe_Values.Look(ref showEternalPowerLabel, "showRegrowthProgress", SettingsDefaults.ShowEternalPowerLabel);

            // Resource settings
            Scribe_Values.Look(ref nutritionCostMultiplier, "nutritionCostMultiplier", SettingsDefaults.NutritionCostMultiplier);

            // Food debt settings
            Scribe_Values.Look(ref maxDebtMultiplier, "maxDebtMultiplier", SettingsDefaults.MaxDebtMultiplier);
            Scribe_Values.Look(ref foodDrainThreshold, "foodDrainThreshold", SettingsDefaults.FoodDrainThreshold);
            Scribe_Values.Look(ref debtRepaymentDays, "debtRepaymentDays", SettingsDefaults.DebtRepaymentDays);

            // Performance settings
            Scribe_Values.Look(ref normalTickRate, "normalTickRate", SettingsDefaults.NormalTickRate);
            Scribe_Values.Look(ref rareTickRate, "rareTickRate", SettingsDefaults.RareTickRate);
            Scribe_Values.Look(ref traitCheckInterval, "traitCheckInterval", SettingsDefaults.TraitCheckInterval);
            Scribe_Values.Look(ref corpseCheckInterval, "corpseCheckInterval", SettingsDefaults.CorpseCheckInterval);
            Scribe_Values.Look(ref mapCheckInterval, "mapCheckInterval", SettingsDefaults.MapCheckInterval);
            Scribe_Values.Look(ref healingHistorySweepInterval, "healingHistorySweepInterval", SettingsDefaults.HealingHistorySweepInterval);

            // Read the legacy root only while loading so old saves can migrate. New writes use
            // the versioned XML store exclusively and cannot preserve obsolete per-hediff fields.
            if (Scribe.mode != LoadSaveMode.Saving)
            {
                Scribe_Deep.Look(ref hediffManager, "hediffManager");
            }

            // Map protection settings (anchors)
            Scribe_Values.Look(ref enableMapAnchors, "enableMapAnchors", SettingsDefaults.EnableMapAnchors);
            Scribe_Values.Look(ref anchorGracePeriodTicks, "anchorGracePeriodTicks", SettingsDefaults.AnchorGracePeriodTicks);
            Scribe_Values.Look(ref enableRoofCollapseProtection, "enableRoofCollapseProtection", SettingsDefaults.EnableRoofCollapseProtection);

            // Effects settings
            Scribe_Values.Look(ref consciousnessBuffEnabled, "consciousnessBuffEnabled", SettingsDefaults.ConsciousnessBuffEnabled);
            Scribe_Values.Look(ref consciousnessMultiplier, "consciousnessMultiplier", SettingsDefaults.ConsciousnessMultiplier);
            Scribe_Values.Look(ref moodBuffEnabled, "moodBuffEnabled", SettingsDefaults.MoodBuffEnabled);
            Scribe_Values.Look(ref moodBuffValue, "moodBuffValue", SettingsDefaults.MoodBuffValue);
            Scribe_Values.Look(ref populationCapEnabled, "populationCapEnabled", SettingsDefaults.PopulationCapEnabled);
            Scribe_Values.Look(ref populationCap, "populationCap", SettingsDefaults.PopulationCap);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // A true legacy debug flag is a one-time migration to the explicit Debug level.
                loggingLevel = SettingsDefaults.MigrateLegacyDebugMode(legacyDebugMode, loggingLevel);
                legacyDebugMode = false;

                // Load validation runs once after every persisted field is available. The UI
                // never calls this method during repaint.
                SettingsValidator.ValidateSettings(this);

                // Defer XML initialization until all game definitions are available.
                LongEventHandler.ExecuteWhenFinished(InitializeHediffSettings);
            }

            // NOTE: Hediff settings are saved in Eternal_Mod.WriteSettings() AFTER base.WriteSettings()
            // completes, to avoid nested Scribe context conflicts (SafeSaver manages its own Scribe lifecycle)
        }

        #endregion

        /// <summary>
        /// Validates settings immediately before ModSettings writes them to disk.
        /// </summary>
        public void ValidateBeforeWrite()
        {
            SettingsValidator.ValidateSettings(this);
        }
    }
}
