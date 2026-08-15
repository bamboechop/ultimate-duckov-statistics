namespace UltimateDuckovStatistics.Adapters;

internal sealed class IncrementalPatchInspectionScheduler
{
    private readonly TimeSpan cycle;
    private DateTime nextDueUtc;
    private int nextIndex;

    public IncrementalPatchInspectionScheduler(TimeSpan cycle)
    {
        if (cycle <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(cycle));
        this.cycle = cycle;
    }

    public void Reset(DateTime nowUtc, int registrationCount)
    {
        ValidateCount(registrationCount);
        nextIndex = 0;
        nextDueUtc = EnsureUtc(nowUtc).Add(Interval(registrationCount));
    }

    public bool TryTake(DateTime nowUtc, int registrationCount, out int index)
    {
        ValidateCount(registrationCount);
        nowUtc = EnsureUtc(nowUtc);
        if (registrationCount == 0 || nowUtc < nextDueUtc)
        {
            index = -1;
            return false;
        }

        index = nextIndex % registrationCount;
        nextIndex = (index + 1) % registrationCount;
        nextDueUtc = nowUtc.Add(Interval(registrationCount));
        return true;
    }

    private TimeSpan Interval(int registrationCount) => registrationCount == 0
        ? cycle
        : TimeSpan.FromTicks(Math.Max(1, cycle.Ticks / registrationCount));

    private static void ValidateCount(int registrationCount)
    {
        if (registrationCount < 0) throw new ArgumentOutOfRangeException(nameof(registrationCount));
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
