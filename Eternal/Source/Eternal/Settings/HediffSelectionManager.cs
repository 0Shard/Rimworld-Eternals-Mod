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
        }

        /// <summary>
        /// Toggles selection state for a hediff.
        /// </summary>
        public void Toggle(string defName)
        {
            if (IsSelected(defName))
            {
                Deselect(defName);
            }
            else
            {
                Select(defName);
            }
        }

        /// <summary>
        /// Checks if a hediff is selected.
        /// </summary>
        public bool IsSelected(string defName)
        {
            return selected.Contains(defName);
        }

        /// <summary>
        /// Selects all hediffs.
        /// </summary>
        public void SelectAll(IEnumerable<string> names)
        {
            selected.Clear();
            foreach (string name in names ?? Enumerable.Empty<string>())
            {
                Select(name);
            }
        }

        /// <summary>
        /// Clears all selections.
        /// </summary>
        public void ClearAll()
        {
            selected.Clear();
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
        public int Count => selected.Count;
    }
}
