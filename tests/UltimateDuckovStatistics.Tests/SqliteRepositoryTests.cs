using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;
using UltimateDuckovStatistics.Sqlite;

namespace UltimateDuckovStatistics.Tests;

public sealed class SqliteRepositoryTests
{
    private static readonly DateTime Now = NativeProfileJsonWriterTests.Now;
    private static readonly ProfileRecordCodec Codec = new(NativeProfileJsonWriter.WriteRecord);
    private static ProfileRepository Repository(string path)
    {
        SqliteLibrary.Initialize(Path.Combine(AppContext.BaseDirectory, "sqlite3.dll"));
        return new ProfileRepository(path, () => Now, () => Guid.NewGuid().ToString("N"),
            writeProfile: NativeProfileJsonWriter.Write,
            createIncrementalStorage: value => new SqliteProfileStorage(value, Codec), recordCodec: Codec);
    }
    private static SaveIdentitySnapshot Identity(int slot = 1) => new()
    {
        Slot = slot,
        GameVersion = "2.3.30",
        SaveFilePresent = true,
        ContentSha256 = new string('a', 64),
        SaveTimeBinary = Now.ToBinary(),
        ObservedWriteUtcTicks = Now.Ticks,
        SaveFileCreationUtcTicks = Now.Ticks,
        ObservedLength = 100
    };

    [Fact]
    public void FreshInstallBaseAndCraftingUseChangedRecordsAndCleanReopen()
    {
        using var directory = new TemporaryDirectory();
        using var repository = Repository(directory.Path);
        repository.Open(Identity());
        Assert.EndsWith("profile.sqlite", repository.CurrentProfilePath, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.ChangeExtension(repository.CurrentProfilePath, ".json")));
        Assert.True(repository.RecordBaseMovementDeferred(new BaseMovementUpdate { GenerationId = repository.CurrentGenerationId, CaptureId = "capture", CapturedMeters = 10, CollectionStartedUtc = Now }));
        repository.SetCraftingCapabilities(CraftingNativeContractPolicy.Supported("completion", "formula"));
        Assert.True(repository.RecordCraftingDeferred(new CraftingMutation(repository.CurrentGenerationId, Now,
            new[] { new CraftingMutationRow("100", "Bandage", "formula", 1, 3, new Dictionary<string, long> { ["3"] = 1 }) })));
        repository.Flush();
        var expected = Codec.Encode(repository.Current);
        repository.CloseClean();
        using var reopened = Repository(directory.Path);
        var result = reopened.Open(Identity());
        Assert.False(result.InterruptedSessionRecovered);
        Assert.Equal(expected, Codec.Encode(reopened.Current));
        reopened.CloseClean();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CurrentJsonAndBackupImportPreserveEveryOriginalCandidate(bool corruptPrimary)
    {
        using var directory = new TemporaryDirectory();
        var legacy = new ProfileRepository(directory.Path, () => Now, () => Guid.NewGuid().ToString("N"));
        legacy.Open(Identity()); legacy.CloseClean();
        var path = Path.Combine(directory.Path, "profiles", "slot-01", "current", "profile.json");
        if (corruptPrimary) File.WriteAllText(path, "{broken");
        var original = Directory.GetFiles(Path.GetDirectoryName(path)!).ToDictionary(value => value, File.ReadAllBytes);
        using var repository = Repository(directory.Path);
        var result = repository.Open(Identity());
        Assert.Equal(corruptPrimary, result.RecoveredSnapshot);
        foreach (var file in original) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
        repository.RecordBaseMovementDeferred(new BaseMovementUpdate { GenerationId = repository.CurrentGenerationId, CaptureId = "new-base", CapturedMeters = 5, CollectionStartedUtc = Now });
        repository.CloseClean();
        using var reopened = Repository(directory.Path);
        reopened.Open(Identity());
        Assert.Equal(5, reopened.Current.Statistics.BaseMovement!.RecordedMeters);
        foreach (var file in original) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
        reopened.CloseClean();
    }

    [Fact]
    public void SlotSwitchAndSameSlotReselectionDoNotReportCleanSessionAsInterrupted()
    {
        using var directory = new TemporaryDirectory();
        using var repository = Repository(directory.Path);
        repository.Open(Identity());
        var first = repository.CurrentGenerationId;
        repository.RecordBaseMovementDeferred(new BaseMovementUpdate { GenerationId = first, CaptureId = "slot-one", CapturedMeters = 12, CollectionStartedUtc = Now });
        Assert.False(repository.Open(Identity()).InterruptedSessionRecovered);
        Assert.Equal(first, repository.CurrentGenerationId);
        repository.Open(Identity(2));
        Assert.NotEqual(first, repository.CurrentGenerationId);
        Assert.Null(repository.Current.Statistics.BaseMovement);
        repository.Open(Identity());
        Assert.Equal(12, repository.Current.Statistics.BaseMovement!.RecordedMeters);
        repository.CloseClean();
    }

    [Fact]
    public void UnreadableLegacySessionStillRecordsOneInterruptionWithoutInventingIdentity()
    {
        using var directory = new TemporaryDirectory();
        using (var legacy = new ProfileRepository(directory.Path, () => Now, () => Guid.NewGuid().ToString("N")))
        { legacy.Open(Identity()); legacy.CloseClean(); }
        var session = Path.Combine(directory.Path, "profiles", "slot-01", "current", "session.json");
        File.WriteAllText(session, "{broken-session");
        using var repository = Repository(directory.Path);
        Assert.True(repository.Open(Identity()).InterruptedSessionRecovered);
        Assert.Equal(1, repository.Current.InterruptedSessionCount);
        Assert.Equal("{broken-session", File.ReadAllText(session));
        repository.CloseClean();
        using var reopened = Repository(directory.Path);
        Assert.False(reopened.Open(Identity()).InterruptedSessionRecovered);
        Assert.Equal(1, reopened.Current.InterruptedSessionCount);
        reopened.CloseClean();
    }

