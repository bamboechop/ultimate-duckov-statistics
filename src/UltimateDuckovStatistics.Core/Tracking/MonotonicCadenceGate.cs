namespace UltimateDuckovStatistics.Core.Tracking;

public sealed class MonotonicCadenceGate
{
    private readonly double intervalSeconds;
    private double? nextDueMonotonicSeconds;

    public MonotonicCadenceGate(double intervalSeconds)
    {
        if (intervalSeconds <= 0 || double.IsNaN(intervalSeconds) || double.IsInfinity(intervalSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(intervalSeconds));
        }

        this.intervalSeconds = intervalSeconds;
    }

    public bool IsDue(double monotonicSeconds)
    {
        Validate(monotonicSeconds);
        return !nextDueMonotonicSeconds.HasValue || monotonicSeconds >= nextDueMonotonicSeconds.Value;
    }

    public void MarkCompleted(double monotonicSeconds)
    {
        Validate(monotonicSeconds);
        nextDueMonotonicSeconds = monotonicSeconds + intervalSeconds;
    }

    public void Reset() => nextDueMonotonicSeconds = null;

    private static void Validate(double monotonicSeconds)
    {
        if (monotonicSeconds < 0 || double.IsNaN(monotonicSeconds) || double.IsInfinity(monotonicSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(monotonicSeconds));
        }
    }
}
