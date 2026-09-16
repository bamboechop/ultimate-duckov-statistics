namespace UltimateDuckovStatistics.Encounters;

internal readonly struct EncounterRouteStroke
{
    public EncounterRouteStroke(EncounterMapPoint from, EncounterMapPoint to, bool dotted)
    { From = from; To = to; Dotted = dotted; Available = true; }
    public EncounterMapPoint From { get; }
    public EncounterMapPoint To { get; }
    public bool Dotted { get; }
    public bool Available { get; }
}

// A pane-local, software-clipped layer. No per-stroke GUI matrices, pivots or
// scissor state: the native viewer draws this entire texture over the map once.
internal sealed class EncounterRouteRaster
{
    public const int MaximumEdges = 4096;
    private const int MaximumDimension = 1024;
    private readonly double logicalWidth, logicalHeight, pixelScale;
    public int Width { get; }
    public int Height { get; }
    // Straight-alpha RGBA, bottom row first (Unity LoadRawTextureData order).
    public byte[] Pixels { get; }

    public EncounterRouteRaster(double logicalWidth, double logicalHeight)
    {
        if (!Finite(logicalWidth) || !Finite(logicalHeight) || logicalWidth <= 0 || logicalHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(logicalWidth));
        this.logicalWidth = logicalWidth; this.logicalHeight = logicalHeight;
        pixelScale = Math.Min(1, MaximumDimension / Math.Max(logicalWidth, logicalHeight));
        Width = Math.Max(1, (int)Math.Ceiling(logicalWidth * pixelScale));
        Height = Math.Max(1, (int)Math.Ceiling(logicalHeight * pixelScale));
        Pixels = new byte[checked(Width * Height * 4)];
    }

    public void Render(IReadOnlyList<EncounterRouteStroke> strokes, EncounterMapFrame frame,
        EncounterRevealTimeline? timeline, double progress)
    {
        if (strokes.Count > MaximumEdges) throw new ArgumentOutOfRangeException(nameof(strokes));
        if (!Finite(progress) || progress < 0 || progress > 1) throw new ArgumentOutOfRangeException(nameof(progress));
        Array.Clear(Pixels, 0, Pixels.Length);
        // All outlines precede all white cores, so adjacent sample segments do
        // not repeatedly cover the previous segment's core with black caps.
        for (var pass = 0; pass < 3; pass++)
        {
            var offset = pass == 0 ? 2 * pixelScale : 0;
            // 3 px white core plus a 2 px black outline on each side.
            var radius = (pass == 0 ? 4 : pass == 1 ? 3.5 : 1.5) * pixelScale;
            for (var index = 0; index < strokes.Count; index++)
            {
                var stroke = strokes[index];
                var fraction = timeline?.EdgeFraction(index, progress) ?? progress;
                if (!stroke.Available || fraction <= 0) continue;
                var to = new EncounterMapPoint(stroke.From.X + (stroke.To.X - stroke.From.X) * fraction,
                    stroke.From.Y + (stroke.To.Y - stroke.From.Y) * fraction);
                var a = EncounterMapGeometry.ToScreen(stroke.From, frame, logicalWidth, logicalHeight);
                var b = EncounterMapGeometry.ToScreen(to, frame, logicalWidth, logicalHeight);
                DrawStroke(a.X / logicalWidth * Width + offset, a.Y / logicalHeight * Height + offset,
                    b.X / logicalWidth * Width + offset, b.Y / logicalHeight * Height + offset,
                    radius, pass == 2 ? 1 : 0, pass == 0 ? .4 : 1, stroke.Dotted);
            }
        }
    }

