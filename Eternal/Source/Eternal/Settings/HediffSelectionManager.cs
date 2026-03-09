// file path: Eternal/Source/Eternal/Settings/HediffSelectionManager.cs
// Description: Manages multi-selection state for batch operations in the UI.

using System.Collections.Generic;
using System.Linq;

namespace Eternal.Settings
{
    /// <summary>
    /// Manages multi-selection state for batch operations in the UI.
    /// </summary>
    public class HediffSelectionManager
    {
        private readonly HashSet<string> selected = new HashSet<string>();
        private bool selectAllMode = false;
        private int totalCount = 0;

        /// <summary>
        /// Selects a hediff by def name.
        /// </summary>
        public void Select(string defName)
        {
            if (!string.IsNullOrEmpty(defName))
            {
                selected.Add(defName);
            }
        }

        /// <summary>
        /// Deselects a hediff by def name.
        /// </summary>
        public void Deselect(string defName)
        {
            selected.Remove(defName);
            selectAllMode = false;
        }

        /// <summary>
        /// Toggles selection state for a hediff.
        /// </summary>
        public void Toggle(string defName)
        {
            if (IsSelected(defName))
                Deselect(defName);
            else
                Select(defName);
        }

        /// <summary>
        /// Checks if a hediff is selected.
        /// </summary>
        public bool IsSelected(string defName)
        {
            return selectAllMode || selected.Contains(defName);
        }

        /// <summary>
        /// Selects all hediffs.
        /// </summary>
        public void SelectAll(int totalCount = 0)
        {
            selected.Clear();
            selectAllMode = true;
            this.totalCount = totalCount;
        }

        /// <summary>
        /// Clears all selections.
        /// </summary>
        public void ClearAll()
        {
            selected.Clear();
            selectAllMode = false;
            totalCount = 0;
        }

        /// <summary>
        /// Gets all selected hediff def names.
        /// </summary>
        public IEnumerable<string> GetSelected()
        {
            return selected.ToList();
        }

        /// <summary>
        /// Gets the count of selected hediffs.
        /// </summary>
        public int Count => selectAllMode ? totalCount : selected.Count;

        /// <summary>
        /// Checks if any hediffs are selected.
        /// </summary>
        public bool HasSelection => selectAllMode || selected.Count > 0;

        /// <summary>
        /// Checks if select all mode is active.
        /// </summary>
        public bool IsSelectAllMode => selectAllMode;

        /// <summary>
        /// Sets the total count for select all mode.
        /// </summary>
        public void SetTotalCount(int count)
        {
            totalCount = count;
        }
    }
}