    [Fact]
    public void ResetPromotesPreparedGenerationAndPreservesTheOldDatabaseWithItsWal()
    {
        using var directory = new TemporaryDirectory();
        using var repository = Repository(directory.Path);
        repository.Open(Identity());
        var oldGeneration = repository.CurrentGenerationId;
        repository.RecordBaseMovementDeferred(new BaseMovementUpdate { GenerationId = oldGeneration, CaptureId = "base", CapturedMeters = 9, CollectionStartedUtc = Now });
        repository.Rotate(Identity(), "UserReset");
        Assert.NotEqual(oldGeneration, repository.CurrentGenerationId);
        Assert.Null(repository.Current.Statistics.BaseMovement);
        var archive = Assert.Single(Directory.GetDirectories(Path.Combine(directory.Path, "profiles", "slot-01", "archives")));
        var archivedFiles = Directory.GetFiles(archive);
        Assert.Contains(archivedFiles, path => path.EndsWith("profile.sqlite", StringComparison.Ordinal));
        Assert.All(archivedFiles, path => Assert.True((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0));
        repository.CloseClean();
        // Read a copy because archived evidence is intentionally immutable.
        var copied = Path.Combine(directory.Path, "archive-copy"); Directory.CreateDirectory(copied);
        foreach (var file in archivedFiles)
        { var target = Path.Combine(copied, Path.GetFileName(file)); File.Copy(file, target); File.SetAttributes(target, FileAttributes.Normal); }
        using var old = new SqliteProfileStorage(Path.Combine(copied, "profile.sqlite"), Codec);
        var state = old.Load()!;
        Assert.Equal(oldGeneration, state.Profile.GenerationId);
        Assert.Equal(9, state.Profile.Statistics.BaseMovement!.RecordedMeters);
    }

    [Fact]
    public void CheckpointRecoveryCompletesOnceAndClearsItsAtomicOwnership()
    {
        using var directory = new TemporaryDirectory();
        using var repository = Repository(directory.Path);
        repository.Open(Identity());
        repository.EnableDeferredItemPersistence();
        var checkpoint = new ActiveRunCheckpoint
        {
            RunId = "recover-once",
            SaveGenerationId = repository.CurrentGenerationId,
            NativeRaidId = "42",
            StartingMapId = "duckov:map:test",
            StartingMapDisplayName = "Test",
            StartingMapKnown = true,
            StartedUtc = Now,
            LastObservedUtc = Now.AddSeconds(10),
            ActiveDurationSeconds = 10,
            LifecycleCapability = AdapterCapabilityState.Supported,
            MovementCapability = AdapterCapabilityState.Supported,
            MapCapability = AdapterCapabilityState.Supported
        };
        repository.SaveActiveRun(checkpoint);
        repository.CloseClean();
        using var recovered = Repository(directory.Path);
        Assert.True(recovered.Open(Identity()).InterruptedRunRecovered);
        Assert.Equal("recover-once", Assert.Single(recovered.Current.Statistics.Runs).RunId);
        using (var db = new SqliteStore(recovered.CurrentProfilePath!, true))
            Assert.Equal(0, db.ScalarLong("SELECT count(*) FROM records WHERE kind=23"));
        recovered.CloseClean();
        using var again = Repository(directory.Path);
        Assert.False(again.Open(Identity()).InterruptedRunRecovered);
        Assert.Single(again.Current.Statistics.Runs);
        again.CloseClean();
    }

    [Fact]
    public void FailedBoundaryKeepsDirtyOwnershipAndDoesNotPublishSuccessReceipt()
    {
        using var directory = new TemporaryDirectory();
        using var repository = Repository(directory.Path);
        repository.Open(Identity());
        using var db = new SqliteStore(repository.CurrentProfilePath!);
        db.Exec("CREATE TRIGGER fail_revision BEFORE UPDATE ON profile_state BEGIN SELECT RAISE(ABORT,'expected failure'); END");
        repository.RecordBaseMovementDeferred(new BaseMovementUpdate { GenerationId = repository.CurrentGenerationId, CaptureId = "base", CapturedMeters = 5, CollectionStartedUtc = Now });
        var receipt = repository.LastSaveReceipt;
        var clock = 0d;
        var writer = new DeferredSnapshotWriter<ProfilePersistenceSnapshot>(repository.CapturePersistenceSnapshot, repository.SaveSnapshot, () => clock);
        writer.MarkDirty();
        Assert.Equal(DeferredWriteState.Failed, writer.Flush().State);
        Assert.True(writer.IsDirty); Assert.Same(receipt, repository.LastSaveReceipt);
        db.Exec("DROP TRIGGER fail_revision");
        Assert.Equal(DeferredWriteState.Failed, writer.Flush().State);
        clock = 1;
        Assert.Equal(DeferredWriteState.Succeeded, writer.Flush().State);
        repository.CloseClean();
        using var reopened = Repository(directory.Path); reopened.Open(Identity());
        Assert.Equal(5, reopened.Current.Statistics.BaseMovement!.RecordedMeters);
        reopened.CloseClean();
    }
}