    private void DrawStroke(double x0, double y0, double x1, double y1, double radius, double gray, double alpha, bool dotted)
    {
        var dx = x1 - x0; var dy = y1 - y0;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (!Finite(length) || length < .001) return;
        var first = 0d; var last = 1d;
        // Clip before finding dash intervals or scanning rows. Even an off-map
        // endpoint cannot make raster work scale with its world-coordinate size.
        var margin = radius + 1;
        if (!Clip(-dx, x0 + margin, ref first, ref last) || !Clip(dx, Width + margin - x0, ref first, ref last)
            || !Clip(-dy, y0 + margin, ref first, ref last) || !Clip(dy, Height + margin - y0, ref first, ref last)) return;
        if (!dotted)
        {
            Segment(x0 + dx * first, y0 + dy * first, x0 + dx * last, y0 + dy * last, radius, gray, alpha);
            return;
        }
        var period = Math.Max(2, 12 * pixelScale);
        // Keep the dash phase relative to the actual departure, including when
        // the departure is clipped; revealing the route never stretches dashes.
        var startDistance = first * length;
        var endDistance = last * length;
        var dash = Math.Floor(startDistance / period) * period;
        var count = (int)Math.Ceiling((endDistance - dash) / period) + 1;
        for (var i = 0; i < count; i++, dash += period)
        {
            var begin = Math.Max(startDistance, dash) / length;
            var end = Math.Min(endDistance, dash + period * .5) / length;
            if (end <= begin) continue;
            Segment(x0 + dx * begin, y0 + dy * begin, x0 + dx * end, y0 + dy * end, radius, gray, alpha);
        }
    }

    private void Segment(double x0, double y0, double x1, double y1, double radius, double gray, double alpha)
    {
        var dx = x1 - x0; var dy = y1 - y0;
        var squared = dx * dx + dy * dy;
        if (squared < .000001) return;
        var margin = radius + .5;
        var minY = Math.Max(0, (int)Math.Floor(Math.Min(y0, y1) - margin));
        var maxY = Math.Min(Height - 1, (int)Math.Ceiling(Math.Max(y0, y1) + margin));
        for (var y = minY; y <= maxY; y++)
        {
            var row = y + .5;
            var first = 0d; var last = 1d;
            if (Math.Abs(dy) > .000001)
            {
                var a = (row - margin - y0) / dy;
                var b = (row + margin - y0) / dy;
                first = Math.Max(0, Math.Min(a, b)); last = Math.Min(1, Math.Max(a, b));
                if (last < first) continue;
            }
            var xa = x0 + dx * first; var xb = x0 + dx * last;
            var minX = Math.Max(0, (int)Math.Floor(Math.Min(xa, xb) - margin));
            var maxX = Math.Min(Width - 1, (int)Math.Ceiling(Math.Max(xa, xb) + margin));
            for (var x = minX; x <= maxX; x++)
            {
                var column = x + .5;
                var t = Math.Max(0, Math.Min(1, ((column - x0) * dx + (row - y0) * dy) / squared));
                var ax = column - (x0 + t * dx); var ay = row - (y0 + t * dy);
                var coverage = Math.Max(0, Math.Min(1, radius + .5 - Math.Sqrt(ax * ax + ay * ay)));
                if (coverage > 0) Blend(x, y, gray, alpha * coverage);
            }
        }
    }

    private void Blend(int x, int y, double gray, double alpha)
    {
        var index = ((Height - 1 - y) * Width + x) * 4;
        var priorAlpha = Pixels[index + 3] / 255d;
        var resultAlpha = alpha + priorAlpha * (1 - alpha);
        var resultGray = (gray * alpha + Pixels[index] / 255d * priorAlpha * (1 - alpha)) / resultAlpha;
        Pixels[index] = Pixels[index + 1] = Pixels[index + 2] = (byte)Math.Round(resultGray * 255);
        Pixels[index + 3] = (byte)Math.Round(resultAlpha * 255);
    }

    private static bool Clip(double p, double q, ref double first, ref double last)
    {
        if (p == 0) return q >= 0;
        var t = q / p;
        if (p < 0) { if (t > last) return false; first = Math.Max(first, t); }
        else { if (t < first) return false; last = Math.Min(last, t); }
        return true;
    }
    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
