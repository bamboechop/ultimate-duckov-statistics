using UnityEngine;

namespace UltimateDuckovStatistics.UI;

internal static class KillFeedIconGeometry
{
    internal static Rect VisibleBounds(Color32[] pixels, int width, int height)
    {
        var left = width; var bottom = height; var right = -1; var top = -1;
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
            {
                // Keep even faint antialiased edges; remove only fully transparent margins.
                if (pixels[y * width + x].a == 0) continue;
                left = Math.Min(left, x); right = Math.Max(right, x);
                bottom = Math.Min(bottom, y); top = Math.Max(top, y);
            }
        // An empty native icon still needs a valid sprite rectangle.
        return right < left ? new Rect(0, 0, width, height)
            : new Rect(left, bottom, right - left + 1, top - bottom + 1);
    }
}
