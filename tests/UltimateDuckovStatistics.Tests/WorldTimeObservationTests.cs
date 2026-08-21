using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Export;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;
using UltimateDuckovStatistics.UI;

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
    public void SameGenerationReplacementClockLoadRebaselinesWithoutDisablingCapture()
    {
        var boundary = new NativeWorldTimeObservationBoundary();
        var handoff = new NativeWorldTimeProfileHandoffBoundary();
        var oldSceneClock = new object();
        var replacementClock = new object();

        Assert.Equal(
            WorldTimeObservationState.BaselineEstablished,
            handoff.Observe("same-generation", oldSceneClock, Read(4, 100), boundary)!.Value.State);
        Assert.True(handoff.Observe(
            "same-generation",
            oldSceneClock,
            Read(4, 160),
            boundary)!.Value.Accepted);

        // The outgoing scene unpauses after its final save and advances before Unity destroys its clock.
        Assert.True(handoff.Observe(
            "same-generation",
            oldSceneClock,
            Read(4, 180),
            boundary)!.Value.Accepted);

        // Continue creates a new GameClock without SetFile; Load reports the earlier saved coordinate.
        var replacementLoad = handoff.Observe(
            "same-generation",
            replacementClock,
            Read(4, 160),
            boundary)!.Value;
        Assert.Equal(WorldTimeObservationState.BaselineEstablished, replacementLoad.State);
        Assert.True(replacementLoad.Mutation.IsEmpty);
        Assert.True(handoff.Observe(
            "same-generation",
            replacementClock,
            Read(4, 165),
            boundary)!.Value.Accepted);

        var mutation = boundary.TakePending();
        Assert.Equal(0, mutation.CalendarDaysAdvanced);
        Assert.Equal(85 * Second, mutation.ObservedGameTimeTicks);
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
        Assert.True(handoff.CompleteProfileChange(1, "selected-slot", boundary, null, out _, out _));

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

        Assert.True(handoff.CompleteProfileChange(2, "selected-slot", boundary, null, out _, out _));
        var mutation = boundary.TakePending();

        Assert.Equal(0, mutation.CalendarDaysAdvanced);
        Assert.Equal(5 * Second, mutation.ObservedGameTimeTicks);
    }

    [Fact]
    [Trait("Category", "M12")]
    public void SameSlotReopenKeepsTheSameClockCapturingOrdinaryTimeAndSleep()
    {
        using var directory = new TemporaryDirectory();
        var ids = new Queue<string>(["generation-a", "session-a", "session-b"]);
        var repository = new ProfileRepository(
            directory.Path,
            () => new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc),
            ids.Dequeue);
        var identity = new SaveIdentitySnapshot { Slot = 1, SaveFilePresent = false };
        repository.Open(identity);
        repository.SetWorldTimeCapabilities(WorldTimeNativeContractPolicy.Supported("clock", "sleep"));
        var generationId = repository.CurrentGenerationId;
        var boundary = new NativeWorldTimeObservationBoundary();
        var handoff = new NativeWorldTimeProfileHandoffBoundary();
        var clock = new object();

        boundary.ObserveClock(generationId, Read(4, 100));
        handoff.BeginAwaitingNativeLoad(6, clock, Read(4, 100));
        Assert.Null(handoff.Observe(generationId, clock, Read(4, 110), boundary));
        var open = repository.Open(identity, "SaveSlotSelected");
        Assert.False(open.CreatedNew);
        Assert.False(open.RotatedGeneration);
        Assert.Equal(generationId, repository.CurrentGenerationId);
        Assert.True(NativeWorldTimeProfileReopenPolicy.CanReuseCurrentClock(
            open,
            identity.Slot,
            identity.Slot,
            repository.CurrentGenerationId,
            generationId));
        Assert.False(NativeWorldTimeProfileReopenPolicy.CanReuseCurrentClock(
            open,
            observedSlot: 2,
            priorSlot: identity.Slot,
            openedGenerationId: repository.CurrentGenerationId,
            priorGenerationId: generationId));
        Assert.False(NativeWorldTimeProfileReopenPolicy.CanReuseCurrentClock(
            open,
            identity.Slot,
            identity.Slot,
            openedGenerationId: "rotated-generation",
            priorGenerationId: generationId));
        Assert.True(handoff.CompleteProfileChangeWithCurrentClock(
            6,
            generationId,
            boundary,
            clock,
            Read(4, 110),
            out _,
            out var transitionSleepTransferred));
        Assert.False(transitionSleepTransferred);
        Assert.True(repository.RecordWorldTimeDeferred(boundary.TakePending()));

        Assert.True(handoff.Observe(generationId, clock, Read(4, 160), boundary)!.Value.Accepted);
        Assert.True(handoff.Observe(generationId, clock, Read(4, 3_760), boundary)!.Value.Accepted);
        Assert.True(boundary.BeginSleepCompletion(generationId, TimeSpan.FromHours(1).Ticks));
        Assert.True(boundary.CompleteSleep(generationId));
        Assert.True(repository.RecordWorldTimeDeferred(boundary.TakePending()));

        var worldTime = repository.Current.Statistics.WorldTime;
        Assert.Equal(TimeSpan.FromMinutes(61).Ticks, worldTime.ObservedGameTimeTicks);
        Assert.Equal(1, worldTime.CompletedSleepSessions);
        Assert.Equal(TimeSpan.FromHours(1).Ticks, worldTime.SleepAdvancedTimeTicks);
        var export = StatisticsExporter.Create(
            repository.Current,
            new DateTime(2026, 8, 21, 12, 1, 0, DateTimeKind.Utc));
        Assert.Equal(TimeSpan.FromMinutes(61).Ticks, export.Document.WorldTime.ObservedGameTimeTicks);
        Assert.Contains(
            "0,36600000000,3660,1,36000000000,3600,",
            export.WorldTimeCsv,
            StringComparison.Ordinal);
        Assert.Equal("01:01:00", UiText.FormatWorldTimeDuration(
            export.Document.WorldTime.ObservedGameTimeTicks,
            export.Document.WorldTime.Capabilities.ObservedElapsed));
        Assert.Equal("1", UiText.FormatWorldTimeCount(
            export.Document.WorldTime.CompletedSleepSessions,
            export.Document.WorldTime.Capabilities.CompletedSleepSessions));
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "M12")]
    public void PartialOpenFailurePreservesTheFrozenChangedProfileDecisionUntilRetry()
    {
        using var directory = new TemporaryDirectory();
        var ids = new Queue<string>(["generation-a", "session-a", "generation-b", "session-b-failed", "session-b-retry"]);
        var sessionPath = Path.Combine(
            directory.Path,
            "profiles",
            "slot-02",
            "current",
            "session.json");
        var blockedSessionTemporaryPath = AtomicJsonPaths.GetTemporaryPath(sessionPath);
        var repository = new ProfileRepository(
            directory.Path,
            () => new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc),
            () =>
            {
                var id = ids.Dequeue();
                if (id == "session-b-failed") Directory.CreateDirectory(blockedSessionTemporaryPath);
                return id;
            });
        var slotA = new SaveIdentitySnapshot { Slot = 1, SaveFilePresent = false };
        var slotB = new SaveIdentitySnapshot { Slot = 2, SaveFilePresent = false };
        repository.Open(slotA);
        var boundary = new NativeWorldTimeObservationBoundary();
        var handoff = new NativeWorldTimeProfileHandoffBoundary();
        var profileTransition = new NativeProfileTransitionBoundary();
        var slotAClock = new object();
        var slotBClock = new object();
        var identityReads = new List<int>();
        NativeWorldTimeProfilePreOpenState? preOpenState = null;
        ProfileOpenResult? openResult = null;
        bool? reuseCurrentClock = null;

        boundary.ObserveClock(repository.CurrentGenerationId, Read(2, 500));
        handoff.BeginAwaitingNativeLoad(12, slotAClock, Read(2, 500));
        profileTransition.Enqueue(
            "A to B with partial open failure",
            () => preOpenState = NativeWorldTimeProfileReopenPolicy.CapturePreOpenState(
                repository,
                slotB,
                slot =>
                {
                    identityReads.Add(slot);
                    return slotA;
                }),
            () =>
            {
                var result = NativeWorldTimeProfileReopenPolicy.OpenAndDetermineCurrentClockReuse(
                    repository,
                    slotB,
                    preOpenState!,
                    "SaveSlotSelected",
                    out var reuse);
                openResult = result;
                reuseCurrentClock = reuse;
            },
            () =>
            {
                var completed = reuseCurrentClock == true
                    ? handoff.CompleteProfileChangeWithCurrentClock(
                        12,
                        repository.CurrentGenerationId,
                        boundary,
                        slotAClock,
                        Read(2, 500),
                        out _,
                        out _)
                    : handoff.CompleteProfileChange(
                        12,
                        repository.CurrentGenerationId,
                        boundary,
                        Read(2, 500),
                        out _,
                        out _);
                Assert.True(completed);
            });

        Assert.False(profileTransition.Retry(boundaryObserver: null, _ => { }));
        Assert.Equal(1, preOpenState!.Slot);
        Assert.Equal("generation-a", preOpenState.GenerationId);
        Assert.Equal(2, repository.Current.Slot);
        Assert.Equal("generation-b", repository.CurrentGenerationId);
        Assert.Null(openResult);
        Assert.Null(reuseCurrentClock);
        Assert.True(handoff.IsActive);
        Assert.True(Directory.Exists(blockedSessionTemporaryPath));

        Directory.Delete(blockedSessionTemporaryPath);
        Assert.True(profileTransition.Retry(boundaryObserver: null, _ => { }));
        Assert.NotNull(openResult);
        Assert.False(openResult.CreatedNew);
        Assert.False(openResult.RotatedGeneration);
        Assert.False(reuseCurrentClock);
        Assert.Equal([1], identityReads);
        Assert.True(handoff.IsActive);
        Assert.Null(handoff.Observe(repository.CurrentGenerationId, slotAClock, Read(2, 505), boundary));

        Assert.Equal(
            WorldTimeObservationState.BaselineEstablished,
            handoff.Observe(repository.CurrentGenerationId, slotBClock, Read(20, 1_000), boundary)!.Value.State);
        Assert.False(handoff.IsActive);
        Assert.True(handoff.Observe(
            repository.CurrentGenerationId,
            slotBClock,
            Read(20, 1_005),
            boundary)!.Value.Accepted);
        var mutation = boundary.TakePending();
        Assert.Equal(0, mutation.CalendarDaysAdvanced);
        Assert.Equal(5 * Second, mutation.ObservedGameTimeTicks);
        repository.SetWorldTimeCapabilities(WorldTimeNativeContractPolicy.Supported("clock", "sleep"));
        Assert.True(repository.RecordWorldTimeDeferred(mutation));
        Assert.Equal(5 * Second, repository.Current.Statistics.WorldTime.ObservedGameTimeTicks);
        Assert.Equal(
            AdapterCapabilityState.Supported,
            repository.Current.Statistics.WorldTime.Capabilities.ObservedElapsed.State);
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "M12")]
    public void NestedBackupRecoverySetFileCannotReuseThePriorSlotClock()
    {
        using var directory = new TemporaryDirectory();
        var ids = new Queue<string>(
            ["generation-a", "session-a", "generation-b", "session-b", "session-b-reopen"]);
        var repository = new ProfileRepository(
            directory.Path,
            () => new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc),
            ids.Dequeue);
        var slotA = new SaveIdentitySnapshot { Slot = 1, SaveFilePresent = false };
        var slotB = new SaveIdentitySnapshot { Slot = 2, SaveFilePresent = false };
        repository.Open(slotA);
        repository.SetWorldTimeCapabilities(WorldTimeNativeContractPolicy.Supported("clock", "sleep"));
        var boundary = new NativeWorldTimeObservationBoundary();
        var handoff = new NativeWorldTimeProfileHandoffBoundary();
        var profileTransition = new NativeProfileTransitionBoundary();
        var slotAClock = new object();
        var slotBClock = new object();
        var reuseDecisions = new List<bool>();
        NativeWorldTimeProfilePreOpenState? innerPreOpenState = null;
        NativeWorldTimeProfilePreOpenState? outerPreOpenState = null;

        boundary.ObserveClock(repository.CurrentGenerationId, Read(2, 500));

        // RestoreIndexedBackup raises the inner callback while Duckov's outer SetFile(B) is still active.
        handoff.BeginAwaitingNativeLoad(20, slotAClock, Read(2, 500));
        profileTransition.Enqueue(
            "backup recovery inner OnSetFile",
            () => innerPreOpenState = NativeWorldTimeProfileReopenPolicy.CapturePreOpenState(
                repository,
                slotB,
                _ => slotA),
            () =>
            {
                NativeWorldTimeProfileReopenPolicy.OpenAndDetermineCurrentClockReuse(
                    repository,
                    slotB,
                    innerPreOpenState!,
                    "SaveSlotSelected",
                    out var reuseCurrentClock);
                reuseDecisions.Add(reuseCurrentClock);
            },
            () => Assert.True(handoff.CompleteProfileChange(
                20,
                repository.CurrentGenerationId,
                boundary,
                currentReading: null,
                out _,
                out _)));

        // The outer callback arrives before B has replaced A's GameClock.Instance.
        handoff.BeginAwaitingNativeLoad(21, slotAClock, Read(2, 500));
        profileTransition.Enqueue(
            "outer OnSetFile",
            () => outerPreOpenState = NativeWorldTimeProfileReopenPolicy.CapturePreOpenState(
                repository,
                slotB,
                _ => slotA),
            () =>
            {
                NativeWorldTimeProfileReopenPolicy.OpenAndDetermineCurrentClockReuse(
                    repository,
                    slotB,
                    outerPreOpenState!,
                    "SaveSlotSelected",
                    out var reuseCurrentClock);
                reuseDecisions.Add(reuseCurrentClock);
            },
            () => Assert.True(handoff.CompleteProfileChangeWithCurrentClock(
                21,
                repository.CurrentGenerationId,
                boundary,
                slotAClock,
                Read(2, 500),
                out _,
                out _)));

        Assert.Null(handoff.Observe(repository.CurrentGenerationId, slotAClock, Read(2, 505), boundary));
        Assert.False(profileTransition.Retry(boundaryObserver: null, _ => { }));
        Assert.True(profileTransition.Retry(boundaryObserver: null, _ => { }));
        Assert.Equal([false, true], reuseDecisions);
        Assert.Equal(2, repository.Current.Slot);
        Assert.True(handoff.IsActive);
        Assert.Null(handoff.Observe(repository.CurrentGenerationId, slotAClock, Read(2, 510), boundary));

        Assert.Equal(
            WorldTimeObservationState.BaselineEstablished,
            handoff.Observe(repository.CurrentGenerationId, slotBClock, Read(20, 1_000), boundary)!.Value.State);
        Assert.False(handoff.IsActive);
        Assert.True(handoff.Observe(
            repository.CurrentGenerationId,
            slotBClock,
            Read(20, 1_005),
            boundary)!.Value.Accepted);
        var mutation = boundary.TakePending();
        Assert.Equal(0, mutation.CalendarDaysAdvanced);
        Assert.Equal(5 * Second, mutation.ObservedGameTimeTicks);
        repository.SetWorldTimeCapabilities(WorldTimeNativeContractPolicy.Supported("clock", "sleep"));
        Assert.True(repository.RecordWorldTimeDeferred(mutation));
        Assert.Equal(5 * Second, repository.Current.Statistics.WorldTime.ObservedGameTimeTicks);
        Assert.Equal(
            AdapterCapabilityState.Supported,
            repository.Current.Statistics.WorldTime.Capabilities.ObservedElapsed.State);
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "M12")]
    public void NestedSameProfileCallbacksCanStillReuseTheHydratedClock()
    {
        var boundary = new NativeWorldTimeObservationBoundary();
        var handoff = new NativeWorldTimeProfileHandoffBoundary();
        var clock = new object();

        boundary.ObserveClock("slot-a", Read(2, 500));
        handoff.BeginAwaitingNativeLoad(30, clock, Read(2, 500));
        Assert.Null(handoff.Observe("slot-a", clock, Read(2, 510), boundary));
        handoff.BeginAwaitingNativeLoad(31, clock, Read(2, 510));
        Assert.Null(handoff.Observe("slot-a", clock, Read(2, 520), boundary));

        Assert.True(handoff.CompleteProfileChangeWithCurrentClock(
            30,
            "slot-a",
            boundary,
            clock,
            Read(2, 520),
            out _,
            out _));
        Assert.True(handoff.CompleteProfileChangeWithCurrentClock(
            31,
            "slot-a",
            boundary,
            clock,
            Read(2, 520),
            out _,
            out _));

        Assert.False(handoff.IsActive);
        Assert.True(handoff.Observe("slot-a", clock, Read(2, 530), boundary)!.Value.Accepted);
        Assert.Equal(30 * Second, boundary.TakePending().ObservedGameTimeTicks);
    }

    [Fact]
    [Trait("Category", "M12")]
    public void ShutdownDrainPersistsStagedClockAndSleepBeforeCleanup()
    {
        using var directory = new TemporaryDirectory();
        var ids = new Queue<string>(
            ["generation-a", "session-a", "generation-b", "session-b", "session-b-reopen"]);
        var repository = new ProfileRepository(
            directory.Path,
            () => new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc),
            ids.Dequeue);
        var slotA = new SaveIdentitySnapshot { Slot = 1, SaveFilePresent = false };
        var slotB = new SaveIdentitySnapshot { Slot = 2, SaveFilePresent = false };
        repository.Open(slotA);
        var boundary = new NativeWorldTimeObservationBoundary();
        var handoff = new NativeWorldTimeProfileHandoffBoundary();
        var profileTransition = new NativeProfileTransitionBoundary();
        var slotAClock = new object();
        var slotBClock = new object();
        var transitionFaulted = true;

        handoff.Observe(repository.CurrentGenerationId, slotAClock, Read(2, 500), boundary);
        handoff.BeginAwaitingNativeLoad(40, slotAClock, Read(2, 500));
        Assert.Equal(
            WorldTimeObservationState.BaselineEstablished,
            handoff.Observe(repository.CurrentGenerationId, slotBClock, Read(20, 1_000), boundary)!.Value.State);
        Assert.True(handoff.Observe(
            repository.CurrentGenerationId,
            slotBClock,
            Read(20, 4_600),
            boundary)!.Value.Accepted);
        Assert.True(handoff.BeginSleepCompletion(40, TimeSpan.FromHours(1).Ticks));
        Assert.True(handoff.CompleteSleep());

        profileTransition.Enqueue(
            "shutdown slot transition",
            () =>
            {
                if (transitionFaulted) throw new IOException("temporary profile writer failure");
            },
            () => repository.Open(slotB, "SaveSlotSelected"),
            () =>
            {
                Assert.True(handoff.CompleteProfileChange(
                    40,
                    repository.CurrentGenerationId,
                    boundary,
                    currentReading: null,
                    out _,
                    out var completedSleepTransferred));
                Assert.True(completedSleepTransferred);
                repository.SetWorldTimeCapabilities(WorldTimeNativeContractPolicy.Supported("clock", "sleep"));
            });

        Assert.False(profileTransition.Retry(boundaryObserver: null, _ => { }));
        Assert.True(handoff.HasUncommittedData);
        transitionFaulted = false;

        // Cleanup performs the drain directly; no Update retry occurs after the fault clears.
        Assert.True(profileTransition.Drain(
            () => boundary.FlushPending(repository.RecordWorldTimeDeferred),
            _ => { }));
        Assert.False(handoff.HasUncommittedData);
        Assert.True(boundary.FlushPending(repository.RecordWorldTimeDeferred));
        repository.Flush();

        // Idempotent cleanup cannot publish the transferred mutation twice.
        Assert.True(profileTransition.Drain(
            () => boundary.FlushPending(repository.RecordWorldTimeDeferred),
            _ => { }));
        Assert.True(boundary.FlushPending(repository.RecordWorldTimeDeferred));
        repository.Flush();
        repository.CloseClean();

        var reopened = new ProfileRepository(
            directory.Path,
            () => new DateTime(2026, 8, 21, 12, 1, 0, DateTimeKind.Utc),
            ids.Dequeue);
        reopened.Open(slotB);
        var worldTime = reopened.Current.Statistics.WorldTime;
        Assert.Equal(TimeSpan.FromHours(1).Ticks, worldTime.ObservedGameTimeTicks);
        Assert.Equal(1, worldTime.CompletedSleepSessions);
        Assert.Equal(TimeSpan.FromHours(1).Ticks, worldTime.SleepAdvancedTimeTicks);
        reopened.CloseClean();
    }

    [Fact]
    [Trait("Category", "M12")]
    public void ShutdownAfterChangedProfileCommitDiscardsPriorClockBufferAndClosesSessionCleanly()
    {
        using var directory = new TemporaryDirectory();
        var ids = new Queue<string>(
            ["generation-a", "session-a", "generation-b", "session-b", "session-b-reopen"]);
        var repository = new ProfileRepository(
            directory.Path,
            () => new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc),
            ids.Dequeue);
        var slotA = new SaveIdentitySnapshot { Slot = 1, SaveFilePresent = false };
        var slotB = new SaveIdentitySnapshot { Slot = 2, SaveFilePresent = false };
        repository.Open(slotA);
        var boundary = new NativeWorldTimeObservationBoundary();
        var handoff = new NativeWorldTimeProfileHandoffBoundary();
        var profileTransition = new NativeProfileTransitionBoundary();
        var slotAClock = new object();
        var transitionFaulted = true;
        var slotBSessionPath = Path.Combine(
            directory.Path,
            "profiles",
            "slot-02",
            "current",
            "session.json");

        handoff.Observe(repository.CurrentGenerationId, slotAClock, Read(2, 500), boundary);
        handoff.BeginAwaitingNativeLoad(41, slotAClock, Read(2, 500));
        Assert.Null(handoff.Observe(
            repository.CurrentGenerationId,
            slotAClock,
            Read(2, 560),
            boundary));

        profileTransition.Enqueue(
            "shutdown slot transition without replacement clock",
            () =>
            {
                if (transitionFaulted) throw new IOException("temporary profile writer failure");
            },
            () => repository.Open(slotB, "SaveSlotSelected"),
            () => Assert.True(handoff.CompleteProfileChange(
                41,
                repository.CurrentGenerationId,
                boundary,
                currentReading: null,
                out _,
                out _)));

        Assert.False(profileTransition.Retry(boundaryObserver: null, _ => { }));
        Assert.True(handoff.HasUncommittedData);
        transitionFaulted = false;

        // Cleanup drains the changed-profile commit before any B clock instance has loaded.
        Assert.True(profileTransition.Drain(
            () => boundary.FlushPending(repository.RecordWorldTimeDeferred),
            _ => { }));
        Assert.True(handoff.IsActive);
        Assert.False(handoff.HasUncommittedData);
        Assert.Null(handoff.Observe(
            repository.CurrentGenerationId,
            slotAClock,
            Read(2, 620),
            boundary));
        Assert.False(handoff.HasUncommittedData);
        Assert.True(File.Exists(slotBSessionPath));
        Assert.True(boundary.FlushPending(repository.RecordWorldTimeDeferred));
        repository.Flush();

        // Successful adapter cleanup discards only A's now-ineligible buffer, then the
        // coordinator closes B's UDS session checkpoint normally.
        handoff.Reset();
        repository.CloseClean();
        Assert.False(File.Exists(slotBSessionPath));
        Assert.False(File.Exists(AtomicJsonPaths.GetBackupPath(slotBSessionPath)));
        Assert.False(File.Exists(AtomicJsonPaths.GetTemporaryPath(slotBSessionPath)));

        var reopened = new ProfileRepository(
            directory.Path,
            () => new DateTime(2026, 8, 21, 12, 1, 0, DateTimeKind.Utc),
            ids.Dequeue);
        var open = reopened.Open(slotB);
        Assert.False(open.InterruptedSessionRecovered);
        Assert.Equal(0, reopened.Current.InterruptedSessionCount);
        Assert.Equal(0, reopened.Current.Statistics.WorldTime.ObservedGameTimeTicks);
        reopened.CloseClean();
    }

    [Fact]
    [Trait("Category", "M12")]
    public void DeferredAToBThenQueuedBSelectionReusesTheLoadedBClock()
    {
        using var directory = new TemporaryDirectory();
        var ids = new Queue<string>(["generation-a", "session-a", "generation-b", "session-b", "session-b-reopen"]);
        var repository = new ProfileRepository(
            directory.Path,
            () => new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc),
            ids.Dequeue);
        var slotA = new SaveIdentitySnapshot { Slot = 1, SaveFilePresent = false };
        var slotB = new SaveIdentitySnapshot { Slot = 2, SaveFilePresent = false };
        repository.Open(slotA);
        var supportedCapabilities = WorldTimeNativeContractPolicy.Supported("clock", "sleep");
        repository.SetWorldTimeCapabilities(supportedCapabilities);
        var boundary = new NativeWorldTimeObservationBoundary();
        var handoff = new NativeWorldTimeProfileHandoffBoundary();
        var profileTransition = new NativeProfileTransitionBoundary();
        var slotAClock = new object();
        var slotBClock = new object();
        var failBeforeFirstOpen = true;
        var identityReads = new List<int>();
        var reuseDecisions = new List<bool>();
        NativeWorldTimeProfilePreOpenState? slotBPreOpenState = null;
        NativeWorldTimeProfilePreOpenState? slotBAgainPreOpenState = null;

        SaveIdentitySnapshot ReadIdentityForSlot(int slot)
        {
            identityReads.Add(slot);
            return slot switch
            {
                1 => slotA,
                2 => slotB,
                _ => throw new InvalidOperationException($"Unexpected slot {slot}.")
            };
        }

        boundary.ObserveClock(repository.CurrentGenerationId, Read(2, 500));
        handoff.BeginAwaitingNativeLoad(10, slotAClock, Read(2, 500));
        profileTransition.Enqueue(
            "A to B",
            () =>
            {
                if (failBeforeFirstOpen)
                {
                    failBeforeFirstOpen = false;
                    throw new IOException("locked profile snapshot");
                }
            },
            () => slotBPreOpenState = NativeWorldTimeProfileReopenPolicy.CapturePreOpenState(
                repository,
                slotB,
                ReadIdentityForSlot),
            () =>
            {
                NativeWorldTimeProfileReopenPolicy.OpenAndDetermineCurrentClockReuse(
                    repository,
                    slotB,
                    slotBPreOpenState!,
                    "SaveSlotSelected",
                    out var reuseCurrentClock);
                reuseDecisions.Add(reuseCurrentClock);
            },
            () =>
            {
                Assert.True(handoff.CompleteProfileChange(
                    10,
                    repository.CurrentGenerationId,
                    boundary,
                    null,
                    out _,
                    out _));
                var mutation = boundary.TakePending();
                if (!mutation.IsEmpty) Assert.True(repository.RecordWorldTimeDeferred(mutation));
            },
            () => repository.SetWorldTimeCapabilities(supportedCapabilities));
        Assert.False(profileTransition.Retry(boundaryObserver: null, _ => { }));

        Assert.Equal(
            WorldTimeObservationState.BaselineEstablished,
            handoff.Observe(repository.CurrentGenerationId, slotBClock, Read(20, 1_000), boundary)!.Value.State);
        Assert.Equal(
            WorldTimeObservationState.Duplicate,
            handoff.Observe(repository.CurrentGenerationId, slotBClock, Read(20, 1_000), boundary)!.Value.State);
        handoff.BeginAwaitingNativeLoad(11, slotBClock, Read(20, 1_000));
        profileTransition.Enqueue(
            "B again",
            () => slotBAgainPreOpenState = NativeWorldTimeProfileReopenPolicy.CapturePreOpenState(
                repository,
                slotB,
                ReadIdentityForSlot),
            () =>
            {
                NativeWorldTimeProfileReopenPolicy.OpenAndDetermineCurrentClockReuse(
                    repository,
                    slotB,
                    slotBAgainPreOpenState!,
                    "SaveSlotSelected",
                    out var reuseCurrentClock);
                reuseDecisions.Add(reuseCurrentClock);
            },
            () =>
            {
                Assert.True(handoff.CompleteProfileChangeWithCurrentClock(
                    11,
                    repository.CurrentGenerationId,
                    boundary,
                    slotBClock,
                    Read(20, 1_010),
                    out _,
                    out _));
                Assert.True(repository.RecordWorldTimeDeferred(boundary.TakePending()));
            });
        Assert.Null(handoff.Observe(repository.CurrentGenerationId, slotBClock, Read(20, 1_010), boundary));

        Assert.False(profileTransition.Retry(boundaryObserver: null, _ => { }));
        Assert.True(profileTransition.Retry(boundaryObserver: null, _ => { }));
        Assert.Equal([false, true], reuseDecisions);
        Assert.Equal([1], identityReads);
        Assert.Equal(2, repository.Current.Slot);
        Assert.Equal("generation-b", repository.CurrentGenerationId);
        Assert.False(handoff.IsActive);

        Assert.True(handoff.Observe(
            repository.CurrentGenerationId,
            slotBClock,
            Read(20, 1_060),
            boundary)!.Value.Accepted);
        Assert.True(handoff.Observe(
            repository.CurrentGenerationId,
            slotBClock,
            Read(20, 4_660),
            boundary)!.Value.Accepted);
        Assert.True(boundary.BeginSleepCompletion(repository.CurrentGenerationId, TimeSpan.FromHours(1).Ticks));
        Assert.True(boundary.CompleteSleep(repository.CurrentGenerationId));
        Assert.True(repository.RecordWorldTimeDeferred(boundary.TakePending()));

        var worldTime = repository.Current.Statistics.WorldTime;
        Assert.Equal(TimeSpan.FromMinutes(61).Ticks, worldTime.ObservedGameTimeTicks);
        Assert.Equal(1, worldTime.CompletedSleepSessions);
        Assert.Equal(TimeSpan.FromHours(1).Ticks, worldTime.SleepAdvancedTimeTicks);
        var export = StatisticsExporter.Create(
            repository.Current,
            new DateTime(2026, 8, 21, 12, 1, 0, DateTimeKind.Utc));
        Assert.Equal(TimeSpan.FromMinutes(61).Ticks, export.Document.WorldTime.ObservedGameTimeTicks);
        Assert.Contains("0,36600000000,3660,1,36000000000,3600,", export.WorldTimeCsv, StringComparison.Ordinal);
        Assert.Equal("01:01:00", UiText.FormatWorldTimeDuration(
            export.Document.WorldTime.ObservedGameTimeTicks,
            export.Document.WorldTime.Capabilities.ObservedElapsed));
        Assert.Equal("1", UiText.FormatWorldTimeCount(
            export.Document.WorldTime.CompletedSleepSessions,
            export.Document.WorldTime.Capabilities.CompletedSleepSessions));
        repository.CloseClean();
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
        Assert.True(handoff.CompleteProfileChange(3, "slot-b", boundary, null, out _, out _));

        Assert.Null(handoff.ResetCurrentProfile(
            "slot-b-reset",
            priorClock,
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
                out _,
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
    public void DeferredSelectedSlotTransitionRetainsCompletedSleepThroughEveryProjection()
    {
        const long transitionId = 4;
        const string selectedGeneration = "selected-slot";
        var boundary = new NativeWorldTimeObservationBoundary();
        var handoff = new NativeWorldTimeProfileHandoffBoundary();
        var profileTransition = new NativeProfileTransitionBoundary();
        var priorClock = new object();
        var selectedClock = new object();
        var failBeforeCommit = true;

        boundary.ObserveClock("prior-slot", Read(2, 500));
        handoff.BeginAwaitingNativeLoad(transitionId, priorClock);
        Assert.Equal(
            WorldTimeObservationState.BaselineEstablished,
            handoff.Observe("prior-slot", selectedClock, Read(20, 1_000), boundary)!.Value.State);
        profileTransition.Enqueue(
            "OnSetFile",
            () =>
            {
                if (failBeforeCommit)
                {
                    failBeforeCommit = false;
                    throw new IOException("deferred profile write");
                }
            },
            () => Assert.True(handoff.CompleteProfileChange(
                transitionId,
                selectedGeneration,
                boundary,
                null,
                out _,
                out var completedSleepTransferred) && completedSleepTransferred));
        Assert.False(profileTransition.Retry(boundaryObserver: null, _ => { }));

        Assert.True(handoff.Observe(
            "prior-slot",
            selectedClock,
            Read(20, 4_600),
            boundary)!.Value.Accepted);
        Assert.True(handoff.TryGetActiveTransitionId(out var activeTransitionId));
        Assert.Equal(transitionId, activeTransitionId);
        Assert.True(handoff.BeginSleepCompletion(activeTransitionId, TimeSpan.FromHours(1).Ticks));
        Assert.True(handoff.CompleteSleep());
        Assert.True(profileTransition.Retry(boundaryObserver: null, _ => { }));

        var mutation = boundary.TakePending();
        Assert.Equal(TimeSpan.FromHours(1).Ticks, mutation.ObservedGameTimeTicks);
        Assert.Equal(1, mutation.CompletedSleepSessions);
        Assert.Equal(TimeSpan.FromHours(1).Ticks, mutation.SleepAdvancedTimeTicks);

        var aggregate = new WorldTimeStatisticsAggregate();
        WorldTimeStatisticsReducer.InitializeOrRestrictCapabilities(
            aggregate,
            WorldTimeNativeContractPolicy.Supported("clock", "sleep"));
        Assert.True(WorldTimeStatisticsReducer.Apply(aggregate, mutation));
        var profile = new ProfileDocument
        {
            GenerationId = selectedGeneration,
            Statistics = new ProfileStatistics
            {
                SaveGenerationId = selectedGeneration,
                WorldTime = aggregate
            }
        };
        var export = StatisticsExporter.Create(
            profile,
            new DateTime(2026, 8, 21, 12, 0, 0, DateTimeKind.Utc));

        Assert.Equal(1, export.Document.WorldTime.CompletedSleepSessions);
        Assert.Equal(TimeSpan.FromHours(1).Ticks, export.Document.WorldTime.SleepAdvancedTimeTicks);
        Assert.Contains(",1,36000000000,3600,", export.WorldTimeCsv, StringComparison.Ordinal);
        Assert.Equal("1", UiText.FormatWorldTimeCount(
            export.Document.WorldTime.CompletedSleepSessions,
            export.Document.WorldTime.Capabilities.CompletedSleepSessions));
        Assert.Equal("01:00:00", UiText.FormatWorldTimeDuration(
            export.Document.WorldTime.SleepAdvancedTimeTicks,
            export.Document.WorldTime.Capabilities.SleepAdvancedTime));
    }

    [Fact]
    [Trait("Category", "M12")]
    public void PendingHandoffSleepProofTransfersIfProfileCommitsBeforeCompletionCallback()
    {
        const long transitionId = 5;
        var boundary = new NativeWorldTimeObservationBoundary();
        var handoff = new NativeWorldTimeProfileHandoffBoundary();
        var priorClock = new object();
        var selectedClock = new object();

        handoff.BeginAwaitingNativeLoad(transitionId, priorClock);
        handoff.Observe("prior-slot", selectedClock, Read(20, 1_000), boundary);
        handoff.Observe("prior-slot", selectedClock, Read(20, 4_600), boundary);
        Assert.True(handoff.BeginSleepCompletion(transitionId, TimeSpan.FromHours(1).Ticks));

        Assert.True(handoff.CompleteProfileChange(
            transitionId,
            "selected-slot",
            boundary,
            null,
            out _,
            out var completedSleepTransferred));
        Assert.False(completedSleepTransferred);
        Assert.False(handoff.CompleteSleep());
        Assert.True(boundary.CompleteSleep("selected-slot"));

        var mutation = boundary.TakePending();
        Assert.Equal(TimeSpan.FromHours(1).Ticks, mutation.ObservedGameTimeTicks);
        Assert.Equal(1, mutation.CompletedSleepSessions);
        Assert.Equal(TimeSpan.FromHours(1).Ticks, mutation.SleepAdvancedTimeTicks);
    }

    [Fact]
    [Trait("Category", "M12")]
    public void QueuedSlotTransitionsPreserveCompletedSleepForBothGenerations()
    {
        var boundary = new NativeWorldTimeObservationBoundary();
        var handoff = new NativeWorldTimeProfileHandoffBoundary();
        var profileTransition = new NativeProfileTransitionBoundary();
        var slotAClock = new object();
        var slotBClock = new object();
        var slotCClock = new object();
        var failSlotB = true;
        var committed = new List<(string Generation, WorldTimeMutation Mutation)>();

        boundary.ObserveClock("slot-a", Read(2, 500));
        handoff.BeginAwaitingNativeLoad(10, slotAClock);
        handoff.Observe("slot-a", slotBClock, Read(20, 1_000), boundary);
        handoff.Observe("slot-a", slotBClock, Read(20, 4_600), boundary);
        Assert.True(handoff.BeginSleepCompletion(10, TimeSpan.FromHours(1).Ticks));
        Assert.True(handoff.CompleteSleep());
        profileTransition.Enqueue(
            "slot B",
            () =>
            {
                if (failSlotB)
                {
                    failSlotB = false;
                    throw new IOException("slot B profile write");
                }
            },
            () =>
            {
                Assert.True(handoff.CompleteProfileChange(
                    10,
                    "slot-b",
                    boundary,
                    null,
                    out _,
                    out var sleepTransferred));
                Assert.True(sleepTransferred);
                committed.Add(("slot-b", boundary.TakePending()));
            });
        Assert.False(profileTransition.Retry(boundaryObserver: null, _ => { }));

        handoff.Observe("slot-a", slotBClock, Read(20, 4_600), boundary);
        handoff.BeginAwaitingNativeLoad(11, slotBClock);
        Assert.Null(handoff.Observe("slot-a", slotBClock, Read(20, 4_605), boundary));
        handoff.Observe("slot-a", slotCClock, Read(30, 2_000), boundary);
        handoff.Observe("slot-a", slotCClock, Read(30, 9_200), boundary);
        Assert.True(handoff.BeginSleepCompletion(11, TimeSpan.FromHours(2).Ticks));
        Assert.True(handoff.CompleteSleep());
        profileTransition.Enqueue(
            "slot C",
            () =>
            {
                Assert.True(handoff.CompleteProfileChange(
                    11,
                    "slot-c",
                    boundary,
                    null,
                    out _,
                    out var sleepTransferred));
                Assert.True(sleepTransferred);
                committed.Add(("slot-c", boundary.TakePending()));
            });

        Assert.False(profileTransition.Retry(boundaryObserver: null, _ => { }));
        Assert.True(profileTransition.Retry(boundaryObserver: null, _ => { }));

        Assert.Equal(["slot-b", "slot-c"], committed.Select(result => result.Generation));
        Assert.Equal(TimeSpan.FromHours(1).Ticks, committed[0].Mutation.ObservedGameTimeTicks);
        Assert.Equal(1, committed[0].Mutation.CompletedSleepSessions);
        Assert.Equal(TimeSpan.FromHours(1).Ticks, committed[0].Mutation.SleepAdvancedTimeTicks);
        Assert.Equal(TimeSpan.FromHours(2).Ticks, committed[1].Mutation.ObservedGameTimeTicks);
        Assert.Equal(1, committed[1].Mutation.CompletedSleepSessions);
        Assert.Equal(TimeSpan.FromHours(2).Ticks, committed[1].Mutation.SleepAdvancedTimeTicks);
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
