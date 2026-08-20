using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class NativeWorldTimeObservationBoundary
{
    private readonly WorldTimeObservationTracker tracker = new();
    private readonly SleepCompletionGate sleepCompletion = new();
    private long calendarDays;
    private long observedTicks;
    private long completedSleepSessions;
    private long sleepAdvancedTicks;

    public WorldTimeObservationResult ObserveClock(string generationId, WorldClockReading reading)
    {
        var result = tracker.Observe(generationId, reading);
        if (!result.Accepted) return result;
        checked
        {
            calendarDays += result.Mutation.CalendarDaysAdvanced;
            observedTicks += result.Mutation.ObservedGameTimeTicks;
        }
        return result;
    }

    public bool BeginSleepCompletion(string generationId, long advancedTicks) =>
        sleepCompletion.Begin(generationId, advancedTicks);

    public bool CompleteSleep(string generationId)
    {
        if (!sleepCompletion.Complete(generationId, out var mutation)) return false;
        checked
        {
            completedSleepSessions += mutation.CompletedSleepSessions;
            sleepAdvancedTicks += mutation.SleepAdvancedTimeTicks;
        }
        return true;
    }

    public WorldTimeMutation TakePending()
    {
        var result = new WorldTimeMutation(calendarDays, observedTicks, completedSleepSessions, sleepAdvancedTicks);
        calendarDays = 0;
        observedTicks = 0;
        completedSleepSessions = 0;
        sleepAdvancedTicks = 0;
        return result;
    }

    public bool FlushPending(Func<WorldTimeMutation, bool> publish)
    {
        if (publish == null) throw new ArgumentNullException(nameof(publish));
        var mutation = TakePending();
        if (mutation.IsEmpty) return true;
        try
        {
            if (publish(mutation)) return true;
        }
        catch
        {
            RestorePending(mutation);
            throw;
        }
        RestorePending(mutation);
        return false;
    }

    public void RestorePending(WorldTimeMutation mutation)
    {
        checked
        {
            calendarDays += mutation.CalendarDaysAdvanced;
            observedTicks += mutation.ObservedGameTimeTicks;
            completedSleepSessions += mutation.CompletedSleepSessions;
            sleepAdvancedTicks += mutation.SleepAdvancedTimeTicks;
        }
    }

    public void Reset()
    {
        tracker.Reset();
        sleepCompletion.Clear();
        calendarDays = 0;
        observedTicks = 0;
        completedSleepSessions = 0;
        sleepAdvancedTicks = 0;
    }

    public WorldTimeObservationResult ResetAndEstablishBaseline(
        string generationId,
        WorldClockReading reading)
    {
        Reset();
        return ObserveClock(generationId, reading);
    }

    public void ClearPendingSleep() => sleepCompletion.Clear();
}
