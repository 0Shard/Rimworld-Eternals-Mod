// Relative Path: Eternal/Source/Eternal/Settings/HediffSettingsStore.cs
// Creation Date: 01-01-2025
// Last Edit: 03-01-2026
// Author: 0Shard
// Description: Storage and CRUD operations for hediff settings.
//              No filtering, no UI state - just data management.
//              Relaxed hediff exclusion to include mod hediffs (AA_, zzz_, Abstract).
//              Now supports XML file storage and migration from old ModSettings.

using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Eternal.Settings
{
    /// <summary>
    /// Storage and CRUD operations for hediff settings.
    /// No filtering, no UI state - just data management.
    /// </summary>
    public class HediffSettingsStore : IExposable
    {
        private Dictionary<string, EternalHediffSetting> settings = new Dictionary<string, EternalHediffSetting>();
        private Dictionary<string, EternalHediffSetting> savedSettings = new Dictionary<string, EternalHediffSetting>();
        private bool isInitialized = false;
        public HediffSettingsLoadOutcome XmlLoadOutcome { get; private set; } = HediffSettingsLoadOutcome.Missing;

        public EternalHediffSetting GetExisting(string defName)
            => defName != null && settings.TryGetValue(defName, out var value) ? value : null;

        /// <summary>
        /// Gets a setting by hediff def name.
        /// </summary>
        public EternalHediffSetting Get(string defName)
        {
            EnsureInitialized();

            if (settings.TryGetValue(defName, out var setting))
            {
                return setting;
            }

            return null;
        }

        /// <summary>
        /// Gets or creates a setting for a hediff definition.
        /// </summary>
        public EternalHediffSetting GetOrCreate(HediffDef hediffDef)
        {
            if (hediffDef == null)
            {
                return null;
            }

            EnsureInitialized();

            if (!settings.TryGetValue(hediffDef.defName, out var setting))
            {
                setting = new EternalHediffSetting(hediffDef);
                settings[hediffDef.defName] = setting;
            }
            else
            {
                // Ensure source info is initialized
                setting.EnsureSourceInitialized(hediffDef.defName);
            }

            return setting;
        }

        /// <summary>
        /// Gets or creates a setting by def name.
        /// </summary>
        public EternalHediffSetting GetOrCreate(string defName)
        {
            EnsureInitialized();

            if (!settings.TryGetValue(defName, out var setting))
            {
                var hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
                setting = new EternalHediffSetting(hediffDef);
                settings[defName] = setting;
            }

            return setting;
        }

        /// <summary>
        /// Sets a setting for a hediff.
        /// </summary>
        public void Set(string defName, EternalHediffSetting setting)
        {
            if (setting != null)
            {
                settings[defName] = setting;
            }
        }

        /// <summary>
        /// Removes a setting.
        /// </summary>
        public bool Remove(string defName)
        {
            return settings.Remove(defName);
        }

        /// <summary>
        /// Gets all settings.
        /// </summary>
        public IEnumerable<KeyValuePair<string, EternalHediffSetting>> GetAll()
        {
            EnsureInitialized();
            return settings;
        }

        /// <summary>
        /// Gets all settings with their hediff definitions loaded.
        /// Creates settings on-demand for all hediffs in the database.
        /// </summary>
        public IEnumerable<KeyValuePair<string, EternalHediffSetting>> GetAllWithDefs()
        {
            EnsureInitialized();

            foreach (var hediffDef in DefDatabase<HediffDef>.AllDefs)
            {
                if (hediffDef == null || ShouldExcludeHediff(hediffDef))
                {
                    continue;
                }

                var setting = GetOrCreate(hediffDef);
                yield return new KeyValuePair<string, EternalHediffSetting>(hediffDef.defName, setting);
            }
        }

        /// <summary>
        /// Gets the count of all settings.
        /// </summary>
        public int Count => settings.Count;

        /// <summary>
        /// Checks if a setting exists.
        /// </summary>
        public bool Contains(string defName)
        {
            return settings.ContainsKey(defName);
        }

        /// <summary>
        /// Resets all settings to defaults.
        /// </summary>
        public void ResetAll()
        {
            foreach (var setting in settings.Values)
            {
                setting.ResetToDefaults();
            }
        }

        /// <summary>
        /// Resets settings for a collection of hediff names.
        /// </summary>
        public int ResetMany(IEnumerable<string> defNames)
        {
            int count = 0;
            foreach (var defName in defNames)
            {
                if (settings.TryGetValue(defName, out var setting) && setting.HasCustomSettings())
                {
                    setting.ResetToDefaults();
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// Clears all settings.
        /// </summary>
        public void Clear()
        {
            settings.Clear();
            isInitialized = false;
        }

        /// <summary>
        /// Gets available mod sources from all settings.
        /// </summary>
        public List<string> GetAvailableModSources()
        {
            EnsureInitialized();
            return settings.Values
                .Select(s => s.modSource)
                .Distinct()
                .OrderBy(s => s)
                .ToList();
        }

        #region XML Storage

        /// <summary>
        /// Saves current settings to XML file.
        /// </summary>
        public void SaveToXml()
        {
            if (XmlLoadOutcome == HediffSettingsLoadOutcome.Rejected)
            {
                Log.Warning("[Eternal] Rejected hediff settings source remains protected; reload a valid file or explicitly Reset All to replace it.");
                return;
            }
            var slimSettings = GetCustomizedSettings().ToDictionary(
                kvp => kvp.Key, kvp => new HediffSettingSlim(kvp.Key, kvp.Value));

            HediffSettingsXmlStore.Save(slimSettings);
        }

        private IEnumerable<KeyValuePair<string, EternalHediffSetting>> GetCustomizedSettings()
        {
            return settings.Where(kvp => kvp.Value != null && HediffPersistencePolicy.ShouldPersistSetting(
                DefDatabase<HediffDef>.GetNamedSilentFail(kvp.Key) != null, kvp.Value.HasCustomSettings()));
        }

        /// <summary>
        /// Loads settings from XML file and applies them.
        /// </summary>
        public void LoadFromXml()
        {
            var load = HediffSettingsXmlStore.Load();
            XmlLoadOutcome = load.Outcome;
            if (load.Outcome == HediffSettingsLoadOutcome.Rejected)
            {
                return;
            }
            var slimSettings = load.Settings;

            foreach (var kvp in slimSettings)
            {
                if (kvp.Value != null && !string.IsNullOrEmpty(kvp.Key))
                {
                    var setting = GetOrCreate(kvp.Key);
                    if (setting != null)
                    {
                        kvp.Value.ApplyTo(setting);
                    }
                }
            }

            Log.Message($"[Eternal] Applied {slimSettings.Count} settings from XML file");
        }

        /// <summary>
        /// Resets a single hediff setting to defaults.
        /// </summary>
        public void ResetSingle(string defName)
        {
            if (settings.TryGetValue(defName, out var setting))
            {
                setting.ResetToDefaults();
            }
        }

        /// <summary>
        /// Resets all settings to defaults and deletes the XML file.
        /// </summary>
        public void ResetAllAndDeleteXml()
        {
            // Reset is an explicit recovery choice. Never destroy rejected user bytes if
            // the durable recovery copy cannot be created.
            if (XmlLoadOutcome == HediffSettingsLoadOutcome.Rejected && !HediffSettingsXmlStore.BackupRejectedSource())
            {
                return;
            }
            EnsureInitialized();
            // An absent definition has no authoritative reset default. Explicit reset
            // removes that override; ordinary saves still retain unresolved choices.
            foreach (string name in settings.Keys.Where(name => DefDatabase<HediffDef>.GetNamedSilentFail(name) == null).ToList())
            {
                settings.Remove(name);
            }
            savedSettings.Clear();
            ResetAll();
            HediffSettingsXmlStore.Delete();
            XmlLoadOutcome = HediffSettingsLoadOutcome.Missing;
            Log.Message("[Eternal] Reset all hediff settings to defaults and deleted XML file");
        }

        #endregion

        #region Migration Support

        /// <summary>
        /// Checks if there are old saved settings from ModSettings.
        /// </summary>
        public bool HasOldSavedSettings()
        {
            return savedSettings != null && savedSettings.Count > 0;
        }

        /// <summary>
        /// Gets all old saved settings for migration.
        /// </summary>
        public Dictionary<string, EternalHediffSetting> GetAllOldSettings()
        {
            return savedSettings ?? new Dictionary<string, EternalHediffSetting>();
        }

        /// <summary>
        /// Clears old saved settings after migration.
        /// </summary>
        public void ClearOldSavedSettings()
        {
            savedSettings?.Clear();
            Log.Message("[Eternal] Cleared old ModSettings data after migration");
        }

        #endregion

        /// <summary>
        /// Ensures the store is initialized.
        /// </summary>
        private void EnsureInitialized()
        {
            if (!isInitialized)
            {
                Initialize();
            }
        }

        /// <summary>
        /// Initializes the store.
        /// </summary>
        private void Initialize()
        {
            if (settings == null)
            {
                settings = new Dictionary<string, EternalHediffSetting>();
            }

            if (savedSettings == null)
            {
                savedSettings = new Dictionary<string, EternalHediffSetting>();
            }

            // Load saved settings
            foreach (var kvp in savedSettings)
            {
                if (!settings.ContainsKey(kvp.Key))
                {
                    settings[kvp.Key] = kvp.Value;
                }
                else if (kvp.Value.HasCustomSettings())
                {
                    settings[kvp.Key] = kvp.Value;
                }
            }

            isInitialized = true;

            // Count available hediffs
            int availableCount = DefDatabase<HediffDef>.AllDefsListForReading?.Count ?? 0;
            int excludedCount = DefDatabase<HediffDef>.AllDefsListForReading?.Count(ShouldExcludeHediff) ?? 0;
            int visibleCount = availableCount - excludedCount;

            Log.Message($"[Eternal] HediffSettingsStore initialized - {visibleCount} hediffs available ({excludedCount} excluded)");
        }

        /// <summary>
        /// Checks if a hediff should be excluded from the list.
        /// Only excludes truly debug/test content - be conservative to include mod hediffs.
        /// </summary>
        private bool ShouldExcludeHediff(HediffDef hediffDef)
        {
            if (hediffDef == null)
            {
                return true;
            }

            string defName = hediffDef.defName;

            // Only exclude explicit debug/test patterns
            // NOTE: Removed AA_ (Alpha Animals), zzz_/ZZZ_ (may be legitimate), Abstract (may be real hediffs)
            if (defName.StartsWith("DEBUG_") || defName.StartsWith("Test_") ||
                defName.StartsWith("___"))
            {
                return true;
            }

            // Only exclude explicit debug/test suffixes
            if (defName.EndsWith("_Debug") || defName.EndsWith("_Test") ||
                defName.EndsWith("_Dummy"))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Serializes and deserializes the store data.
        /// </summary>
        public void ExposeData()
        {
            EnsureInitialized();

            if (Scribe.mode == LoadSaveMode.Saving)
            {
                // Keep full record references for callers using the legacy Scribe route.
                savedSettings = GetCustomizedSettings().ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            }

            Scribe_Collections.Look(ref savedSettings, "hediffSettingsSave", LookMode.Value, LookMode.Deep);

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                if (settings == null)
                {
                    settings = new Dictionary<string, EternalHediffSetting>();
                }

                if (savedSettings == null)
                {
                    savedSettings = new Dictionary<string, EternalHediffSetting>();
                }

                isInitialized = false;
            }
        }
    }

    internal static class HediffPersistencePolicy
    {
        internal static bool ShouldPersistSetting(bool definitionResolved, bool hasCustomSettings)
            => !definitionResolved || hasCustomSettings;
    }
}
