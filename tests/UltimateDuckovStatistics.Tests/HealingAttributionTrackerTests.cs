using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Tests;

public sealed class HealingAttributionTrackerTests
{
    private static readonly DateTime StartedAt = new(2026, 8, 9, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    [Trait("Category", "Healing")]
    public void ImmediateHealingIsBufferedUntilSuccessfulRaidUseIsProven()
    {
        var tracker = CreateTracker();
        tracker.BeginUse(CreateContext("use-a", runtimeItemId: 10, "item:a"));

        var beforeSuccess = tracker.Observe("use-a", CreateObservation("application-a", 25));
        var proven = tracker.CompleteUse(10, CreateSuccessfulUse("event-use-a", "item:a"));

        Assert.Empty(beforeSuccess);
        var healing = Assert.Single(proven);
        Assert.Equal(25, healing.ActualHealthRestored);
        Assert.Equal("event-use-a", healing.SourceItemUseEventId);
        Assert.Equal("item:a", healing.ItemId);
    }

    [Theory]
    [Trait("Category", "Healing")]
    [InlineData(40, 100, 25, 25)]
    [InlineData(90, 100, 25, 10)]
    [InlineData(100, 100, 25, 0)]
    [InlineData(110, 100, 25, 0)]
    [InlineData(40, 100, -5, 0)]
    public void ExactApplicationBoundaryExcludesPartialAndCompleteOverheal(
        double current,
        double maximum,
        double requested,
        double expected)
    {
        Assert.Equal(
            expected,
            HealingAttributionTracker.CalculateActualRestoration(current, maximum, requested),
            precision: 6);
    }

    [Fact]
    [Trait("Category", "Healing")]
    public void FullHealthSuccessfulUseProducesNoHealingEvent()
    {
        var tracker = CreateTracker();
        tracker.BeginUse(CreateContext("use-full", 10, "item:a"));
        var actual = HealingAttributionTracker.CalculateActualRestoration(100, 100, 25);

        var observed = tracker.Observe("use-full", CreateObservation("application-full", actual));
        var completed = tracker.CompleteUse(10, CreateSuccessfulUse("event-use-full", "item:a"));

        Assert.Equal(0, actual);
        Assert.Empty(observed);
        Assert.Empty(completed);
    }

    [Theory]
    [Trait("Category", "Healing")]
    [InlineData(40, 40, 100, 0)]
    [InlineData(40, 45, 100, 5)]
    [InlineData(90, 100, 100, 10)]
    [InlineData(90, 120, 100, 10)]
    [InlineData(40, 35, 100, 0)]
    public void SynchronousCallDeltaRejectsSuppressedHealingAndUsesActualAppliedAmount(
        double before,
        double after,
        double maximum,
        double expected)
    {
        Assert.Equal(
            expected,
            HealingAttributionTracker.CalculateAppliedRestoration(before, after, maximum),
            precision: 6);
    }

    [Fact]
    [Trait("Category", "Healing")]
    public void DamageAfterTheApplicationDoesNotChangeAttributedHealing()
    {
        var exactApplication = HealingAttributionTracker.CalculateActualRestoration(40, 100, 30);
        var healthAfterInterleavedDamage = 55;

        Assert.Equal(30, exactApplication);
        Assert.NotEqual(healthAfterInterleavedDamage - 40, exactApplication);
    }

    [Fact]
    [Trait("Category", "Healing")]
    public void DelayedBuffHealingUsesTheProvenOriginatingItem()
    {
        var tracker = CreateTracker();
        tracker.BeginUse(CreateContext("use-a", 10, "item:a"));
        Assert.True(tracker.BindBuff(runtimeBuffId: 501, "use-a"));
        Assert.Empty(tracker.CompleteUse(10, CreateSuccessfulUse("event-use-a", "item:a")));

        var delayed = tracker.Observe(
            tracker.TryGetBuffCorrelation(501),
            CreateObservation("application-delayed", 7));

        var healing = Assert.Single(delayed);
        Assert.Equal("item:a", healing.ItemId);
        Assert.Equal(7, healing.ActualHealthRestored);
    }

    [Fact]
    [Trait("Category", "Healing")]
    public void OverlappingBuffsRemainAttributedToTheirOwnItems()
    {
        var tracker = CreateTracker();
        tracker.BeginUse(CreateContext("use-a", 10, "item:a"));
        tracker.BindBuff(501, "use-a");
        tracker.CompleteUse(10, CreateSuccessfulUse("event-use-a", "item:a"));
        tracker.BeginUse(CreateContext("use-b", 20, "item:b"));
        tracker.BindBuff(502, "use-b");
        tracker.CompleteUse(20, CreateSuccessfulUse("event-use-b", "item:b"));

        var fromB = Assert.Single(tracker.Observe(
            tracker.TryGetBuffCorrelation(502),
            CreateObservation("application-b", 4)));
        var fromA = Assert.Single(tracker.Observe(
            tracker.TryGetBuffCorrelation(501),
            CreateObservation("application-a", 6)));

        Assert.Equal("item:b", fromB.ItemId);
        Assert.Equal("item:a", fromA.ItemId);
    }

    [Fact]
    [Trait("Category", "Healing")]
    public void ConsecutiveUseCanDeterministicallyTakeOwnershipOfRefreshedBuff()
    {
        var tracker = CreateTracker();
        tracker.BeginUse(CreateContext("use-old", 10, "item:old"));
        tracker.BindBuff(501, "use-old");
        tracker.CompleteUse(10, CreateSuccessfulUse("event-old", "item:old"));
        tracker.BeginUse(CreateContext("use-new", 20, "item:new"));
        tracker.BindBuff(501, "use-new");
        tracker.CompleteUse(20, CreateSuccessfulUse("event-new", "item:new"));

        var healing = Assert.Single(tracker.Observe(
            tracker.TryGetBuffCorrelation(501),
            CreateObservation("application-new", 3)));

        Assert.Equal("item:new", healing.ItemId);
    }

    [Fact]
    [Trait("Category", "Healing")]
    public void UnownedRefreshClearsReusedBuffInstanceProvenance()
    {
        var tracker = CreateTracker();
        tracker.BeginUse(CreateContext("use-old", 10, "item:old"));
        Assert.True(tracker.ReconcileBuff(501, "use-old"));
        tracker.CompleteUse(10, CreateSuccessfulUse("event-old", "item:old"));

        Assert.True(tracker.ReconcileBuff(501, correlationId: null));

        Assert.Null(tracker.TryGetBuffCorrelation(501));
        Assert.Equal(0, tracker.BuffSourceCount);
        Assert.Empty(tracker.Observe(
            "use-old",
            CreateObservation("application-after-unowned-refresh", 3)));
    }

    [Fact]
    [Trait("Category", "Healing")]
    public void DuplicateCallbackUnrelatedHealingAndNonPlayerTargetsAreIgnored()
    {
        var tracker = CreateTracker();
        tracker.BeginUse(CreateContext("use-a", 10, "item:a"));
        tracker.BindBuff(501, "use-a");
        tracker.CompleteUse(10, CreateSuccessfulUse("event-use-a", "item:a"));

        Assert.Empty(tracker.Observe(null, CreateObservation("unrelated", 9)));
        var nonPlayer = CreateObservation("non-player", 9);
        nonPlayer.IsMainPlayerTarget = false;
        Assert.Empty(tracker.Observe("use-a", nonPlayer));

        var first = tracker.Observe("use-a", CreateObservation("duplicate", 5));
        var duplicate = tracker.Observe("use-a", CreateObservation("duplicate", 5));
        Assert.Single(first);
        Assert.Empty(duplicate);
    }

    [Fact]
    [Trait("Category", "Healing")]
    public void CancelledFailedAndBaseUsesDiscardBufferedHealingAndBuffOwnership()
    {
        var tracker = CreateTracker();
        tracker.BeginUse(CreateContext("cancelled", 10, "item:a"));
        tracker.BindBuff(501, "cancelled");
        tracker.Observe("cancelled", CreateObservation("application-a", 8));
        Assert.Empty(tracker.CompleteUse(10, successfulUse: null));
        Assert.Null(tracker.TryGetBuffCorrelation(501));

        tracker.BeginUse(CreateContext("base", 20, "item:b"));
        tracker.Observe("base", CreateObservation("application-b", 8));
        var baseUse = CreateSuccessfulUse("event-base", "item:b");
        baseUse.GameplayContext = GameplayContext.Base;
        Assert.Empty(tracker.CompleteUse(20, baseUse));
    }

    [Fact]
    [Trait("Category", "Healing")]
    public void RestartDoesNotGuessAtPendingOrPreviouslyMappedBuffAttribution()
    {
        var beforeRestart = CreateTracker();
        beforeRestart.BeginUse(CreateContext("use-a", 10, "item:a"));
        beforeRestart.BindBuff(501, "use-a");
        beforeRestart.Observe("use-a", CreateObservation("application-a", 8));

        var afterRestart = CreateTracker();

        Assert.Null(afterRestart.TryGetBuffCorrelation(501));
        Assert.Empty(afterRestart.Observe("use-a", CreateObservation("application-b", 8)));
    }

    [Fact]
    [Trait("Category", "Healing")]
    public void PendingContextsExpireWithoutProducingHealing()
    {
        var tracker = CreateTracker();
        tracker.BeginUse(CreateContext("use-a", 10, "item:a"));
        tracker.Observe("use-a", CreateObservation("application-a", 5));

        Assert.Equal(1, tracker.ExpirePendingBefore(StartedAt.AddMinutes(1)));
        Assert.Empty(tracker.CompleteUse(10, CreateSuccessfulUse("event-use-a", "item:a")));
    }

    private static HealingAttributionTracker CreateTracker()
    {
        var sequence = 0;
        return new HealingAttributionTracker(() => $"healing-event-{++sequence}");
    }

    private static HealingUseContext CreateContext(string correlationId, int runtimeItemId, string itemId) => new()
    {
        CorrelationId = correlationId,
        RuntimeItemId = runtimeItemId,
        StartedUtc = StartedAt,
        SaveGenerationId = "generation-a",
        RunId = "raid-1",
        MapId = "warehouse",
        GameVersion = "2.3.30",
        GameBuild = "24013657",
        GameplayContext = GameplayContext.Raid,
        IntegrityTags = IntegrityTags.Normal,
        AdapterCapability = AdapterCapabilityState.Supported,
        AdapterVersion = "native-healing-attribution/2.3.30+harmony-2.4.1",
        ItemId = itemId,
        DisplayName = itemId,
        Group = CanonicalItemGroup.Healing
    };

    private static HealingObservation CreateObservation(string applicationId, double amount) => new()
    {
        ApplicationId = applicationId,
        TimestampUtc = StartedAt.AddSeconds(2),
        ActualHealthRestored = amount,
        IsMainPlayerTarget = true
    };

    private static ItemUseRecorded CreateSuccessfulUse(string eventId, string itemId) => new()
    {
        EventId = eventId,
        TimestampUtc = StartedAt.AddSeconds(1),
        SaveGenerationId = "generation-a",
        RunId = "raid-1",
        MapId = "warehouse",
        GameVersion = "2.3.30",
        GameBuild = "24013657",
        GameplayContext = GameplayContext.Raid,
        IntegrityTags = IntegrityTags.Normal,
        AdapterCapability = AdapterCapabilityState.Supported,
        AdapterVersion = "native-item-use/2.3.30",
        ItemId = itemId,
        DisplayName = itemId,
        Group = CanonicalItemGroup.Healing,
        ActivationCount = 1,
        AmountConsumed = 1,
        ConsumptionUnit = ConsumptionUnit.Item
    };
}
