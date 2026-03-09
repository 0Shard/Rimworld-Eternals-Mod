// Relative Path: Eternal/Source/Eternal/UI/EternalHediffManager.cs
// Creation Date: 01-01-2025
// Last Edit: 19-02-2026
// Author: 0Shard
// Description: Coordinator for hediff settings management. Delegates to specialized
//              classes for storage, filtering, selection, and bulk operations.
//              Statistics now track HealingHediffs (canHeal=true) instead of enabled count.
//              PERF-02: Added filter result cache with dirty flag + 200ms debounce.

using System.Collections.Generic;
using System.Linq;
using Eternal.Settings;
using UnityEngine;
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

        // PERF-02: Filter result cache with debounce
        private List<KeyValuePair<string, EternalHediffSetting>> _filteredCache;
        private bool _filterDirty = true;
        private float _filterChangedAt = -0.2f; // Negative offset ensures immediate first build
        private const float FILTER_DEBOUNCE_SECONDS = 0.2f;

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
        /// Gets cached filtered results. Rebuilds after debounce period when dirty.
        /// Call from render loop instead of GetFilteredHediffs().
        /// </summary>
        public IReadOnlyList<KeyValuePair<string, EternalHediffSetting>> GetFilteredHediffsCached()
        {
            bool debounceElapsed = (Time.realtimeSinceStartup - _filterChangedAt) >= FILTER_DEBOUNCE_SECONDS;

            if (_filterDirty && debounceElapsed)
            {
                _filteredCache = HediffFilterEngine.FilterToList(Store, FilterState);
                _filterDirty = false;
            }

            // Null-coalescing initial build (handles first call before debounce fires)
            return _filteredCache ?? (_filteredCache = HediffFilterEngine.FilterToList(Store, FilterState));
        }

        /// <summary>Marks filter cache dirty. Call when filter state or hediff settings change.</summary>
        public void MarkFilterDirty()
        {
            _filterDirty = true;
            _filterChangedAt = Time.realtimeSinceStartup;
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
            if (hediffDef == null) return null;
            return Store.GetOrCreate(hediffDef);
        }

        /// <summary>
        /// Sets the setting for a specific hediff.
        /// </summary>
        public void SetHediffSetting(string hediffDefName, EternalHediffSetting setting)
        {
            Store.Set(hediffDefName, setting);
            MarkFilterDirty();
        }

        /// <summary>
        /// Removes a hediff setting.
        /// </summary>
        public bool RemoveHediffSetting(string hediffDefName)
        {
            bool result = Store.Remove(hediffDefName);
            MarkFilterDirty();
            return result;
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
        /// Resets selected hediffs to defaults.
        /// </summary>
        public int ResetSelected()
        {
            var selected = Selection.GetSelected().ToList();
            int result = Store.ResetMany(selected);
            MarkFilterDirty();
            return result;
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

        /// <summary>
        /// Resets hediffs by category to defaults.
        /// </summary>
        public int ResetByCategory(HediffCategory category)
        {
            var matching = Store.GetAll()
                .Where(kvp =>
                {
                    bool matchesCategory = category == HediffCategory.All ||
                        kvp.Value.allowedCategories == category ||
                        (category == HediffCategory.Injury && kvp.Value.isInjuryFilter) ||
                        (category == HediffCategory.Disease && kvp.Value.isDiseaseFilter) ||
                        (category == HediffCategory.Condition && kvp.Value.isConditionFilter);
                    return matchesCategory && kvp.Value.HasCustomSettings();
                })
                .Select(kvp => kvp.Key);

            int result = Store.ResetMany(matching);
            MarkFilterDirty();
            return result;
        }

        /// <summary>
        /// Resets hediffs from a specific mod source to defaults.
        /// </summary>
        public int ResetByModSource(string modSource)
        {
            var matching = Store.GetAll()
                .Where(kvp =>
                    kvp.Value.modSource.Equals(modSource, System.StringComparison.OrdinalIgnoreCase) &&
                    kvp.Value.HasCustomSettings())
                .Select(kvp => kvp.Key);

            return Store.ResetMany(matching);
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
                Selection.Select(hediffDefName);
            else
                Selection.Deselect(hediffDefName);
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
                Selection.SelectAll(Store.Count);
            else
                Selection.ClearAll();
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
            if (template == null) return;

            foreach (string name in hediffNames)
            {
                var setting = GetHediffSetting(name);
                if (setting != null)
                {
                    setting.enabled = template.isEnabled;
                    setting.canHeal = template.canHeal;
                    setting.requireCureToResurrect = template.requireCureToResurrect;
                    setting.noThreshold = template.noThreshold;  // Copy instant healing setting
                    setting.healingRate = template.healingRate;  // Copy healing rate (may be custom or USE_GLOBAL_RATE)
                    setting.maxSeverityThreshold = template.maxSeverityThreshold;
                    setting.healPermanentInjuries = template.healPermanentInjuries;
                    setting.healScars = template.healScars;
                    setting.healMissingParts = template.healMissingParts;
                    setting.allowedCategories = template.allowedCategories;
                    setting.nutritionCostMultiplier = template.nutritionCostMultiplier;
                    setting.consumeExtraResources = template.consumeExtraResources;
                }
            }
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
                HealingHediffs = allSettings.Count(kvp => kvp.Value.canHeal),  // Count actual healing, not just enabled
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
        public int EnabledHediffs => HealingHediffs;

        public float HealingPercentage => TotalHediffs > 0 ? (float)HealingHediffs / TotalHediffs * 100f : 0f;
        public float CustomizedPercentage => TotalHediffs > 0 ? (float)CustomizedHediffs / TotalHediffs * 100f : 0f;
    }
}
