// Relative Path: Eternal/Source/Eternal/Settings/HediffFilterState.cs
// Creation Date: 01-01-2025
// Last Edit: 01-01-2026
// Author: 0Shard
// Description: Manages filter state for the hediff settings UI.
//              Pure state container - no business logic.
//              Includes FilterBeneficial for filtering beneficial (non-bad) hediffs.

namespace Eternal.Settings
{
    /// <summary>
    /// Manages filter state for the hediff settings UI.
    /// Pure state container - no business logic.
    /// </summary>
    public class HediffFilterState
    {
        // Search filter
        public string SearchTerm { get; set; } = "";

        // Category filter
        public HediffCategory CategoryFilter { get; set; } = HediffCategory.All;

        // Toggle filters
        public bool ShowOnlyEnabled { get; set; } = false;
        public bool ShowOnlyCustom { get; set; } = false;

        // Source filters
        public string ModSourceFilter { get; set; } = "";
        public bool FilterByBaseGame { get; set; } = false;
        public bool FilterByDLC { get; set; } = false;
        public bool FilterByMods { get; set; } = false;

        // Property filters
        public bool FilterIsBad { get; set; } = false;
        public bool FilterIsLethal { get; set; } = false;
        public bool FilterIsTendable { get; set; } = false;
        public bool FilterIsChronic { get; set; } = false;
        public bool FilterBeneficial { get; set; } = false;  // Filter for beneficial (non-bad) hediffs

        /// <summary>
        /// Clears all filters to their default values.
        /// </summary>
        public void ClearAll()
        {
            SearchTerm = "";
            CategoryFilter = HediffCategory.All;
            ShowOnlyEnabled = false;
            ShowOnlyCustom = false;

            ModSourceFilter = "";
            FilterByBaseGame = false;
            FilterByDLC = false;
            FilterByMods = false;

            FilterIsBad = false;
            FilterIsLethal = false;
            FilterIsTendable = false;
            FilterIsChronic = false;
            FilterBeneficial = false;
        }

        /// <summary>
        /// Checks if any filter is active.
        /// </summary>
        public bool HasActiveFilters()
        {
            return !string.IsNullOrEmpty(SearchTerm) ||
                   CategoryFilter != HediffCategory.All ||
                   ShowOnlyEnabled ||
                   ShowOnlyCustom ||
                   !string.IsNullOrEmpty(ModSourceFilter) ||
                   FilterByBaseGame ||
                   FilterByDLC ||
                   FilterByMods ||
                   FilterIsBad ||
                   FilterIsLethal ||
                   FilterIsTendable ||
                   FilterIsChronic ||
                   FilterBeneficial;
        }

        /// <summary>
        /// Sets the source type filters.
        /// </summary>
        public void SetSourceTypeFilter(bool baseGame, bool dlc, bool mods)
        {
            FilterByBaseGame = baseGame;
            FilterByDLC = dlc;
            FilterByMods = mods;
        }

        /// <summary>
        /// Sets the property filters.
        /// </summary>
        public void SetPropertyFilters(bool isBad, bool isLethal, bool isTendable, bool isChronic, bool beneficial = false)
        {
            FilterIsBad = isBad;
            FilterIsLethal = isLethal;
            FilterIsTendable = isTendable;
            FilterIsChronic = isChronic;
            FilterBeneficial = beneficial;
        }
    }
}
