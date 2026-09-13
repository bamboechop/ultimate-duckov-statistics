using System.Reflection;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Sqlite;

namespace UltimateDuckovStatistics.Tests;

public sealed class SqliteFullFailureTests
{
    [Fact]
    public async Task SqliteFullRollsBackAndDirtyOwnerCanRetryNewerValue()
    {
        using var directory = new TemporaryDirectory();
        SqliteLibrary.Initialize(Path.Combine(AppContext.BaseDirectory, "sqlite3.dll"));
        var codec = new ProfileRecordCodec(NativeProfileJsonWriter.WriteRecord);
        var profile = NativeProfileJsonWriterTests.CreateProfile();
        using var storage = new SqliteProfileStorage(Path.Combine(directory.Path, "profile.sqlite"), codec);
        storage.Import(profile, null, null); _ = storage.Load();
        // Impose SQLite's real page-allocation limit on the actual worker's
        // connection. No production fault-injection API or host disk filling.
        var connection = (SqliteStore)typeof(SqliteProfileStorage).GetField("connection", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(storage)!;
        var pages = connection.ScalarLong("PRAGMA page_count");
        Assert.Equal(pages, connection.ScalarLong("PRAGMA max_page_count=" + pages));
        var changes = new ProfileChangeJournal(profile.GenerationId, codec);
        profile.GenerationReason = new string('x', 1024 * 1024); profile.Revision++;
        changes.Metadata();
        var failed = await Assert.ThrowsAsync<SqliteFailure>(() => storage.Commit(changes.Capture(profile)));
        Assert.Equal(13, failed.Code & 255); // SQLITE_FULL from the real engine.
        using (var reader = new SqliteStore(storage.Path, readOnly: true))
        {
            Assert.Equal(0, reader.ScalarLong("SELECT revision FROM profile_state"));
            Assert.Equal(0, reader.ScalarLong("SELECT count(*) FROM receipt"));
            Assert.Equal("ok", reader.ScalarText("PRAGMA integrity_check"));
        }
        connection.ScalarLong("PRAGMA max_page_count=1073741823");
        profile.GenerationReason = "accepted retry"; profile.Revision++; changes.Metadata();
        var retry = changes.Capture(profile); await storage.Commit(retry); changes.Acknowledge(retry);
        Assert.Equal("accepted retry", storage.Load()!.Profile.GenerationReason);
    }
}
