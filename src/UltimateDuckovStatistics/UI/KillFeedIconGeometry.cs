using UnityEngine;

namespace UltimateDuckovStatistics.UI;

internal static class KillFeedIconGeometry
{
    // An upward blast with uneven spikes, an open center, and a ground line.
    internal static Color32[] ExplosionPixels()
    {
        var outline = new (int x, int y)[] {
            (5,13),(1,27),(12,22),(3,40),(17,30),(10,49),(25,35),
            (13,63),(32,39),(35,47),(39,34),(56,57),(49,32),(63,45),
            (54,24),(63,28),(57,15),(63,17),(60,12),(48,12),
            (50,22),(43,19),(46,32),(36,23),(34,34),(30,22),
            (22,31),(24,18),(14,22),(18,12)
        };
        var pixels = new Color32[64 * 64];
        for (var y = 0; y < 64; y++)
            for (var x = 0; x < 64; x++)
            {
                var inside = false;
                for (int i = 0, j = outline.Length - 1; i < outline.Length; j = i++)
                {
                    var a = outline[i]; var b = outline[j];
                    if ((a.y > y + .5) != (b.y > y + .5)
                        && x + .5 < (b.x - a.x) * (y + .5 - a.y) / (b.y - a.y) + a.x)
                        inside = !inside;
                }
                if (inside || (y >= 3 && y <= 5 && x >= 8 && x <= 57))
                    pixels[y * 64 + x] = new Color32(255, 255, 255, 255);
            }
        return pixels;
    }

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
