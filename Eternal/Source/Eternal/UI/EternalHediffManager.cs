// Relative Path: Eternal/Source/Eternal/UI/EternalHediffManager.cs
// Creation Date: 01-01-2025
// Last Edit: 16-07-2026
// Author: 0Shard
// Description: Coordinator for hediff settings management. Delegates to specialized
//              classes for storage, filtering, selection, and bulk operations.
//              Statistics now track HealingHediffs (canHeal=true) instead of enabled count.
//              PERF-02: Filter result cache with dirty flag. The 200ms debounce was removed —
//              it reset per keystroke, so rapid typing showed stale results indefinitely
//              ("filters not taking effect"). With HediffDefTextCache the rebuild is cheap
//              enough to run on the frame the filter changes.

using Eternal.Compatibility;
using System.Collections.Generic;
using System.Linq;
using Eternal.Settings;
using Verse;

namespace Eternal
{
    /// <summary>
    /// Coordinator for hediff settings management.
    /// Delegates to specialized classes for storage, filtering, and selection.
    /// </summary>
    public class EternalHediffManager : IExposable
    {
        // Component classes - use these directly for specific operations
        public HediffSettingsStore Store { get; } = new HediffSettingsStore();
        public HediffFilterState FilterState { get; } = new HediffFilterState();
        public HediffSelectionManager Selection { get; } = new HediffSelectionManager();

        // PERF-02: Filter result cache with dirty flag (rebuild only when something changed)
        private List<KeyValuePair<string, EternalHediffSetting>> _filteredCache;
        private bool _filterDirty = true;

        #region Filtered Hediffs

        /// <summary>
        /// Gets all hediff settings that match the current filters.
        /// Non-cached version — use for one-shot operations (e.g. SelectAll, ResetFiltered).
        /// </summary>
        public IEnumerable<KeyValuePair<string, EternalHediffSetting>> GetFilteredHediffs()
        {
            return HediffFilterEngine.Filter(Store, FilterState);
        }

        /// <summary>
        /// Gets cached filtered results. Rebuilds on the frame the filter was marked dirty.
        /// Call from render loop instead of GetFilteredHediffs().
        /// </summary>
        public IReadOnlyList<KeyValuePair<string, EternalHediffSetting>> GetFilteredHediffsCached()
        {
            if (_filterDirty || _filteredCache == null)
            {
                _filteredCache = HediffFilterEngine.FilterToList(Store, FilterState);
                _filterDirty = false;
            }

            return _filteredCache;
        }

        /// <summary>Marks filter cache dirty. Call when filter state or hediff settings change.</summary>
        public void MarkFilterDirty()
        {
            _filterDirty = true;
        }

        #endregion

        #region Settings CRUD

        /// <summary>
        /// Gets the setting for a specific hediff.
        /// </summary>
        public EternalHediffSetting GetHediffSetting(string hediffDefName)
        {
            return Store.GetOrCreate(hediffDefName);
        }

        /// <summary>
        /// Gets the setting for a specific hediff definition.
        /// </summary>
        public EternalHediffSetting GetHediffSetting(HediffDef hediffDef)
        {
            if (hediffDef == null)
            {
                return null;
            }

            return Store.GetOrCreate(hediffDef);
        }

        #endregion

        #region Reset Operations

        /// <summary>
        /// Resets all hediff settings to defaults and deletes the XML file.
        /// </summary>
        public void ResetAllToDefaults()
        {
            Store.ResetAllAndDeleteXml();
            Selection.ClearAll();
            MarkFilterDirty();
        }

        /// <summary>
        /// Resets a single hediff setting to its default.
        /// </summary>
        public void ResetSingleHediff(string hediffDefName)
        {
            Store.ResetSingle(hediffDefName);
            MarkFilterDirty();
        }

        /// <summary>
        /// Resets hediffs matching current filters to defaults.
        /// </summary>
        public int ResetFiltered()
        {
            var filtered = GetFilteredHediffs()
                .Where(kvp => kvp.Value.HasCustomSettings())
                .Select(kvp => kvp.Key);
            int result = Store.ResetMany(filtered);
            MarkFilterDirty();
            return result;
        }

        #endregion

        #region Selection

        /// <summary>
        /// Gets all selected hediff names.
        /// </summary>
        public IEnumerable<string> GetSelectedHediffs()
        {
            return Selection.GetSelected();
        }

        /// <summary>
        /// Sets the selection state for a hediff.
        /// </summary>
        public void SetHediffSelected(string hediffDefName, bool selected)
        {
            if (selected)
            {
                Selection.Select(hediffDefName);
            }
            else
            {
                Selection.Deselect(hediffDefName);
            }
        }

        /// <summary>
        /// Gets the selection state for a hediff.
        /// </summary>
        public bool IsHediffSelected(string hediffDefName)
        {
            return Selection.IsSelected(hediffDefName);
        }

        /// <summary>
        /// Selects or deselects all hediffs.
        /// </summary>
        public void SelectAllHediffs(bool select)
        {
            if (select)
            {
                Selection.SelectAll(GetFilteredHediffs().Select(kvp => kvp.Key).ToList());
            }
            else
            {
                Selection.ClearAll();
            }
        }

        /// <summary>
        /// Gets the count of selected hediffs.
        /// </summary>
        public int GetSelectedCount()
        {
            return Selection.Count;
        }

        #endregion

        #region Filters

        /// <summary>
        /// Sets the search filter.
        /// </summary>
        public void SetSearchFilter(string searchTerm)
        {
            FilterState.SearchTerm = searchTerm ?? "";
            MarkFilterDirty();
        }

