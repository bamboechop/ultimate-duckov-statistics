namespace UltimateDuckovStatistics.Core.Compatibility;

public sealed class CleanupCompletionGate
{
    private readonly Action completion;
    private int remaining;

    public CleanupCompletionGate(int participantCount, Action completion)
    {
        if (participantCount <= 0) throw new ArgumentOutOfRangeException(nameof(participantCount));
        this.completion = completion ?? throw new ArgumentNullException(nameof(completion));
        remaining = participantCount;
    }

    public int Remaining => Math.Max(Volatile.Read(ref remaining), 0);

    public void Signal()
    {
        var current = Interlocked.Decrement(ref remaining);
        if (current == 0) completion();
        if (current < 0) Interlocked.Increment(ref remaining);
    }
}
