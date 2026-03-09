// Relative Path: Eternal/Source/Eternal/UI/HediffSettings/EternalHediffModel.cs
// Creation Date: 01-01-2025
// Last Edit: 19-02-2026
// Author: 0Shard
// Description: Data access and business logic layer for hediff settings UI.
//              GetHediffTypeColor now returns green for beneficial hediffs.
//              PERF-02: Added GetFilteredHediffsCached() proxy for render-loop use.

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Eternal.UI.HediffSettings
{
    /// <summary>
    /// Model layer for hediff settings UI.
    /// Handles data access, business rules, and statistics calculation.
    /// </summary>
    public class EternalHediffModel
    {
        private readonly EternalHediffManager manager;

        public EternalHediffModel(EternalHediffManager manager)
        {
            this.manager = manager;
        }

        #region Data Access

        /// <summary>
        /// Gets filtered hediffs based on current filter settings.
        /// Non-cached — use for one-shot operations (e.g. SelectAll, bulk ops).
        /// </summary>
        public IEnumerable<KeyValuePair<string, EternalHediffSetting>> GetFilteredHediffs()
        {
            return manager.GetFilteredHediffs();
        }

        /// <summary>
        /// Gets cached filtered results for render-loop use.
        /// Delegates to manager's debounced cache.
        /// </summary>
        public IReadOnlyList<KeyValuePair<string, EternalHediffSetting>> GetFilteredHediffsCached()
            => manager.GetFilteredHediffsCached();

        /// <summary>
        /// Gets statistics about hediff settings.
        /// </summary>
        public HediffStatistics GetStatistics()
        {
            return manager.GetStatistics();
        }

        /// <summary>
        /// Gets available mod sources for filtering.
        /// </summary>
        public IEnumerable<string> GetAvailableModSources()
        {
            return manager.GetAvailableModSources();
        }

        /// <summary>
        /// Gets all selected hediffs.
        /// </summary>
        public IEnumerable<string> GetSelectedHediffs()
        {
            return manager.GetSelectedHediffs();
        }

        /// <summary>
        /// Gets count of selected hediffs.
        /// </summary>
        public int GetSelectedCount()
        {
            return manager.GetSelectedCount();
        }

        #endregion

        #region Selection Operations

        /// <summary>
        /// Checks if a hediff is selected.
        /// </summary>
        public bool IsHediffSelected(string hediffName)
        {
            return manager.IsHediffSelected(hediffName);
        }

        /// <summary>
        /// Sets hediff selection state.
        /// </summary>
        public void SetHediffSelected(string hediffName, bool selected)
        {
            manager.SetHediffSelected(hediffName, selected);
        }

        /// <summary>
        /// Selects or deselects all hediffs.
        /// </summary>
        public void SelectAllHediffs(bool selected)
        {
            manager.SelectAllHediffs(selected);
        }

        #endregion

        #region Filter Operations

        /// <summary>
        /// Sets search filter text.
        /// </summary>
        public void SetSearchFilter(string searchTerm)
        {
            manager.SetSearchFilter(searchTerm);
        }

        /// <summary>
        /// Sets category filter.
        /// </summary>
        public void SetCategoryFilter(HediffCategory category)
        {
            manager.SetCategoryFilter(category);
        }

        /// <summary>
        /// Sets mod source filter.
        /// </summary>
        public void SetModSourceFilter(string modSource)
        {
            manager.SetModSourceFilter(modSource);
        }

        /// <summary>
        /// Sets source type filters (base game, DLC, mods).
        /// </summary>
        public void SetSourceTypeFilter(bool baseGame, bool dlc, bool mods)
        {
            manager.SetSourceTypeFilter(baseGame, dlc, mods);
        }

        /// <summary>
        /// Sets property filters (bad, lethal, tendable, chronic, beneficial).
        /// </summary>
        public void SetPropertyFilters(bool isBad, bool isLethal, bool tendable, bool chronic, bool beneficial = false)
        {
            manager.SetPropertyFilters(isBad, isLethal, tendable, chronic, beneficial);
        }

        /// <summary>
        /// Sets enabled-only filter.
        /// </summary>
        public void SetShowOnlyEnabled(bool showOnlyEnabled)
        {
            manager.SetShowOnlyEnabled(showOnlyEnabled);
        }

        /// <summary>
        /// Sets custom-only filter.
        /// </summary>
        public void SetShowOnlyCustom(bool showOnlyCustom)
        {
            manager.SetShowOnlyCustom(showOnlyCustom);
        }

        /// <summary>
        /// Clears all filters.
        /// </summary>
        public void ClearAllFilters()
        {
            manager.ClearAllFilters();
        }

        #endregion

        #region Bulk Operations

        /// <summary>
        /// Applies settings to a group of hediffs.
        /// </summary>
        public void ApplySettingsToGroup(IEnumerable<string> hediffNames, EternalHediffSetting template)
        {
            manager.ApplySettingsToGroup(hediffNames.ToList(), template);
        }

        /// <summary>
        /// Resets all hediff settings to defaults.
        /// </summary>
        public void ResetAllToDefaults()
        {
            manager.ResetAllToDefaults();
        }

        /// <summary>
        /// Resets a single hediff setting to its default.
        /// </summary>
        public void ResetSingleHediff(string hediffName)
        {
            manager.ResetSingleHediff(hediffName);
        }

        #endregion

        #region Hediff Display Helpers

        /// <summary>
        /// Gets the display color for a hediff based on its type.
        /// </summary>
        public Color GetHediffTypeColor(EternalHediffSetting setting)
        {
            if (setting.isBeneficial) return new Color(0.5f, 1f, 0.5f); // Light green for beneficial
            if (setting.isInjuryFilter) return new Color(1f, 0.8f, 0.3f); // Orange for injuries
            if (setting.isDiseaseFilter) return new Color(1f, 0.3f, 0.3f); // Red for diseases
            if (setting.isConditionFilter) return new Color(0.3f, 0.7f, 1f); // Blue for conditions
            return Color.white; // Default
        }

        /// <summary>
        /// Gets the display label for a hediff.
        /// </summary>
        public string GetHediffDisplayLabel(string hediffName)
        {
            var hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(hediffName);
            return hediffDef?.LabelCap ?? hediffName;
        }

        /// <summary>
        /// Gets the description for a hediff.
        /// </summary>
        public string GetHediffDescription(string hediffName)
        {
            var hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(hediffName);
            return hediffDef?.description;
        }

        #endregion
    }
}
