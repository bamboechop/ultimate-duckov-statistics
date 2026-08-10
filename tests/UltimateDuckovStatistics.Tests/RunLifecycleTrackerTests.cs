using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Tests;

public sealed class RunLifecycleTrackerTests
{
    private static readonly DateTime Origin = new(2026, 8, 10, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    [Trait("Category", "Run")]
    public void ControlReadyStartsOnlyAfterRaidInitializationAndTerminalCallbacksFinalizeOnce()
    {
        var tracker = CreateTracker();

        Assert.False(tracker.Apply(Event(RunLifecycleEventKind.ControlReady, 0, Context())).Started);
        tracker.Apply(Event(RunLifecycleEventKind.RaidInitialized, 0, nativeRaidId: "42"));
        Assert.True(tracker.Apply(Event(RunLifecycleEventKind.ControlReady, 1, Context())).Started);
        Assert.False(tracker.Apply(Event(RunLifecycleEventKind.ControlReady, 2, Context())).Started);

        var completed = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 11)).Completed;

        Assert.NotNull(completed);
        Assert.Equal("run-1", completed.RunId);
        Assert.Equal("42", completed.NativeRaidId);
        Assert.Equal(RunOutcome.Extracted, completed.Outcome);
        Assert.Equal(10, completed.ActiveDurationSeconds);
        Assert.Null(tracker.Apply(Event(RunLifecycleEventKind.Died, 12)).Completed);
        Assert.Null(tracker.Apply(Event(RunLifecycleEventKind.Extracted, 13)).Completed);
    }

    [Fact]
    [Trait("Category", "Run")]
    public void BaseLoadingPlaceholderAndReorderedTerminalEventsCannotStartOrPreemptARun()
    {
        var tracker = CreateTracker();

        Assert.Null(tracker.Apply(Event(RunLifecycleEventKind.Died, 0)).Completed);
        Assert.False(tracker.Apply(Event(RunLifecycleEventKind.ControlReady, 1, Context())).Started);
        tracker.Apply(Event(RunLifecycleEventKind.LoadingStarted, 2));
        tracker.Apply(Event(RunLifecycleEventKind.RaidInitialized, 3, nativeRaidId: "42"));
        Assert.False(tracker.Apply(Event(RunLifecycleEventKind.ControlReady, 4)).Started);
        Assert.False(tracker.Apply(Event(RunLifecycleEventKind.ControlReady, 5, Context())).Started);
        tracker.Apply(Event(RunLifecycleEventKind.LoadingEnded, 6));

        Assert.True(tracker.Apply(Event(RunLifecycleEventKind.ControlReady, 7, Context())).Started);
        Assert.NotNull(tracker.Apply(Event(RunLifecycleEventKind.Extracted, 8)).Completed);
        Assert.Null(tracker.Apply(Event(RunLifecycleEventKind.Died, 8.01)).Completed);
    }

    [Fact]
    [Trait("Category", "Run")]
    public void ActiveDurationExcludesPauseAndLoadingWithIndependentIdempotentReasons()
    {
        var tracker = StartedTracker();

        tracker.Apply(Event(RunLifecycleEventKind.PauseStarted, 2));
        tracker.Apply(Event(RunLifecycleEventKind.PauseStarted, 3));
        tracker.Apply(Event(RunLifecycleEventKind.LoadingStarted, 4));
        tracker.Apply(Event(RunLifecycleEventKind.PauseEnded, 7));
        Assert.True(tracker.IsSuspended);
        tracker.Apply(Event(RunLifecycleEventKind.LoadingEnded, 8));
        Assert.False(tracker.IsSuspended);

        var completed = tracker.Apply(Event(RunLifecycleEventKind.Died, 10)).Completed;

        Assert.NotNull(completed);
        Assert.Equal(4, completed.ActiveDurationSeconds);
        Assert.Equal(10, completed.WallClockDurationSeconds);
    }

    [Fact]
    [Trait("Category", "Run")]
    public void CheckpointCadenceIsThrottledAndLifecycleBoundariesRequireImmediateCheckpoint()
    {
        var tracker = StartedTracker();

        Assert.False(tracker.Tick(Origin.AddSeconds(4.9), 4.9));
        Assert.True(tracker.Tick(Origin.AddSeconds(5), 5));
        var checkpoint = tracker.CreateCheckpoint(Origin.AddSeconds(5), 5);
        Assert.NotNull(checkpoint);
        tracker.MarkCheckpointSaved(5);
        Assert.False(tracker.Tick(Origin.AddSeconds(9.9), 9.9));
        Assert.True(tracker.Apply(Event(RunLifecycleEventKind.PauseStarted, 10)).CheckpointRequired);
    }

    [Fact]
    [Trait("Category", "Run")]
    public void InterruptedCheckpointNeverBecomesRecordEligible()
    {
        var tracker = StartedTracker();
        var checkpoint = tracker.CreateCheckpoint(Origin.AddSeconds(3), 3)!;

        var summary = checkpoint.ToInterruptedSummary();

        Assert.Equal(RunOutcome.Interrupted, summary.Outcome);
        Assert.False(summary.RecordEligible);
        Assert.Equal(3, summary.ActiveDurationSeconds);
    }

    [Fact]
    [Trait("Category", "Run")]
    [Trait("Category", "Integrity")]
    public void IntegrityChangesDuringAnActiveRunAccumulateAndDisqualifyRecords()
    {
        var tracker = StartedTracker();

        Assert.True(tracker.ObserveIntegrity(IntegrityTags.CheatOrCustomDifficulty));
        Assert.False(tracker.ObserveIntegrity(IntegrityTags.Normal));
        Assert.True(tracker.ObserveIntegrity(IntegrityTags.ModdedContent));
        var checkpoint = tracker.CreateCheckpoint(Origin.AddSeconds(5), 5)!;
        var completed = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 10)).Completed!;

        var expected = IntegrityTags.CheatOrCustomDifficulty | IntegrityTags.ModdedContent;
        Assert.Equal(expected, checkpoint.IntegrityTags);
        Assert.Equal(expected, completed.IntegrityTags);
        Assert.False(completed.RecordEligible);
    }

    [Theory]
    [Trait("Category", "Run")]
    [Trait("Category", "Integrity")]
    [InlineData(IntegrityTags.Normal, IntegrityTags.Unknown, IntegrityTags.Unknown)]
    [InlineData(IntegrityTags.Unknown, IntegrityTags.Normal, IntegrityTags.Unknown)]
    [InlineData(IntegrityTags.CheatOrCustomDifficulty, IntegrityTags.Unknown, IntegrityTags.CheatOrCustomDifficulty)]
    public void IntegrityAccumulationNeverUpgradesAnUncertainOrDisqualifiedRun(
        IntegrityTags accumulated,
        IntegrityTags observed,
        IntegrityTags expected)
    {
        Assert.Equal(expected, RunIntegrityPolicy.Accumulate(accumulated, observed));
    }

    private static RunLifecycleTracker StartedTracker()
    {
        var tracker = CreateTracker();
        tracker.Apply(Event(RunLifecycleEventKind.RaidInitialized, 0, nativeRaidId: "42"));
        tracker.Apply(Event(RunLifecycleEventKind.ControlReady, 0, Context()));
        return tracker;
    }

    private static RunLifecycleTracker CreateTracker() => new(() => "run-1");

    private static RunLifecycleEvent Event(
        RunLifecycleEventKind kind,
        double seconds,
        RunStartContext? context = null,
        string? nativeRaidId = null) => new()
        {
            Kind = kind,
            TimestampUtc = Origin.AddSeconds(seconds),
            MonotonicSeconds = seconds,
            StartContext = context,
            NativeRaidId = nativeRaidId
        };

    private static RunStartContext Context() => new()
    {
        SaveGenerationId = "generation-1",
        Map = new MapIdentity { MapId = "duckov:map:warehouse", DisplayName = "Warehouse", IsKnown = true },
        IntegrityTags = IntegrityTags.Normal,
        GameVersion = "2.3.30",
        GameBuild = "24013657",
        LifecycleCapability = AdapterCapabilityState.Supported,
        LifecycleAdapterVersion = "native-run-lifecycle/2.3.30",
        MovementCapability = AdapterCapabilityState.Supported,
        MovementAdapterVersion = "native-main-duck-movement/2.3.30",
        MapCapability = AdapterCapabilityState.Supported,
        MapAdapterVersion = "native-map-identity/2.3.30"
    };
}
