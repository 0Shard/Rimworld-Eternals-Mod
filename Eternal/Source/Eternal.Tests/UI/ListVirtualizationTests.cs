// Relative Path: Eternal/Source/Eternal.Tests/UI/ListVirtualizationTests.cs
// Creation Date: 10-07-2026
// Last Edit: 10-07-2026
// Author: 0Shard
// Description: Boundary tests for the virtualized hediff list index math. The view draws
//              only [first, last] — an off-by-one here shows up as a blank row while
//              scrolling, so every boundary is pinned. Pure math — no Verse/Unity types.

using Xunit;
using Eternal.UI.HediffSettings;

namespace Eternal.Tests.UI
{
    public class ListVirtualizationTests
    {
        // View defaults: ENTRY_HEIGHT 36 + 2 spacing = 38 stride, 5 buffer rows
        private const float Stride = 38f;
        private const int Buffer = 5;

        [Fact]
        public void EmptyList_ReturnsEmptyRange()
        {
            var (first, last) = ListVirtualization.GetVisibleRange(0f, 500f, Stride, 0, Buffer);
            Assert.True(last < first); // for-loop body never runs
        }

        [Fact]
        public void ZeroRowStride_ReturnsEmptyRange()
        {
            var (first, last) = ListVirtualization.GetVisibleRange(0f, 500f, 0f, 100, Buffer);
            Assert.True(last < first);
        }

        [Fact]
        public void ScrollAtTop_StartsAtRowZero_WithBufferBelow()
        {
            var (first, last) = ListVirtualization.GetVisibleRange(0f, 500f, Stride, 100, Buffer);
            Assert.Equal(0, first);
            // lastVisible = 500 / 38 = 13, + 5 buffer = 18
            Assert.Equal(18, last);
        }

        [Fact]
        public void ScrollAtBottom_EndsAtLastRow()
        {
            // 100 rows × 38 = 3800 total, viewport 500 → max scroll 3300
            var (first, last) = ListVirtualization.GetVisibleRange(3300f, 500f, Stride, 100, Buffer);
            Assert.Equal(99, last);
            // firstVisible = 3300 / 38 = 86, - 5 buffer = 81
            Assert.Equal(81, first);
        }

        [Fact]
        public void ListShorterThanViewport_CoversAllRows()
        {
            var (first, last) = ListVirtualization.GetVisibleRange(0f, 500f, Stride, 5, Buffer);
            Assert.Equal(0, first);
            Assert.Equal(4, last);
        }

        [Fact]
        public void NegativeScroll_ClampsToTop()
        {
            var (first, last) = ListVirtualization.GetVisibleRange(-50f, 500f, Stride, 100, Buffer);
            Assert.Equal(0, first);
            Assert.Equal(18, last);
        }

        [Fact]
        public void MidScroll_RangeCoversViewportPlusBufferBothSides()
        {
            // scroll 1000: firstVisible = 26, lastVisible = (1500/38) = 39
            var (first, last) = ListVirtualization.GetVisibleRange(1000f, 500f, Stride, 100, Buffer);
            Assert.Equal(21, first);
            Assert.Equal(44, last);
        }

        [Fact]
        public void EveryVisibleRowIsInsideRange_NoGapsWhileScrolling()
        {
            // Sweep scroll positions; the row under the viewport's first/last pixel
            // must always be inside the returned range (the "no blank row" invariant)
            const int count = 200;
            for (float scrollY = 0f; scrollY <= count * Stride - 500f; scrollY += 7f)
            {
                var (first, last) = ListVirtualization.GetVisibleRange(scrollY, 500f, Stride, count, Buffer);
                int topRow = (int)(scrollY / Stride);
                int bottomRow = (int)((scrollY + 500f) / Stride);
                Assert.True(first <= topRow, $"top row {topRow} outside range at scroll {scrollY}");
                Assert.True(last >= System.Math.Min(count - 1, bottomRow), $"bottom row {bottomRow} outside range at scroll {scrollY}");
            }
        }
    }
}
