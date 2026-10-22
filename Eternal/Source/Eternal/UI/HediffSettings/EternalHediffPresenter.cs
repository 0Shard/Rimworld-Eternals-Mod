// Relative Path: Eternal/Source/Eternal/UI/HediffSettings/EternalHediffPresenter.cs
// Creation Date: 01-01-2025
// Last Edit: 16-07-2026
// Author: 0Shard
// Description: Presenter layer for hediff settings UI. Manages UI state and handles events.
//              Includes FilterBeneficial for filtering beneficial (non-bad) hediffs.
//              Added per-hediff and global reset operations.
//              PERF-02: Added GetFilteredHediffsCached() proxy for render-loop use.

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;

namespace Eternal.UI.HediffSettings
{
    /// <summary>
    /// Presenter layer for hediff settings UI.
    /// Manages filter state, UI state, and coordinates between View and Model.
    /// </summary>
    public class EternalHediffPresenter
    {
        private readonly EternalHediffModel model;

        #region Filter State

        // Read the manager's authoritative state, including changes made outside this presenter.
        public string SearchTerm => model.FilterState.SearchTerm;
        public HediffCategory SelectedCategory => model.FilterState.CategoryFilter;
        public bool ShowOnlyEnabled => model.FilterState.ShowOnlyEnabled;
        public bool ShowOnlyCustom => model.FilterState.ShowOnlyCustom;

        // Advanced filter state
        public string ModSourceFilter => model.FilterState.ModSourceFilter;
        public bool FilterBaseGame => model.FilterState.FilterByBaseGame;
        public bool FilterDLC => model.FilterState.FilterByDLC;
        public bool FilterMods => model.FilterState.FilterByMods;
        public bool FilterIsBad => model.FilterState.FilterIsBad;
        public bool FilterIsLethal => model.FilterState.FilterIsLethal;
        public bool FilterTendable => model.FilterState.FilterIsTendable;
        public bool FilterChronic => model.FilterState.FilterIsChronic;
        public bool FilterBeneficial => model.FilterState.FilterBeneficial;

        #endregion

        #region UI State

        public int SelectedTabIndex { get; private set; } = 0;
        public readonly string[] TabLabels = { "General", "Bulk" };

        // Bulk template
        public EternalHediffSetting BulkTemplate { get; private set; }

        #endregion

        public EternalHediffPresenter(EternalHediffModel model)
        {
            this.model = model;
            BulkTemplate = new EternalHediffSetting();
        }

        #region Tab Events

        /// <summary>
        /// Handles tab selection change.
        /// </summary>
        public void OnTabSelected(int tabIndex)
        {
            SelectedTabIndex = tabIndex;
        }

        #endregion

        #region Filter Events

        /// <summary>
        /// Handles search term change.
        /// </summary>
        public void OnSearchTermChanged(string newSearchTerm)
        {
            model.SetSearchFilter(newSearchTerm);
        }

        /// <summary>
        /// Handles category filter change.
        /// </summary>
        public void OnCategoryChanged(HediffCategory category)
        {
            model.SetCategoryFilter(category);
        }

        /// <summary>
        /// Handles mod source filter change.
        /// </summary>
        public void OnModSourceChanged(string modSource)
        {
            model.SetModSourceFilter(modSource);
        }

        /// <summary>
        /// Handles source type filter changes.
        /// </summary>
        public void OnSourceTypeFilterChanged(bool baseGame, bool dlc, bool mods)
        {
            model.SetSourceTypeFilter(baseGame, dlc, mods);
        }

        /// <summary>
        /// Handles property filter changes.
        /// </summary>
        public void OnPropertyFilterChanged(bool isBad, bool isLethal, bool tendable, bool chronic, bool beneficial = false)
        {
            model.SetPropertyFilters(isBad, isLethal, tendable, chronic, beneficial);
        }

