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
    [Trait("Category", "M12")]
    public void SelectedSlotIgnoresStalePriorClockUntilAReplacementInstanceLoads()
    {
        var boundary = new NativeWorldTimeObservationBoundary();
        var handoff = new NativeWorldTimeProfileHandoffBoundary();
        var priorClock = new object();
        var selectedClock = new object();

        boundary.ObserveClock("prior-slot", Read(2, 500));
        handoff.BeginAwaitingNativeLoad(1, priorClock);
        Assert.True(handoff.CompleteProfileChange(1, "selected-slot", boundary, null, out _));

        Assert.Null(handoff.Observe("selected-slot", priorClock, Read(2, 560), boundary));
        Assert.Equal(
            WorldTimeObservationState.BaselineEstablished,
            handoff.Observe("selected-slot", selectedClock, Read(20, 1_000), boundary)!.Value.State);
        Assert.Equal(
            5 * Second,
            handoff.Observe("selected-slot", selectedClock, Read(20, 1_005), boundary)!.Value.Mutation.ObservedGameTimeTicks);

        var mutation = boundary.TakePending();
        Assert.Equal(0, mutation.CalendarDaysAdvanced);
        Assert.Equal(5 * Second, mutation.ObservedGameTimeTicks);
    }

    [Fact]
    [Trait("Category", "M12")]
    public void SelectedSlotLoadBeforeDeferredProfileCommitIsBufferedForTheTargetGeneration()
    {
        var boundary = new NativeWorldTimeObservationBoundary();
        var handoff = new NativeWorldTimeProfileHandoffBoundary();
        var priorClock = new object();
        var selectedClock = new object();

        boundary.ObserveClock("prior-slot", Read(2, 500));
        handoff.BeginAwaitingNativeLoad(2, priorClock);
        Assert.Null(handoff.Observe("prior-slot", priorClock, Read(2, 560), boundary));
        Assert.Equal(
            WorldTimeObservationState.BaselineEstablished,
            handoff.Observe("prior-slot", selectedClock, Read(20, 1_000), boundary)!.Value.State);
        Assert.True(handoff.Observe("prior-slot", selectedClock, Read(20, 1_005), boundary)!.Value.Accepted);

        Assert.True(handoff.CompleteProfileChange(2, "selected-slot", boundary, null, out _));
        var mutation = boundary.TakePending();

        Assert.Equal(0, mutation.CalendarDaysAdvanced);
        Assert.Equal(5 * Second, mutation.ObservedGameTimeTicks);
    }

    [Fact]
    [Trait("Category", "M12")]
    public void ResetWhileSelectedSlotAwaitsLoadCannotBaselineFromThePriorSlotClock()
    {
        var boundary = new NativeWorldTimeObservationBoundary();
        var handoff = new NativeWorldTimeProfileHandoffBoundary();
        var priorClock = new object();
        var selectedClock = new object();

        boundary.ObserveClock("slot-a", Read(2, 500));
        handoff.BeginAwaitingNativeLoad(3, priorClock);
        Assert.True(handoff.CompleteProfileChange(3, "slot-b", boundary, null, out _));

        Assert.Null(handoff.ResetCurrentProfile(
            "slot-b-reset",
            Read(2, 500),
            boundary,
            out var awaitingNativeLoad));
        Assert.True(awaitingNativeLoad);
        Assert.Null(handoff.Observe("slot-b-reset", priorClock, Read(2, 560), boundary));
        Assert.Equal(
            WorldTimeObservationState.BaselineEstablished,
            handoff.Observe("slot-b-reset", selectedClock, Read(20, 1_000), boundary)!.Value.State);

        Assert.True(boundary.TakePending().IsEmpty);
        Assert.Equal(
            5 * Second,
            handoff.Observe("slot-b-reset", selectedClock, Read(20, 1_005), boundary)!.Value.Mutation.ObservedGameTimeTicks);
    }

    [Fact]
    [Trait("Category", "M12")]
    public void DeferredNewGameTransitionBuffersBootAdvanceUntilTheNewGenerationCommits()
    {
        var boundary = new NativeWorldTimeObservationBoundary();
        var handoff = new NativeWorldTimeProfileHandoffBoundary();
        var profileTransition = new NativeProfileTransitionBoundary();
        var sequence = new List<string>();
        var failBeforeBaseline = true;
        var clock = new object();

        sequence.Add("Load");
        Assert.Equal(
            WorldTimeObservationState.BaselineEstablished,
            boundary.ObserveClock("pre-rotation", Read(2, 86_000)).State);
        handoff.BeginNewGame(2, Read(2, 86_000));
        profileTransition.Enqueue(
            "OnNewGameReport",
            () =>
            {
                sequence.Add("OnNewGameReport");
                if (failBeforeBaseline)
                {
                    failBeforeBaseline = false;
                    throw new IOException("deferred profile write");
                }
            },
            () => Assert.True(handoff.CompleteProfileChange(
                2,
                "new-game",
                boundary,
                Read(3, 100),
                out _)));
        Assert.False(profileTransition.Retry(boundaryObserver: null, _ => { }));

        sequence.Add("OnNewBoot");
        var boot = handoff.Observe("pre-rotation", clock, Read(3, 100), boundary);
        Assert.True(profileTransition.Retry(boundaryObserver: null, _ => { }));
        var mutation = boundary.TakePending();

        Assert.Equal(["Load", "OnNewGameReport", "OnNewBoot", "OnNewGameReport"], sequence);
        Assert.True(boot!.Value.Accepted);
        Assert.Equal(1, mutation.CalendarDaysAdvanced);
        Assert.Equal(400 * Second, mutation.ObservedGameTimeTicks);
        Assert.Equal(0, mutation.CompletedSleepSessions);
        Assert.Equal(0, mutation.SleepAdvancedTimeTicks);
    }

    [Fact]
    [Trait("Category", "M12")]
    public void SupersededProfileCompletionCannotConsumeANewerHandoff()
    {
        var boundary = new NativeWorldTimeObservationBoundary();
        var handoff = new NativeWorldTimeProfileHandoffBoundary();
        var clock = new object();

        handoff.BeginAwaitingNativeLoad(10, clock);
        handoff.BeginNewGame(11, Read(4, 86_000));

        Assert.False(handoff.CompleteProfileChange(10, "deleted-save", boundary, null, out _));
        Assert.True(handoff.Observe("deleted-save", clock, Read(5, 100), boundary)!.Value.Accepted);
        Assert.True(handoff.CompleteProfileChange(11, "new-game", boundary, Read(5, 100), out _));

        var mutation = boundary.TakePending();
        Assert.Equal(1, mutation.CalendarDaysAdvanced);
        Assert.Equal(400 * Second, mutation.ObservedGameTimeTicks);
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
    [Trait("Category", "Performance")]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M12")]
    public void OneHourOfFrameRateObservationBoundsLongProfilePersistenceToThirtySecondCadence()
    {
        const int framesPerSecond = 60;
        const int simulatedSeconds = 60 * 60;
        const long representativeLongProfileBytes = 512 * 1024;
        var cadence = new NativeWorldTimePersistenceCadence();
        var representativeProfile = new string('p', (int)representativeLongProfileBytes);
        long persistedBytes = 0;
        var durableWriter = new DeferredSnapshotWriter<string>(
            () => representativeProfile,
            snapshot => persistedBytes += snapshot.Length);
        cadence.Start(0);
        var publications = 0;
        var durableWrites = 0;

        for (var frame = 1; frame <= simulatedSeconds * framesPerSecond; frame++)
        {
            var monotonicSeconds = frame / (double)framesPerSecond;
            if (cadence.ShouldPublish(monotonicSeconds))
            {
                publications++;
                cadence.RecordPublicationAttempt(succeeded: true, changed: true, monotonicSeconds);
            }

            if (cadence.ShouldSchedulePersistence(monotonicSeconds))
            {
                durableWrites++;
                durableWriter.MarkDirty();
                Assert.Equal(DeferredWriteState.Succeeded, durableWriter.Flush().State);
                cadence.RecordPersistenceAttempt(succeeded: true, monotonicSeconds);
            }
        }

        Assert.InRange(publications, simulatedSeconds - 1, simulatedSeconds);
        Assert.InRange(durableWrites, 119, 120);
        Assert.Equal(durableWrites * representativeLongProfileBytes, persistedBytes);
        Assert.True(persistedBytes <= 60L * 1024 * 1024);
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M12")]
    public void WindowsClockRewindCannotDelayMonotonicPublicationOrPersistence()
    {
        var cadence = new NativeWorldTimePersistenceCadence();
        var boundary = new NativeWorldTimeObservationBoundary();
        cadence.Start(100);
        var priorWallClockDeadline = new DateTime(2026, 8, 20, 12, 0, 1, DateTimeKind.Utc);
        var correctedWallClock = priorWallClockDeadline.AddHours(-2);
        boundary.ObserveClock("g", Read(1, 100));
        boundary.ObserveClock("g", Read(1, 160));

        Assert.True(correctedWallClock < priorWallClockDeadline);
        Assert.True(cadence.ShouldPublish(101));
        var published = new List<WorldTimeMutation>();
        Assert.True(boundary.FlushPending(mutation =>
        {
            published.Add(mutation);
            return true;
        }));
        cadence.RecordPublicationAttempt(succeeded: true, changed: published.Count != 0, monotonicSeconds: 101);
        Assert.Equal(60 * Second, Assert.Single(published).ObservedGameTimeTicks);
        Assert.False(cadence.ShouldSchedulePersistence(129.999));
        Assert.True(cadence.ShouldSchedulePersistence(130));
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M12")]
    public void CompletedSleepAndLifecycleBoundariesCanRequestDurabilityImmediately()
    {
        var cadence = new NativeWorldTimePersistenceCadence();
        cadence.Start(0);
        cadence.RecordPublicationAttempt(succeeded: true, changed: true, monotonicSeconds: 1);

        Assert.False(cadence.ShouldSchedulePersistence(1));
        Assert.True(cadence.ShouldSchedulePersistence(1, force: true));
        cadence.RecordPersistenceAttempt(succeeded: true, monotonicSeconds: 1);
        Assert.False(cadence.HasUnpersistedChanges);
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M12")]
    public void FailedDurableRequestRetriesAtOneSecondInsteadOfEveryFrame()
    {
        var cadence = new NativeWorldTimePersistenceCadence();
        cadence.Start(0);
        cadence.RecordPublicationAttempt(succeeded: true, changed: true, monotonicSeconds: 1);

        Assert.True(cadence.ShouldSchedulePersistence(30));
        cadence.RecordPersistenceAttempt(succeeded: false, monotonicSeconds: 30);
        Assert.False(cadence.ShouldSchedulePersistence(30.999));
        Assert.True(cadence.ShouldSchedulePersistence(31));

        cadence.Start(100);
        cadence.RecordPublicationAttempt(succeeded: true, changed: true, monotonicSeconds: 101);
        Assert.True(cadence.ShouldSchedulePersistence(101, force: true));
        cadence.RecordPersistenceAttempt(succeeded: false, monotonicSeconds: 101);
        Assert.False(cadence.ShouldSchedulePersistence(101.999));
        Assert.True(cadence.ShouldSchedulePersistence(101.001, force: true));
        Assert.True(cadence.ShouldSchedulePersistence(102));
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
