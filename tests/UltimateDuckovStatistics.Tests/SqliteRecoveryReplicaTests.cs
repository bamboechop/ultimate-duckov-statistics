using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Sqlite;

namespace UltimateDuckovStatistics.Tests;

public sealed class SqliteRecoveryReplicaTests
{
    private static readonly ProfileRecordCodec Codec = new(NativeProfileJsonWriter.WriteRecord);
    private static RecoverableSqliteProfileStorage Storage(string path)
    { SqliteLibrary.Initialize(Path.Combine(AppContext.BaseDirectory, "sqlite3.dll")); return new(path, Codec); }

    [Fact]
    public async Task ReplicaFailureCannotAcknowledgeAndNewerUnionRepairsBothBeforeSuccess()
    {
        using var directory = new TemporaryDirectory(); var path = Path.Combine(directory.Path, "profile.sqlite");
        var profile = NativeProfileJsonWriterTests.CreateProfile();
        using var storage = Storage(path); storage.Import(profile, null, null);
        var journal = new ProfileChangeJournal(profile.GenerationId, Codec);
        profile.Statistics.BaseMovement = new BaseMovementStatistics { CollectionStartedUtc = profile.CreatedUtc, RecordedMeters = 3 };
        profile.Revision++; journal.BaseMovement(); var first = journal.Capture(profile);
        using (var blocker = new SqliteStore(storage.RecoveryPath))
        {
            blocker.Exec("CREATE TRIGGER replica_failure BEFORE UPDATE ON profile_state BEGIN SELECT RAISE(ABORT,'replica failure'); END");
            await Assert.ThrowsAsync<SqliteFailure>(() => storage.Commit(first));
            using var primary = new SqliteStore(path, true);
            Assert.Equal(profile.Revision, primary.ScalarLong("SELECT revision FROM profile_state"));
            Assert.NotEqual(profile.Revision, blocker.ScalarLong("SELECT revision FROM profile_state"));
            blocker.Exec("DROP TRIGGER replica_failure");
        }
        profile.Revision++; profile.Statistics.BaseMovement.RecordedMeters = 8; journal.BaseMovement();
        var latest = journal.Capture(profile); await storage.Commit(latest); journal.Acknowledge(latest);
        Assert.Equal(8, storage.Load()!.Profile.Statistics.BaseMovement!.RecordedMeters);
        using var firstReader = new SqliteStore(path, true); using var secondReader = new SqliteStore(storage.RecoveryPath, true);
        Assert.Equal(firstReader.Blob("SELECT payload FROM records WHERE kind=3"), secondReader.Blob("SELECT payload FROM records WHERE kind=3"));
        Assert.Equal(firstReader.Blob("SELECT digest FROM receipt"), secondReader.Blob("SELECT digest FROM receipt"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CorruptOrMissingPrimaryRecoversAcknowledgedStateAndPreservesEvidence(bool missing)
    {
        using var directory = new TemporaryDirectory(); var path = Path.Combine(directory.Path, "profile.sqlite");
        var profile = NativeProfileJsonWriterTests.CreateProfile();
        using (var storage = Storage(path))
        {
            storage.Import(profile, null, null); var journal = new ProfileChangeJournal(profile.GenerationId, Codec);
            profile.Revision++; profile.Statistics.BaseMovement = new BaseMovementStatistics { RecordedMeters = 7, CollectionStartedUtc = profile.CreatedUtc };
            journal.BaseMovement(); await storage.Commit(journal.Capture(profile));
        }
        if (missing) File.Move(path, path + ".removed"); else File.WriteAllText(path, "damaged primary");
        using var recovered = Storage(path);
        var state = recovered.Load()!;
        Assert.Equal(Codec.Encode(profile), Codec.Encode(state.Profile));
        if (!missing) Assert.Contains(Directory.GetDirectories(directory.Path), value => Path.GetFileName(value).StartsWith("profile.sqlite.preserved-", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CorruptUnloadedRunStopsAcknowledgementAndReopenRecoversItsIndependentCopy()
    {
        using var directory = new TemporaryDirectory(); var path = Path.Combine(directory.Path, "profile.sqlite");
        var profile = NativeProfileJsonWriterTests.CreateProfile();
        var checkpoint = new ActiveRunCheckpoint
        {
            RunId = "retained",
            SaveGenerationId = profile.GenerationId,
            StartedUtc = profile.CreatedUtc,
            LastObservedUtc = profile.CreatedUtc.AddSeconds(10),
            ActiveDurationSeconds = 10,
            LifecycleCapability = AdapterCapabilityState.Supported,
            MovementCapability = AdapterCapabilityState.Supported
        };
        RunReducer.Apply(profile.Statistics, checkpoint.ToRecoverySummary());
        var storage = Storage(path); storage.Import(profile, null, null);
        var state = storage.Load()!;
        using (var corruptor = new SqliteStore(path)) corruptor.Exec("UPDATE records SET payload=? WHERE kind=17", new byte[] { 123, 125 });
        Assert.Throws<InvalidDataException>(() => state.Profile.Statistics.Runs[0]);
        Assert.True(File.Exists(path + ".read-failure"));
        var journal = new ProfileChangeJournal(profile.GenerationId, Codec);
        await Assert.ThrowsAsync<InvalidDataException>(() => storage.Commit(journal.Capture(state.Profile)));
        Assert.Throws<InvalidDataException>(() => storage.Dispose());
        using var recovered = Storage(path);
        var restored = recovered.Load()!;
        Assert.Equal(Codec.Encode(profile), Codec.Encode(restored.Profile));
        Assert.False(File.Exists(path + ".read-failure"));
        Assert.Single(Directory.GetDirectories(directory.Path, "profile.sqlite.preserved-*"));
    }

    [Fact]
    public void BothDamagedCopiesFailClosedWithoutSelectingLegacyJson()
    {
        using var directory = new TemporaryDirectory(); var path = Path.Combine(directory.Path, "profile.sqlite");
        using (var storage = Storage(path)) storage.Import(NativeProfileJsonWriterTests.CreateProfile(), null, null);
        File.WriteAllText(path, "primary broken"); File.WriteAllText(path + ".recovery", "replica broken");
        var failed = Storage(path);
        Assert.Throws<SqliteFailure>(() => failed.Load());
        Assert.Throws<SqliteFailure>(() => failed.Dispose());
        Assert.Equal("primary broken", File.ReadAllText(path)); Assert.Equal("replica broken", File.ReadAllText(path + ".recovery"));
    }
}
