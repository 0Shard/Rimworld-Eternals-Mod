// Author: 0Shard
// Description: Independent numeric regressions for parent viewport intersection and shrink transitions.
using Eternal.UI.HediffSettings;
using Xunit;

namespace Eternal.Tests.UI
{
    public class EmbeddedHediffLayoutTests
    {
        [Theory]
        [InlineData(0f)]
        [InlineData(161f)]
        [InlineData(38700f)]
        [InlineData(40000f)]
        public void ViewportOutsideList_DrawsNoBufferedRows(float pageTop)
        {
            var (first, last) = EmbeddedHediffLayout.GetVisibleRange(pageTop, 539f, 700f, 38f, 1000, 5);
            Assert.True(last < first);
        }

        [Theory]
        [InlineData(500f, 0, 13)]
        [InlineData(1000f, 2, 27)]
        [InlineData(38161f, 980, 999)]
        [InlineData(38690f, 994, 999)]
        public void IntersectionRequestsOnlyVisibleRowsAndExistingBuffer(float pageTop, int expectedFirst, int expectedLast)
        {
            var range = EmbeddedHediffLayout.GetVisibleRange(pageTop, 539f, 700f, 38f, 1000, 5);
            Assert.Equal((expectedFirst, expectedLast), range);
        }

        [Fact]
        public void SectionOriginIsAppliedExactlyOnce()
        {
            var zeroOrigin = EmbeddedHediffLayout.GetVisibleRange(1000f, 539f, 700f, 38f, 1000, 5);
            var shifted = EmbeddedHediffLayout.GetVisibleRange(1237f, 539f, 937f, 38f, 1000, 5);
            Assert.Equal((2, 27), shifted);
            Assert.Equal(zeroOrigin, shifted);
        }

        [Fact]
        public void RowBoundariesIncludeFirstAndLastVisibleRowsWithoutDrawingEntireList()
        {
            for (int row = 0; row < 990; row += 7)
            {
                float pageTop = 237f + row * 38f;
                var (first, last) = EmbeddedHediffLayout.GetVisibleRange(pageTop, 76f, 237f, 38f, 1000, 5);
                Assert.True(first <= row);
                Assert.True(last >= row + 2);
                Assert.InRange(last - first + 1, 1, 13);
            }
        }

        [Theory]
        [InlineData(0, 30f)]
        [InlineData(1, 38f)]
        [InlineData(3, 114f)]
        [InlineData(1000, 38000f)]
        public void LogicalListHeightIsIndependentOfVisibleSlice(int count, float expectedHeight)
        {
            Assert.Equal(expectedHeight, EmbeddedHediffLayout.ListHeight(count, 38f));
        }

        [Theory]
        [InlineData(39900f, 1250f, 711f)] // many results -> empty filtered list
        [InlineData(39900f, 1100f, 561f)] // General -> Bulk
        [InlineData(39900f, 800f, 261f)] // collapse, following Advanced sections remain
        [InlineData(50f, 200f, 0f)]
        [InlineData(-50f, 800f, 0f)]
        public void ShrinkingPageKeepsScrollInsideFreshContent(float scroll, float contentHeight, float expectedScroll)
        {
            Assert.Equal(expectedScroll, EmbeddedHediffLayout.ClampScroll(scroll, contentHeight, 539f));
        }

        [Fact]
        public void EmptyListAndNonPositiveViewportNeverDrawRows()
        {
            Assert.True(EmbeddedHediffLayout.GetVisibleRange(700f, 539f, 700f, 38f, 0, 5).last < 0);
            Assert.True(EmbeddedHediffLayout.GetVisibleRange(700f, 0f, 700f, 38f, 1000, 5).last < 0);
        }
    }
}
