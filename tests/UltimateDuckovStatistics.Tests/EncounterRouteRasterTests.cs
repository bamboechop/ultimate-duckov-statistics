using UltimateDuckovStatistics.Encounters.Diagnostics;
#if UDS_ENCOUNTER_DIAGNOSTICS
using System.Diagnostics;
using UltimateDuckovStatistics.Encounters;
using Xunit;
using Xunit.Abstractions;

namespace UltimateDuckovStatistics.Tests;

public sealed class EncounterRouteRasterTests(ITestOutputHelper output)
{
    private static readonly EncounterMapFrame UnitFrame = new(0, 0, 1, 1);

    [Theory]
    [InlineData(.2, .8, .8, .8)]
    [InlineData(.8, .8, .2, .8)]
    [InlineData(.2, .8, .2, .2)]
    [InlineData(.2, .2, .2, .8)]
    [InlineData(.2, .8, .8, .2)]
    [InlineData(.8, .2, .2, .8)]
    [InlineData(.2, .2, .8, .8)]
    [InlineData(.8, .8, .2, .2)]
    public void AllLineDirectionsStayOnTheProjectedSegment(double ax, double ay, double bx, double by)
    {
        var raster = new EncounterRouteRaster(100, 100);
        raster.Render(new[] { Stroke(ax, ay, bx, by) }, UnitFrame, null, 1);
        for (var step = 1; step < 10; step++)
        {
            var x = (ax + (bx - ax) * step / 10) * 100;
            var y = (1 - (ay + (by - ay) * step / 10)) * 100;
            Assert.InRange(Alpha(raster, (int)x, (int)y), 240, 255);
            Assert.InRange(Gray(raster, (int)x, (int)y), 110, 255);
        }
        var from = new EncounterMapPoint(ax * 100, (1 - ay) * 100);
        var to = new EncounterMapPoint(bx * 100, (1 - by) * 100);
        for (var y = 0; y < 100; y++)
            for (var x = 0; x < 100; x++)
                if (Alpha(raster, x, y) != 0)
                    Assert.True(DistanceToSegment(x + .5, y + .5, from, to) < 8, $"Unexpected pixel at {x},{y}");
    }

    [Fact]
    public void MockupStrokeHasThreeWhitePixelsAndTwoBlackPixelsOnEachSide()
    {
        var raster = new EncounterRouteRaster(100, 100);
        raster.Render(new[] { Stroke(.1, .795, .9, .795) }, UnitFrame, null, 1);
        foreach (var y in new[] { 19, 20, 21 }) Assert.Equal(255, Gray(raster, 50, y));
        foreach (var y in new[] { 17, 18, 22, 23 })
        { Assert.Equal(0, Gray(raster, 50, y)); Assert.Equal(255, Alpha(raster, 50, y)); }
        Assert.Equal(0, Alpha(raster, 50, 16));
    }

    [Fact]
    public void UploadBufferUsesBottomLeftOriginWithoutVerticallyMirroringTheRoute()
    {
        var raster = new EncounterRouteRaster(100, 100);
        raster.Render(new[] { Stroke(.2, .8, .8, .8) }, UnitFrame, null, 1);
        Assert.Equal(255, Alpha(raster, 50, 20));
        Assert.Equal(0, Alpha(raster, 50, 80));
        Assert.Equal(255, raster.Pixels[((100 - 1 - 20) * 100 + 50) * 4 + 3]);
    }

    [Fact]
    public void OffPaneEdgesAreClippedAndFullyOutsideEdgesLeaveNoPixels()
    {
        var raster = new EncounterRouteRaster(100, 80);
        raster.Render(new[] { Stroke(-1000000, .5, 1000000, .5) }, UnitFrame, null, 1);
        Assert.Equal(255, Alpha(raster, 0, 40));
        Assert.Equal(255, Alpha(raster, 99, 40));
        Assert.Equal(0, Alpha(raster, 50, 5));
        raster.Render(new[] { Stroke(-1000, -1000, -500, -500) }, UnitFrame, null, 1);
        Assert.All(raster.Pixels, value => Assert.Equal(0, value));
    }

    [Fact]
    public void TeleportDashesKeepTheirLengthAndPhaseDuringRevealAndClipping()
    {
        var raster = new EncounterRouteRaster(120, 80);
        var strokes = new[] { Stroke(-1, .5, 1, .5, true) };
        raster.Render(strokes, UnitFrame, null, .75);
        var partial = raster.Pixels.ToArray();
        Assert.Equal(255, Alpha(raster, 3, 40));
        Assert.Equal(0, Gray(raster, 9, 40)); // gap; a shadow may reach it
        Assert.Equal(0, Alpha(raster, 90, 40));
        raster.Render(strokes, UnitFrame, null, 1);
        for (var y = 0; y < 80; y++)
            for (var x = 0; x < 45; x++)
                Assert.Equal(partial[((80 - 1 - y) * 120 + x) * 4 + 3], Alpha(raster, x, y));
        Assert.Equal(255, Alpha(raster, 99, 40));
    }

    [Fact]
    public void TimelineRevealsAnOrderedPrefixAndReplayClearsCompletedPixels()
    {
        var raster = new EncounterRouteRaster(100, 100);
        var strokes = new[] { Stroke(.1, .8, .5, .8), Stroke(.5, .8, .5, .2) };
        var timeline = new EncounterRevealTimeline(new[] { new EncounterRevealSpan(0, 1, 40, false), new EncounterRevealSpan(1, 2, 60, false) });
        raster.Render(strokes, UnitFrame, timeline, .2);
        Assert.Equal(255, Alpha(raster, 20, 20));
        Assert.Equal(0, Alpha(raster, 45, 20));
        Assert.Equal(0, Alpha(raster, 50, 60));
        raster.Render(strokes, UnitFrame, timeline, 1);
        Assert.Equal(255, Alpha(raster, 50, 60));
        raster.Render(strokes, UnitFrame, timeline, 0);
        Assert.All(raster.Pixels, value => Assert.Equal(0, value));
    }

