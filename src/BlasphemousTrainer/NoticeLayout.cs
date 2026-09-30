using System;
using System.Collections.Generic;

namespace BlasphemousTrainer
{
    internal sealed class NoticePlacement
    {
        internal int Index;
        internal float X, Y, Width, Height;
    }
    internal static class NoticeLayout
    {
        internal static NoticePlacement[] Plan(float screenWidth, float screenHeight, float preferredY, float[] heights)
        {
            float margin = Math.Min(12, Math.Min(screenWidth, screenHeight) / 4);
            float width = Math.Max(1, Math.Min(420, screenWidth - margin * 2));
            float available = Math.Max(1, screenHeight - margin * 2);
            var kept = new List<NoticePlacement>();
            float total = 0;
            // Newest notice always wins. Older notices are shown only when they fit completely.
            for (int i = heights.Length - 1; i >= 0; i--)
            {
                float height = Math.Min(available, Math.Max(1, heights[i]));
                float extra = height + (kept.Count == 0 ? 0 : 6);
                if (total + extra > available) break;
                kept.Insert(0, new NoticePlacement { Index = i, Width = width, Height = height });
                total += extra;
            }
            float y = Math.Max(margin, Math.Min(preferredY, screenHeight - margin - total));
            foreach (var item in kept) { item.X = screenWidth - margin - width; item.Y = y; y += item.Height + 6; }
            return kept.ToArray();
        }
    }
}
