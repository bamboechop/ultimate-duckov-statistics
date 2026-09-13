using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;
using UltimateDuckovStatistics.Sqlite;

namespace UltimateDuckovStatistics.Tests;

public sealed class IncrementalCheckpointProtocolTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "uds-checkpoint-records-" + Guid.NewGuid().ToString("N"));
    private readonly ProfileRecordCodec codec = new(NativeProfileJsonWriter.WriteRecord);
    private static readonly DateTime Origin = new(2026, 8, 10, 10, 0, 0, DateTimeKind.Utc);
    public IncrementalCheckpointProtocolTests()
    { Directory.CreateDirectory(directory); SqliteLibrary.Initialize(Path.Combine(AppContext.BaseDirectory, "sqlite3.dll")); }

    [Fact]
    public async Task ChangedEntriesReopenAsTheSamePublicCheckpointAndRetainOlderShotIdentities()
    {
        var profile = NativeProfileJsonWriterTests.CreateProfile();
        var tracker = Started(profile.GenerationId);
        using var storage = new SqliteProfileStorage(Path.Combine(directory, "profile.sqlite"), codec);
        storage.Import(profile, null, null);
        var journal = new ProfileChangeJournal(profile.GenerationId, codec);
        for (var index = 0; index < 8; index++)
        {
            var shot = Shot(profile.GenerationId, index);
            Assert.True(tracker.RecordShot(shot));
            var packet = tracker.CaptureIncrementalCheckpoint(Origin.AddSeconds(index + 1), index + 1, null, codec)!;
            journal.Checkpoint(packet); var write = journal.Capture(profile);
            await storage.Commit(write); journal.Acknowledge(write);
            var expected = tracker.CreateCheckpoint(Origin.AddSeconds(index + 1), index + 1)!;
            var actual = storage.Load()!.Checkpoint!;
            Assert.Equal(codec.Encode(expected), codec.Encode(actual));
            Assert.Equal(index + 1, actual.WeaponStatistics.Weapons.Count);
            if (index > 0)
                Assert.Single(write.Records, record => record.Address.Kind == ProfileRecordKind.CheckpointEntry
                    && record.Address.First == "" && record.Address.Second == "1");
        }
    }

    [Fact]
    public async Task FailedCheckpointAndOlderAcknowledgementCannotDropNewerEntries()
    {
        var profile = NativeProfileJsonWriterTests.CreateProfile(); var tracker = Started(profile.GenerationId);
        using var storage = new SqliteProfileStorage(Path.Combine(directory, "profile.sqlite"), codec);
        storage.Import(profile, null, null); var journal = new ProfileChangeJournal(profile.GenerationId, codec);
        Assert.True(tracker.RecordShot(Shot(profile.GenerationId, 0)));
        journal.Checkpoint(tracker.CaptureIncrementalCheckpoint(Origin.AddSeconds(1), 1, null, codec)!);
        var first = journal.Capture(profile); await storage.Commit(first);
        Assert.True(tracker.RecordShot(Shot(profile.GenerationId, 1)));
        journal.Checkpoint(tracker.CaptureIncrementalCheckpoint(Origin.AddSeconds(2), 2, null, codec)!);
        var second = journal.Capture(profile);
        journal.Acknowledge(first);
        using var fault = new SqliteStore(storage.Path);
        fault.Exec("CREATE TRIGGER fail_checkpoint BEFORE UPDATE ON profile_state BEGIN SELECT RAISE(ABORT,'checkpoint failure'); END");
        await Assert.ThrowsAsync<SqliteFailure>(() => storage.Commit(second));
        fault.Exec("DROP TRIGGER fail_checkpoint");
        Assert.True(tracker.RecordShot(Shot(profile.GenerationId, 2)));
        journal.Checkpoint(tracker.CaptureIncrementalCheckpoint(Origin.AddSeconds(3), 3, null, codec)!);
        var third = journal.Capture(profile); await storage.Commit(third); journal.Acknowledge(third);
        Assert.Equal(codec.Encode(tracker.CreateCheckpoint(Origin.AddSeconds(3), 3)!), codec.Encode(storage.Load()!.Checkpoint!));
        Assert.Equal(3, storage.Load()!.Checkpoint!.WeaponStatistics.Weapons.Count);
    }

    [Fact]
    public async Task RouteEquipmentItemsAndLateSourceAttributionReconstructExactly()
    {
        var profile = NativeProfileJsonWriterTests.CreateProfile(); var tracker = Started(profile.GenerationId, route: true);
        using var storage = new SqliteProfileStorage(Path.Combine(directory, "profile.sqlite"), codec);
        storage.Import(profile, null, null); var journal = new ProfileChangeJournal(profile.GenerationId, codec);
        tracker.ObserveEquipment(EquipmentCompositionTests.Snapshot(), Origin, 0);
        var firstSegment = tracker.ActiveSegmentId!;
        Assert.True(tracker.RecordItemUse(new ItemUseRecorded
        {
            EventId = "item-use",
            TimestampUtc = Origin.AddSeconds(1),
            SaveGenerationId = profile.GenerationId,
            RunId = tracker.ActiveRunId,
            MapId = "map-1",
            SegmentId = firstSegment,
            GameplayContext = GameplayContext.Raid,
            AdapterCapability = AdapterCapabilityState.Supported,
            ItemId = "medkit",
            DisplayName = "Medkit",
            Group = CanonicalItemGroup.Healing,
            ActivationCount = 1,
            AmountConsumed = 1,
            ConsumptionUnit = ConsumptionUnit.Item
        }));
        await SaveAndCompare(2);
        tracker.Apply(new RunLifecycleEvent { Kind = RunLifecycleEventKind.MapTransitionStarted, TimestampUtc = Origin.AddSeconds(3), MonotonicSeconds = 3 });
        await SaveAndCompare(3);
        tracker.Apply(new RunLifecycleEvent { Kind = RunLifecycleEventKind.LoadingEnded, TimestampUtc = Origin.AddSeconds(4), MonotonicSeconds = 4 });
        tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.DestinationControlReady,
            TimestampUtc = Origin.AddSeconds(5),
            MonotonicSeconds = 5,
            Map = new MapIdentity { MapId = "map-2", DisplayName = "Second map", IsKnown = true }
        });
        tracker.SetHealingCapability(AdapterCapabilityState.Supported);
        Assert.True(tracker.RecordHealing(new HealingApplied
        {
            EventId = "healing",
            ApplicationId = "application",
            SourceItemUseEventId = "item-use",
            TimestampUtc = Origin.AddSeconds(6),
            SaveGenerationId = profile.GenerationId,
            RunId = tracker.ActiveRunId,
            MapId = "map-1",
            SourceSegmentId = firstSegment,
            SourceMapId = "map-1",
            OutcomeSegmentId = tracker.ActiveSegmentId,
            OutcomeMapId = "map-2",
            GameplayContext = GameplayContext.Raid,
            AdapterCapability = AdapterCapabilityState.Supported,
            ItemId = "medkit",
            DisplayName = "Medkit",
            Group = CanonicalItemGroup.Healing,
            ActualHealthRestored = 7
        }));
        await SaveAndCompare(7);
        await SaveAndCompare(12);
        async Task SaveAndCompare(int seconds)
        {
            journal.Checkpoint(tracker.CaptureIncrementalCheckpoint(Origin.AddSeconds(seconds), seconds, null, codec)!);
            var write = journal.Capture(profile); await storage.Commit(write); journal.Acknowledge(write);
            Assert.Equal(codec.Encode(tracker.CreateCheckpoint(Origin.AddSeconds(seconds), seconds)!), codec.Encode(storage.Load()!.Checkpoint!));
        }
    }

    [Fact]
    public async Task MissingChangedEntryRollsBackAndCheckpointClearRejectsOldReplay()
    {
        var profile = NativeProfileJsonWriterTests.CreateProfile(); var tracker = Started(profile.GenerationId);
        using var storage = new SqliteProfileStorage(Path.Combine(directory, "profile.sqlite"), codec);
        storage.Import(profile, null, null); var journal = new ProfileChangeJournal(profile.GenerationId, codec);
        Assert.True(tracker.RecordShot(Shot(profile.GenerationId, 0)));
        journal.Checkpoint(tracker.CaptureIncrementalCheckpoint(Origin.AddSeconds(1), 1, null, codec)!);
        var command = journal.Capture(profile);
        var incomplete = new IncrementalProfileWrite(command.GenerationId, command.Owner, command.Order, command.CoveredAfter,
            command.ThroughVersion, command.Revision, command.Records.Where(record => record.Address.Kind != ProfileRecordKind.CheckpointEntry).ToArray());
        await Assert.ThrowsAsync<InvalidDataException>(() => storage.Commit(incomplete));
        using var inspection = new SqliteStore(storage.Path, true);
        Assert.Equal(0, inspection.ScalarLong("SELECT count(*) FROM receipt"));
        await storage.Commit(command); journal.Acknowledge(command);
        var cleared = journal.Capture(profile, checkpointChanged: true);
        await storage.Commit(cleared); journal.Acknowledge(cleared);
        Assert.Null(storage.Load()!.Checkpoint);
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.Commit(command));
        await storage.Commit(cleared);
    }

    internal static RunLifecycleTracker Started(string generation, bool route = false, string runId = "checkpoint-run")
    {
        var tracker = new RunLifecycleTracker(() => runId);
        tracker.Apply(new RunLifecycleEvent { Kind = RunLifecycleEventKind.RaidInitialized, TimestampUtc = Origin, MonotonicSeconds = 0, NativeRaidId = "raid" });
        tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.ControlReady,
            TimestampUtc = Origin,
            MonotonicSeconds = 0,
            StartContext = new RunStartContext
            {
                SaveGenerationId = generation,
                Map = new MapIdentity { MapId = "map-1", DisplayName = "Map", IsKnown = true },
                IntegrityTags = IntegrityTags.Normal,
                GameVersion = "2.3.30",
                GameBuild = "24013657",
                LifecycleCapability = AdapterCapabilityState.Supported,
                MovementCapability = AdapterCapabilityState.Supported,
                MapCapability = AdapterCapabilityState.Supported,
                EquipmentCapabilities = EquipmentNativeContractPolicy.CreateSupportedCapabilities(),
                RouteCapabilities = route ? RouteStatisticsReducer.Supported("checkpoint test") : RouteStatisticsReducer.Unavailable("test")
            }
        });
        Assert.True(tracker.IsActive); return tracker;
    }
    private static ShotRecorded Shot(string generation, int index)
    {
        var shot = WeaponStatisticsTests.Shot("shot-" + index, "weapon-" + index, "Weapon", "ammunition", "Ammo", 1);
        shot.RunId = "checkpoint-run"; shot.SaveGenerationId = generation; shot.TimestampUtc = Origin.AddSeconds(index + 1); return shot;
    }
    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
