using UltimateDuckovStatistics.Core.Compatibility;
using System.Globalization;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Export;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Tests;

public sealed class ContainerStatisticsTests
{
    private static readonly DateTime Origin = new(2026, 8, 12, 12, 0, 0, DateTimeKind.Utc);
    private static readonly int[] ExpectedCheckpointKeys = { 101, 202 };

    [Fact]
    [Trait("Category", "Container")]
    public void SuccessfulAccessCountsEachStableKeyOncePerRun()
    {
        var tracker = StartedTracker();

        Assert.True(tracker.RecordContainer(Event(tracker, 101, "first")));
        Assert.False(tracker.RecordContainer(Event(tracker, 101, "reopen")));
        Assert.False(tracker.RecordContainer(Event(tracker, 101, "second-callback-path")));
        Assert.True(tracker.RecordContainer(Event(tracker, 202, "second-container")));

        var summary = tracker.Apply(Lifecycle(RunLifecycleEventKind.Extracted, 10)).Completed!;
        Assert.Equal(2, summary.ContainerStatistics.UniqueContainersLooted);
        Assert.Equal(AdapterCapabilityState.Supported,
            summary.ContainerStatistics.Capabilities.UniqueContainersLooted.State);
    }

    [Fact]
    [Trait("Category", "Container")]
    public void RuntimeKeysAreCanonicalRegardlessOfObservationOrderWithoutRepair()
    {
        var tracker = StartedTracker();

        Assert.True(tracker.RecordContainer(Event(tracker, 202, "higher-key-first")));
        Assert.True(tracker.RecordContainer(Event(tracker, 101, "lower-key-second")));
        var checkpoint = tracker.CreateCheckpoint(Origin.AddSeconds(1), 1)!;

        Assert.Equal(ExpectedCheckpointKeys, checkpoint.ContainerState.LootedContainerKeys);
        Assert.False(checkpoint.ContainerState.WasRepairedFromInvalidState);
        Assert.False(checkpoint.ContainerState.Statistics.WasRepairedFromInvalidState);
    }

    [Fact]
    [Trait("Category", "Container")]
    public void SameStableKeyCanCountAgainInLaterRun()
    {
        var tracker = StartedTracker();
        Assert.True(tracker.RecordContainer(Event(tracker, 101, "run-one")));
        var first = tracker.Apply(Lifecycle(RunLifecycleEventKind.Extracted, 5)).Completed!;

        tracker.Apply(Lifecycle(RunLifecycleEventKind.RaidInitialized, 6, nativeRaidId: "2"));
        tracker.Apply(Lifecycle(RunLifecycleEventKind.ControlReady, 6, Context(), "2"));
        Assert.True(tracker.RecordContainer(Event(tracker, 101, "run-two")));
        var second = tracker.Apply(Lifecycle(RunLifecycleEventKind.Extracted, 10)).Completed!;

        Assert.Equal(1, first.ContainerStatistics.UniqueContainersLooted);
        Assert.Equal(1, second.ContainerStatistics.UniqueContainersLooted);
    }

    [Theory]
    [Trait("Category", "Container")]
    [InlineData(false, true, true, false, true)]
    [InlineData(true, false, true, false, true)]
    [InlineData(true, true, false, false, true)]
    [InlineData(true, true, true, true, true)]
    [InlineData(true, true, true, false, false)]
    public void AttemptsFailuresCorpsesOtherDucksBaseAndMissingIdentityDoNotQualify(
        bool runActive,
        bool raid,
        bool exactMainDuck,
        bool corpse,
        bool stableKey)
    {
        Assert.False(ContainerLootAcceptancePolicy.ShouldAccept(
            runActive, raid, exactMainDuck, corpse, stableKey));
    }

