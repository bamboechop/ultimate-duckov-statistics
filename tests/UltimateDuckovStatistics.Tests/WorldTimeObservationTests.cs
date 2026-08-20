using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Tests;

public sealed class WorldTimeObservationTests
{
    private const long Second = TimeSpan.TicksPerSecond;

    [Fact]
    public void OrdinaryForwardProgressionUsesNativeTicks()
    {
        var tracker = new WorldTimeObservationTracker();
        Assert.Equal(WorldTimeObservationState.BaselineEstablished, tracker.Observe("g", Read(3, 100)).State);

        var result = tracker.Observe("g", Read(3, 160.25));

        Assert.True(result.Accepted);
        Assert.Equal(0, result.Mutation.CalendarDaysAdvanced);
        Assert.Equal(TimeSpan.FromSeconds(60.25).Ticks, result.Mutation.ObservedGameTimeTicks);
    }

    [Fact]
    public void MidnightCrossingCountsOneDayAndOnlyActualNativeElapsedTime()
    {
        var tracker = new WorldTimeObservationTracker();
        tracker.Observe("g", Read(7, 86_290));

        var result = tracker.Observe("g", Read(8, 10));

        Assert.Equal(1, result.Mutation.CalendarDaysAdvanced);
        Assert.Equal(20 * Second, result.Mutation.ObservedGameTimeTicks);
    }

    [Fact]
    public void MultiDayAdvancementUsesTheInstalled86300SecondDay()
    {
        var tracker = new WorldTimeObservationTracker();
        tracker.Observe("g", Read(1, 100));

        var result = tracker.Observe("g", Read(4, 200));

        Assert.Equal(3, result.Mutation.CalendarDaysAdvanced);
        Assert.Equal((3 * WorldTimeObservationTracker.NativeSecondsPerDay + 100) * Second,
            result.Mutation.ObservedGameTimeTicks);
    }

    [Fact]
    public void NonSleepFastForwardContributesToObservedTimeButNotSleepTotals()
    {
        var boundary = new NativeWorldTimeObservationBoundary();
        boundary.ObserveClock("g", Read(5, 20_000));
        boundary.ObserveClock("g", Read(6, 25_000));

        var mutation = boundary.TakePending();

        Assert.Equal(1, mutation.CalendarDaysAdvanced);
        Assert.Equal((WorldTimeObservationTracker.NativeSecondsPerDay + 5_000) * Second,
            mutation.ObservedGameTimeTicks);
        Assert.Equal(0, mutation.CompletedSleepSessions);
        Assert.Equal(0, mutation.SleepAdvancedTimeTicks);
    }

    [Fact]
    public void LoadInitializationDuplicateAndGenerationReplacementDoNotCount()
    {
        var tracker = new WorldTimeObservationTracker();
        Assert.Equal(WorldTimeObservationState.BaselineEstablished, tracker.Observe("g1", Read(2, 300)).State);
        Assert.Equal(WorldTimeObservationState.Duplicate, tracker.Observe("g1", Read(2, 300)).State);
        Assert.Equal(WorldTimeObservationState.BaselineEstablished, tracker.Observe("g2", Read(20, 500)).State);
        Assert.Equal(5 * Second, tracker.Observe("g2", Read(20, 505)).Mutation.ObservedGameTimeTicks);
    }

    [Fact]
    public void BackwardMovementIsRejectedThenRebaselined()
    {
        var tracker = new WorldTimeObservationTracker();
        tracker.Observe("g", Read(4, 1000));

        Assert.Equal(WorldTimeObservationState.Backward, tracker.Observe("g", Read(4, 900)).State);
        Assert.Equal(5 * Second, tracker.Observe("g", Read(4, 905)).Mutation.ObservedGameTimeTicks);
    }

