using System.Text.Json;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Export;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Tests;

public sealed class ActiveRunPersistenceTests
{
    private static long economySequence;
    private static readonly DateTime TestTime = new(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M8")]
    public void CurrentSchemaIncompleteRoutePrimaryLosesToValidBackup()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var generation = repository.CurrentGenerationId;
        var tracker = new RunLifecycleTracker(() => "route-run");
        tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.RaidInitialized,
            TimestampUtc = TestTime,
            MonotonicSeconds = 0,
            NativeRaidId = "42"
        });
        tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.ControlReady,
            TimestampUtc = TestTime,
            MonotonicSeconds = 0,
            StartContext = new RunStartContext
            {
                SaveGenerationId = generation,
                NativeRaidId = "42",
                Map = new MapIdentity { MapId = "duckov:map:A", DisplayName = "A", IsKnown = true },
                LifecycleCapability = AdapterCapabilityState.Supported,
                MovementCapability = AdapterCapabilityState.Supported,
                MapCapability = AdapterCapabilityState.Supported,
                EconomyCapabilities = SupportedEconomyCapabilities(),
                RouteCapabilities = RouteStatisticsReducer.Supported("test")
            }
        });
        var validBackup = tracker.CreateCheckpoint(TestTime.AddSeconds(5), 5)!;
        repository.SaveActiveRun(validBackup);
        repository.CloseClean();

        validBackup.ActiveDurationSeconds = 8;
        validBackup.LastObservedUtc = TestTime.AddSeconds(8);
        validBackup.Segments[0].ActiveDurationSeconds = 8;
        validBackup.RouteCapabilities.Segments = null!;
        new AtomicJsonStore<ActiveRunCheckpoint>().Save(ActiveRunPath(directory.Path), validBackup);

        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity()).InterruptedRunRecovered);
        var recovered = Assert.Single(recovery.Current.Statistics.Runs);
        Assert.Equal(5, recovered.ActiveDurationSeconds);
        Assert.Single(recovered.Segments);
        Assert.Equal(MapSegmentExitReason.Interrupted, recovered.Segments[0].ExitReason);
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M8")]
    public void MultiSegmentCheckpointRecoversExactlyOnceWithCompletedAndCurrentSegments()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var generation = repository.CurrentGenerationId;
        var tracker = new RunLifecycleTracker(() => "route-recovery-run");
        tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.RaidInitialized,
            TimestampUtc = TestTime,
            MonotonicSeconds = 0,
            NativeRaidId = "42"
        });
        tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.ControlReady,
            TimestampUtc = TestTime,
            MonotonicSeconds = 0,
            StartContext = new RunStartContext
            {
                SaveGenerationId = generation,
                NativeRaidId = "42",
                Map = new MapIdentity { MapId = "duckov:map:A", DisplayName = "A", IsKnown = true },
                LifecycleCapability = AdapterCapabilityState.Supported,
                MovementCapability = AdapterCapabilityState.Supported,
                MapCapability = AdapterCapabilityState.Supported,
                RouteCapabilities = RouteStatisticsReducer.Supported("test")
            }
        });
        tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.MapTransitionStarted,
            TimestampUtc = TestTime.AddSeconds(2),
            MonotonicSeconds = 2
        });
        tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.DestinationControlReady,
            TimestampUtc = TestTime.AddSeconds(5),
            MonotonicSeconds = 5,
            Map = new MapIdentity { MapId = "duckov:map:B", DisplayName = "B", IsKnown = true }
        });
        Assert.True(tracker.RecordItemUse(new ItemUseRecorded
        {
            EventId = "item-b",
            TimestampUtc = TestTime.AddSeconds(6),
            SaveGenerationId = generation,
            RunId = tracker.ActiveRunId,
            MapId = tracker.ActiveMapId!,
            SegmentId = tracker.ActiveSegmentId,
            GameplayContext = GameplayContext.Raid,
            ItemId = "duckov:item:test",
            DisplayName = "Test item",
            Group = CanonicalItemGroup.OtherUnknown,
            ActivationCount = 1,
            AmountConsumed = 1,
            ConsumptionUnit = ConsumptionUnit.Item
        }));
        repository.SaveActiveRun(tracker.CreateCheckpoint(TestTime.AddSeconds(8), 8)!);

        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity()).InterruptedRunRecovered);
        var run = Assert.Single(recovery.Current.Statistics.Runs);
        Assert.Equal("duckov:map:A>duckov:map:B", run.RouteSignature);
        Assert.Collection(
            run.Segments,
            segment => Assert.Equal(MapSegmentExitReason.Transition, segment.ExitReason),
            segment => Assert.Equal(MapSegmentExitReason.Interrupted, segment.ExitReason));
        Assert.Equal(5, run.ActiveDurationSeconds);
        Assert.Equal(1, run.ItemStatistics.Overall.ActivationCount);
        Assert.Equal(1, run.Segments[1].ItemStatistics.Overall.ActivationCount);
        Assert.Equal(2, recovery.Current.Statistics.RunTotals.RouteMaps["duckov:map:A"].ActiveDurationSeconds);
        Assert.Equal(3, recovery.Current.Statistics.RunTotals.RouteMaps["duckov:map:B"].ActiveDurationSeconds);
        recovery.CloseClean();

        var repeated = Repository(directory.Path);
        Assert.False(repeated.Open(Identity()).InterruptedRunRecovered);
        Assert.Single(repeated.Current.Statistics.Runs);
        Assert.Equal(1, repeated.Current.Statistics.RunTotals.ItemStatistics.Overall.ActivationCount);
        repeated.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Performance")]
    public void DeferredLifetimeRecoveryAppliesTheCheckpointDeltaAfterAnEmptyPersistedWatermark()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        repository.EnableDeferredItemPersistence();
        var tracker = ActiveTracker(repository.CurrentGenerationId);
        var (itemUse, healing) = ConsumableEvents(tracker, repository.CurrentGenerationId, 1, 12.5);

        Assert.True(repository.RecordDeferred(itemUse));
        Assert.True(repository.RecordDeferred(healing));
        Assert.True(tracker.RecordItemUse(itemUse));
        Assert.True(tracker.RecordHealing(healing));
        repository.SaveActiveRun(tracker.CreateCheckpoint(TestTime.AddSeconds(5), 5)!);

        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity()).InterruptedRunRecovered);
        Assert.Equal(1, recovery.Current.Statistics.Overall.ActivationCount);
        Assert.Equal(12.5, recovery.Current.Statistics.Overall.ActualHealthRestored, precision: 6);
        Assert.Equal(1, recovery.Current.Statistics.RunTotals.ItemStatistics.Overall.ActivationCount);
        Assert.Equal(12.5, recovery.Current.Statistics.RunTotals.ItemStatistics.Overall.ActualHealthRestored, precision: 6);
        Assert.Null(recovery.Current.DeferredItemPersistence!.RunId);
        recovery.CloseClean();

        var repeated = Repository(directory.Path);
        Assert.False(repeated.Open(Identity()).InterruptedRunRecovered);
        Assert.Equal(1, repeated.Current.Statistics.Overall.ActivationCount);
        Assert.Equal(12.5, repeated.Current.Statistics.Overall.ActualHealthRestored, precision: 6);
        repeated.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Performance")]
    public void DeferredLifetimeRecoveryAddsOnlyEventsNewerThanThePersistedWatermark()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        repository.EnableDeferredItemPersistence();
        var tracker = ActiveTracker(repository.CurrentGenerationId);
        var (firstUse, firstHealing) = ConsumableEvents(tracker, repository.CurrentGenerationId, 1, 10);
        Assert.True(repository.RecordDeferred(firstUse));
        Assert.True(repository.RecordDeferred(firstHealing));
        Assert.True(tracker.RecordItemUse(firstUse));
        Assert.True(tracker.RecordHealing(firstHealing));
        repository.SaveSnapshot(repository.CapturePersistenceSnapshot());

        var (secondUse, secondHealing) = ConsumableEvents(tracker, repository.CurrentGenerationId, 2, 15);
        Assert.True(repository.RecordDeferred(secondUse));
        Assert.True(repository.RecordDeferred(secondHealing));
        Assert.True(tracker.RecordItemUse(secondUse));
        Assert.True(tracker.RecordHealing(secondHealing));
        repository.SaveActiveRun(tracker.CreateCheckpoint(TestTime.AddSeconds(8), 8)!);

        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity()).InterruptedRunRecovered);
        Assert.Equal(2, recovery.Current.Statistics.Overall.ActivationCount);
        Assert.Equal(25, recovery.Current.Statistics.Overall.ActualHealthRestored, precision: 6);
        var item = Assert.Single(recovery.Current.Statistics.Items).Value;
        Assert.Equal(CanonicalItemGroup.Healing, item.Group);
        Assert.Equal(2, item.Totals.ActivationCount);
        Assert.Equal(25, item.Totals.ActualHealthRestored, precision: 6);
        Assert.Null(recovery.Current.DeferredItemPersistence!.RunId);

        var export = StatisticsExporter.Create(recovery.Current, TestTime.AddSeconds(9));
        using (var json = JsonDocument.Parse(export.Json))
        {
            var overall = json.RootElement.GetProperty("Overall");
            Assert.Equal(2, overall.GetProperty("ActivationCount").GetInt64());
            Assert.Equal(25, overall.GetProperty("ActualHealthRestored").GetDouble(), precision: 6);
        }
        AssertItemTotals(SingleCsvRow(export.OverviewCsv), "activation_count", "actual_hp_restored");
        AssertItemTotals(SingleCsvRow(export.GroupsCsv), "activation_count", "actual_hp_restored");
        AssertItemTotals(SingleCsvRow(export.ItemsCsv), "activation_count", "actual_hp_restored");
        AssertItemTotals(SingleCsvRow(export.MapTotalsCsv), "item_activations", "actual_health_restored");
        AssertItemTotals(SingleCsvRow(export.RouteMapTotalsCsv), "item_activations", "actual_health_restored");
        AssertItemTotals(SingleCsvRow(export.SegmentsCsv), "item_activations", "actual_health_restored");
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M9")]
    [Trait("Category", "Performance")]
    public void DeferredEconomyRecoveryAddsOnlyTheCheckpointDeltaExactlyOnce()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        repository.EnableDeferredItemPersistence();
        var generation = repository.CurrentGenerationId;
        var tracker = ActiveTracker(generation);
        var first = EconomyFlow("economy:first", tracker, generation, CurrencyFlowDirection.Inflow, 11);
        Assert.True(repository.RecordDeferred(first));
        Assert.True(tracker.RecordCurrencyFlow(first));
        Assert.False(repository.RecordDeferred(first));
        Assert.False(tracker.RecordCurrencyFlow(first));
        repository.SaveSnapshot(repository.CapturePersistenceSnapshot());
        var persistedProfile = new AtomicJsonStore<ProfileDocument>().Load(repository.CurrentProfilePath!).Value!;
        Assert.Equal(first.ProducerActivationId, persistedProfile.Statistics.Economy.ReplayCursor!.ActivationId);
        Assert.Equal(first.ProducerSequence, persistedProfile.Statistics.Economy.ReplayCursor.ClosedThroughSequence);

        var second = EconomyFlow("economy:second", tracker, generation, CurrencyFlowDirection.Outflow, 4);
        Assert.True(repository.RecordDeferred(second));
        Assert.True(tracker.RecordCurrencyFlow(second));
        repository.SaveActiveRun(tracker.CreateCheckpoint(TestTime.AddSeconds(8), 8)!);
        var persistedCheckpoint = new AtomicJsonStore<ActiveRunCheckpoint>().Load(ActiveRunPath(directory.Path)).Value!;
        Assert.Equal(second.ProducerActivationId, persistedCheckpoint.Economy.ReplayCursor!.ActivationId);
        Assert.Equal(second.ProducerSequence, persistedCheckpoint.Economy.ReplayCursor.ClosedThroughSequence);
        Assert.False(repository.RecordDeferred(second));
        Assert.False(tracker.RecordCurrencyFlow(second));

        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity()).InterruptedRunRecovered);
        var lifetime = recovery.Current.Statistics.Economy.Currencies["Money"].Totals;
        Assert.Equal(11, lifetime.GrossInflow);
        Assert.Equal(4, lifetime.GrossOutflow);
        var run = Assert.Single(recovery.Current.Statistics.Runs);
        Assert.Equal(11, run.Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.Equal(4, run.Economy.Currencies["Money"].Totals.GrossOutflow);
        Assert.Null(recovery.Current.DeferredItemPersistence!.RunId);
        recovery.CloseClean();

        var repeated = Repository(directory.Path);
        Assert.False(repeated.Open(Identity()).InterruptedRunRecovered);
        Assert.Equal(11, repeated.Current.Statistics.Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.Equal(4, repeated.Current.Statistics.Economy.Currencies["Money"].Totals.GrossOutflow);
        repeated.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M9")]
    public void LegacyIdentityEvidenceIsCompactedOnlyAfterItsCheckpointIsRecoveredAndDeleted()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        repository.EnableDeferredItemPersistence();
        var generation = repository.CurrentGenerationId;
        var profilePath = repository.CurrentProfilePath!;
        repository.CloseClean();

        var profileStore = new AtomicJsonStore<ProfileDocument>();
        var legacyProfile = profileStore.Load(profilePath).Value!;
        SetMoneyInflow(legacyProfile.Statistics.Economy, 5);
        legacyProfile.Statistics.Economy.RecentEventIds.Add("legacy:persisted");
        legacyProfile.Statistics.Economy.ReplayCursor = null;
        legacyProfile.DeferredItemPersistence!.RunId = "run-checkpoint";
        SetMoneyInflow(legacyProfile.DeferredItemPersistence.AppliedLifetimeEconomy, 5);
        legacyProfile.DeferredItemPersistence.AppliedLifetimeEconomy.RecentEventIds.Add("legacy:persisted");
        legacyProfile.DeferredItemPersistence.AppliedLifetimeEconomy.ReplayCursor = null;
        profileStore.Save(profilePath, legacyProfile);

        var checkpoint = Checkpoint(generation, 8);
        SetMoneyInflow(checkpoint.Economy, 12);
        checkpoint.Economy.RecentEventIds.AddRange(["legacy:persisted", "legacy:checkpoint-only"]);
        checkpoint.Economy.ReplayCursor = null;
        foreach (var segment in checkpoint.Segments)
            segment.Economy.ReplayCursor = null;
        var activeRunPath = ActiveRunPath(directory.Path);
        new AtomicJsonStore<ActiveRunCheckpoint>().Save(activeRunPath, checkpoint);

        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity()).InterruptedRunRecovered);
        Assert.Equal(12, recovery.Current.Statistics.Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.Equal(12, Assert.Single(recovery.Current.Statistics.Runs).Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.Empty(recovery.Current.Statistics.Economy.RecentEventIds);
        Assert.Empty(recovery.Current.DeferredItemPersistence!.AppliedLifetimeEconomy.RecentEventIds);
        Assert.False(File.Exists(activeRunPath));
        Assert.False(File.Exists(AtomicJsonPaths.GetBackupPath(activeRunPath)));
        Assert.False(File.Exists(AtomicJsonPaths.GetTemporaryPath(activeRunPath)));
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M9")]
    [Trait("Category", "Performance")]
    public void DeferredBaseEconomyMutationRequiresTheCoalescedSnapshotWriterToPersist()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        repository.EnableDeferredItemPersistence();
        var profilePath = repository.CurrentProfilePath!;
        var persistedBefore = File.ReadAllBytes(profilePath);
        var generation = repository.CurrentGenerationId;

        Assert.True(repository.RecordDeferred(new CurrencyFlowRecorded
        {
            EventId = "economy:base-deferred",
            TimestampUtc = TestTime,
            SaveGenerationId = generation,
            MapId = MapIdentity.UnknownId,
            Currency = CurrencyKind.Money,
            Direction = CurrencyFlowDirection.Outflow,
            Amount = 1,
            Source = CurrencySourceCategory.UnknownAdjustment,
            GameplayContext = GameplayContext.Base,
            ProducerActivationId = "test-active-run-persistence",
            ProducerSequence = Interlocked.Increment(ref economySequence)
        }));

        Assert.Equal(1, repository.Current.Statistics.Economy.Currencies["Money"].Totals.GrossOutflow);
        Assert.Equal(persistedBefore, File.ReadAllBytes(profilePath));

        repository.SaveSnapshot(repository.CapturePersistenceSnapshot());
        Assert.NotEqual(persistedBefore, File.ReadAllBytes(profilePath));
        repository.CloseClean();

        var reopened = Repository(directory.Path);
        reopened.Open(Identity());
        Assert.Equal(1, reopened.Current.Statistics.Economy.Currencies["Money"].Totals.GrossOutflow);
        reopened.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M9")]
    public void DeferredEconomyWatermarkRemainsExactBeyondTheLegacyIdentityLimit()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        repository.EnableDeferredItemPersistence();
        var generation = repository.CurrentGenerationId;
        var tracker = ActiveTracker(generation);
        CurrencyFlowRecorded? first = null;
        CurrencyFlowRecorded? last = null;
        for (var index = 0; index < 4096; index++)
        {
            last = EconomyFlow(
                $"economy:deferred:{index}",
                tracker,
                generation,
                CurrencyFlowDirection.Inflow,
                1);
            first ??= last;
            Assert.True(repository.RecordDeferred(last));
        }

        Assert.False(repository.RecordDeferred(first!));
        Assert.Equal(4096, repository.Current.Statistics.Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.Empty(repository.Current.Statistics.Economy.RecentEventIds);
        Assert.False(repository.Current.Statistics.Economy.DeduplicationSaturated);
        Assert.Equal(last!.ProducerActivationId, repository.Current.Statistics.Economy.ReplayCursor!.ActivationId);
        Assert.Equal(last.ProducerSequence, repository.Current.Statistics.Economy.ReplayCursor.ClosedThroughSequence);
        var watermark = repository.Current.DeferredItemPersistence!.AppliedLifetimeEconomy;
        Assert.Equal(4096, watermark.Currencies["Money"].Totals.GrossInflow);
        Assert.Empty(watermark.RecentEventIds);
        Assert.Equal(last.ProducerSequence, watermark.ReplayCursor!.ClosedThroughSequence);
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M9")]
    public void DeferredArithmeticSaturationPersistsWithoutAdvancingTheRecoveryWatermark()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        repository.EnableDeferredItemPersistence();
        var generation = repository.CurrentGenerationId;
        var tracker = ActiveTracker(generation);
        Assert.True(EconomyStatisticsReducer.Record(
            repository.Current.Statistics.Economy,
            generation,
            new CurrencyFlowRecorded
            {
                EventId = "economy:max",
                TimestampUtc = TestTime,
                SaveGenerationId = generation,
                MapId = "base",
                Currency = CurrencyKind.Money,
                Direction = CurrencyFlowDirection.Inflow,
                Amount = long.MaxValue,
                Source = CurrencySourceCategory.UnknownAdjustment,
                GameplayContext = GameplayContext.Base,
                ProducerActivationId = "test-active-run-persistence",
                ProducerSequence = Interlocked.Increment(ref economySequence)
            }));

        Assert.True(repository.RecordDeferred(EconomyFlow(
            "economy:overflow",
            tracker,
            generation,
            CurrencyFlowDirection.Inflow,
            1)));
        Assert.True(repository.Current.Statistics.Economy.MoneyArithmeticSaturated);
        Assert.Empty(repository.Current.DeferredItemPersistence!.AppliedLifetimeEconomy.RecentEventIds);
        repository.SaveSnapshot(repository.CapturePersistenceSnapshot());
        repository.CloseClean();

        var reopened = Repository(directory.Path);
        reopened.Open(Identity());
        Assert.True(reopened.Current.Statistics.Economy.MoneyArithmeticSaturated);
        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            reopened.Current.Statistics.Economy.Capabilities.MoneyAmountDirection.State);
        Assert.Equal(
            long.MaxValue,
            reopened.Current.Statistics.Economy.Currencies["Money"].Totals.GrossInflow);
        reopened.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M9")]
    public void DeferredArithmeticSaturationRecoversFromCheckpointWhenProfileStillHasTheExactMaximum()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        repository.EnableDeferredItemPersistence();
        var generation = repository.CurrentGenerationId;
        var tracker = ActiveTracker(generation);
        var maximum = EconomyFlow(
            "economy:checkpoint-maximum",
            tracker,
            generation,
            CurrencyFlowDirection.Inflow,
            1);
        maximum.Amount = long.MaxValue;
        Assert.True(repository.RecordDeferred(maximum));
        Assert.True(tracker.RecordCurrencyFlow(maximum));
        repository.SaveSnapshot(repository.CapturePersistenceSnapshot());

        var overflow = EconomyFlow(
            "economy:checkpoint-overflow",
            tracker,
            generation,
            CurrencyFlowDirection.Inflow,
            1);
        Assert.True(repository.RecordDeferred(overflow));
        Assert.True(tracker.RecordCurrencyFlow(overflow));
        repository.SaveActiveRun(tracker.CreateCheckpoint(TestTime.AddSeconds(8), 8)!);

        var persistedProfile = new AtomicJsonStore<ProfileDocument>().Load(repository.CurrentProfilePath!).Value!;
        Assert.False(persistedProfile.Statistics.Economy.MoneyArithmeticSaturated);
        Assert.Equal(
            long.MaxValue,
            persistedProfile.Statistics.Economy.Currencies[CurrencyKind.Money.ToString()].Totals.GrossInflow);
        var persistedCheckpoint = new AtomicJsonStore<ActiveRunCheckpoint>().Load(ActiveRunPath(directory.Path)).Value!;
        Assert.True(persistedCheckpoint.Economy.MoneyArithmeticSaturated);

        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity()).InterruptedRunRecovered);
        Assert.True(recovery.Current.Statistics.Economy.MoneyArithmeticSaturated);
        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            recovery.Current.Statistics.Economy.Capabilities.MoneyAmountDirection.State);
        Assert.Equal(
            long.MaxValue,
            recovery.Current.Statistics.Economy.Currencies[CurrencyKind.Money.ToString()].Totals.GrossInflow);
        var recoveredRun = Assert.Single(recovery.Current.Statistics.Runs);
        Assert.True(recoveredRun.Economy.MoneyArithmeticSaturated);
        recovery.CloseClean();

        var repeated = Repository(directory.Path);
        Assert.False(repeated.Open(Identity()).InterruptedRunRecovered);
        Assert.True(repeated.Current.Statistics.Economy.MoneyArithmeticSaturated);
        Assert.Equal(
            long.MaxValue,
            repeated.Current.Statistics.Economy.Currencies[CurrencyKind.Money.ToString()].Totals.GrossInflow);
        repeated.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M9")]
    public void RecoveryMergesRunOnlyEconomyExactlyBeyondTheLegacyIdentityLimit()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        repository.EnableDeferredItemPersistence();
        var generation = repository.CurrentGenerationId;
        for (var index = 0; index < 4096; index++)
        {
            Assert.True(EconomyStatisticsReducer.Record(
                repository.Current.Statistics.Economy,
                generation,
                new CurrencyFlowRecorded
                {
                    EventId = $"economy:saturated:{index}",
                    TimestampUtc = TestTime,
                    SaveGenerationId = generation,
                    MapId = "base",
                    Currency = CurrencyKind.Money,
                    Direction = CurrencyFlowDirection.Inflow,
                    Amount = 1,
                    Source = CurrencySourceCategory.UnknownAdjustment,
                    GameplayContext = GameplayContext.Base,
                    ProducerActivationId = "test-active-run-persistence",
                    ProducerSequence = Interlocked.Increment(ref economySequence)
                }));
        }
        repository.SaveSnapshot(repository.CapturePersistenceSnapshot());

        var tracker = ActiveTracker(generation);
        var runOnly = EconomyFlow(
            "economy:run-only",
            tracker,
            generation,
            CurrencyFlowDirection.Inflow,
            7);
        Assert.True(tracker.RecordCurrencyFlow(runOnly));
        repository.SaveActiveRun(tracker.CreateCheckpoint(TestTime.AddSeconds(8), 8)!);

        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity()).InterruptedRunRecovered);
        Assert.Equal(4103, recovery.Current.Statistics.Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.Empty(recovery.Current.Statistics.Economy.RecentEventIds);
        Assert.Equal(
            7,
            Assert.Single(recovery.Current.Statistics.Runs).Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.False(recovery.Current.Statistics.Economy.DeduplicationSaturated);
        recovery.CloseClean();

        var repeated = Repository(directory.Path);
        Assert.False(repeated.Open(Identity()).InterruptedRunRecovered);
        Assert.Equal(4103, repeated.Current.Statistics.Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.Single(repeated.Current.Statistics.Runs);
        repeated.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M9")]
    public void StaleCheckpointAfterFinalizedRunCannotReapplyLifetimeEconomy()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        repository.EnableDeferredItemPersistence();
        var generation = repository.CurrentGenerationId;
        var tracker = ActiveTracker(generation);
        var flow = EconomyFlow("economy:finalized", tracker, generation, CurrencyFlowDirection.Inflow, 9);
        Assert.True(repository.RecordDeferred(flow));
        Assert.True(tracker.RecordCurrencyFlow(flow));
        var staleCheckpoint = tracker.CreateCheckpoint(TestTime.AddSeconds(5), 5)!;
        var summary = tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.Extracted,
            TimestampUtc = TestTime.AddSeconds(6),
            MonotonicSeconds = 6
        }).Completed!;
        Assert.True(repository.CompleteRun(summary));
        repository.CloseClean();
        new AtomicJsonStore<ActiveRunCheckpoint>().Save(ActiveRunPath(directory.Path), staleCheckpoint);

        var recovery = Repository(directory.Path);
        Assert.False(recovery.Open(Identity()).InterruptedRunRecovered);
        Assert.Single(recovery.Current.Statistics.Runs);
        Assert.Equal(9, recovery.Current.Statistics.Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.Equal(9, recovery.Current.Statistics.RunTotals.Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.False(File.Exists(ActiveRunPath(directory.Path)));
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M9")]
    public void TerminalBoundaryCheckpointsQueuedEconomyBeforeTheRunBecomesInactive()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        repository.EnableDeferredItemPersistence();
        var generation = repository.CurrentGenerationId;
        var tracker = ActiveTracker(generation);
        repository.SaveActiveRun(tracker.CreateCheckpoint(TestTime.AddSeconds(1), 1)!);
        var boundary = new NativeRunTerminalBoundary();
        var observerCalls = 0;
        boundary.SetTerminalObserver(() =>
        {
            observerCalls++;
            if (observerCalls != 1) return true;
            var flow = EconomyFlow(
                "economy:terminal-boundary",
                tracker,
                generation,
                CurrencyFlowDirection.Inflow,
                17);
            Assert.True(repository.RecordDeferred(flow));
            Assert.True(tracker.RecordCurrencyFlow(flow));
            return true;
        });

        var lifecycleEvent =
            new RunLifecycleEvent
            {
                Kind = RunLifecycleEventKind.RaidInitialized,
                TimestampUtc = TestTime.AddSeconds(3),
                MonotonicSeconds = 3,
                NativeRaidId = "43"
            };
        var blocked = boundary.Apply(
            tracker,
            lifecycleEvent,
            _ => { },
            () => false);

        Assert.Null(blocked.Completed);
        Assert.True(tracker.IsActive);
        Assert.True(boundary.HasPendingTerminal);

        var transition = boundary.Retry(
            tracker,
            _ => { },
            pendingEvent =>
            {
                Assert.Same(lifecycleEvent, pendingEvent);
                repository.SaveActiveRun(tracker.CreateCheckpoint(TestTime.AddSeconds(3), 3)!);
                return true;
            });

        Assert.NotNull(transition.Completed);
        Assert.False(tracker.IsActive);
        Assert.Equal(2, observerCalls);
        Assert.Equal(
            17,
            transition.Completed!.Economy.Currencies[CurrencyKind.Money.ToString()].Totals.GrossInflow);

        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity()).InterruptedRunRecovered);
        Assert.Equal(
            17,
            recovery.Current.Statistics.Economy.Currencies[CurrencyKind.Money.ToString()].Totals.GrossInflow);
        var recoveredRun = Assert.Single(recovery.Current.Statistics.Runs);
        Assert.Equal(17, recoveredRun.Economy.Currencies[CurrencyKind.Money.ToString()].Totals.GrossInflow);
        Assert.Equal(
            17,
            Assert.Single(recoveredRun.Segments).Economy.Currencies[CurrencyKind.Money.ToString()].Totals.GrossInflow);
        Assert.Equal(
            17,
            recovery.Current.Statistics.RunTotals.Economy.Currencies[CurrencyKind.Money.ToString()].Totals.GrossInflow);
        Assert.Equal(
            17,
            recovery.Current.Statistics.RunTotals.Maps["duckov:map:A"].Economy
                .Currencies[CurrencyKind.Money.ToString()].Totals.GrossInflow);
        Assert.Equal(
            17,
            recovery.Current.Statistics.RunTotals.RouteMaps["duckov:map:A"].Economy
                .Currencies[CurrencyKind.Money.ToString()].Totals.GrossInflow);
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M9")]
    public void TerminalCheckpointRecoversExactOutcomeWhenCompletedRunSaveNeverLands()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        repository.EnableDeferredItemPersistence();
        var generation = repository.CurrentGenerationId;
        var tracker = ActiveTracker(generation);
        var cash = new CurrencyFlowRecorded
        {
            EventId = "cash:terminal-recovery",
            TimestampUtc = TestTime.AddSeconds(2),
            SaveGenerationId = generation,
            RunId = tracker.ActiveRunId,
            SegmentId = tracker.ActiveSegmentId,
            MapId = tracker.ActiveMapId!,
            Currency = CurrencyKind.Cash,
            Direction = CurrencyFlowDirection.Inflow,
            Amount = 5,
            Source = CurrencySourceCategory.LootOrPickup,
            GameplayContext = GameplayContext.Raid,
            IntegrityTags = IntegrityTags.Normal,
            AdapterVersion = "test",
            ProvenExternalRaidAcquisition = true,
            ProducerActivationId = "test-terminal-recovery",
            ProducerSequence = 1
        };
        Assert.True(repository.RecordDeferred(cash));
        Assert.True(tracker.RecordCurrencyFlow(cash));
        var checkpoint = tracker.CreateCheckpoint(TestTime.AddSeconds(3), 3)!;
        checkpoint.PendingTerminalOutcome = RunOutcome.Extracted;
        repository.SaveActiveRun(checkpoint);

        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity()).InterruptedRunRecovered);
        var recovered = Assert.Single(recovery.Current.Statistics.Runs);
        Assert.Equal(RunOutcome.Extracted, recovered.Outcome);
        Assert.Equal(IntegrityTags.Normal, recovered.IntegrityTags);
        Assert.Equal(AdapterCapabilityState.Supported, recovered.LifecycleCapability);
        Assert.True(recovered.RecordEligible);
        Assert.Equal(MapSegmentExitReason.Extracted, Assert.Single(recovered.Segments).ExitReason);
        Assert.Equal(5, recovered.Economy.CashRaidOutcomes.Acquired);
        Assert.Equal(5, recovered.Economy.CashRaidOutcomes.Secured);
        Assert.Equal(0, recovered.Economy.CashRaidOutcomes.Unresolved);
        Assert.Equal(1, recovery.Current.Statistics.RunTotals.Outcomes[nameof(RunOutcome.Extracted)]);
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Performance")]
    public void ImmediatePersistenceProfileWithoutWatermarkKeepsLegacyRecoverySemantics()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var tracker = ActiveTracker(repository.CurrentGenerationId);
        var (itemUse, healing) = ConsumableEvents(tracker, repository.CurrentGenerationId, 1, 9);
        Assert.True(repository.Record(itemUse));
        Assert.True(repository.Record(healing));
        Assert.True(tracker.RecordItemUse(itemUse));
        Assert.True(tracker.RecordHealing(healing));
        repository.SaveActiveRun(tracker.CreateCheckpoint(TestTime.AddSeconds(5), 5)!);

        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity()).InterruptedRunRecovered);
        Assert.Equal(1, recovery.Current.Statistics.Overall.ActivationCount);
        Assert.Equal(9, recovery.Current.Statistics.Overall.ActualHealthRestored, precision: 6);
        Assert.Null(recovery.Current.DeferredItemPersistence);
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M8")]
    public void CurrentSchemaInvalidNestedSegmentAggregateLosesToValidBackup()
    {
        AssertCurrentSchemaRoutePrimaryRejected(checkpoint =>
            checkpoint.Segments[0].ItemStatistics.Overall.ActivationCount = -1);
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M8")]
    public void CurrentSchemaInvalidTopLevelItemAggregateLosesToValidBackup()
    {
        AssertCurrentSchemaRoutePrimaryRejected(checkpoint =>
            checkpoint.ItemStatistics.Overall.ActivationCount = -1);
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M8")]
    public void CurrentSchemaInconsistentTopLevelItemGroupsLoseToValidBackup()
    {
        AssertCurrentSchemaRoutePrimaryRejected(checkpoint =>
            checkpoint.ItemStatistics.Groups[nameof(CanonicalItemGroup.Healing)] =
                new AggregateTotals { ActivationCount = 1 });
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M8")]
    public void CurrentSchemaUnavailableRouteStillValidatesRetainedSegmentsBeforeCandidateSelection()
    {
        AssertCurrentSchemaRoutePrimaryRejected(checkpoint =>
        {
            checkpoint.RouteCapabilities = RouteStatisticsReducer.Unavailable("injected route failure");
            checkpoint.CurrentSegmentId = null;
            checkpoint.Segments[0].ExitReason = MapSegmentExitReason.Interrupted;
            checkpoint.Segments[0].ExitedUtc = checkpoint.LastObservedUtc;
            checkpoint.Segments[0].ItemStatistics.Overall.ActivationCount = -1;
        });
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M8")]
    public void CurrentSchemaUnavailableRouteCannotRetainAnActiveSegmentPointer()
    {
        AssertCurrentSchemaRoutePrimaryRejected(checkpoint =>
            checkpoint.RouteCapabilities = RouteStatisticsReducer.Unavailable("injected route failure"));
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M8")]
    public void CurrentSchemaUnjoinableSegmentAssociationLosesToValidBackup()
    {
        AssertCurrentSchemaRoutePrimaryRejected(checkpoint =>
            checkpoint.SegmentEventAssociations.Add(new SegmentEventAssociation
            {
                EventId = "invalid-association",
                EventKind = "combat",
                TimestampUtc = TestTime.AddSeconds(7),
                FirstTimestampUtc = TestTime.AddSeconds(7),
                LastTimestampUtc = TestTime.AddSeconds(7),
                Count = 1,
                SourceSegmentId = checkpoint.Segments[0].SegmentId,
                SourceMapId = "duckov:map:not-A"
            }));
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M8")]
    public void CurrentSchemaOneSidedSegmentAssociationLosesToValidBackup()
    {
        AssertCurrentSchemaRoutePrimaryRejected(checkpoint =>
            checkpoint.SegmentEventAssociations.Add(new SegmentEventAssociation
            {
                EventId = "one-sided-association",
                EventKind = "combat",
                TimestampUtc = TestTime.AddSeconds(7),
                FirstTimestampUtc = TestTime.AddSeconds(7),
                LastTimestampUtc = TestTime.AddSeconds(7),
                Count = 1,
                OutcomeSegmentId = checkpoint.Segments[0].SegmentId,
                OutcomeMapId = checkpoint.Segments[0].MapId
            }));
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M8")]
    public void CurrentSchemaMissingCurrentSegmentIdentityLosesToValidBackup()
    {
        AssertCurrentSchemaRoutePrimaryRejected(checkpoint => checkpoint.CurrentSegmentId = null);
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M8")]
    public void CurrentSchemaMismatchedStartingMapLosesToValidBackup()
    {
        AssertCurrentSchemaRoutePrimaryRejected(checkpoint => checkpoint.StartingMapId = "duckov:map:not-A");
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M8")]
    public void CurrentSchemaPendingTransitionWithCurrentSegmentLosesToValidBackup()
    {
        AssertCurrentSchemaRoutePrimaryRejected(checkpoint =>
        {
            checkpoint.TransitionPending = true;
            checkpoint.Segments[0].ExitReason = MapSegmentExitReason.Transition;
            checkpoint.Segments[0].ExitedUtc = checkpoint.LastObservedUtc;
        });
    }

    [Fact]
    [Trait("Category", "M10")]
    [Trait("Category", "Persistence")]
    public void CurrentSchemaInvalidAggregateAssociationLosesToValidBackup()
    {
        AssertCurrentSchemaRoutePrimaryRejected(checkpoint =>
        {
            var segment = checkpoint.Segments[0];
            checkpoint.SegmentEventAssociations.Add(new SegmentEventAssociation
            {
                EventKind = "item-use",
                TimestampUtc = TestTime.AddSeconds(7),
                FirstTimestampUtc = TestTime.AddSeconds(7),
                LastTimestampUtc = TestTime.AddSeconds(7),
                SourceSegmentId = segment.SegmentId,
                SourceMapId = segment.MapId,
                OutcomeSegmentId = segment.SegmentId,
                OutcomeMapId = segment.MapId,
                Representation = SegmentEventAssociationRepresentation.ExactAggregate,
                Count = 0
            });
        });
    }

    [Fact]
    [Trait("Category", "M10")]
    [Trait("Category", "Persistence")]
    public void SchemaNineSaturatedActiveCheckpointRecoversExactRowsWithIncompleteProvenance()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var generation = repository.CurrentGenerationId;
        repository.CloseClean();
        var checkpoint = RouteCheckpoint(generation, 5);
        checkpoint.SchemaVersion = 9;
        checkpoint.RouteCapabilities.CurrentEventAttributionCapture = null!;
        RouteStatisticsReducer.DisableAttribution(
            checkpoint.RouteCapabilities,
            "The defensive 2048-event association bound was reached.");
        var segment = checkpoint.Segments[0];
        for (var index = 0; index < RouteStatisticsReducer.LegacyMaximumRawEventAssociationsPerRun; index++)
            checkpoint.SegmentEventAssociations.Add(LegacyRouteAssociation($"legacy-checkpoint-{index}", segment));
        new AtomicJsonStore<ActiveRunCheckpoint>().Save(ActiveRunPath(directory.Path), checkpoint);

        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity()).InterruptedRunRecovered);
        var run = Assert.Single(recovery.Current.Statistics.Runs);
        Assert.Equal(RouteStatisticsReducer.LegacyMaximumRawEventAssociationsPerRun, run.SegmentEventAssociations.Count);
        Assert.All(run.SegmentEventAssociations, association =>
        {
            Assert.Equal(SegmentEventAssociationRepresentation.LegacyRaw, association.Representation);
            Assert.Equal(1, association.Count);
        });
        Assert.True(run.HistoricalEventAttributionIncomplete);
        Assert.Contains("2,048", run.HistoricalEventAttributionProvenance, StringComparison.Ordinal);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, run.RouteCapabilities.EventAttribution.State);
        Assert.Equal(AdapterCapabilityState.Supported, run.RouteCapabilities.CurrentEventAttributionCapture.State);
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "M10")]
    [Trait("Category", "Persistence")]
    public void SchemaTenAggregateCheckpointSurvivesDurableRestartWithoutRawGrowth()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var generation = repository.CurrentGenerationId;
        var tracker = ActiveTracker(generation);
        for (var index = 0; index < 2049; index++)
        {
            var events = ConsumableEvents(tracker, generation, index + 1, 1);
            Assert.True(tracker.RecordItemUse(events.ItemUse));
        }
        repository.SaveActiveRun(tracker.CreateCheckpoint(TestTime.AddSeconds(3000), 3000)!);
        repository.CloseClean();

        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity()).InterruptedRunRecovered);
        var run = Assert.Single(recovery.Current.Statistics.Runs);
        var association = Assert.Single(run.SegmentEventAssociations);
        Assert.Equal(SegmentEventAssociationRepresentation.ExactAggregate, association.Representation);
        Assert.Equal(2049, association.Count);
        Assert.Equal(2049, run.ItemStatistics.Overall.ActivationCount);
        Assert.Equal(AdapterCapabilityState.Supported, run.RouteCapabilities.EventAttribution.State);
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M8")]
    public void CurrentSchemaInconsistentRouteCapabilitiesLoseToValidBackup()
    {
        AssertCurrentSchemaRoutePrimaryRejected(checkpoint =>
            checkpoint.RouteCapabilities.EventAttribution.State = AdapterCapabilityState.DisabledIncompatible);
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Container")]
    public void ContainerStableKeysAndTotalAreRecoveredFromCrashCheckpoint()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var checkpoint = Checkpoint(repository.CurrentGenerationId, 4);
        checkpoint.ContainerState = ContainerCheckpoint(11, 22);
        repository.SaveActiveRun(checkpoint);

        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity()).InterruptedRunRecovered);
        var run = Assert.Single(recovery.Current.Statistics.Runs);
        Assert.Equal(2, run.ContainerStatistics.UniqueContainersLooted);
        Assert.Equal(2, recovery.Current.Statistics.RunTotals.ContainerStatistics.UniqueContainersLooted);
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Container")]
    public void ContainerCheckpointMismatchRejectsPrimaryAndRecoversValidBackup()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var generation = repository.CurrentGenerationId;
        var backup = Checkpoint(generation, 5);
        backup.ContainerState = ContainerCheckpoint(11);
        repository.SaveActiveRun(backup);
        repository.CloseClean();

        var primary = Checkpoint(generation, 8);
        primary.ContainerState = ContainerCheckpoint(11, 22);
        primary.ContainerState.Statistics.UniqueContainersLooted = 1;
        new AtomicJsonStore<ActiveRunCheckpoint>().Save(ActiveRunPath(directory.Path), primary);

        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity()).InterruptedRunRecovered);
        var run = Assert.Single(recovery.Current.Statistics.Runs);
        Assert.Equal(5, run.ActiveDurationSeconds);
        Assert.Equal(1, run.ContainerStatistics.UniqueContainersLooted);
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Container")]
    public void CurrentSchemaContainerRootMissingFromPrimaryRecoversValidBackup()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var generation = repository.CurrentGenerationId;
        var backup = Checkpoint(generation, 5);
        backup.ContainerState = ContainerCheckpoint(11);
        repository.SaveActiveRun(backup);
        repository.CloseClean();

        var primary = Checkpoint(generation, 8);
        primary.ContainerState = null!;
        new AtomicJsonStore<ActiveRunCheckpoint>().Save(ActiveRunPath(directory.Path), primary);

        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity()).InterruptedRunRecovered);
        var run = Assert.Single(recovery.Current.Statistics.Runs);
        Assert.Equal(5, run.ActiveDurationSeconds);
        Assert.Equal(1, run.ContainerStatistics.UniqueContainersLooted);
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M9")]
    public void ProfileNewerThanAStaleActiveCheckpointNeverReappliesLifetimeEconomy()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        repository.EnableDeferredItemPersistence();
        var generation = repository.CurrentGenerationId;
        var tracker = ActiveTracker(generation);
        var checkpointed = EconomyFlow("economy:checkpointed", tracker, generation, CurrencyFlowDirection.Inflow, 5);
        Assert.True(repository.RecordDeferred(checkpointed));
        Assert.True(tracker.RecordCurrencyFlow(checkpointed));
        repository.SaveActiveRun(tracker.CreateCheckpoint(TestTime.AddSeconds(5), 5)!);

        var profileOnly = EconomyFlow("economy:profile-newer", tracker, generation, CurrencyFlowDirection.Inflow, 7);
        Assert.True(repository.RecordDeferred(profileOnly));
        Assert.True(tracker.RecordCurrencyFlow(profileOnly));
        repository.SaveSnapshot(repository.CapturePersistenceSnapshot());

        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity()).InterruptedRunRecovered);
        Assert.Equal(12, recovery.Current.Statistics.Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.Equal(5, Assert.Single(recovery.Current.Statistics.Runs).Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.Null(recovery.Current.DeferredItemPersistence!.RunId);
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M9")]
    public void CurrentSchemaMissingEconomyRootInPrimaryRecoversValidBackup()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var generation = repository.CurrentGenerationId;
        var backup = Checkpoint(generation, 5);
        EconomyStatisticsReducer.Record(
            backup.Economy,
            generation,
            new CurrencyFlowRecorded
            {
                EventId = "economy:backup",
                TimestampUtc = TestTime,
                SaveGenerationId = generation,
                RunId = backup.RunId,
                MapId = backup.MapId,
                Currency = CurrencyKind.Money,
                Direction = CurrencyFlowDirection.Inflow,
                Amount = 7,
                Source = CurrencySourceCategory.UnknownAdjustment,
                GameplayContext = GameplayContext.Raid,
                ProducerActivationId = "test-active-run-persistence",
                ProducerSequence = Interlocked.Increment(ref economySequence)
            });
        repository.SaveActiveRun(backup);
        repository.CloseClean();

        var primary = Checkpoint(generation, 8);
        primary.Economy = null!;
        new AtomicJsonStore<ActiveRunCheckpoint>().Save(ActiveRunPath(directory.Path), primary);

        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity()).InterruptedRunRecovered);
        var run = Assert.Single(recovery.Current.Statistics.Runs);
        Assert.Equal(5, run.ActiveDurationSeconds);
        Assert.Equal(7, run.Economy.Currencies["Money"].Totals.GrossInflow);
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M9")]
    public void ValidTemporaryCheckpointDefeatsMalformedReplayMetadataInPrimaryAndBackup()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var generation = repository.CurrentGenerationId;
        repository.CloseClean();
        var path = ActiveRunPath(directory.Path);
        var store = new AtomicJsonStore<ActiveRunCheckpoint>();
        var malformedBackup = Checkpoint(generation, 5);
        malformedBackup.Economy.ReplayCursor = new EconomyReplayCursor
        {
            ActivationId = "invalid:backup",
            ClosedThroughSequence = 1
        };
        var malformedPrimary = Checkpoint(generation, 8);
        malformedPrimary.Economy.ReplayCursor = new EconomyReplayCursor
        {
            ActivationId = "invalid-primary",
            ClosedThroughSequence = -1
        };
        var validTemporary = Checkpoint(generation, 9);
        SetMoneyInflow(validTemporary.Economy, 9);
        store.Save(path, malformedBackup);
        store.Save(path, malformedPrimary);
        store.Save(AtomicJsonPaths.GetTemporaryPath(path), validTemporary);

        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity()).InterruptedRunRecovered);
        var run = Assert.Single(recovery.Current.Statistics.Runs);
        Assert.Equal(9, run.ActiveDurationSeconds);
        Assert.Equal(9, run.Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(AtomicJsonPaths.GetBackupPath(path)));
        Assert.False(File.Exists(AtomicJsonPaths.GetTemporaryPath(path)));
        recovery.CloseClean();
    }

    [Theory]
    [InlineData(true, false, false, false)]
    [InlineData(false, true, false, false)]
    [InlineData(true, true, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, false, false, true)]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Container")]
    public void CurrentSchemaContainerNestedRootsMissingFromPrimaryRecoversValidBackup(
        bool missingStatistics,
        bool missingStableKeys,
        bool missingCapabilities,
        bool missingAvailability)
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var generation = repository.CurrentGenerationId;
        var backup = Checkpoint(generation, 5);
        backup.ContainerState = ContainerCheckpoint(11, 22);
        repository.SaveActiveRun(backup);
        repository.CloseClean();

        var primary = Checkpoint(generation, 8);
        var primaryStatistics = ContainerCheckpoint().Statistics;
        if (missingCapabilities)
            primaryStatistics.Capabilities = null!;
        else if (missingAvailability)
            primaryStatistics.Capabilities.UniqueContainersLooted = null!;
        primary.ContainerState = new ContainerRunCheckpointState
        {
            Statistics = missingStatistics ? null! : primaryStatistics,
            LootedContainerKeys = missingStableKeys ? null! : new List<int>()
        };
        new AtomicJsonStore<ActiveRunCheckpoint>().Save(ActiveRunPath(directory.Path), primary);

        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity()).InterruptedRunRecovered);
        var run = Assert.Single(recovery.Current.Statistics.Runs);
        Assert.Equal(5, run.ActiveDurationSeconds);
        Assert.Equal(2, run.ContainerStatistics.UniqueContainersLooted);
        Assert.Equal(AdapterCapabilityState.Supported,
            run.ContainerStatistics.Capabilities.UniqueContainersLooted.State);
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Equipment")]
    public void EquipmentStateAndActiveTimeAreRecoveredFromCrashCheckpoint()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var generation = repository.CurrentGenerationId;
        var tracker = new RunLifecycleTracker(() => "run-equipment-recovery");
        tracker.Apply(LifecycleEvent(RunLifecycleEventKind.RaidInitialized, generation, 0));
        tracker.Apply(LifecycleEvent(RunLifecycleEventKind.ControlReady, generation, 0));
        Assert.True(tracker.ObserveEquipment(new EquipmentSnapshot
        {
            SnapshotId = "snapshot:a",
            LoadoutId = "loadout:a",
            SelectedWeaponId = "weapon:a",
            SelectedWeaponSlotId = "slot:primary",
            TotemSetId = "totems:a",
            Items = new List<EquippedItemSnapshot>
            { new() { SlotId = "slot:primary", ItemId = "weapon:a", ItemDisplayName = "Rifle", Kind = EquipmentItemKind.Weapon, AttachmentSignature = "attachments:a" } }
        }));
        repository.SaveActiveRun(tracker.CreateCheckpoint(TestTime.AddSeconds(4), 4)!);

        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity()).InterruptedRunRecovered);
        var run = Assert.Single(recovery.Current.Statistics.Runs);
        Assert.Equal(4, run.EquipmentStatistics.Loadouts["loadout:a"].ActiveDurationSeconds);
        Assert.Equal("weapon:a", run.EquipmentStatistics.CurrentSnapshot!.SelectedWeaponId);
        Assert.Equal(4, recovery.Current.Statistics.RunTotals.EquipmentStatistics.Loadouts["loadout:a"].ActiveDurationSeconds);
        Assert.Equal(0, recovery.Current.Statistics.RunTotals.EquipmentStatistics.Loadouts["loadout:a"].RunOccurrences);
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Run")]
    [Trait("Category", "Weapon")]
    public void AcceptedShotIsRecoveredFromTheCheckpointCreatedAfterTheProductionMutationSequence()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var generation = repository.CurrentGenerationId;
        var tracker = new UltimateDuckovStatistics.Core.Tracking.RunLifecycleTracker(() => "run-live-shot");
        tracker.Apply(LifecycleEvent(RunLifecycleEventKind.RaidInitialized, generation, 0));
        tracker.Apply(LifecycleEvent(RunLifecycleEventKind.ControlReady, generation, 0));
        Assert.True(tracker.RecordShot(LiveShot(generation)));
        Assert.True(tracker.CombatCheckpointRequired);
        repository.SaveActiveRun(tracker.CreateCheckpoint(TestTime.AddSeconds(1), 1)!);
        tracker.MarkCheckpointSaved(1);
        Assert.False(tracker.CombatCheckpointRequired);

        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity()).InterruptedRunRecovered);

        var run = Assert.Single(recovery.Current.Statistics.Runs);
        Assert.Equal("run-live-shot", run.RunId);
        Assert.Equal(1, run.WeaponStatistics.Totals.FiringActions);
        Assert.Equal(1, recovery.Current.Statistics.RunTotals.WeaponStatistics.Totals.FiringActions);
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Run")]
    [Trait("Category", "Weapon")]
    public void PartiallyPopulatedWeaponCheckpointIsNormalizedBeforeInterruptedRecovery()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var checkpoint = Checkpoint(repository.CurrentGenerationId, 3);
        checkpoint.WeaponStatistics.Totals = null!;
        checkpoint.WeaponStatistics.Weapons = null!;
        checkpoint.WeaponStatistics.AmmunitionTypes = null!;
        checkpoint.WeaponStatistics.Capabilities = null!;
        repository.CloseClean();
        new AtomicJsonStore<ActiveRunCheckpoint>().Save(ActiveRunPath(directory.Path), checkpoint);

        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity()).InterruptedRunRecovered);

        var recovered = Assert.Single(recovery.Current.Statistics.Runs);
        Assert.NotNull(recovered.WeaponStatistics.Totals);
        Assert.NotNull(recovered.WeaponStatistics.Capabilities.FiringActions);
        Assert.Empty(recovered.WeaponStatistics.Weapons);
        Assert.Empty(recovered.WeaponStatistics.AmmunitionTypes);
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Run")]
    [Trait("Category", "Weapon")]
    public void NullCheckpointAvailabilityMembersAreNormalizedBeforeInterruptedRecovery()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var checkpoint = Checkpoint(repository.CurrentGenerationId, 3);
        checkpoint.WeaponStatistics.Capabilities = new WeaponMetricCapabilities
        {
            FiringActions = null!,
            AmmunitionConsumption = null!,
            Projectiles = null!,
            WeaponIdentity = null!,
            AmmunitionIdentity = null!
        };
        repository.CloseClean();
        new AtomicJsonStore<ActiveRunCheckpoint>().Save(ActiveRunPath(directory.Path), checkpoint);

        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity()).InterruptedRunRecovered);

        var recovered = Assert.Single(recovery.Current.Statistics.Runs);
        Assert.NotNull(recovered.WeaponStatistics.Capabilities.FiringActions);
        Assert.NotNull(recovered.WeaponStatistics.Capabilities.AmmunitionConsumption);
        Assert.NotNull(recovered.WeaponStatistics.Capabilities.Projectiles);
        Assert.NotNull(recovered.WeaponStatistics.Capabilities.WeaponIdentity);
        Assert.NotNull(recovered.WeaponStatistics.Capabilities.AmmunitionIdentity);
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Run")]
    [Trait("Category", "Weapon")]
    public void NegativeWeaponCheckpointIsArchivedReadOnlyWithoutAbortingProfileOpen()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var checkpoint = Checkpoint(repository.CurrentGenerationId, 3);
        checkpoint.WeaponStatistics.Totals.FiringActions = -1;
        repository.CloseClean();
        new AtomicJsonStore<ActiveRunCheckpoint>().Save(ActiveRunPath(directory.Path), checkpoint);

        var recovery = Repository(directory.Path);
        var result = recovery.Open(Identity());

        Assert.False(result.InterruptedRunRecovered);
        Assert.Empty(recovery.Current.Statistics.Runs);
        var preserved = Directory.GetFiles(
            Path.Combine(Path.GetDirectoryName(ActiveRunPath(directory.Path))!, "checkpoint-recovery"));
        Assert.NotEmpty(preserved);
        Assert.All(preserved, file => Assert.True(File.GetAttributes(file).HasFlag(FileAttributes.ReadOnly)));
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Run")]
    public void ActiveRunCheckpointBecomesOneInterruptedSummaryAcrossRepeatedRestarts()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var generation = repository.CurrentGenerationId;
        repository.SaveActiveRun(Checkpoint(generation, 12));
        repository.CloseClean();

        var recovery = Repository(directory.Path);
        var result = recovery.Open(Identity());

        Assert.True(result.InterruptedRunRecovered);
        var run = Assert.Single(recovery.Current.Statistics.Runs);
        Assert.Equal(RunOutcome.Interrupted, run.Outcome);
        Assert.Equal(12, run.ActiveDurationSeconds);
        Assert.Equal(4, run.PhysicalDistance);
        Assert.Equal(9, run.TeleportDistance);
        Assert.Equal(1, run.WeaponStatistics.Totals.FiringActions);
        Assert.Equal(1, run.WeaponStatistics.Totals.AmmunitionUnitsConsumed);
        Assert.Equal(6, run.WeaponStatistics.Totals.Projectiles);
        Assert.Equal(1, recovery.Current.Statistics.RunTotals.WeaponStatistics.Totals.FiringActions);
        Assert.Null(recovery.Current.Statistics.RunRecords.Extraction.Shortest);
        recovery.CloseClean();

        var repeated = Repository(directory.Path);
        var repeatedResult = repeated.Open(Identity());
        Assert.False(repeatedResult.InterruptedRunRecovered);
        Assert.Single(repeated.Current.Statistics.Runs);
        Assert.Equal(1, repeated.Current.Statistics.RunTotals.WeaponStatistics.Totals.FiringActions);
        repeated.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Run")]
    [Trait("Category", "Combat")]
    public void SchemaFourCheckpointRecoveryRetainsHistoricalCombatUnavailability()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var checkpoint = Checkpoint(repository.CurrentGenerationId, 4);
        checkpoint.SchemaVersion = 4;
        checkpoint.CombatStatistics = null!;
        repository.CloseClean();
        new AtomicJsonStore<ActiveRunCheckpoint>().Save(ActiveRunPath(directory.Path), checkpoint);

        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity()).InterruptedRunRecovered);

        var run = Assert.Single(recovery.Current.Statistics.Runs);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
            run.CombatStatistics.Capabilities.DamageDealt.State);
        Assert.Contains("predates M5", run.CombatStatistics.Capabilities.DamageDealt.Provenance);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
            recovery.Current.Statistics.RunTotals.CombatStatistics.Capabilities.DamageDealt.State);
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Run")]
    public void ActiveRunRecoveryUsesBackupWhenPrimaryIsCorrupt()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var generation = repository.CurrentGenerationId;
        repository.SaveActiveRun(Checkpoint(generation, 5));
        repository.SaveActiveRun(Checkpoint(generation, 8));
        repository.CloseClean();
        File.WriteAllText(ActiveRunPath(directory.Path), "{invalid");

        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity()).InterruptedRunRecovered);
        Assert.Equal(5, Assert.Single(recovery.Current.Statistics.Runs).ActiveDurationSeconds);
        Assert.False(File.Exists(ActiveRunPath(directory.Path)));
        Assert.False(File.Exists(AtomicJsonPaths.GetBackupPath(ActiveRunPath(directory.Path))));
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Run")]
    [Trait("Category", "Weapon")]
    public void ActiveRunRecoveryUsesValidBackupWhenPrimaryHasNegativeCombatCounter()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var generation = repository.CurrentGenerationId;
        repository.SaveActiveRun(Checkpoint(generation, 5));
        repository.SaveActiveRun(Checkpoint(generation, 8));
        repository.CloseClean();
        var path = ActiveRunPath(directory.Path);
        var primary = File.ReadAllText(path);
        const string validCounter = "\"FiringActions\":1";
        var counterIndex = primary.IndexOf(validCounter, StringComparison.Ordinal);
        Assert.True(counterIndex >= 0);
        File.WriteAllText(
            path,
            string.Concat(
                primary.AsSpan(0, counterIndex),
                "\"FiringActions\":-1",
                primary.AsSpan(counterIndex + validCounter.Length)));

        var recovery = Repository(directory.Path);
        var result = recovery.Open(Identity());

        Assert.True(result.InterruptedRunRecovered);
        var run = Assert.Single(recovery.Current.Statistics.Runs);
        Assert.Equal(5, run.ActiveDurationSeconds);
        Assert.Equal(1, run.WeaponStatistics.Totals.FiringActions);
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(AtomicJsonPaths.GetBackupPath(path)));
        var recoveryDirectory = Path.Combine(Path.GetDirectoryName(path)!, "checkpoint-recovery");
        Assert.False(Directory.Exists(recoveryDirectory));
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Run")]
    [Trait("Category", "Combat")]
    public void ActiveRunRecoveryUsesValidBackupWhenPrimaryHasSemanticallyInvalidCombatState()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var generation = repository.CurrentGenerationId;
        var backup = Checkpoint(generation, 5);
        backup.CombatStatistics.Totals.DamageCaused = 5;
        backup.CombatStatistics.Totals.DamageDealt = 5;
        var primary = Checkpoint(generation, 8);
        primary.CombatStatistics.Totals.DamageCaused = 8;
        primary.CombatStatistics.Totals.DamageDealt = 8;
        repository.SaveActiveRun(backup);
        repository.SaveActiveRun(primary);
        repository.CloseClean();
        var path = ActiveRunPath(directory.Path);
        var json = File.ReadAllText(path);
        const string validCounter = "\"DamageDealt\":8";
        var counterIndex = json.IndexOf(validCounter, StringComparison.Ordinal);
        Assert.True(counterIndex >= 0);
        File.WriteAllText(
            path,
            string.Concat(
                json.AsSpan(0, counterIndex),
                "\"DamageDealt\":-1",
                json.AsSpan(counterIndex + validCounter.Length)));

        var recovery = Repository(directory.Path);
        var result = recovery.Open(Identity());

        Assert.True(result.InterruptedRunRecovered);
        var run = Assert.Single(recovery.Current.Statistics.Runs);
        Assert.Equal(5, run.ActiveDurationSeconds);
        Assert.Equal(5, run.CombatStatistics.Totals.DamageDealt);
        Assert.False(run.CombatStatistics.WasRepairedFromInvalidState);
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(AtomicJsonPaths.GetBackupPath(path)));
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Equipment")]
    public void ActiveRunRecoveryUsesValidBackupWhenPrimaryHasInvalidEquipmentDuration()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var generation = repository.CurrentGenerationId;
        var backup = Checkpoint(generation, 5);
        backup.EquipmentStatistics = EquipmentCheckpointStatistics(5);
        repository.SaveActiveRun(backup);
        repository.CloseClean();

        var path = ActiveRunPath(directory.Path);
        var invalidPrimary = Checkpoint(generation, 8);
        invalidPrimary.EquipmentStatistics = EquipmentCheckpointStatistics(8);
        invalidPrimary.EquipmentStatistics.Loadouts["loadout:a"].ActiveDurationSeconds = -1;
        new AtomicJsonStore<ActiveRunCheckpoint>().Save(path, invalidPrimary);

        var recovery = Repository(directory.Path);
        var result = recovery.Open(Identity());

        Assert.True(result.InterruptedRunRecovered);
        var run = Assert.Single(recovery.Current.Statistics.Runs);
        Assert.Equal(5, run.ActiveDurationSeconds);
        Assert.Equal(5, run.EquipmentStatistics.Loadouts["loadout:a"].ActiveDurationSeconds);
        Assert.False(run.EquipmentStatistics.WasRepairedFromInvalidState);
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(AtomicJsonPaths.GetBackupPath(path)));
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Equipment")]
    public void ActiveRunRecoveryUsesValidBackupWhenPrimaryEquipmentTransitionsAreNonMonotonic()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var generation = repository.CurrentGenerationId;
        var backup = Checkpoint(generation, 5);
        backup.EquipmentStatistics = EquipmentCheckpointStatistics(5);
        repository.SaveActiveRun(backup);
        repository.CloseClean();

        var path = ActiveRunPath(directory.Path);
        var invalidPrimary = Checkpoint(generation, 10);
        invalidPrimary.EquipmentStatistics = EquipmentCheckpointStatistics(10);
        invalidPrimary.EquipmentStatistics.Transitions = new List<EquipmentTransition>
        {
            new() { ActiveTimeSeconds = 8, ToSnapshotId = "snapshot:a", ToLoadoutId = "loadout:a" },
            new() { ActiveTimeSeconds = 4, ToSnapshotId = "snapshot:b", ToLoadoutId = "loadout:b" }
        };
        invalidPrimary.EquipmentStatistics.TransitionCount = 2;
        new AtomicJsonStore<ActiveRunCheckpoint>().Save(path, invalidPrimary);

        var recovery = Repository(directory.Path);
        var result = recovery.Open(Identity());

        Assert.True(result.InterruptedRunRecovered);
        var run = Assert.Single(recovery.Current.Statistics.Runs);
        Assert.Equal(5, run.ActiveDurationSeconds);
        Assert.Single(run.EquipmentStatistics.Transitions);
        Assert.Equal(0, run.EquipmentStatistics.Transitions[0].ActiveTimeSeconds);
        Assert.False(run.EquipmentStatistics.WasRepairedFromInvalidState);
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Equipment")]
    public void ActiveRunRecoveryUsesValidBackupWhenCurrentSchemaPrimaryIsMissingEquipmentRoot()
    {
        AssertCurrentSchemaEquipmentPrimaryRejected(checkpoint => checkpoint.EquipmentStatistics = null!);
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Equipment")]
    public void ActiveRunRecoveryUsesValidBackupWhenCurrentSchemaPrimaryIsMissingRetainedTransitions()
    {
        AssertCurrentSchemaEquipmentPrimaryRejected(checkpoint =>
        {
            checkpoint.EquipmentStatistics.TransitionCount = 1;
            checkpoint.EquipmentStatistics.Transitions = null!;
        });
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Equipment")]
    public void ActiveRunRecoveryUsesValidBackupWhenCurrentSchemaPrimaryHasOneSidedTransitionSelection()
    {
        AssertCurrentSchemaEquipmentPrimaryRejected(checkpoint =>
        {
            var transition = Assert.Single(checkpoint.EquipmentStatistics.Transitions);
            transition.SelectedWeaponSlotId = "slot:primary";
            transition.SelectedWeaponId = string.Empty;
        });
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Run")]
    [Trait("Category", "Combat")]
    public void ActiveRunRecoveryUsesValidBackupWhenPrimaryHasImpossibleNestedCombatOutcomes()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var generation = repository.CurrentGenerationId;
        var backup = Checkpoint(generation, 5);
        backup.CombatStatistics = CombatCheckpointStatistics(nestedRangedHits: 1);
        repository.SaveActiveRun(backup);
        repository.CloseClean();

        var path = ActiveRunPath(directory.Path);
        var invalidPrimary = Checkpoint(generation, 8);
        invalidPrimary.CombatStatistics = CombatCheckpointStatistics(nestedRangedHits: 2);
        new AtomicJsonStore<ActiveRunCheckpoint>().Save(path, invalidPrimary);

        var recovery = Repository(directory.Path);
        var result = recovery.Open(Identity());

        Assert.True(result.InterruptedRunRecovered);
        var run = Assert.Single(recovery.Current.Statistics.Runs);
        Assert.Equal(5, run.ActiveDurationSeconds);
        Assert.Equal(1, run.CombatStatistics.Totals.CompletedPlayerProjectiles);
        Assert.Equal(1, run.CombatStatistics.Totals.RangedHits);
        var weapon = Assert.Single(run.CombatStatistics.Weapons).Value;
        Assert.Equal(1, weapon.Totals.CompletedPlayerProjectiles);
        Assert.Equal(1, weapon.Totals.RangedHits);
        Assert.False(run.CombatStatistics.WasRepairedFromInvalidState);
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(AtomicJsonPaths.GetBackupPath(path)));
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Run")]
    [Trait("Category", "Combat")]
    public void ActiveRunCheckpointWritesAndRecoversIndependentMultiTargetHeadshotFinalBlow()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var generation = repository.CurrentGenerationId;
        var checkpoint = Checkpoint(generation, 5);
        var combat = new CombatStatisticsAggregate();
        CombatStatisticsReducer.Apply(combat, CombatEvent(generation, "headshot", "duckov:target:a", "First target") with
        {
            ActualDamageToTarget = 10,
            ActualDamageDealt = 10,
            Headshots = 1
        });
        CombatStatisticsReducer.Apply(combat, CombatEvent(generation, "final-blow", "duckov:target:b", "Fatal target") with
        {
            ActualDamageToTarget = 5,
            ActualDamageDealt = 5,
            EnemiesKilled = 1,
            HeadshotFinalBlows = 1,
            IsFinalBlow = true
        });
        checkpoint.CombatStatistics = combat;

        repository.SaveActiveRun(checkpoint);

        Assert.True(File.Exists(ActiveRunPath(directory.Path)));
        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity()).InterruptedRunRecovered);
        var run = Assert.Single(recovery.Current.Statistics.Runs);
        Assert.Equal(1, run.CombatStatistics.Totals.Headshots);
        Assert.Equal(1, run.CombatStatistics.Totals.HeadshotFinalBlows);
        Assert.Equal(1, run.CombatStatistics.Totals.EnemiesKilled);
        Assert.Equal(1, run.CombatStatistics.Enemies["duckov:target:a"].Totals.Headshots);
        Assert.Equal(0, run.CombatStatistics.Enemies["duckov:target:a"].Totals.HeadshotFinalBlows);
        Assert.Equal(0, run.CombatStatistics.Enemies["duckov:target:b"].Totals.Headshots);
        Assert.Equal(1, run.CombatStatistics.Enemies["duckov:target:b"].Totals.HeadshotFinalBlows);
        Assert.Equal(1, run.CombatStatistics.Enemies["duckov:target:b"].Totals.EnemiesKilled);
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Run")]
    public void ActiveRunRecoveryUsesOrphanedTemporarySnapshot()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var generation = repository.CurrentGenerationId;
        repository.SaveActiveRun(Checkpoint(generation, 7));
        repository.CloseClean();
        File.Move(ActiveRunPath(directory.Path), AtomicJsonPaths.GetTemporaryPath(ActiveRunPath(directory.Path)));

        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity()).InterruptedRunRecovered);
        Assert.Equal(7, Assert.Single(recovery.Current.Statistics.Runs).ActiveDurationSeconds);
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Run")]
    public void ActiveRunCheckpointIsIsolatedBySaveSlotAndGeneration()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity(slot: 1));
        var slotOneGeneration = repository.CurrentGenerationId;
        repository.SaveActiveRun(Checkpoint(slotOneGeneration, 6));
        Assert.Throws<ArgumentException>(() => repository.SaveActiveRun(Checkpoint("different-generation", 7)));

        repository.Open(Identity(slot: 2));
        Assert.Equal(2, repository.Current.Slot);
        Assert.Empty(repository.Current.Statistics.Runs);
        Assert.Equal(0, repository.Current.Statistics.RunTotals.WeaponStatistics.Totals.FiringActions);
        repository.CloseClean();

        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity(slot: 1)).InterruptedRunRecovered);
        Assert.Equal(slotOneGeneration, Assert.Single(recovery.Current.Statistics.Runs).SaveGenerationId);
        Assert.Equal(1, recovery.Current.Statistics.RunTotals.WeaponStatistics.Totals.FiringActions);
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Run")]
    public void IdentityRotationRecoversInterruptedStateIntoTheOldGenerationBeforeArchiving()
    {
        using var directory = new TemporaryDirectory();
        var interrupted = Repository(directory.Path);
        interrupted.Open(Identity());
        var oldGeneration = interrupted.CurrentGenerationId;
        interrupted.SaveActiveRun(Checkpoint(oldGeneration, 11));

        var replacement = Repository(directory.Path);
        var result = replacement.Open(Identity(creationTicks: 999, hashCharacter: 'f'));

        Assert.True(result.RotatedGeneration);
        Assert.True(result.InterruptedRunRecovered);
        Assert.True(result.InterruptedSessionRecovered);
        Assert.NotEqual(oldGeneration, replacement.CurrentGenerationId);
        Assert.Empty(replacement.Current.Statistics.Runs);
        Assert.Equal(0, replacement.Current.InterruptedSessionCount);

        var archive = Assert.Single(Directory.EnumerateDirectories(Path.Combine(
            directory.Path,
            "profiles",
            "slot-01",
            "archives")));
        var archived = new AtomicJsonStore<ProfileDocument>().Load(Path.Combine(archive, "profile.json")).Value!;
        var recovered = Assert.Single(archived.Statistics.Runs);
        Assert.Equal(oldGeneration, archived.GenerationId);
        Assert.Equal(oldGeneration, recovered.SaveGenerationId);
        Assert.Equal(RunOutcome.Interrupted, recovered.Outcome);
        Assert.Equal(11, recovered.ActiveDurationSeconds);
        Assert.False(recovered.RecordEligible);
        Assert.Equal(1, archived.InterruptedSessionCount);
        Assert.Empty(Directory.EnumerateFiles(archive, "active-run.json*", SearchOption.TopDirectoryOnly));
        Assert.False(File.Exists(Path.Combine(archive, "session.json")));
        replacement.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Run")]
    public void UnrecoverableCheckpointArtifactsArePreservedReadOnlyWithoutInventingARun()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var generation = repository.CurrentGenerationId;
        repository.SaveActiveRun(Checkpoint(generation, 5));
        repository.SaveActiveRun(Checkpoint(generation, 8));
        repository.CloseClean();
        var path = ActiveRunPath(directory.Path);
        File.WriteAllText(path, "{invalid-primary");
        File.WriteAllText(AtomicJsonPaths.GetBackupPath(path), "{invalid-backup");
        File.WriteAllText(AtomicJsonPaths.GetTemporaryPath(path), "{invalid-temporary");

        var recovery = Repository(directory.Path);
        var result = recovery.Open(Identity());

        Assert.False(result.InterruptedRunRecovered);
        Assert.Empty(recovery.Current.Statistics.Runs);
        Assert.False(File.Exists(path));
        var preserved = Directory.GetFiles(
            Path.Combine(Path.GetDirectoryName(path)!, "checkpoint-recovery"));
        Assert.Equal(3, preserved.Length);
        Assert.All(preserved, file => Assert.True(File.GetAttributes(file).HasFlag(FileAttributes.ReadOnly)));
        recovery.CloseClean();
    }

    private static ProfileRepository Repository(string path) => new(
        path,
        () => TestTime.AddMinutes(1),
        () => Guid.NewGuid().ToString("N"));

    private static void AssertCurrentSchemaEquipmentPrimaryRejected(Action<ActiveRunCheckpoint> corrupt)
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var generation = repository.CurrentGenerationId;
        var backup = Checkpoint(generation, 5);
        backup.EquipmentStatistics = EquipmentCheckpointStatistics(5);
        repository.SaveActiveRun(backup);
        repository.CloseClean();

        var path = ActiveRunPath(directory.Path);
        var primary = Checkpoint(generation, 8);
        primary.EquipmentStatistics = EquipmentCheckpointStatistics(8);
        corrupt(primary);
        new AtomicJsonStore<ActiveRunCheckpoint>().Save(path, primary);

        var recovery = Repository(directory.Path);
        var result = recovery.Open(Identity());

        Assert.True(result.InterruptedRunRecovered);
        var run = Assert.Single(recovery.Current.Statistics.Runs);
        Assert.Equal(5, run.ActiveDurationSeconds);
        Assert.Single(run.EquipmentStatistics.Transitions);
        Assert.Equal(0, run.EquipmentStatistics.Transitions[0].ActiveTimeSeconds);
        Assert.False(run.EquipmentStatistics.WasRepairedFromInvalidState);
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(AtomicJsonPaths.GetBackupPath(path)));
        recovery.CloseClean();
    }

    private static void AssertCurrentSchemaRoutePrimaryRejected(Action<ActiveRunCheckpoint> corrupt)
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var generation = repository.CurrentGenerationId;
        repository.SaveActiveRun(RouteCheckpoint(generation, 5));
        repository.CloseClean();

        var primary = RouteCheckpoint(generation, 8);
        corrupt(primary);
        new AtomicJsonStore<ActiveRunCheckpoint>().Save(ActiveRunPath(directory.Path), primary);

        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity()).InterruptedRunRecovered);
        var run = Assert.Single(recovery.Current.Statistics.Runs);
        Assert.Equal(5, run.ActiveDurationSeconds);
        Assert.Single(run.Segments);
        Assert.False(run.RouteWasRepairedFromInvalidState);
        recovery.CloseClean();
    }

    private static ActiveRunCheckpoint RouteCheckpoint(string generation, double activeSeconds)
    {
        var tracker = new RunLifecycleTracker(() => "route-checkpoint");
        tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.RaidInitialized,
            TimestampUtc = TestTime,
            MonotonicSeconds = 0,
            NativeRaidId = "42"
        });
        tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.ControlReady,
            TimestampUtc = TestTime,
            MonotonicSeconds = 0,
            StartContext = new RunStartContext
            {
                SaveGenerationId = generation,
                NativeRaidId = "42",
                Map = new MapIdentity { MapId = "duckov:map:A", DisplayName = "A", IsKnown = true },
                LifecycleCapability = AdapterCapabilityState.Supported,
                MovementCapability = AdapterCapabilityState.Supported,
                MapCapability = AdapterCapabilityState.Supported,
                EconomyCapabilities = SupportedEconomyCapabilities(),
                RouteCapabilities = RouteStatisticsReducer.Supported("test")
            }
        });
        return tracker.CreateCheckpoint(TestTime.AddSeconds(activeSeconds), activeSeconds)!;
    }

    private static SegmentEventAssociation LegacyRouteAssociation(string eventId, MapSegmentSummary segment) => new()
    {
        EventId = eventId,
        EventKind = "item-use",
        TimestampUtc = TestTime.AddSeconds(1),
        FirstTimestampUtc = TestTime.AddSeconds(1),
        LastTimestampUtc = TestTime.AddSeconds(1),
        Count = 1,
        SourceSegmentId = segment.SegmentId,
        SourceMapId = segment.MapId,
        OutcomeSegmentId = segment.SegmentId,
        OutcomeMapId = segment.MapId
    };

    private static RunLifecycleTracker ActiveTracker(string generation)
    {
        var tracker = new RunLifecycleTracker(() => "run-deferred-items");
        tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.RaidInitialized,
            TimestampUtc = TestTime,
            MonotonicSeconds = 0,
            NativeRaidId = "42"
        });
        tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.ControlReady,
            TimestampUtc = TestTime,
            MonotonicSeconds = 0,
            StartContext = new RunStartContext
            {
                SaveGenerationId = generation,
                NativeRaidId = "42",
                Map = new MapIdentity { MapId = "duckov:map:A", DisplayName = "A", IsKnown = true },
                IntegrityTags = IntegrityTags.Normal,
                LifecycleCapability = AdapterCapabilityState.Supported,
                MovementCapability = AdapterCapabilityState.Supported,
                MapCapability = AdapterCapabilityState.Supported,
                EconomyCapabilities = SupportedEconomyCapabilities(),
                RouteCapabilities = RouteStatisticsReducer.Supported("test")
            }
        });
        return tracker;
    }

    private static CurrencyFlowRecorded EconomyFlow(
        string eventId,
        RunLifecycleTracker tracker,
        string generation,
        CurrencyFlowDirection direction,
        long amount) => new()
        {
            EventId = eventId,
            TimestampUtc = TestTime.AddSeconds(amount),
            SaveGenerationId = generation,
            RunId = tracker.ActiveRunId,
            SegmentId = tracker.ActiveSegmentId,
            MapId = tracker.ActiveMapId!,
            Currency = CurrencyKind.Money,
            Direction = direction,
            Amount = amount,
            Source = CurrencySourceCategory.UnknownAdjustment,
            GameplayContext = GameplayContext.Raid,
            IntegrityTags = IntegrityTags.Normal,
            AdapterVersion = "test",
            ProducerActivationId = "test-active-run-persistence",
            ProducerSequence = Interlocked.Increment(ref economySequence)
        };

    private static EconomyMetricCapabilities SupportedEconomyCapabilities()
    {
        static MetricAvailability Available() => new() { State = AdapterCapabilityState.Supported, Provenance = "test" };
        return new EconomyMetricCapabilities
        {
            MoneyAmountDirection = Available(),
            MoneySourceAttribution = Available(),
            MoneyContextAttribution = Available(),
            CashAmountDirection = Available(),
            CashExternalAcquisition = Available(),
            CashContextAttribution = Available(),
            CashTerminalOutcomes = Available(),
            RouteAttribution = Available()
        };
    }

    private static (ItemUseRecorded ItemUse, HealingApplied Healing) ConsumableEvents(
        RunLifecycleTracker tracker,
        string generation,
        int sequence,
        double healingAmount)
    {
        var itemEventId = $"item-{sequence}";
        var itemUse = new ItemUseRecorded
        {
            EventId = itemEventId,
            TimestampUtc = TestTime.AddSeconds(sequence),
            SaveGenerationId = generation,
            RunId = tracker.ActiveRunId,
            MapId = tracker.ActiveMapId,
            SegmentId = tracker.ActiveSegmentId,
            GameplayContext = GameplayContext.Raid,
            ItemId = "duckov:item:medkit",
            DisplayName = "Med Kit",
            Group = CanonicalItemGroup.OtherUnknown,
            ActivationCount = 1,
            AmountConsumed = 1,
            ConsumptionUnit = ConsumptionUnit.Item
        };
        var healing = new HealingApplied
        {
            EventId = $"healing-{sequence}",
            ApplicationId = $"application-{sequence}",
            SourceItemUseEventId = itemEventId,
            TimestampUtc = TestTime.AddSeconds(sequence).AddMilliseconds(1),
            SaveGenerationId = generation,
            RunId = tracker.ActiveRunId,
            MapId = tracker.ActiveMapId,
            SourceMapId = tracker.ActiveMapId,
            OutcomeMapId = tracker.ActiveMapId,
            SourceSegmentId = tracker.ActiveSegmentId,
            OutcomeSegmentId = tracker.ActiveSegmentId,
            GameplayContext = GameplayContext.Raid,
            ItemId = "duckov:item:medkit",
            DisplayName = "Med Kit",
            Group = CanonicalItemGroup.Healing,
            ActualHealthRestored = healingAmount
        };
        return (itemUse, healing);
    }

    private static ActiveRunCheckpoint Checkpoint(string generation, double activeSeconds) => new()
    {
        RunId = "run-checkpoint",
        SaveGenerationId = generation,
        NativeRaidId = "42",
        MapId = "duckov:map:warehouse",
        MapDisplayName = "Warehouse",
        MapKnown = true,
        StartedUtc = TestTime,
        LastObservedUtc = TestTime.AddSeconds(20),
        ActiveDurationSeconds = activeSeconds,
        PhysicalDistance = 4,
        TeleportDistance = 9,
        IntegrityTags = IntegrityTags.Normal,
        GameVersion = "2.3.30",
        GameBuild = "24013657",
        LifecycleCapability = AdapterCapabilityState.Supported,
        LifecycleAdapterVersion = "native-run-lifecycle/2.3.30",
        MovementCapability = AdapterCapabilityState.Supported,
        MovementAdapterVersion = "native-main-duck-movement/2.3.30",
        MapCapability = AdapterCapabilityState.Supported,
        MapAdapterVersion = "native-map-identity/2.3.30",
        WeaponStatistics = CombatStatistics()
    };

    private static WeaponStatisticsAggregate CombatStatistics()
    {
        var statistics = new WeaponStatisticsAggregate();
        WeaponStatisticsReducer.Apply(statistics, new ShotRecorded
        {
            EventId = "shot-checkpoint",
            TimestampUtc = TestTime,
            SaveGenerationId = "unused-after-aggregation",
            RunId = "run-checkpoint",
            MapId = "duckov:map:warehouse",
            GameplayContext = GameplayContext.Raid,
            WeaponId = "duckov:weapon:1",
            WeaponDisplayName = "Test shotgun",
            AmmunitionId = "duckov:ammo:2",
            AmmunitionDisplayName = "Test shell",
            FiringActionCount = 1,
            AmmunitionUnitsConsumed = 1,
            ProjectileCount = 6,
            Capabilities = SupportedCapabilities()
        });
        return statistics;
    }

    private static CombatStatisticsAggregate CombatCheckpointStatistics(long nestedRangedHits)
    {
        var statistics = new CombatStatisticsAggregate
        {
            Totals = new CombatMetricTotals
            {
                CompletedPlayerProjectiles = 1,
                RangedHits = 1
            }
        };
        statistics.Weapons["duckov:weapon:1"] = new CombatBreakdownAggregate
        {
            Id = "duckov:weapon:1",
            DisplayName = "Test weapon",
            Totals = new CombatMetricTotals
            {
                CompletedPlayerProjectiles = 1,
                RangedHits = nestedRangedHits
            }
        };
        return statistics;
    }

    private static EquipmentStatisticsAggregate EquipmentCheckpointStatistics(double activeSeconds)
    {
        var statistics = new EquipmentStatisticsAggregate
        {
            Capabilities = EquipmentNativeContractPolicy.CreateSupportedCapabilities()
        };
        EquipmentStatisticsReducer.Observe(statistics, new EquipmentSnapshot
        {
            SnapshotId = "snapshot:a",
            LoadoutId = "loadout:a",
            TotemSetId = "totems:none",
            Items = new List<EquippedItemSnapshot>
            {
                new() { SlotId = "slot:primary", ItemId = "weapon:a", ItemDisplayName = "Rifle" }
            }
        }, 0);
        EquipmentStatisticsReducer.Advance(statistics, activeSeconds);
        return statistics;
    }

    private static ContainerRunCheckpointState ContainerCheckpoint(params int[] keys) => new()
    {
        Statistics = new ContainerStatisticsAggregate
        {
            Capabilities = ContainerNativeContractPolicy.Supported(),
            UniqueContainersLooted = keys.Length
        },
        LootedContainerKeys = keys.OrderBy(value => value).ToList()
    };

    private static CombatRecorded CombatEvent(
        string generation,
        string eventId,
        string targetId,
        string targetDisplayName) => new()
        {
            EventId = eventId,
            TimestampUtc = TestTime,
            SaveGenerationId = generation,
            RunId = "run-checkpoint",
            MapId = "duckov:map:warehouse",
            GameplayContext = GameplayContext.Raid,
            IntegrityTags = IntegrityTags.Normal,
            GameVersion = "2.3.30",
            GameBuild = "24013657",
            AdapterVersion = "test",
            Ownership = CombatOwnership.Player,
            AttackKind = CombatAttackKind.Ranged,
            TargetId = targetId,
            TargetDisplayName = targetDisplayName,
            TargetIsEnemy = true,
            TargetFamilyId = "duckov:family:unknown",
            TargetFamilyDisplayName = "Unknown family",
            WeaponId = "duckov:weapon:1",
            WeaponDisplayName = "Test rifle",
            AmmunitionId = "duckov:ammo:2",
            AmmunitionDisplayName = "Test round",
            Capabilities = CombatNativeContractPolicy.CreateSupportedCapabilities()
        };

    private static WeaponMetricCapabilities SupportedCapabilities() => new()
    {
        FiringActions = Supported(),
        AmmunitionConsumption = Supported(),
        Projectiles = Supported(),
        WeaponIdentity = Supported(),
        AmmunitionIdentity = Supported()
    };

    private static MetricAvailability Supported() => new()
    {
        State = AdapterCapabilityState.Supported,
        Provenance = "test"
    };

    private static RunLifecycleEvent LifecycleEvent(
        RunLifecycleEventKind kind,
        string generation,
        double seconds) => new()
        {
            Kind = kind,
            TimestampUtc = TestTime.AddSeconds(seconds),
            MonotonicSeconds = seconds,
            NativeRaidId = "42",
            StartContext = kind == RunLifecycleEventKind.ControlReady
                ? new RunStartContext
                {
                    SaveGenerationId = generation,
                    NativeRaidId = "42",
                    Map = new MapIdentity
                    {
                        MapId = "duckov:map:warehouse",
                        DisplayName = "Warehouse",
                        IsKnown = true
                    },
                    IntegrityTags = IntegrityTags.Normal,
                    LifecycleCapability = AdapterCapabilityState.Supported,
                    MovementCapability = AdapterCapabilityState.Supported,
                    MapCapability = AdapterCapabilityState.Supported,
                    WeaponCapabilities = SupportedCapabilities()
                }
                : null
        };

    private static ShotRecorded LiveShot(string generation) => new()
    {
        EventId = "live-shot",
        TimestampUtc = TestTime,
        SaveGenerationId = generation,
        RunId = "run-live-shot",
        MapId = "duckov:map:warehouse",
        GameplayContext = GameplayContext.Raid,
        IntegrityTags = IntegrityTags.Normal,
        WeaponId = "duckov:weapon:1",
        WeaponDisplayName = "Test rifle",
        AmmunitionId = "duckov:ammo:2",
        AmmunitionDisplayName = "Test round",
        FiringActionCount = 1,
        AmmunitionUnitsConsumed = 1,
        ProjectileCount = 1,
        Capabilities = SupportedCapabilities()
    };

    private static SaveIdentitySnapshot Identity(
        int slot = 1,
        long creationTicks = 100,
        char hashCharacter = 'a') => new()
        {
            Slot = slot,
            SaveFilePresent = true,
            SaveFileCreationUtcTicks = creationTicks,
            ObservedWriteUtcTicks = 110,
            ObservedLength = 4096,
            GameVersion = "2.3.30",
            ContentSha256 = new string(slot == 1 ? hashCharacter : 'b', 64),
            SaveTimeBinary = TestTime.ToBinary()
        };

    private static void SetMoneyInflow(EconomyStatisticsAggregate economy, long amount)
    {
        economy.Currencies[CurrencyKind.Money.ToString()] = new CurrencyEconomyAggregate
        {
            Currency = CurrencyKind.Money,
            Totals = new CurrencyFlowTotals { GrossInflow = amount },
            Sources = new Dictionary<string, CurrencyFlowTotals>(StringComparer.Ordinal)
            {
                [CurrencySourceCategory.UnknownAdjustment.ToString()] = new() { GrossInflow = amount }
            },
            Contexts = new Dictionary<string, CurrencyFlowTotals>(StringComparer.Ordinal)
            {
                [GameplayContext.Unknown.ToString()] = new() { GrossInflow = amount }
            }
        };
    }

    private static string ActiveRunPath(string root) => Path.Combine(
        root,
        "profiles",
        "slot-01",
        "current",
        "active-run.json");

    private static Dictionary<string, string> SingleCsvRow(string csv)
    {
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .ToArray();
        Assert.Equal(2, lines.Length);
        var headers = lines[0].Split(',');
        var values = lines[1].Split(',');
        Assert.Equal(headers.Length, values.Length);
        return headers.Select((header, index) => new KeyValuePair<string, string>(header, values[index]))
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private static void AssertItemTotals(
        Dictionary<string, string> row,
        string activationColumn,
        string healingColumn)
    {
        Assert.Equal("2", row[activationColumn]);
        Assert.Equal("25", row[healingColumn]);
    }
}