        /// <summary>
        /// Sets the category filter.
        /// </summary>
        public void SetCategoryFilter(HediffCategory category)
        {
            FilterState.CategoryFilter = category;
            MarkFilterDirty();
        }

        /// <summary>
        /// Sets whether to show only enabled hediffs.
        /// </summary>
        public void SetShowOnlyEnabled(bool showOnlyEnabled)
        {
            FilterState.ShowOnlyEnabled = showOnlyEnabled;
            MarkFilterDirty();
        }

        /// <summary>
        /// Sets whether to show only hediffs with custom settings.
        /// </summary>
        public void SetShowOnlyCustom(bool showOnlyCustom)
        {
            FilterState.ShowOnlyCustom = showOnlyCustom;
            MarkFilterDirty();
        }

        /// <summary>
        /// Sets the mod source filter.
        /// </summary>
        public void SetModSourceFilter(string modSource)
        {
            FilterState.ModSourceFilter = modSource ?? "";
            MarkFilterDirty();
        }

        /// <summary>
        /// Sets the source type filter.
        /// </summary>
        public void SetSourceTypeFilter(bool baseGame, bool dlc, bool mods)
        {
            FilterState.SetSourceTypeFilter(baseGame, dlc, mods);
            MarkFilterDirty();
        }

        /// <summary>
        /// Sets property filters.
        /// </summary>
        public void SetPropertyFilters(bool isBad, bool isLethal, bool isTendable, bool isChronic, bool beneficial = false)
        {
            FilterState.SetPropertyFilters(isBad, isLethal, isTendable, isChronic, beneficial);
            MarkFilterDirty();
        }

        /// <summary>
        /// Gets available mod sources from all hediffs.
        /// </summary>
        public List<string> GetAvailableModSources()
        {
            return Store.GetAvailableModSources();
        }

        /// <summary>
        /// Clears all filters.
        /// </summary>
        public void ClearAllFilters()
        {
            FilterState.ClearAll();
            MarkFilterDirty();
        }

        #endregion

        #region Group Operations

        /// <summary>
        /// Applies settings to a group of hediffs.
        /// </summary>
        public void ApplySettingsToGroup(IEnumerable<string> hediffNames, EternalHediffSetting template)
        {
            ApplySettingsToGroupWithCount(hediffNames, template);
        }

        internal int ApplySettingsToGroupWithCount(IEnumerable<string> hediffNames, EternalHediffSetting template)
        {
            if (template == null)
            {
                return 0;
            }

            int affected = 0;
            foreach (string name in hediffNames)
            {
                var setting = GetHediffSetting(name);
                if (setting != null && !RjwHealthPolicy.IsProtected(DefDatabase<HediffDef>.GetNamedSilentFail(name)))
                {
                    affected++;
                    if (!Eternal.Healing.HediffHealingConfig.IsRegrowthManaged(DefDatabase<HediffDef>.GetNamedSilentFail(name)))
                    {
                        setting.canHeal = template.canHeal;
                        setting.noThreshold = template.noThreshold;
                    }
                    setting.nutritionCostMultiplier = SettingsDefaults.NormalizeNutritionMultiplier(template.nutritionCostMultiplier);
                    setting.nutritionSeverityRangeOverride = SettingsDefaults.NormalizeNutritionRange(template.nutritionSeverityRangeOverride);
                    setting.healingRate = template.healingRate;  // Copy healing rate (may be custom or USE_GLOBAL_RATE)
                    setting.allowedCategories = template.allowedCategories;
                }
            }

            MarkFilterDirty();
            return affected;
        }

        #endregion

        #region Statistics

        /// <summary>
        /// Gets statistics about the current configuration.
        /// </summary>
        public HediffStatistics GetStatistics()
        {
            var allSettings = Store.GetAll().ToList();

            return new HediffStatistics
            {
                TotalHediffs = allSettings.Count,
                HealingHediffs = allSettings.Count(kvp => RjwHealthPolicy.EffectiveCanHeal(
                    DefDatabase<HediffDef>.GetNamedSilentFail(kvp.Key), kvp.Value)),  // Count actual healing, not just enabled
                BeneficialHediffs = allSettings.Count(kvp => kvp.Value.isBeneficial),
                CustomizedHediffs = allSettings.Count(kvp => kvp.Value.HasCustomSettings()),
                Injuries = allSettings.Count(kvp => kvp.Value.isInjuryFilter),
                Diseases = allSettings.Count(kvp => kvp.Value.isDiseaseFilter),
                Conditions = allSettings.Count(kvp => kvp.Value.isConditionFilter)
            };
        }

        /// <summary>
        /// Gets the total number of hediff configurations.
        /// </summary>
        public int GetTotalCount()
        {
            return Store.Count;
        }

        /// <summary>
        /// Gets the number of hediffs with custom (non-default) settings.
        /// </summary>
        public int GetConfiguredCount()
        {
            return Store.GetAll().Count(kvp => kvp.Value.HasCustomSettings());
        }

        #endregion

        #region Serialization

        /// <summary>
        /// Serializes and deserializes the manager data.
        /// </summary>
        public void ExposeData()
        {
            Store.ExposeData();
        }

        #endregion
    }

    /// <summary>
    /// Statistics about the current hediff configuration.
    /// </summary>
    public class HediffStatistics
    {
        public int TotalHediffs { get; set; }
        public int HealingHediffs { get; set; }     // Count where canHeal=true (actual healing)
        public int BeneficialHediffs { get; set; }  // Count of beneficial (non-bad) hediffs
        public int CustomizedHediffs { get; set; }
        public int Injuries { get; set; }
        public int Diseases { get; set; }
        public int Conditions { get; set; }

        // Legacy property for backwards compatibility
    }
}
