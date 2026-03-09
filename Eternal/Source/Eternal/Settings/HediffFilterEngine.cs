// Relative Path: Eternal/Source/Eternal/Settings/HediffFilterEngine.cs
// Creation Date: 01-01-2025
// Last Edit: 01-01-2026
// Author: 0Shard
// Description: Applies filters to hediff settings.
//              Stateless - takes inputs, returns filtered output.
//              Includes beneficial filter for filtering beneficial (non-bad) hediffs.

using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace Eternal.Settings
{
    /// <summary>
    /// Applies filters to hediff settings.
    /// Stateless - takes inputs, returns filtered output.
    /// </summary>
    public static class HediffFilterEngine
    {
        /// <summary>
        /// Filters hediff settings based on the provided filter state.
        /// </summary>
        public static IEnumerable<KeyValuePair<string, EternalHediffSetting>> Filter(
            HediffSettingsStore store,
            HediffFilterState filterState)
        {
            if (store == null)
                yield break;

            var query = store.GetAllWithDefs();

            foreach (var kvp in query)
            {
                if (PassesAllFilters(kvp.Key, kvp.Value, filterState))
                {
                    yield return kvp;
                }
            }
        }

        /// <summary>
        /// Filters and returns results as an ordered list.
        /// Sorts by human-readable label (not defName) for better UI display.
        /// </summary>
        public static List<KeyValuePair<string, EternalHediffSetting>> FilterToList(
            HediffSettingsStore store,
            HediffFilterState filterState)
        {
            return Filter(store, filterState)
                .OrderBy(kvp => GetDisplayLabel(kvp.Key))
                .ToList();
        }

        /// <summary>
        /// Gets the human-readable display label for a hediff.
        /// Falls back to defName if hediff can't be resolved.
        /// </summary>
        private static string GetDisplayLabel(string defName)
        {
            var hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
            if (hediffDef == null)
                return defName;
            return hediffDef.LabelCap.ToString();
        }

        /// <summary>
        /// Checks if a hediff setting passes all active filters.
        /// </summary>
        private static bool PassesAllFilters(
            string defName,
            EternalHediffSetting setting,
            HediffFilterState filterState)
        {
            if (setting == null)
                return false;

            // Search filter
            if (!PassesSearchFilter(defName, setting, filterState.SearchTerm))
                return false;

            // Source filters
            if (!PassesSourceFilters(setting, filterState))
                return false;

            // Property filters
            if (!PassesPropertyFilters(setting, filterState))
                return false;

            // Category filter
            if (!PassesCategoryFilter(setting, filterState.CategoryFilter))
                return false;

            // Toggle filters
            if (filterState.ShowOnlyEnabled && !setting.enabled)
                return false;

            if (filterState.ShowOnlyCustom && !setting.HasCustomSettings())
                return false;

            return true;
        }

        /// <summary>
        /// Checks if a hediff passes the search filter.
        /// </summary>
        private static bool PassesSearchFilter(
            string defName,
            EternalHediffSetting setting,
            string searchTerm)
        {
            if (string.IsNullOrEmpty(searchTerm))
                return true;

            // Get hediff definition for label and description
            var hediffDef = DefDatabase<HediffDef>.GetNamedSilentFail(defName);
            string label = hediffDef?.LabelCap ?? string.Empty;
            string description = hediffDef?.description ?? string.Empty;
            string modSource = setting.modSource ?? string.Empty;

            // Case-insensitive matches
            bool labelMatch = label.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0;
            bool defNameMatch = defName.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0;
            bool descMatch = description.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0;
            bool modMatch = modSource.IndexOf(searchTerm, StringComparison.OrdinalIgnoreCase) >= 0;

            return labelMatch || defNameMatch || descMatch || modMatch;
        }

        /// <summary>
        /// Checks if a hediff passes source filters.
        /// </summary>
        private static bool PassesSourceFilters(
            EternalHediffSetting setting,
            HediffFilterState filterState)
        {
            // Mod source filter
            if (!string.IsNullOrEmpty(filterState.ModSourceFilter))
            {
                if (setting.modSource.IndexOf(filterState.ModSourceFilter, StringComparison.OrdinalIgnoreCase) < 0)
                    return false;
            }

            // Source type filters (at least one must be active for these to apply)
            if (filterState.FilterByBaseGame || filterState.FilterByDLC || filterState.FilterByMods)
            {
                bool matches = false;

                if (filterState.FilterByBaseGame && setting.isFromBaseGame)
                    matches = true;
                if (filterState.FilterByDLC && setting.isFromDLC)
                    matches = true;
                if (filterState.FilterByMods && !setting.isFromBaseGame && !setting.isFromDLC)
                    matches = true;

                if (!matches)
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Checks if a hediff passes property filters.
        /// </summary>
        private static bool PassesPropertyFilters(
            EternalHediffSetting setting,
            HediffFilterState filterState)
        {
            if (filterState.FilterIsBad && !setting.isBad)
                return false;

            if (filterState.FilterIsLethal && !setting.isLethal)
                return false;

            if (filterState.FilterIsTendable && !setting.isTendable)
                return false;

            if (filterState.FilterIsChronic && !setting.isChronic)
                return false;

            if (filterState.FilterBeneficial && !setting.isBeneficial)
                return false;

            return true;
        }

        /// <summary>
        /// Checks if a hediff passes the category filter.
        /// </summary>
        private static bool PassesCategoryFilter(
            EternalHediffSetting setting,
            HediffCategory category)
        {
            if (category == HediffCategory.All)
                return true;

            return setting.allowedCategories == category ||
                   (category == HediffCategory.Injury && setting.isInjuryFilter) ||
                   (category == HediffCategory.Disease && setting.isDiseaseFilter) ||
                   (category == HediffCategory.Condition && setting.isConditionFilter);
        }

        /// <summary>
        /// Counts how many hediffs match the current filters.
        /// </summary>
        public static int Count(HediffSettingsStore store, HediffFilterState filterState)
        {
            return Filter(store, filterState).Count();
        }
    }
}
