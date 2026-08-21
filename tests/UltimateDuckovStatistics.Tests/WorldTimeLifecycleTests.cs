using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Tests;

public sealed class WorldTimeLifecycleTests
{
    private const long Second = TimeSpan.TicksPerSecond;

    [Fact]
    public void ProfileDependencySurvivesUntilRunAndWorldTimeCleanupBothSucceed()
    {
        var disposals = 0;
        var gate = new CleanupCompletionGate(2, () => disposals++);
        var worldTimeResource = new WorldTimeCleanupResource { FailCleanup = true };
        var runResource = new RunCleanupResource();
        var worldTimeOwner = new ProcessLifetimeCleanupOwner<WorldTimeCleanupResource>();
        var runOwner = new ProcessLifetimeCleanupOwner<RunCleanupResource>();
        worldTimeOwner.Assign(worldTimeResource);
        runOwner.Assign(runResource);

        Assert.False(worldTimeOwner.TryCleanupOwned(gate.Signal));
        Assert.True(runOwner.TryCleanupOwned(gate.Signal));
        gate.Signal();

        Assert.Equal(1, gate.Remaining);
        Assert.Equal(0, disposals);

        worldTimeResource.FailCleanup = false;
        Assert.True(worldTimeOwner.TryCleanupPending());

        Assert.Equal(0, gate.Remaining);
        Assert.Equal(1, disposals);
        gate.Signal();
        Assert.Equal(1, disposals);
    }

    [Fact]
    [Trait("Category", "M12")]
    public void RetainedWorldTimeCleanupKeepsItsGenerationProviderAfterBehaviourFieldRelease()
    {
        using var directory = new TemporaryDirectory();
        var ids = new Queue<string>(
            ["generation-a", "session-a", "generation-b", "session-b", "session-b-reopen"]);
        var repository = new ProfileRepository(
            directory.Path,
            () => new DateTime(2026, 8, 21, 13, 0, 0, DateTimeKind.Utc),
            ids.Dequeue);
        var slotA = new SaveIdentitySnapshot { Slot = 1, SaveFilePresent = false };
        var slotB = new SaveIdentitySnapshot { Slot = 2, SaveFilePresent = false };
        repository.Open(slotA);
        var transition = new NativeProfileTransitionBoundary();
        var boundary = new NativeWorldTimeObservationBoundary();
        var handoff = new NativeWorldTimeProfileHandoffBoundary();
        RetainedProfileCoordinator? coordinatorField = new(
            repository,
            transition,
            boundary);
        var retainedCoordinator = coordinatorField;
        var generationProvider = NativeWorldTimeProfileBinding.CaptureGenerationProvider(
            retainedCoordinator,
            static coordinator => coordinator.CurrentGenerationId);
        var slotAClock = new object();
        var slotBClock = new object();
        var transitionFaulted = true;
        var publicationFailures = 0;

        handoff.Observe(repository.CurrentGenerationId, slotAClock, Read(2, 500), boundary);
        handoff.BeginAwaitingNativeLoad(50, slotAClock, Read(2, 500));
        Assert.Equal(
            WorldTimeObservationState.BaselineEstablished,
            handoff.Observe(repository.CurrentGenerationId, slotBClock, Read(20, 1_000), boundary)!.Value.State);
        Assert.True(handoff.Observe(
            repository.CurrentGenerationId,
            slotBClock,
            Read(20, 4_600),
            boundary)!.Value.Accepted);
        Assert.True(handoff.BeginSleepCompletion(50, TimeSpan.FromHours(1).Ticks));
        Assert.True(handoff.CompleteSleep());

        transition.Enqueue(
            "retained world-time cleanup",
            () =>
            {
                if (transitionFaulted) throw new IOException("temporary profile writer failure");
            },
            () => repository.Open(slotB, "SaveSlotSelected"),
            () => ProfileChangePublication.PublishIndependently(
                () => Assert.True(handoff.CompleteProfileChange(
                    50,
                    generationProvider(),
                    boundary,
                    currentReading: null,
                    out _,
                    out _)),
                _ => publicationFailures++));

        var cleanupResource = new RetainedWorldTimeCleanupResource(() =>
        {
            if (!retainedCoordinator.DrainPendingProfileTransitions()) return false;
            if (handoff.HasUncommittedData) return false;
            if (!retainedCoordinator.FlushPendingWorldTime()) return false;
            retainedCoordinator.Flush();
            handoff.Reset();
            return true;
        });
        var originalOwner = new ProcessLifetimeCleanupOwner<RetainedWorldTimeCleanupResource>();
        originalOwner.Assign(cleanupResource);

        Assert.False(originalOwner.TryCleanupOwned(retainedCoordinator.Dispose));
        Assert.True(handoff.HasUncommittedData);
        coordinatorField = null;
        transitionFaulted = false;

        var replacementOwner = new ProcessLifetimeCleanupOwner<RetainedWorldTimeCleanupResource>();
        Assert.True(replacementOwner.HasPendingCleanup);
        Assert.True(replacementOwner.TryCleanupPending());
        Assert.Null(coordinatorField);
        Assert.Equal(0, publicationFailures);
        Assert.True(retainedCoordinator.Disposed);
        Assert.False(handoff.HasUncommittedData);

        var reopened = new ProfileRepository(
            directory.Path,
            () => new DateTime(2026, 8, 21, 13, 1, 0, DateTimeKind.Utc),
            ids.Dequeue);
        var open = reopened.Open(slotB);
        Assert.False(open.InterruptedSessionRecovered);
        Assert.Equal(0, reopened.Current.InterruptedSessionCount);
        Assert.Equal(TimeSpan.FromHours(1).Ticks, reopened.Current.Statistics.WorldTime.ObservedGameTimeTicks);
        Assert.Equal(1, reopened.Current.Statistics.WorldTime.CompletedSleepSessions);
        Assert.Equal(TimeSpan.FromHours(1).Ticks, reopened.Current.Statistics.WorldTime.SleepAdvancedTimeTicks);
        reopened.CloseClean();

        var replacementResource = new RetainedWorldTimeCleanupResource(() => true);
        replacementOwner.Assign(replacementResource);
        Assert.Same(replacementResource, replacementOwner.OwnedValue);
        Assert.True(replacementOwner.TryCleanupOwned());
    }

