namespace UltimateDuckovStatistics.Core.Tracking;

using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;

public readonly struct Position3D
{
    public Position3D(double x, double y, double z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    public double X { get; }

    public double Y { get; }

    public double Z { get; }

    public bool IsFinite => IsFiniteValue(X) && IsFiniteValue(Y) && IsFiniteValue(Z);

    public double DistanceTo(Position3D other)
    {
        var x = other.X - X;
        var y = other.Y - Y;
        var z = other.Z - Z;
        return Math.Sqrt((x * x) + (y * y) + (z * z));
    }

    private static bool IsFiniteValue(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}

public enum MovementObservationKind
{
    Regular,
    ResumeBoundary,
    LoadingBoundary,
    MapBoundary,
    ExplicitTeleport,
    ObjectReplacement
}

public enum MovementDisposition
{
    BaselineEstablished,
    Physical,
    Teleport,
    TransitionExcluded,
    JitterIgnored,
    DuplicateIgnored,
    InvalidIgnored,
    ObjectReplacementReset
}

public sealed class MovementObservationResult
{
    public MovementObservationResult(MovementDisposition disposition, double distance, double allowedDistance)
    {
        Disposition = disposition;
        Distance = distance;
        AllowedDistance = allowedDistance;
    }

    public MovementDisposition Disposition { get; }

    public double Distance { get; }

    public double AllowedDistance { get; }
}

public sealed class MovementAccumulator
{
    public const double DefaultJitterEpsilonMeters = 0.02;
    public const double DefaultSpeedToleranceMultiplier = 1.75;
    public const double DefaultBaseToleranceMeters = 0.35;
    public const double DefaultMaximumSampleGapSeconds = 2;

    private readonly double jitterEpsilonMeters;
    private readonly double speedToleranceMultiplier;
    private readonly double baseToleranceMeters;
    private readonly double maximumSampleGapSeconds;
    private Position3D? baseline;
    private double baselineMonotonicSeconds;

    public MovementAccumulator(
        double jitterEpsilonMeters = DefaultJitterEpsilonMeters,
        double speedToleranceMultiplier = DefaultSpeedToleranceMultiplier,
        double baseToleranceMeters = DefaultBaseToleranceMeters,
        double maximumSampleGapSeconds = DefaultMaximumSampleGapSeconds)
    {
        if (!IsFiniteNonNegative(jitterEpsilonMeters))
        {
            throw new ArgumentOutOfRangeException(nameof(jitterEpsilonMeters));
        }

        if (!IsFinitePositive(speedToleranceMultiplier))
        {
            throw new ArgumentOutOfRangeException(nameof(speedToleranceMultiplier));
        }

        if (!IsFiniteNonNegative(baseToleranceMeters))
        {
            throw new ArgumentOutOfRangeException(nameof(baseToleranceMeters));
        }

        if (!IsFinitePositive(maximumSampleGapSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(maximumSampleGapSeconds));
        }

        this.jitterEpsilonMeters = jitterEpsilonMeters;
        this.speedToleranceMultiplier = speedToleranceMultiplier;
        this.baseToleranceMeters = baseToleranceMeters;
        this.maximumSampleGapSeconds = maximumSampleGapSeconds;
    }

    public double PhysicalDistance { get; private set; }

    public double TeleportDistance { get; private set; }

    public double TransitionExcludedDistance { get; private set; }

    public bool HasBaseline => baseline.HasValue;

    public MovementBaselineState CaptureBaseline() => baseline.HasValue
        ? new MovementBaselineState
        {
            HasBaseline = true,
            X = baseline.Value.X,
            Y = baseline.Value.Y,
            Z = baseline.Value.Z,
            MonotonicSeconds = baselineMonotonicSeconds
        }
        : new MovementBaselineState();

    public MovementObservationResult Observe(
        Position3D position,
        double monotonicSeconds,
        double maximumPlausibleSpeed,
        MovementObservationKind kind = MovementObservationKind.Regular)
    {
        if (!position.IsFinite || !IsFiniteNonNegative(monotonicSeconds))
        {
            baseline = null;
            return new MovementObservationResult(MovementDisposition.InvalidIgnored, 0, 0);
        }

        if (kind == MovementObservationKind.ObjectReplacement)
        {
            SetBaseline(position, monotonicSeconds);
            return new MovementObservationResult(MovementDisposition.ObjectReplacementReset, 0, 0);
        }

        if (!baseline.HasValue)
        {
            SetBaseline(position, monotonicSeconds);
            return new MovementObservationResult(MovementDisposition.BaselineEstablished, 0, 0);
        }

        var elapsed = monotonicSeconds - baselineMonotonicSeconds;
        if (elapsed <= 0 && kind == MovementObservationKind.Regular)
        {
            return new MovementObservationResult(MovementDisposition.DuplicateIgnored, 0, 0);
        }

        var distance = baseline.Value.DistanceTo(position);
        if (!IsFiniteNonNegative(distance))
        {
            baseline = null;
            return new MovementObservationResult(MovementDisposition.InvalidIgnored, 0, 0);
        }

        if (distance <= jitterEpsilonMeters)
        {
            SetBaseline(position, Math.Max(monotonicSeconds, baselineMonotonicSeconds));
            return new MovementObservationResult(MovementDisposition.JitterIgnored, distance, 0);
        }

        if (kind is MovementObservationKind.LoadingBoundary or MovementObservationKind.MapBoundary)
        {
            TransitionExcludedDistance = RouteStatisticsReducer.SaturatingAdd(TransitionExcludedDistance, distance);
            SetBaseline(position, Math.Max(monotonicSeconds, baselineMonotonicSeconds));
            return new MovementObservationResult(MovementDisposition.TransitionExcluded, distance, 0);
        }

        if (kind is MovementObservationKind.ResumeBoundary or MovementObservationKind.ExplicitTeleport)
        {
            TeleportDistance = RouteStatisticsReducer.SaturatingAdd(TeleportDistance, distance);
            SetBaseline(position, Math.Max(monotonicSeconds, baselineMonotonicSeconds));
            return new MovementObservationResult(MovementDisposition.Teleport, distance, 0);
        }

        if (elapsed > maximumSampleGapSeconds)
        {
            TeleportDistance = RouteStatisticsReducer.SaturatingAdd(TeleportDistance, distance);
            SetBaseline(position, monotonicSeconds);
            return new MovementObservationResult(MovementDisposition.Teleport, distance, 0);
        }

        if (!IsFinitePositive(maximumPlausibleSpeed))
        {
            baseline = null;
            return new MovementObservationResult(MovementDisposition.InvalidIgnored, 0, 0);
        }

        var allowedDistance = (maximumPlausibleSpeed * elapsed * speedToleranceMultiplier) + baseToleranceMeters;
        if (distance <= allowedDistance)
        {
            PhysicalDistance = RouteStatisticsReducer.SaturatingAdd(PhysicalDistance, distance);
            SetBaseline(position, monotonicSeconds);
            return new MovementObservationResult(MovementDisposition.Physical, distance, allowedDistance);
        }

        TeleportDistance = RouteStatisticsReducer.SaturatingAdd(TeleportDistance, distance);
        SetBaseline(position, monotonicSeconds);
        return new MovementObservationResult(MovementDisposition.Teleport, distance, allowedDistance);
    }

    public void Reset()
    {
        baseline = null;
        baselineMonotonicSeconds = 0;
        PhysicalDistance = 0;
        TeleportDistance = 0;
        TransitionExcludedDistance = 0;
    }

    private void SetBaseline(Position3D position, double monotonicSeconds)
    {
        baseline = position;
        baselineMonotonicSeconds = monotonicSeconds;
    }

    private static bool IsFiniteNonNegative(double value) =>
        value >= 0 && !double.IsNaN(value) && !double.IsInfinity(value);

    private static bool IsFinitePositive(double value) => IsFiniteNonNegative(value) && value > 0;
}
