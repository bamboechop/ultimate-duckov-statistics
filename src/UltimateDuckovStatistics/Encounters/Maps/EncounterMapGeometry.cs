namespace UltimateDuckovStatistics.Encounters;

internal sealed class EncounterMapCalibration
{
    public string MapId { get; set; } = string.Empty;
    public double CenterX { get; set; }
    public double CenterZ { get; set; }
    public double WorldSize { get; set; }
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public bool Combined { get; set; }
    public double CombinedCenterX { get; set; }
    public double CombinedCenterY { get; set; }
    public double CombinedSize { get; set; }
    public bool Available { get; set; }
    public bool Hidden { get; set; }
    public bool NoSignal { get; set; }
}

internal readonly struct EncounterMapPoint
{
    public EncounterMapPoint(double x, double y) { X = x; Y = y; }
    public double X { get; }
    public double Y { get; }
}

internal readonly struct EncounterMapFrame
{
    public EncounterMapFrame(double minX, double minY, double width, double height)
    { MinX = minX; MinY = minY; Width = width; Height = height; }
    public double MinX { get; }
    public double MinY { get; }
    public double Width { get; }
    public double Height { get; }
}

internal static class EncounterMapGeometry
{
    // UV is measured against the full square art canvas, with a bottom-left origin.
    // The individual entry's offset cancels against its own artwork position. For a
    // combined image, subtract the combined artwork position instead.
    public static bool TryProject(EncounterMapCalibration calibration, double worldX, double worldZ, out EncounterMapPoint uv)
    {
        uv = default;
        if (!calibration.Available || calibration.Hidden || calibration.NoSignal
            || !Finite(worldX) || !Finite(worldZ) || !Finite(calibration.CenterX) || !Finite(calibration.CenterZ)) return false;
        var size = calibration.Combined ? calibration.CombinedSize : calibration.WorldSize;
        if (!Finite(size) || size <= 0) return false;
        var x = worldX - calibration.CenterX;
        var y = worldZ - calibration.CenterZ;
        if (calibration.Combined)
        {
            x += calibration.OffsetX - calibration.CombinedCenterX;
            y += calibration.OffsetY - calibration.CombinedCenterY;
        }
        if (!Finite(x) || !Finite(y)) return false;
        uv = new EncounterMapPoint(x / size + 0.5, y / size + 0.5);
        return Finite(uv.X) && Finite(uv.Y);
    }

    public static EncounterMapFrame Overview(double viewportWidth, double viewportHeight, double paddingPixels = 16)
        => Fit(0, 0, 1, 1, viewportWidth, viewportHeight, paddingPixels);

    public static EncounterMapFrame Focus(EncounterMapPoint a, EncounterMapPoint b, double viewportWidth, double viewportHeight,
        double minimumUvExtent = 0.08, double paddingPixels = 32)
    {
        if (!Finite(a.X) || !Finite(a.Y) || !Finite(b.X) || !Finite(b.Y) || !Finite(minimumUvExtent) || minimumUvExtent <= 0)
            throw new ArgumentException("Focus points and minimum extent must be finite.");
        var width = Math.Max(minimumUvExtent, Math.Abs(a.X - b.X));
        var height = Math.Max(minimumUvExtent, Math.Abs(a.Y - b.Y));
        if (!Finite(width) || !Finite(height)) throw new ArgumentException("Focus span exceeds finite coordinate range.");
        return Fit(a.X * 0.5 + b.X * 0.5 - width * 0.5, a.Y * 0.5 + b.Y * 0.5 - height * 0.5, width, height,
            viewportWidth, viewportHeight, paddingPixels);
    }

    public static EncounterMapPoint ToScreen(EncounterMapPoint uv, EncounterMapFrame frame, double viewportWidth, double viewportHeight)
    {
        var x = (uv.X - frame.MinX) / frame.Width * viewportWidth;
        var y = (1 - (uv.Y - frame.MinY) / frame.Height) * viewportHeight;
        if (!Finite(x) || !Finite(y)) throw new ArgumentException("Projection exceeds finite screen coordinate range.");
        return new EncounterMapPoint(x, y);
    }

    public static EncounterMapPoint DistanceLabel(EncounterMapPoint a, EncounterMapPoint b,
        double width, double height, double viewportWidth, double viewportHeight)
    {
        var dx = b.X - a.X; var dy = b.Y - a.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        var nx = length > .001 ? -dy / length : 0;
        var ny = length > .001 ? dx / length : 1;
        if (ny < 0) { nx = -nx; ny = -ny; }
        var mx = (a.X + b.X) * .5; var my = (a.Y + b.Y) * .5;
        // The box's support along the normal keeps every corner off the 7 px
        // outlined connector. Prefer the lower side, then the opposite side at edges.
        var support = Math.Abs(nx) * width * .5 + Math.Abs(ny) * height * .5;
        EncounterMapPoint Candidate(double side)
        {
            var x = mx + side * nx * (support + 8) - width * .5;
            var y = my + side * ny * (support + 8) - height * .5;
            return new EncounterMapPoint(Math.Max(4, Math.Min(viewportWidth - width - 4, x)),
                Math.Max(4, Math.Min(viewportHeight - height - 4, y)));
        }
        double Clearance(EncounterMapPoint point) =>
            Math.Abs((point.X + width * .5 - mx) * nx + (point.Y + height * .5 - my) * ny) - support;
        var preferred = Candidate(1); var alternate = Candidate(-1);
        return Clearance(preferred) >= 6 || Clearance(preferred) >= Clearance(alternate) ? preferred : alternate;
    }

    private static EncounterMapFrame Fit(double minX, double minY, double width, double height,
        double viewportWidth, double viewportHeight, double padding)
    {
        if (!Finite(viewportWidth) || !Finite(viewportHeight) || viewportWidth <= 0 || viewportHeight <= 0
            || !Finite(padding) || padding < 0 || !Finite(minX) || !Finite(minY) || !Finite(width) || !Finite(height) || width <= 0 || height <= 0)
            throw new ArgumentException("Bounds, viewport and padding must be finite.");
        padding = Math.Min(padding, Math.Min(viewportWidth, viewportHeight) * 0.45);
        var scale = Math.Min((viewportWidth - 2 * padding) / width, (viewportHeight - 2 * padding) / height);
        var frameWidth = viewportWidth / scale;
        var frameHeight = viewportHeight / scale;
        if (!Finite(frameWidth) || !Finite(frameHeight) || frameWidth <= 0 || frameHeight <= 0)
            throw new ArgumentException("Framing exceeds finite coordinate range.");
        var frameMinX = minX + (width - frameWidth) * 0.5;
        var frameMinY = minY + (height - frameHeight) * 0.5;
        if (!Finite(frameMinX) || !Finite(frameMinY)) throw new ArgumentException("Framing origin exceeds finite coordinate range.");
        return new EncounterMapFrame(frameMinX, frameMinY, frameWidth, frameHeight);
    }

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
