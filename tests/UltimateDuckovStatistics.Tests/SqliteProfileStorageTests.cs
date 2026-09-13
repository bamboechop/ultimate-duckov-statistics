using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Sqlite;
using Xunit;

namespace UltimateDuckovStatistics.Tests;

public sealed class SqliteProfileStorageTests : IDisposable
{
    private readonly string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "uds-sqlite-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ProfileRecordCodec codec = new(NativeProfileJsonWriter.WriteRecord);
    public SqliteProfileStorageTests()
    { Directory.CreateDirectory(directory); SqliteLibrary.Initialize(System.IO.Path.Combine(AppContext.BaseDirectory, "sqlite3.dll")); }
    private string DatabasePath => System.IO.Path.Combine(directory, "profile.sqlite");

    [Fact]
    public async Task ChangedValueCommitsWithoutRewritingHistoryAndReopensExactly()
    {
        var profile = NativeProfileJsonWriterTests.CreateProfile();
        using (var storage = new SqliteProfileStorage(DatabasePath, codec))
        {
            storage.Import(profile, null, null);
            var journal = new ProfileChangeJournal(profile.GenerationId, codec);
            profile.Revision++;
            profile.Statistics.BaseMovement = new BaseMovementStatistics { RecordedMeters = 1.125, CollectionStartedUtc = profile.CreatedUtc };
            journal.BaseMovement();
            var command = journal.Capture(profile);
            Assert.Equal(2, command.ChangedRecordCount);
            await storage.Commit(command); journal.Acknowledge(command);
            await storage.Commit(command); // Exact latest replay is success.
        }
        using var reopened = new SqliteProfileStorage(DatabasePath, codec);
        var loaded = Assert.IsType<IncrementalProfileState>(reopened.Load());
        Assert.Equal(codec.Encode(profile), codec.Encode(loaded.Profile));
        using var db = new SqliteStore(DatabasePath, true);
        Assert.Equal(1, db.ScalarLong("SELECT count(*) FROM receipt"));
    }

    [Fact]
    public async Task FailedTransactionRetainsDirtyUnionAndRetriesWithNewerValues()
    {
        var profile = NativeProfileJsonWriterTests.CreateProfile();
        using var storage = new SqliteProfileStorage(DatabasePath, codec);
        storage.Import(profile, null, null);
        var journal = new ProfileChangeJournal(profile.GenerationId, codec);
        profile.Revision++;
        profile.Statistics.BaseMovement = new BaseMovementStatistics { RecordedMeters = 4, CollectionStartedUtc = profile.CreatedUtc };
        journal.BaseMovement();
        using var blocker = new SqliteStore(DatabasePath);
        blocker.Exec("CREATE TRIGGER fail_revision BEFORE UPDATE ON profile_state BEGIN SELECT RAISE(ABORT,'injected revision failure'); END");
        var failed = journal.Capture(profile);
        await Assert.ThrowsAsync<SqliteFailure>(() => storage.Commit(failed));
        Assert.Null(blocker.Blob("SELECT payload FROM records WHERE kind=3"));
        Assert.Equal(0, blocker.ScalarLong("SELECT count(*) FROM receipt"));
        blocker.Exec("DROP TRIGGER fail_revision");
        profile.Revision++;
        profile.Statistics.BaseMovement.RecordedMeters = 7;
        journal.BaseMovement();
        var retry = journal.Capture(profile);
        await storage.Commit(retry); journal.Acknowledge(retry);
        Assert.Equal(7, storage.Load()!.Profile.Statistics.BaseMovement!.RecordedMeters);
    }

    [Fact]
    public async Task BusyCommandDoesNotAcknowledgeAndLatestUnionCanProceed()
    {
        var profile = NativeProfileJsonWriterTests.CreateProfile();
        using var storage = new SqliteProfileStorage(DatabasePath, codec);
        storage.Import(profile, null, null);
        // Establish WAL before taking the competing write lock.
        Assert.NotNull(storage.Load());
        var journal = new ProfileChangeJournal(profile.GenerationId, codec);
        profile.Revision++;
        using var blocker = new SqliteStore(DatabasePath);
        blocker.Exec("BEGIN IMMEDIATE");
        var command = journal.Capture(profile);
        var failure = await Assert.ThrowsAsync<SqliteFailure>(() => storage.Commit(command));
        Assert.Equal(5, failure.Code);
        blocker.Exec("ROLLBACK");
        await storage.Commit(command); journal.Acknowledge(command);
    }

    [Fact]
    public async Task OmittedPredecessorConflictingReplayAndForeignGenerationReject()
    {
        var profile = NativeProfileJsonWriterTests.CreateProfile();
        using var storage = new SqliteProfileStorage(DatabasePath, codec);
        storage.Import(profile, null, null);
        var journal = new ProfileChangeJournal(profile.GenerationId, codec);
        var first = journal.Capture(profile);
        var missing = new IncrementalProfileWrite(first.GenerationId, first.Owner, first.Order, 1, first.ThroughVersion, first.Revision, first.Records);
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.Commit(missing));
        await storage.Commit(first);
        var conflict = new IncrementalProfileWrite(first.GenerationId, first.Owner, first.Order, first.CoveredAfter, first.ThroughVersion + 1, first.Revision, first.Records);
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.Commit(conflict));
        var foreign = new IncrementalProfileWrite("foreign", first.Owner, first.Order + 1, 0, first.ThroughVersion, first.Revision, first.Records);
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.Commit(foreign));
        await storage.Commit(first); // Clear the observed failed tail for disposal.
    }

    [Fact]
    public async Task PositiveButInconsistentCraftingFanOutRollsBackAllRows()
    {
        var profile = NativeProfileJsonWriterTests.CreateProfile();
        using var storage = new SqliteProfileStorage(DatabasePath, codec);
        storage.Import(profile, null, null);
        var journal = new ProfileChangeJournal(profile.GenerationId, codec);
        var mutation = new CraftingMutation(profile.GenerationId, profile.CreatedUtc,
            new[] { new CraftingMutationRow("100", "Bandage", "formula", 1, 3, new Dictionary<string, long> { ["3"] = 1 }) });
        Assert.True(CraftingStatisticsReducer.Apply(profile.Statistics.Crafting, profile.GenerationId, mutation));
        profile.Revision++;
        journal.Crafting(mutation);
        profile.Statistics.Crafting.Outputs["100"].ProducedQuantity++;
        await Assert.ThrowsAsync<InvalidDataException>(() => storage.Commit(journal.Capture(profile)));
        using var db = new SqliteStore(DatabasePath, true);
        Assert.Equal(0, db.ScalarLong("SELECT count(*) FROM crafting"));
        profile.Statistics.Crafting.Outputs["100"].ProducedQuantity--;
        await storage.Commit(journal.Capture(profile));
        Assert.Equal(3, storage.Load()!.Profile.Statistics.Crafting.ProducedQuantity);
    }

    [Fact]
    public void ImportNeverOverwritesExistingDatabaseAndExclusiveLeaseIsReleased()
    {
        var profile = NativeProfileJsonWriterTests.CreateProfile();
        using (var storage = new SqliteProfileStorage(DatabasePath, codec))
        {
            storage.Import(profile, null, null);
            Assert.Throws<IOException>(() => new SqliteProfileStorage(DatabasePath, codec));
        }
        using var reopened = new SqliteProfileStorage(DatabasePath, codec);
        Assert.Equal(profile.GenerationId, reopened.Load()!.Profile.GenerationId);
    }

    public void Dispose() => Directory.Delete(directory, true);
}
