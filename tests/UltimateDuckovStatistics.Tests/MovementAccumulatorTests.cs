using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Tests;

public sealed class MovementAccumulatorTests
{
    [Fact]
    [Trait("Category", "Movement")]
    public void NormalMovementAndStationaryJitterAreSeparated()
    {
        var accumulator = new MovementAccumulator();
        accumulator.Observe(Position(0), 0, 5);

        var physical = accumulator.Observe(Position(1), 0.2, 5);
        var jitter = accumulator.Observe(Position(1.01), 0.4, 5);

        Assert.Equal(MovementDisposition.Physical, physical.Disposition);
        Assert.Equal(MovementDisposition.JitterIgnored, jitter.Disposition);
        Assert.Equal(1, accumulator.PhysicalDistance, 6);
        Assert.Equal(0, accumulator.TeleportDistance);
    }

    [Fact]
    [Trait("Category", "Movement")]
    public void PlausibilityUsesVerifiedSpeedAndActualElapsedTime()
    {
        var accumulator = new MovementAccumulator();
        accumulator.Observe(Position(0), 0, 2);

        var atEnvelope = accumulator.Observe(Position(2.1), 0.5, 2);
        var outsideEnvelope = accumulator.Observe(Position(4.3), 1, 2);

        Assert.Equal(MovementDisposition.Physical, atEnvelope.Disposition);
        Assert.Equal(2.1, atEnvelope.AllowedDistance, 6);
        Assert.Equal(MovementDisposition.Teleport, outsideEnvelope.Disposition);
        Assert.Equal(2.1, accumulator.PhysicalDistance, 6);
        Assert.Equal(2.2, accumulator.TeleportDistance, 6);
    }

    [Fact]
    [Trait("Category", "Movement")]
    public void PauseLoadingAndExplicitPositionBoundariesNeverIncreasePhysicalDistance()
    {
        var accumulator = new MovementAccumulator();
        accumulator.Observe(Position(0), 0, 5);

        accumulator.Observe(Position(10), 1, 5, MovementObservationKind.ResumeBoundary);
        accumulator.Observe(Position(30), 2, 5, MovementObservationKind.LoadingBoundary);
        accumulator.Observe(Position(50), 2, 5, MovementObservationKind.ExplicitTeleport);

        Assert.Equal(0, accumulator.PhysicalDistance);
        Assert.Equal(50, accumulator.TeleportDistance, 6);
    }

    [Fact]
    [Trait("Category", "Movement")]
    public void MapBoundaryClassifiesCrossMapDisplacementAsTeleportExactlyOnce()
    {
        var accumulator = new MovementAccumulator();
        accumulator.Observe(Position(0), 0, 5);

        var boundary = accumulator.Observe(Position(50), 0.2, 5, MovementObservationKind.MapBoundary);
        var next = accumulator.Observe(Position(50.5), 0.4, 5);

        Assert.Equal(MovementDisposition.Teleport, boundary.Disposition);
        Assert.Equal(50, accumulator.TeleportDistance, 6);
        Assert.Equal(MovementDisposition.Physical, next.Disposition);
        Assert.Equal(0.5, accumulator.PhysicalDistance, 6);
    }

    [Fact]
    [Trait("Category", "Movement")]
    public void InvalidCoordinatesLongGapsDuplicatesAndObjectReplacementAreSafe()
    {
        var accumulator = new MovementAccumulator();
        accumulator.Observe(Position(0), 0, 5);
        Assert.Equal(
            MovementDisposition.DuplicateIgnored,
            accumulator.Observe(Position(1), 0, 5).Disposition);
        Assert.Equal(
            MovementDisposition.InvalidIgnored,
            accumulator.Observe(new Position3D(double.NaN, 0, 0), 0.2, 5).Disposition);
        Assert.Equal(
            MovementDisposition.BaselineEstablished,
            accumulator.Observe(Position(100), 0.4, 5).Disposition);
        Assert.Equal(
            MovementDisposition.Teleport,
            accumulator.Observe(Position(110), 3, 5).Disposition);
        Assert.Equal(
            MovementDisposition.ObjectReplacementReset,
            accumulator.Observe(Position(1000), 3.1, 5, MovementObservationKind.ObjectReplacement).Disposition);

        Assert.Equal(0, accumulator.PhysicalDistance);
        Assert.Equal(10, accumulator.TeleportDistance, 6);
    }

    private static Position3D Position(double x) => new(x, 0, 0);
}
