using System.Diagnostics;

namespace UltimateDuckovStatistics.Core.Tracking;

/// <summary>Shared monotonic retry budget; callers retain ownership of failed data.</summary>
public sealed class PersistenceRetryBackoff
{
    private readonly Func<double> clock;
    private double nextAttempt;
    private double delay;

    public PersistenceRetryBackoff(Func<double>? clock = null) =>
        this.clock = clock ?? (() => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency);

    public bool IsDue => delay == 0 || clock() >= nextAttempt;
    public double DelaySeconds => delay;

    public void Failed()
    {
        delay = delay == 0 ? 1 : Math.Min(60, delay * 2);
        nextAttempt = clock() + delay;
    }

    public void Reset()
    {
        delay = 0;
        nextAttempt = 0;
    }
}