    [Theory]
    [InlineData(480, 360)]
    [InlineData(860, 624)]
    [InlineData(1900, 1240)]
    public void OverviewAndFocusShareArtworkCoordinatesAtDifferentPaneSizes(double width, double height)
    {
        var from = new EncounterMapPoint(.45, .38); var to = new EncounterMapPoint(.58, .4);
        var raster = new EncounterRouteRaster(width, height);
        Assert.InRange(raster.Width, 1, 1024); Assert.InRange(raster.Height, 1, 1024);
        foreach (var frame in new[] { EncounterMapGeometry.Overview(width, height), EncounterMapGeometry.Focus(from, to, width, height) })
        {
            raster.Render(new[] { new EncounterRouteStroke(from, to, false) }, frame, null, 1);
            foreach (var point in new[] { from, to, new EncounterMapPoint((from.X + to.X) / 2, (from.Y + to.Y) / 2) })
            {
                var screen = EncounterMapGeometry.ToScreen(point, frame, width, height);
                var x = (int)(screen.X / width * raster.Width); var y = (int)(screen.Y / height * raster.Height);
                Assert.True(Alpha(raster, x, y) > 100);
            }
        }
    }

    [Fact]
    public void MissingEndpointsAreNotRenderedAndEdgeBudgetIsExplicit()
    {
        var raster = new EncounterRouteRaster(100, 100);
        raster.Render(new EncounterRouteStroke[1], UnitFrame, null, 1);
        Assert.All(raster.Pixels, value => Assert.Equal(0, value));
        Assert.Throws<ArgumentOutOfRangeException>(() => raster.Render(new EncounterRouteStroke[EncounterRouteRaster.MaximumEdges + 1], UnitFrame, null, 1));
    }

    [Fact]
    public void SavedNativeCaptureRendersThroughTheSameRasterAsTheViewer()
    {
        var path = Environment.GetEnvironmentVariable("UDS_ENCOUNTER_REPLAY_SAMPLE");
        if (string.IsNullOrEmpty(path)) return;
        var group = Assert.Single(EncounterCaptureReplay.Load(path).Groups);
        var calibration = new EncounterMapCalibration { Available = true, CenterX = 329, CenterZ = 241, WorldSize = 560 };
        var strokes = group.PathEdges.Select(edge =>
        {
            Assert.True(EncounterMapGeometry.TryProject(calibration, edge.From.Position.X, edge.From.Position.Z, out var a));
            Assert.True(EncounterMapGeometry.TryProject(calibration, edge.To.Position.X, edge.To.Position.Z, out var b));
            return new EncounterRouteStroke(a, b, edge.Style == EncounterReplayEdgeStyle.Teleport);
        }).ToArray();
        Assert.Equal(2, strokes.Count(stroke => stroke.Dotted));
        var timeline = new EncounterRevealTimeline(group.PathEdges.Select(edge => new EncounterRevealSpan(edge.FromTime, edge.ToTime,
            Math.Sqrt(Math.Pow(edge.To.Position.X - edge.From.Position.X, 2) + Math.Pow(edge.To.Position.Z - edge.From.Position.Z, 2)), edge.Style == EncounterReplayEdgeStyle.Teleport)));
        var raster = new EncounterRouteRaster(860, 624);
        var frame = EncounterMapGeometry.Overview(860, 624);
        var stopwatch = Stopwatch.StartNew();
        for (var tick = 0; tick <= 100; tick++) raster.Render(strokes, frame, timeline, tick / 100d);
        stopwatch.Stop();
        output.WriteLine($"101 reveal frames, {strokes.Length} edges, managed raster total {stopwatch.Elapsed.TotalMilliseconds:F1} ms (not native frame time).");
        var alphaPixels = 0;
        for (var y = 0; y < raster.Height; y++)
            for (var x = 0; x < raster.Width; x++)
            {
                if (Alpha(raster, x, y) == 0) continue;
                alphaPixels++;
                // The whole native route including both teleports lies in this
                // independently bounded southeast portion of the full canvas.
                Assert.InRange(x, 250, 570); Assert.InRange(y, 270, 485);
            }
        Assert.InRange(alphaPixels, 1000, 20000);
        var artifact = Environment.GetEnvironmentVariable("UDS_ENCOUNTER_RASTER_OUTPUT");
        if (!string.IsNullOrEmpty(artifact)) File.WriteAllBytes(artifact, raster.Pixels);
    }

    private static EncounterRouteStroke Stroke(double x0, double y0, double x1, double y1, bool dotted = false) => new(new(x0, y0), new(x1, y1), dotted);
    private static byte Alpha(EncounterRouteRaster raster, int x, int y) => raster.Pixels[((raster.Height - 1 - y) * raster.Width + x) * 4 + 3];
    private static byte Gray(EncounterRouteRaster raster, int x, int y) => raster.Pixels[((raster.Height - 1 - y) * raster.Width + x) * 4];
    private static double DistanceToSegment(double x, double y, EncounterMapPoint a, EncounterMapPoint b)
    {
        var dx = b.X - a.X; var dy = b.Y - a.Y;
        var t = Math.Clamp(((x - a.X) * dx + (y - a.Y) * dy) / (dx * dx + dy * dy), 0, 1);
        return Math.Sqrt(Math.Pow(x - a.X - t * dx, 2) + Math.Pow(y - a.Y - t * dy, 2));
    }
}
#endif