        /// <summary>
        /// Handles enabled-only filter change.
        /// </summary>
        public void OnShowOnlyEnabledChanged(bool showOnlyEnabled)
        {
            model.SetShowOnlyEnabled(showOnlyEnabled);
        }

        /// <summary>
        /// Handles custom-only filter change.
        /// </summary>
        public void OnShowOnlyCustomChanged(bool showOnlyCustom)
        {
            model.SetShowOnlyCustom(showOnlyCustom);
        }

        /// <summary>
        /// Clears all filter state and resets to defaults.
        /// </summary>
        public void OnClearAllFilters()
        {
            model.ClearAllFilters();
        }

        /// <summary>
        /// Notifies the presenter that a per-hediff setting mutated (row Heal toggle,
        /// slider input) so it can mark the filter cache dirty and keep the filtered
        /// list in sync with the new custom/heal state.
        /// </summary>
        public void NotifyHediffSettingChanged()
        {
            model.MarkFilterDirty();
        }

        #endregion

        #region Selection Events

        /// <summary>
        /// Handles hediff selection toggle.
        /// </summary>
        public void OnHediffSelectionToggled(string hediffName)
        {
            bool currentlySelected = model.IsHediffSelected(hediffName);
            model.SetHediffSelected(hediffName, !currentlySelected);
        }

        /// <summary>
        /// Handles select all action.
        /// </summary>
        public void OnSelectAll()
        {
            model.SelectAllHediffs(true);
        }

        /// <summary>
        /// Handles clear selection action.
        /// </summary>
        public void OnClearSelection()
        {
            model.SelectAllHediffs(false);
        }

        #endregion

        #region Bulk Operations

        /// <summary>
        /// Applies bulk template to selected hediffs.
        /// </summary>
        /// <returns>Number of hediffs affected.</returns>
        public int OnApplyToSelected()
        {
            var selected = model.GetSelectedHediffs().ToList();
            if (selected.Any())
            {
                return model.ApplySettingsToGroupWithCount(selected, BulkTemplate);
            }
            return 0;
        }

        /// <summary>
        /// Applies bulk template to all filtered hediffs.
        /// </summary>
        /// <returns>Number of hediffs affected.</returns>
        public int OnApplyToAll()
        {
            var allHediffs = model.GetFilteredHediffs().Select(kvp => kvp.Key).ToList();
            return model.ApplySettingsToGroupWithCount(allHediffs, BulkTemplate);
        }

        /// <summary>
        /// Resets all hediff settings to defaults.
        /// </summary>
        public void OnResetAllToDefaults()
        {
            model.ResetAllToDefaults();
        }

        /// <summary>
        /// Resets a single hediff setting to its default.
        /// </summary>
        public void OnResetSingleHediff(string hediffName)
        {
            model.ResetSingleHediff(hediffName);
        }

        #endregion

        #region Data Access (Pass-through to Model)

        public IEnumerable<KeyValuePair<string, EternalHediffSetting>> GetFilteredHediffs()
            => model.GetFilteredHediffs();

        /// <summary>
        /// Gets cached filtered hediff results for render-loop use.
        /// Delegates to the model's cache, rebuilt immediately when dirty.
        /// </summary>
        public IReadOnlyList<KeyValuePair<string, EternalHediffSetting>> GetFilteredHediffsCached()
            => model.GetFilteredHediffsCached();

        public HediffStatistics GetStatistics()
            => model.GetStatistics();

        public IEnumerable<string> GetAvailableModSources()
            => model.GetAvailableModSources();

        public int GetSelectedCount()
            => model.GetSelectedCount();

        public bool IsHediffSelected(string hediffName)
            => model.IsHediffSelected(hediffName);

        public Color GetHediffTypeColor(EternalHediffSetting setting)
            => model.GetHediffTypeColor(setting);

        public string GetHediffDisplayLabel(string hediffName)
            => model.GetHediffDisplayLabel(hediffName);

        public string GetHediffDescription(string hediffName)
            => model.GetHediffDescription(hediffName);

        #endregion
    }
}
