namespace UltimateDuckovStatistics.Encounters;

internal readonly struct EncounterRevealSpan
{
    internal EncounterRevealSpan(double startTime, double endTime, double distance, bool teleport)
    { StartTime = startTime; EndTime = endTime; Distance = distance; Teleport = teleport; }
    internal double StartTime { get; }
    internal double EndTime { get; }
    internal double Distance { get; }
    internal bool Teleport { get; }
}

// Geometry is prepared once. Event visibility follows the recorded timeline,
// including revisits, rather than the nearest location on a looping route.
internal sealed class EncounterRevealTimeline
{
    private readonly EncounterRevealSpan[] spans;
    private readonly double[] starts, ends;
    private readonly double total;

    internal EncounterRevealTimeline(IEnumerable<EncounterRevealSpan> source)
    {
        spans = source.ToArray();
        starts = new double[spans.Length]; ends = new double[spans.Length];
        for (var index = 0; index < spans.Length; index++)
        {
            var span = spans[index];
            if (!Finite(span.StartTime) || !Finite(span.EndTime) || !Finite(span.Distance)
                || span.Distance < 0 || span.EndTime < span.StartTime
                || index > 0 && span.StartTime < spans[index - 1].EndTime)
                throw new ArgumentException("Reveal spans must have finite, chronological observations.", nameof(source));
            starts[index] = total;
            // Teleports get a brief reveal step; their distance is never added to walking distance.
            total += span.Teleport ? Math.Min(10, span.Distance) : span.Distance;
            ends[index] = total;
        }
    }

    internal double EdgeFraction(int index, double progress)
    {
        if (progress >= 1) return 1;
        var shown = Math.Max(0, progress) * total;
        return ends[index] <= starts[index] ? (shown >= ends[index] ? 1 : 0)
            : Math.Min(1, Math.Max(0, (shown - starts[index]) / (ends[index] - starts[index])));
    }

    internal double MarkerProgress(double eventTime)
    {
        if (!Finite(eventTime) || total <= 0) return 1;
        var low = 0; var high = spans.Length;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (spans[middle].EndTime < eventTime) low = middle + 1; else high = middle;
        }
        if (low == spans.Length || eventTime < spans[low].StartTime) return 1;
        var span = spans[low];
        var fraction = span.EndTime == span.StartTime ? 1
            : (eventTime - span.StartTime) / (span.EndTime - span.StartTime);
        return (starts[low] + (ends[low] - starts[low]) * fraction) / total;
    }

    private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}
