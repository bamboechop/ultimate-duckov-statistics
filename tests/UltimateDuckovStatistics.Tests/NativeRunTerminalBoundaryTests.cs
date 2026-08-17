using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Tests;

public sealed class NativeRunTerminalBoundaryTests
{
    private static readonly DateTime Origin = new(2026, 8, 17, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    [Trait("Category", "Run")]
    [Trait("Category", "M9")]
    public void NewRaidDrainsQueuedEconomyBeforeInterruptingTheOldRun()
    {
        var tracker = new RunLifecycleTracker(() => "run-old");
        tracker.Apply(Event(RunLifecycleEventKind.RaidInitialized, 0, nativeRaidId: "41"));
        tracker.Apply(Event(RunLifecycleEventKind.ControlReady, 1, Context(), nativeRaidId: "41"));
        var attribution = tracker.ActiveEventContext!;
        var observerCalls = 0;
        var diagnostics = new List<string>();
        var boundary = new NativeRunTerminalBoundary();
        boundary.SetTerminalObserver(() =>
        {
            observerCalls++;
            Assert.True(tracker.RecordCurrencyFlow(new CurrencyFlowRecorded
            {
                EventId = "queued-money",
                TimestampUtc = Origin.AddSeconds(2),
                SaveGenerationId = "generation-1",
                RunId = attribution.RunId,
                SegmentId = attribution.SegmentId,
                MapId = attribution.MapId,
                Currency = CurrencyKind.Money,
                Direction = CurrencyFlowDirection.Inflow,
                Amount = 17,
                Source = CurrencySourceCategory.UnknownAdjustment,
                GameplayContext = GameplayContext.Raid,
                IntegrityTags = IntegrityTags.Normal,
                AdapterVersion = "test",
                ProducerActivationId = "test-activation",
                ProducerSequence = 1
            }));
        });

        var transition = boundary.Apply(
            tracker,
            Event(RunLifecycleEventKind.RaidInitialized, 3, nativeRaidId: "42"),
            diagnostics.Add,
            () => true);

        Assert.Equal(1, observerCalls);
        Assert.Empty(diagnostics);
        Assert.NotNull(transition.Completed);
        Assert.Equal(RunOutcome.Interrupted, transition.Completed!.Outcome);
        Assert.Equal(17, transition.Completed.Economy.Currencies["Money"].Totals.GrossInflow);
    }

    [Fact]
    [Trait("Category", "Run")]
    [Trait("Category", "M9")]
    public void FailedTerminalCheckpointKeepsTheRunActiveUntilDurableRetry()
    {
        var tracker = new RunLifecycleTracker(() => "run-old");
        tracker.Apply(Event(RunLifecycleEventKind.RaidInitialized, 0, nativeRaidId: "41"));
        tracker.Apply(Event(RunLifecycleEventKind.ControlReady, 1, Context(), nativeRaidId: "41"));
        var observerCalls = 0;
        var checkpointAttempts = 0;
        var diagnostics = new List<string>();
        var boundary = new NativeRunTerminalBoundary();
        boundary.SetTerminalObserver(() => observerCalls++);

        var terminalEvent = Event(RunLifecycleEventKind.Extracted, 3);
        var blocked = boundary.Apply(
            tracker,
            terminalEvent,
            diagnostics.Add,
            () =>
            {
                checkpointAttempts++;
                return false;
            });

        Assert.Null(blocked.Completed);
        Assert.True(tracker.IsActive);
        Assert.True(boundary.HasPendingTerminal);
        Assert.Same(terminalEvent, boundary.PendingTerminalEvent);
        Assert.Equal(Origin.AddSeconds(3), boundary.PendingTerminalEvent!.TimestampUtc);
        Assert.Equal(3, boundary.PendingTerminalEvent.MonotonicSeconds);
        Assert.Equal(1, observerCalls);
        Assert.Equal(1, checkpointAttempts);

        var completed = boundary.Retry(
            tracker,
            diagnostics.Add,
            lifecycleEvent =>
            {
                checkpointAttempts++;
                Assert.Equal(Origin.AddSeconds(3), lifecycleEvent.TimestampUtc);
                Assert.Equal(3, lifecycleEvent.MonotonicSeconds);
                return true;
            });

        Assert.False(tracker.IsActive);
        Assert.False(boundary.HasPendingTerminal);
        Assert.Equal(RunOutcome.Extracted, completed.Completed!.Outcome);
        Assert.Equal(1, observerCalls);
        Assert.Equal(2, checkpointAttempts);
        Assert.Collection(
            diagnostics,
            message => Assert.Contains("terminalization deferred", message, StringComparison.Ordinal));
    }

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
        NativeRaidId = "41",
        Map = new MapIdentity { MapId = "duckov:map:warehouse", DisplayName = "Warehouse", IsKnown = true },
        IntegrityTags = IntegrityTags.Normal,
        GameVersion = "2.3.30",
        GameBuild = "24013657",
        LifecycleCapability = AdapterCapabilityState.Supported,
        LifecycleAdapterVersion = "native-run-lifecycle/2.3.30",
        MovementCapability = AdapterCapabilityState.Supported,
        MovementAdapterVersion = "native-main-duck-movement/2.3.30",
        MapCapability = AdapterCapabilityState.Supported,
        MapAdapterVersion = "native-map-identity/2.3.30",
        EconomyCapabilities = SupportedEconomyCapabilities()
    };

    private static EconomyMetricCapabilities SupportedEconomyCapabilities()
    {
        MetricAvailability Supported() => new()
        {
            State = AdapterCapabilityState.Supported,
            Provenance = "test native contract"
        };

        return new EconomyMetricCapabilities
        {
            MoneyAmountDirection = Supported(),
            MoneySourceAttribution = Supported(),
            MoneyContextAttribution = Supported(),
            CashAmountDirection = Supported(),
            CashExternalAcquisition = Supported(),
            CashContextAttribution = Supported(),
            CashTerminalOutcomes = Supported(),
            RouteAttribution = Supported()
        };
    }
}