    [Theory]
    [Trait("Category", "Container")]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, true, true)]
    public void ExcludedAccessesDoNotRequireStableIdentity(
        bool runActive,
        bool raid,
        bool exactMainDuck,
        bool corpse)
    {
        Assert.False(ContainerLootAcceptancePolicy.RequiresStableIdentity(
            runActive, raid, exactMainDuck, corpse));
    }

    [Fact]
    [Trait("Category", "Container")]
    public void UnknownOrModdedNonCorpseQualifiesWhenStableIdentityExists()
    {
        Assert.True(ContainerLootAcceptancePolicy.ShouldAccept(
            runActive: true,
            raidContext: true,
            exactMainDuck: true,
            corpse: false,
            stableKeyAvailable: true));
    }

    [Fact]
    [Trait("Category", "Container")]
    public void MissingEmptyThrowingAndIncompatibleGetKeyEvidenceNeverFabricatesIdentity()
    {
        Assert.False(ContainerLootAcceptancePolicy.TryReadStableKey(null!, out _, out var missing));
        Assert.Contains("missing", missing, StringComparison.OrdinalIgnoreCase);
        Assert.False(ContainerLootAcceptancePolicy.TryReadStableKey(() => null, out _, out var empty));
        Assert.Contains("no identity", empty, StringComparison.OrdinalIgnoreCase);
        Assert.False(ContainerLootAcceptancePolicy.TryReadStableKey(() => "101", out _, out var incompatible));
        Assert.Contains("incompatible", incompatible, StringComparison.OrdinalIgnoreCase);
        Assert.False(ContainerLootAcceptancePolicy.TryReadStableKey(
            () => throw new InvalidOperationException("native failure"), out _, out var throwing));
        Assert.Contains("native failure", throwing, StringComparison.Ordinal);
        Assert.True(ContainerLootAcceptancePolicy.TryReadStableKey(() => 0, out var zero, out _));
        Assert.Equal(0, zero);
    }

    [Fact]
    [Trait("Category", "Container")]
    public void RuntimeIdentityFailureRestrictsTheActiveRunCapability()
    {
        var tracker = StartedTracker();
        Assert.True(tracker.UpdateContainerCapabilities(ContainerNativeContractPolicy.Unavailable(
            "GetKey failed for a successful access.")));

        var summary = tracker.Apply(Lifecycle(RunLifecycleEventKind.Extracted, 2)).Completed!;

        Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
            summary.ContainerStatistics.Capabilities.UniqueContainersLooted.State);
        Assert.Contains("GetKey failed", summary.ContainerStatistics.Capabilities.UniqueContainersLooted.Provenance);
    }

    [Fact]
    [Trait("Category", "Container")]
    public void CheckpointAndInterruptedRecoveryRetainTotalAndStableKeySet()
    {
        var tracker = StartedTracker();
        tracker.RecordContainer(Event(tracker, 101, "first"));
        tracker.RecordContainer(Event(tracker, 202, "second"));

        var checkpoint = tracker.CreateCheckpoint(Origin.AddSeconds(3), 3)!;
        var interrupted = checkpoint.ToInterruptedSummary();

        Assert.Equal(ExpectedCheckpointKeys, checkpoint.ContainerState.LootedContainerKeys);
        Assert.Equal(2, checkpoint.ContainerState.Statistics.UniqueContainersLooted);
        Assert.Equal(2, interrupted.ContainerStatistics.UniqueContainersLooted);
        Assert.Equal(RunOutcome.Interrupted, interrupted.Outcome);
    }

    [Fact]
    [Trait("Category", "Container")]
    public void DeduplicationStateIsBoundedAndDisablesRatherThanEvicting()
    {
        var state = new ContainerRunCheckpointState
        {
            Statistics = new ContainerStatisticsAggregate
            {
                Capabilities = ContainerNativeContractPolicy.Supported()
            }
        };
        for (var key = 0; key < ContainerRunCheckpointState.DeduplicationCapacity; key++)
            Assert.True(ContainerStatisticsReducer.Record(state, Event(key, key.ToString(CultureInfo.InvariantCulture))));

        Assert.False(ContainerStatisticsReducer.Record(state, Event(int.MaxValue, "overflow")));
        Assert.Equal(ContainerRunCheckpointState.DeduplicationCapacity, state.LootedContainerKeys.Count);
        Assert.Equal(ContainerRunCheckpointState.DeduplicationCapacity, state.Statistics.UniqueContainersLooted);
        Assert.True(state.DeduplicationSaturated);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
            state.Statistics.Capabilities.UniqueContainersLooted.State);
    }

    [Fact]
    [Trait("Category", "Container")]
    public void SaturationTransitionDirtiesCheckpointWithoutIncrementingTheCount()
    {
        var tracker = StartedTracker();
        for (var key = 0; key < ContainerRunCheckpointState.DeduplicationCapacity; key++)
            Assert.True(tracker.RecordContainer(Event(tracker, key, key.ToString(CultureInfo.InvariantCulture))));

        tracker.MarkCheckpointSaved(ContainerRunCheckpointState.DeduplicationCapacity);
        Assert.False(tracker.CombatCheckpointRequired);

        Assert.False(tracker.RecordContainer(Event(tracker, int.MaxValue, "saturating-key")));

        Assert.True(tracker.CombatCheckpointRequired);
        var checkpoint = tracker.CreateCheckpoint(
            Origin.AddSeconds(ContainerRunCheckpointState.DeduplicationCapacity + 1),
            ContainerRunCheckpointState.DeduplicationCapacity + 1)!;
        Assert.Equal(ContainerRunCheckpointState.DeduplicationCapacity,
            checkpoint.ContainerState.Statistics.UniqueContainersLooted);
        Assert.True(checkpoint.ContainerState.DeduplicationSaturated);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
            checkpoint.ContainerState.Statistics.Capabilities.UniqueContainersLooted.State);
    }

    [Fact]
    [Trait("Category", "Container")]
    public void PersistedSaturationRequiresCompleteKeysAndUnavailableCapability()
    {
        var incomplete = new ContainerRunCheckpointState
        {
            Statistics = Supported(1),
            LootedContainerKeys = [1],
            DeduplicationSaturated = true
        };
        Assert.Throws<ArgumentException>(() => ContainerStatisticsReducer.NormalizeCheckpoint(incomplete));

        var complete = new ContainerRunCheckpointState
        {
            Statistics = Supported(ContainerRunCheckpointState.DeduplicationCapacity),
            LootedContainerKeys = Enumerable.Range(0, ContainerRunCheckpointState.DeduplicationCapacity).ToList(),
            DeduplicationSaturated = true
        };
        Assert.True(ContainerStatisticsReducer.NormalizeCheckpoint(complete));
        Assert.True(complete.WasRepairedFromInvalidState);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
            complete.Statistics.Capabilities.UniqueContainersLooted.State);
        Assert.Contains("repaired", complete.Statistics.Capabilities.UniqueContainersLooted.Provenance,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Container")]
    public void PersistedUnsortedKeysRepairCanonicallyAndDisableCapability()
    {
        var state = new ContainerRunCheckpointState
        {
            Statistics = Supported(2),
            LootedContainerKeys = [202, 101]
        };

        Assert.True(ContainerStatisticsReducer.NormalizeCheckpoint(state));

        Assert.Equal(ExpectedCheckpointKeys, state.LootedContainerKeys);
        Assert.True(state.WasRepairedFromInvalidState);
        Assert.True(state.Statistics.WasRepairedFromInvalidState);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
            state.Statistics.Capabilities.UniqueContainersLooted.State);
        Assert.Contains("repaired", state.Statistics.Capabilities.UniqueContainersLooted.Provenance,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    [Trait("Category", "Container")]
    public void InvalidPersistedCountersRepairWithProvenanceAndMergeSaturates()
    {
        var repaired = new ContainerStatisticsAggregate
        {
            UniqueContainersLooted = -5,
            Capabilities = null!
        };
        Assert.True(ContainerStatisticsReducer.NormalizePersisted(repaired));
        Assert.Equal(0, repaired.UniqueContainersLooted);
        Assert.True(repaired.WasRepairedFromInvalidState);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
            repaired.Capabilities.UniqueContainersLooted.State);
        Assert.Contains("repaired", repaired.Capabilities.UniqueContainersLooted.Provenance,
            StringComparison.OrdinalIgnoreCase);

        var target = Supported(long.MaxValue - 1);
        ContainerStatisticsReducer.Merge(target, Supported(5));
        Assert.Equal(long.MaxValue, target.UniqueContainersLooted);
    }

    [Fact]
    [Trait("Category", "Container")]
    public void FirstRunCapabilityIsAdoptedWithoutUpgradingEarlierUnavailableZero()
    {
        var firstSupported = new ContainerStatisticsAggregate();
        ContainerStatisticsReducer.Merge(
            firstSupported,
            Supported(0),
            adoptSourceCapability: true);
        Assert.Equal(AdapterCapabilityState.Supported,
            firstSupported.Capabilities.UniqueContainersLooted.State);

        var retainedUnavailable = new ContainerStatisticsAggregate();
        ContainerStatisticsReducer.Merge(
            retainedUnavailable,
            new ContainerStatisticsAggregate
            {
                Capabilities = ContainerNativeContractPolicy.Unavailable("First run was incompatible.")
            },
            adoptSourceCapability: true);
        ContainerStatisticsReducer.Merge(
            retainedUnavailable,
            Supported(0),
            adoptSourceCapability: false);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
            retainedUnavailable.Capabilities.UniqueContainersLooted.State);
        Assert.Contains("First run", retainedUnavailable.Capabilities.UniqueContainersLooted.Provenance);
    }

    [Fact]
    [Trait("Category", "Container")]
    public void SchemaSixMigrationPreservesM1ToM6AndMarksHistoricalM7Unavailable()
    {
        var profile = new ProfileDocument
        {
            SchemaVersion = 6,
            GenerationId = "generation-1",
            Statistics = new ProfileStatistics
            {
                SchemaVersion = 6,
                SaveGenerationId = "generation-1",
                Overall = new AggregateTotals { ActivationCount = 9, ActualHealthRestored = 17 },
                RunTotals = new RunAggregateTotals
                {
                    TotalRuns = 3,
                    PhysicalDistance = 42,
                    WeaponStatistics = new WeaponStatisticsAggregate
                    {
                        Totals = new WeaponMetricTotals { FiringActions = 4 }
                    },
                    CombatStatistics = new CombatStatisticsAggregate
                    {
                        Totals = new CombatMetricTotals { DamageDealt = 23 }
                    },
                    EquipmentStatistics = new EquipmentStatisticsAggregate
                    {
                        ObservedActiveDurationSeconds = 11
                    }
                }
            }
        };

        Assert.True(ProfileMigrator.Migrate(profile));

        Assert.Equal(10, profile.SchemaVersion);
        Assert.Equal(9, profile.Statistics.Overall.ActivationCount);
        Assert.Equal(17, profile.Statistics.Overall.ActualHealthRestored);
        Assert.Equal(3, profile.Statistics.RunTotals.TotalRuns);
        Assert.Equal(42, profile.Statistics.RunTotals.PhysicalDistance);
        Assert.Equal(4, profile.Statistics.RunTotals.WeaponStatistics.Totals.FiringActions);
        Assert.Equal(23, profile.Statistics.RunTotals.CombatStatistics.Totals.DamageDealt);
        Assert.Equal(11, profile.Statistics.RunTotals.EquipmentStatistics.ObservedActiveDurationSeconds);
        Assert.Equal(0, profile.Statistics.RunTotals.ContainerStatistics.UniqueContainersLooted);
        Assert.True(profile.Statistics.RunTotals.ContainerStatistics.HistoricalUnavailable);
        Assert.Contains("predates M7",
            profile.Statistics.RunTotals.ContainerStatistics.Capabilities.UniqueContainersLooted.Provenance);
    }

    [Fact]
    [Trait("Category", "Container")]
    public void RepairedCurrentContainerRootRemainsUnavailableInUiJsonAndCsv()
    {
        var profile = Profile();
        profile.Statistics.RunTotals.ContainerStatistics = null!;
        Assert.True(ProfileMigrator.Migrate(profile));
        var lifetime = profile.Statistics.RunTotals.ContainerStatistics;
        Assert.True(lifetime.WasRepairedFromInvalidState);
        Assert.False(lifetime.HistoricalUnavailable);
        profile.Capabilities.Add(ContainerNativeContractPolicy.ToRecord(
            ContainerNativeContractPolicy.Supported(), "container/test"));

        var model = ContainerStatisticsViewModelFactory.Create(profile);
        var export = StatisticsExporter.Create(profile, Origin.AddMinutes(1));

        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, model.CurrentCapability);
        Assert.True(model.Lifetime.WasRepairedFromInvalidState);
        Assert.False(string.IsNullOrWhiteSpace(model.CapabilityDetail));
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
            export.Document.RunTotals.ContainerStatistics.Capabilities.UniqueContainersLooted.State);
        Assert.Contains("lifetime,generation-1,,0,DisabledIncompatible,false,true", export.ContainersCsv);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
            lifetime.Capabilities.UniqueContainersLooted.State);
    }

    [Fact]
    [Trait("Category", "Container")]
    public void LifetimeMapRunJsonAndCsvTotalsAgree()
    {
        var profile = Profile();
        var tracker = StartedTracker();
        tracker.RecordContainer(Event(tracker, 101, "first"));
        tracker.RecordContainer(Event(tracker, 202, "second"));
        var summary = tracker.Apply(Lifecycle(RunLifecycleEventKind.Extracted, 5)).Completed!;
        Assert.True(RunReducer.Apply(profile.Statistics, summary));
        profile.Capabilities.Add(ContainerNativeContractPolicy.ToRecord(
            ContainerNativeContractPolicy.Supported(), "container/test"));

        var export = StatisticsExporter.Create(profile, Origin.AddMinutes(1));

        Assert.Equal(2, profile.Statistics.RunTotals.ContainerStatistics.UniqueContainersLooted);
        Assert.Equal(2, profile.Statistics.RunTotals.Maps[summary.MapId].ContainerStatistics.UniqueContainersLooted);
        Assert.Equal(AdapterCapabilityState.Supported,
            profile.Statistics.RunTotals.ContainerStatistics.Capabilities.UniqueContainersLooted.State);
        Assert.Equal(AdapterCapabilityState.Supported,
            profile.Statistics.RunTotals.Maps[summary.MapId].ContainerStatistics.Capabilities.UniqueContainersLooted.State);
        Assert.Contains("\"UniqueContainersLooted\":2", export.Json, StringComparison.Ordinal);
        Assert.Contains("run," + summary.RunId + ",Warehouse,2,Supported", export.ContainersCsv, StringComparison.Ordinal);
        Assert.Contains(",2,Supported", export.RunsCsv, StringComparison.Ordinal);
    }

    [Fact]
    [Trait("Category", "Container")]
    public void RepeatedSubscriptionSetupAndCleanupDoNotDuplicateCallbacks()
    {
        var handlers = new List<Action>();
        var count = 0;
        Action callback = () => count++;
        var subscriptions = new IdempotentSubscriptionSet();
        var binding = new SubscriptionBinding(
            () => handlers.Add(callback),
            () => { handlers.Remove(callback); });

        Assert.True(subscriptions.Activate([binding]));
        Assert.False(subscriptions.Activate([binding]));
        Assert.Single(handlers)();
        subscriptions.Deactivate();
        Assert.Empty(handlers);
    }

    [Fact]
    [Trait("Category", "Container")]
    public void CleanupFailureRetainsRetryableOwnerAndBlocksUnsafeReplacement()
    {
        var owner = new ProcessLifetimeCleanupOwner<RetryingCleanup>();
        var cleanup = new RetryingCleanup();
        owner.Assign(cleanup);

        Assert.False(owner.TryCleanupOwned());
        Assert.True(owner.HasPendingCleanup);
        Assert.Throws<InvalidOperationException>(() => owner.Assign(new RetryingCleanup()));
        Assert.True(owner.TryCleanupPending());
        Assert.False(owner.HasValue);
    }

    private static RunLifecycleTracker StartedTracker()
    {
        var tracker = new RunLifecycleTracker(() => Guid.NewGuid().ToString("N"));
        tracker.Apply(Lifecycle(RunLifecycleEventKind.RaidInitialized, 0, nativeRaidId: "1"));
        tracker.Apply(Lifecycle(RunLifecycleEventKind.ControlReady, 0, Context(), "1"));
        return tracker;
    }

    private static RunStartContext Context() => new()
    {
        SaveGenerationId = "generation-1",
        NativeRaidId = "1",
        Map = new MapIdentity { MapId = "duckov:map:warehouse", DisplayName = "Warehouse", IsKnown = true },
        IntegrityTags = IntegrityTags.Normal,
        LifecycleCapability = AdapterCapabilityState.Supported,
        MovementCapability = AdapterCapabilityState.Supported,
        MapCapability = AdapterCapabilityState.Supported,
        ContainerCapabilities = ContainerNativeContractPolicy.Supported()
    };

    private static RunLifecycleEvent Lifecycle(
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

    private static ContainerLooted Event(RunLifecycleTracker tracker, int key, string id) => new()
    {
        EventId = id,
        TimestampUtc = Origin,
        SaveGenerationId = "generation-1",
        RunId = tracker.ActiveRunId!,
        MapId = tracker.ActiveMapId!,
        GameplayContext = GameplayContext.Raid,
        IntegrityTags = IntegrityTags.Normal,
        ContainerKey = key
    };

    private static ContainerLooted Event(int key, string id) => new()
    {
        EventId = id,
        TimestampUtc = Origin,
        SaveGenerationId = "generation-1",
        RunId = "run-1",
        MapId = "duckov:map:warehouse",
        GameplayContext = GameplayContext.Raid,
        ContainerKey = key
    };

    private static ContainerStatisticsAggregate Supported(long count) => new()
    {
        Capabilities = ContainerNativeContractPolicy.Supported(),
        UniqueContainersLooted = count
    };

    private static ProfileDocument Profile() => new()
    {
        GenerationId = "generation-1",
        Slot = 1,
        Statistics = new ProfileStatistics
        {
            SaveGenerationId = "generation-1",
            CreatedUtc = Origin,
            UpdatedUtc = Origin
        }
    };

    private sealed class RetryingCleanup : IRetryableCleanup
    {
        private int attempts;
        public bool TryCleanup() => ++attempts >= 2;
    }
}
