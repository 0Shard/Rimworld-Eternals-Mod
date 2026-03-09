// Relative Path: Eternal/Source/Eternal/Settings/HediffSettingsMigrator.cs
// Creation Date: 03-01-2026
// Last Edit: 03-01-2026
// Author: 0Shard
// Description: Migrates old ModSettings hediff data to the new XML format.
//              Also fixes beneficial hediffs that incorrectly had canHeal=true.

using System.Collections.Generic;
using Verse;

namespace Eternal.Settings
{
    /// <summary>
    /// Handles migration from old ModSettings storage to new XML file storage.
    /// Also applies the beneficial hediff fix during migration.
    /// </summary>
    public static class HediffSettingsMigrator
    {
        private static bool _migrationCompleted = false;

        /// <summary>
        /// Checks if migration is needed.
        /// Migration is needed if: old ModSettings data exists AND new XML file doesn't exist.
        /// </summary>
        public static bool NeedsMigration(HediffSettingsStore store)
        {
            if (_migrationCompleted)
                return false;

            if (store == null)
                return false;

            // Check if we have any old settings and no XML file
            bool hasOldSettings = store.HasOldSavedSettings();
            bool hasXmlFile = HediffSettingsXmlStore.FileExists();

            return hasOldSettings && !hasXmlFile;
        }

        /// <summary>
        /// Performs migration from old ModSettings to new XML format.
        /// Also fixes beneficial hediffs that incorrectly had canHeal=true.
        /// </summary>
        public static void Migrate(HediffSettingsStore store)
        {
            if (store == null)
            {
                Log.Warning("[Eternal] Cannot migrate: store is null");
                return;
            }

            Log.Message("[Eternal] Starting hediff settings migration to XML format...");

            var oldSettings = store.GetAllOldSettings();
            var newSettings = new Dictionary<string, HediffSettingSlim>();
            int migratedCount = 0;
            int fixedBeneficialCount = 0;

            foreach (var kvp in oldSettings)
            {
                string defName = kvp.Key;
                EternalHediffSetting oldSetting = kvp.Value;

                if (oldSetting == null || string.IsNullOrEmpty(defName))
                    continue;

                // Get the hediff def to determine correct defaults
                HediffDef hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
                bool defaultCanHeal = GetDefaultCanHeal(hediffDef);

                // Check if this setting was incorrectly healing a beneficial hediff
                bool wasBeneficialBug = hediffDef != null && !hediffDef.isBad && oldSetting.canHeal;
                if (wasBeneficialBug)
                {
                    // Fix: beneficial hediffs should not heal by default
                    oldSetting.canHeal = false;
                    fixedBeneficialCount++;
                    Log.Message($"[Eternal] Fixed beneficial hediff '{defName}' - set canHeal=false");
                }

                // Only migrate settings that are actually customized
                bool isCustomized = IsCustomized(oldSetting, defaultCanHeal);

                if (isCustomized)
                {
                    newSettings[defName] = new HediffSettingSlim
                    {
                        defName = defName,
                        canHeal = oldSetting.canHeal,
                        healingRate = oldSetting.healingRate,
                        nutritionCostMultiplier = oldSetting.nutritionCostMultiplier
                    };
                    migratedCount++;
                }
            }

            // Save to XML file
            if (newSettings.Count > 0)
            {
                HediffSettingsXmlStore.Save(newSettings);
            }

            // Clear old ModSettings data (will be removed on next save)
            store.ClearOldSavedSettings();

            _migrationCompleted = true;

            Log.Message($"[Eternal] Migration complete: {migratedCount} settings migrated, {fixedBeneficialCount} beneficial hediffs fixed");
        }

        /// <summary>
        /// Gets the default canHeal value for a hediff based on its properties.
        /// Mirrors the logic in EternalHediffSetting.ConfigureDefaultFlags().
        /// </summary>
        private static bool GetDefaultCanHeal(HediffDef hediffDef)
        {
            if (hediffDef == null)
                return true; // Unknown hediffs default to healing

            // Beneficial hediffs should NOT heal by default
            if (!hediffDef.isBad)
                return false;

            // Injuries heal by default
            if (hediffDef.injuryProps != null)
                return true;

            // Diseases and conditions require opt-in
            return false;
        }

        /// <summary>
        /// Checks if a setting has been customized (differs from defaults).
        /// </summary>
        private static bool IsCustomized(EternalHediffSetting setting, bool defaultCanHeal)
        {
            // Check the 3 user-configurable fields
            return setting.canHeal != defaultCanHeal ||
                   setting.HasCustomHealingRate ||
                   setting.nutritionCostMultiplier != 1.0f;
        }

        /// <summary>
        /// Forces re-running migration on next check (for testing/manual reset).
        /// </summary>
        public static void ResetMigrationFlag()
        {
            _migrationCompleted = false;
        }
    }
}
