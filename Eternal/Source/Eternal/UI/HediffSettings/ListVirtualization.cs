// Relative Path: Eternal/Source/Eternal/UI/HediffSettings/ListVirtualization.cs
// Creation Date: 10-07-2026
// Last Edit: 10-07-2026
// Author: 0Shard
// Description: Pure index math for virtualized IMGUI list rendering — computes which row
//              range is visible for a given scroll position, plus a preload buffer so
//              scrolling never reveals an undrawn row. Kept free of RimWorld/Unity types
//              so the boundary math is unit-testable.

using System;

namespace Eternal.UI.HediffSettings
{
    /// <summary>
    /// Computes the visible row range for a fixed-row-height virtualized list.
    /// </summary>
    public static class ListVirtualization
    {
        /// <summary>
        /// Returns the inclusive [first, last] row index range to draw.
        /// Rows outside the range are skipped entirely (their Y offsets are pure arithmetic).
        /// Returns (0, -1) for an empty list — callers loop for (i = first; i &lt;= last; i++).
        /// </summary>
        /// <param name="scrollY">Current vertical scroll offset in pixels</param>
        /// <param name="viewportHeight">Visible viewport height in pixels</param>
        /// <param name="rowStride">Row height including spacing, must be &gt; 0</param>
        /// <param name="itemCount">Total number of rows in the list</param>
        /// <param name="bufferRows">Extra rows to pre-draw above and below the viewport</param>
        public static (int first, int last) GetVisibleRange(
            float scrollY, float viewportHeight, float rowStride, int itemCount, int bufferRows)
        {
            if (itemCount <= 0 || rowStride <= 0f)
                return (0, -1);

            int firstVisible = (int)(Math.Max(0f, scrollY) / rowStride);
            int lastVisible = (int)((Math.Max(0f, scrollY) + viewportHeight) / rowStride);

            int first = Math.Max(0, firstVisible - bufferRows);
            int last = Math.Min(itemCount - 1, lastVisible + bufferRows);
            return (first, last);
        }
    }
}