    private sealed class WorldTimeCleanupResource : IRetryableCleanup
    {
        public bool FailCleanup { get; set; }

        public bool TryCleanup() => !FailCleanup;
    }

    private sealed class RunCleanupResource : IRetryableCleanup
    {
        public bool TryCleanup() => true;
    }

    private sealed class RetainedWorldTimeCleanupResource : IRetryableCleanup
    {
        private readonly Func<bool> cleanup;

        public RetainedWorldTimeCleanupResource(Func<bool> cleanup)
        {
            this.cleanup = cleanup;
        }

        public bool TryCleanup() => cleanup();
    }

    private sealed class RetainedProfileCoordinator
    {
        private readonly ProfileRepository repository;
        private readonly NativeProfileTransitionBoundary transition;
        private readonly NativeWorldTimeObservationBoundary boundary;

        public RetainedProfileCoordinator(
            ProfileRepository repository,
            NativeProfileTransitionBoundary transition,
            NativeWorldTimeObservationBoundary boundary)
        {
            this.repository = repository;
            this.transition = transition;
            this.boundary = boundary;
        }

        public bool Disposed { get; private set; }

        public string CurrentGenerationId => repository.CurrentGenerationId;

        public bool DrainPendingProfileTransitions() => transition.Drain(
            () => boundary.FlushPending(repository.RecordWorldTimeDeferred),
            _ => { });

        public bool FlushPendingWorldTime() => boundary.FlushPending(repository.RecordWorldTimeDeferred);

        public void Flush() => repository.Flush();

        public void Dispose()
        {
            repository.CloseClean();
            Disposed = true;
        }
    }

    private static WorldClockReading Read(long day, double seconds) =>
        new(day, TimeSpan.FromSeconds(seconds).Ticks);
}