    [Theory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    [InlineData(0, WorldTimeObservationTracker.NativeTicksPerDay + 1)]
    public void InvalidClockReadingsFailClosed(long day, long ticks)
    {
        var tracker = new WorldTimeObservationTracker();
        Assert.Equal(WorldTimeObservationState.Invalid, tracker.Observe("g", new WorldClockReading(day, ticks)).State);
    }

    [Fact]
    public void CompletedSleepWithinDayIsAcceptedExactlyOnce()
    {
        var boundary = new NativeWorldTimeObservationBoundary();
        boundary.ObserveClock("g", Read(1, 100));
        boundary.ObserveClock("g", Read(1, 3700));
        Assert.True(boundary.BeginSleepCompletion("g", TimeSpan.FromHours(1).Ticks));

        Assert.True(boundary.CompleteSleep("g"));
        Assert.False(boundary.CompleteSleep("g"));
        var mutation = boundary.TakePending();
        Assert.Equal(TimeSpan.FromHours(1).Ticks, mutation.ObservedGameTimeTicks);
        Assert.Equal(1, mutation.CompletedSleepSessions);
        Assert.Equal(TimeSpan.FromHours(1).Ticks, mutation.SleepAdvancedTimeTicks);
    }

    [Fact]
    public void SleepAcrossMidnightCountsDayAndExactSleepSubset()
    {
        var boundary = new NativeWorldTimeObservationBoundary();
        boundary.ObserveClock("g", Read(2, 86_000));
        boundary.ObserveClock("g", Read(3, 300));
        Assert.True(boundary.BeginSleepCompletion("g", 600 * Second));
        Assert.True(boundary.CompleteSleep("g"));

        var mutation = boundary.TakePending();
        Assert.Equal(1, mutation.CalendarDaysAdvanced);
        Assert.Equal(600 * Second, mutation.ObservedGameTimeTicks);
        Assert.Equal(600 * Second, mutation.SleepAdvancedTimeTicks);
    }

    [Fact]
    public void MultiDaySleepRetainsEveryNativeDayAndTheExactAdvancedDuration()
    {
        var boundary = new NativeWorldTimeObservationBoundary();
        boundary.ObserveClock("g", Read(2, 100));
        boundary.ObserveClock("g", Read(4, 100));
        var duration = 2 * WorldTimeObservationTracker.NativeTicksPerDay;
        Assert.True(boundary.BeginSleepCompletion("g", duration));
        Assert.True(boundary.CompleteSleep("g"));

        var mutation = boundary.TakePending();
        Assert.Equal(2, mutation.CalendarDaysAdvanced);
        Assert.Equal(duration, mutation.ObservedGameTimeTicks);
        Assert.Equal(1, mutation.CompletedSleepSessions);
        Assert.Equal(duration, mutation.SleepAdvancedTimeTicks);
    }

    [Fact]
    public void CancellationFailureOverlapAndGenerationChangeNeverComplete()
    {
        var boundary = new NativeWorldTimeObservationBoundary();
        Assert.False(boundary.BeginSleepCompletion(string.Empty, 100));
        Assert.False(boundary.BeginSleepCompletion("g", -1));
        Assert.True(boundary.BeginSleepCompletion("g", 100));
        Assert.False(boundary.BeginSleepCompletion("g", 200));
        Assert.False(boundary.CompleteSleep("other"));
        Assert.True(boundary.TakePending().IsEmpty);
    }

    [Fact]
    public void ExactSleepAdvanceContractAcceptsWrapAndRejectsUnrelatedClockDelta()
    {
        var before = 3 * WorldTimeObservationTracker.NativeTicksPerDay + 86_000 * Second;
        var requested = 600 * Second;
        Assert.True(SleepAdvanceContract.TryValidate(
            requested,
            before,
            before + requested,
            out var actual,
            out _));
        Assert.Equal(requested, actual);
        Assert.False(SleepAdvanceContract.TryValidate(
            requested,
            before,
            before + requested + 30 * Second,
            out _,
            out var detail));
        Assert.Contains("did not match", detail, StringComparison.Ordinal);
    }

    [Fact]
    public void ResetDropsInterruptedSleepAndReestablishesWithoutDuplicateTotals()
    {
        var boundary = new NativeWorldTimeObservationBoundary();
        boundary.ObserveClock("g", Read(1, 100));
        boundary.ObserveClock("g", Read(1, 200));
        Assert.True(boundary.BeginSleepCompletion("g", 100 * Second));
        boundary.Reset();

        Assert.Equal(WorldTimeObservationState.BaselineEstablished,
            boundary.ObserveClock("g", Read(1, 200)).State);
        Assert.False(boundary.CompleteSleep("g"));
        Assert.True(boundary.TakePending().IsEmpty);
    }

    [Fact]
    public void HighFrequencyObservationsCoalesceWithoutPerEventPublication()
    {
        var boundary = new NativeWorldTimeObservationBoundary();
        boundary.ObserveClock("g", Read(0, 0));
        for (var index = 1; index <= 100_000; index++)
            boundary.ObserveClock("g", new WorldClockReading(0, index));

        var mutation = boundary.TakePending();
        Assert.Equal(100_000, mutation.ObservedGameTimeTicks);
        Assert.True(boundary.TakePending().IsEmpty);
    }

    [Fact]
    public void DeferredPublicationRetriesTheExactProductionBoundaryMutationWithoutDuplication()
    {
        var boundary = new NativeWorldTimeObservationBoundary();
        boundary.ObserveClock("g", Read(4, 100));
        boundary.ObserveClock("g", Read(5, 200));
        Assert.True(boundary.BeginSleepCompletion("g", 300 * Second));
        Assert.True(boundary.CompleteSleep("g"));

        Assert.False(boundary.FlushPending(_ => false));
        Assert.Throws<IOException>(() => boundary.FlushPending(_ => throw new IOException("retry")));

        var publications = new List<WorldTimeMutation>();
        Assert.True(boundary.FlushPending(mutation =>
        {
            publications.Add(mutation);
            return true;
        }));
        Assert.Single(publications);
        Assert.Equal(1, publications[0].CalendarDaysAdvanced);
        Assert.Equal((WorldTimeObservationTracker.NativeSecondsPerDay + 100) * Second,
            publications[0].ObservedGameTimeTicks);
        Assert.Equal(1, publications[0].CompletedSleepSessions);
        Assert.Equal(300 * Second, publications[0].SleepAdvancedTimeTicks);

        Assert.True(boundary.FlushPending(_ => throw new InvalidOperationException("empty flush must not publish")));
    }

    private static WorldClockReading Read(long day, double seconds) =>
        new(day, TimeSpan.FromSeconds(seconds).Ticks);
}
