// Author: 0Shard
// Description: Pure page/list coordinates for the shared settings scrollbar.
using System;

namespace Eternal.UI.HediffSettings
{
    public static class EmbeddedHediffLayout
    {
        public static float ListHeight(int count, float rowStride)
            => Math.Max(30f, count * rowStride);

        public static float ClampScroll(float scroll, float contentHeight, float viewportHeight)
            => Math.Max(0f, Math.Min(scroll, Math.Max(0f, contentHeight - viewportHeight)));

        public static (int first, int last) GetVisibleRange(float pageTop, float viewportHeight,
            float listTop, float rowStride, int count, int bufferRows)
        {
            float start = Math.Max(pageTop, listTop);
            float end = Math.Min(pageTop + viewportHeight, listTop + count * rowStride);
            if (count <= 0 || rowStride <= 0f || viewportHeight <= 0f || end <= start)
            {
                return (0, -1);
            }

            return ListVirtualization.GetVisibleRange(start - listTop,
                end - start, rowStride, count, bufferRows);
        }
    }
}
