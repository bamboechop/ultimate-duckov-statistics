#if UDS_ENCOUNTER_DIAGNOSTICS
using UltimateDuckovStatistics.Encounters;
using Xunit;

namespace UltimateDuckovStatistics.Tests;

public sealed class EncounterMapGeometryPrototypeTests
{
    [Theory]
    [InlineData(100, 120, 400, 120)]
    [InlineData(250, 50, 250, 250)]
    [InlineData(100, 50, 400, 250)]
    [InlineData(100, 250, 400, 50)]
    [InlineData(40, 270, 460, 270)]
    [InlineData(40, 30, 460, 30)]
    [InlineData(30, 50, 30, 250)]
    [InlineData(470, 50, 470, 250)]
    public void DistanceBoxRemainsInsidePaneAndClearOfConnector(double ax, double ay, double bx, double by)
    {
        var a = new EncounterMapPoint(ax, ay); var b = new EncounterMapPoint(bx, by);
        const double width = 86, height = 24;
        var point = EncounterMapGeometry.DistanceLabel(a, b, width, height, 500, 300);
        Assert.InRange(point.X, 4, 500 - width - 4); Assert.InRange(point.Y, 4, 300 - height - 4);
        var length = Math.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
        var nx = -(by - ay) / length; var ny = (bx - ax) / length;
        var centerDistance = Math.Abs((point.X + width * .5 - ax) * nx + (point.Y + height * .5 - ay) * ny);
        var boxSupport = Math.Abs(nx) * width * .5 + Math.Abs(ny) * height * .5;
        Assert.True(centerDistance - boxSupport >= 6, "Distance box intersects the outlined connector.");
    }

    [Fact]
    public void IndividualArtUsesSceneCenterAndDoesNotDoubleApplyEntryOffset()
    {
        var map = GroundZero();
        Assert.True(EncounterMapGeometry.TryProject(map, 329, 241, out var center));
        Assert.Equal(0.5, center.X); Assert.Equal(0.5, center.Y);
        Assert.True(EncounterMapGeometry.TryProject(map, 609, 521, out var corner));
        Assert.Equal(1, corner.X); Assert.Equal(1, corner.Y);
    }

    [Fact]
    public void CombinedArtUsesEntryPlacementRelativeToCombinedImage()
    {
        var map = GroundZero();
        map.Combined = true; map.CombinedSize = 1000;
        map.OffsetX = 200; map.OffsetY = 100; map.CombinedCenterX = 50; map.CombinedCenterY = 25;
        Assert.True(EncounterMapGeometry.TryProject(map, 329, 241, out var point));
        Assert.Equal(0.65, point.X, 8); Assert.Equal(0.575, point.Y, 8);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public void MissingMapEvidenceDoesNotInventTerrain(bool hidden, bool noSignal, bool unavailable)
    {
        var map = GroundZero(); map.Hidden = hidden; map.NoSignal = noSignal; map.Available = !unavailable;
        Assert.False(EncounterMapGeometry.TryProject(map, 329, 241, out _));
    }

    [Fact]
    public void OverviewFitsFullCanvasWithoutDistortingAndUsesTopDownScreenY()
    {
        var frame = EncounterMapGeometry.Overview(800, 400, 20);
        var topLeft = EncounterMapGeometry.ToScreen(new(0, 1), frame, 800, 400);
        var bottomRight = EncounterMapGeometry.ToScreen(new(1, 0), frame, 800, 400);
        Assert.Equal(220, topLeft.X, 8); Assert.Equal(20, topLeft.Y, 8);
        Assert.Equal(580, bottomRight.X, 8); Assert.Equal(380, bottomRight.Y, 8);
    }

    [Fact]
    public void FocusContainsBothParticipantsIncludingOutsideArtAndCoincidentPositions()
    {
        var a = new EncounterMapPoint(-0.2, 0.1); var b = new EncounterMapPoint(1.3, 0.9);
        var frame = EncounterMapGeometry.Focus(a, b, 500, 300);
        foreach (var point in new[] { a, b })
        {
            var screen = EncounterMapGeometry.ToScreen(point, frame, 500, 300);
            Assert.InRange(screen.X, 31.99, 468.01); Assert.InRange(screen.Y, 31.99, 268.01);
        }
        var same = EncounterMapGeometry.Focus(a, a, 500, 300);
        Assert.True(same.Width > 0 && same.Height > 0);
    }

    private static EncounterMapCalibration GroundZero() => new()
    { Available = true, CenterX = 329, CenterZ = 241, WorldSize = 560, OffsetX = -1790.6, OffsetY = 214.8 };

    [Fact]
    public void ExtremeFiniteCoordinatesCannotProduceAnInfiniteFrameOrProjection()
    {
        Assert.Throws<ArgumentException>(() => EncounterMapGeometry.Focus(new(double.MaxValue, 0), new(-double.MaxValue, 0), 500, 300));
        var map = GroundZero(); map.CenterX = -double.MaxValue;
        Assert.False(EncounterMapGeometry.TryProject(map, double.MaxValue, 0, out _));
        Assert.Throws<ArgumentException>(() => EncounterMapGeometry.ToScreen(new(double.MaxValue, 0), new(-double.MaxValue, 0, 1, 1), 500, 300));
    }
}
#endif
