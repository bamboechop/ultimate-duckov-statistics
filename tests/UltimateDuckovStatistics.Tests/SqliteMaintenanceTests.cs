using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Sqlite;

namespace UltimateDuckovStatistics.Tests;

public sealed class SqliteMaintenanceTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "uds-wal-maintenance-" + Guid.NewGuid().ToString("N"));
    private readonly ProfileRecordCodec codec = new(NativeProfileJsonWriter.WriteRecord);
    public SqliteMaintenanceTests()
    { Directory.CreateDirectory(directory); SqliteLibrary.Initialize(Path.Combine(AppContext.BaseDirectory, "sqlite3.dll")); }

    [Fact]
    public async Task PassiveMaintenancePreservesPinnedReaderAndAllowsLaterDurableCommit()
    {
        var profile = NativeProfileJsonWriterTests.CreateProfile();
        var path = Path.Combine(directory, "profile.sqlite");
        using var storage = new SqliteProfileStorage(path, codec);
        storage.Import(profile, null, null);
        var changes = new ProfileChangeJournal(profile.GenerationId, codec);
        async Task Commit()
        {
            profile.Revision++; changes.Metadata(); var write = changes.Capture(profile);
            await storage.Commit(write); changes.Acknowledge(write);
        }
        await Commit();
        using var reader = new SqliteStore(path, readOnly: true);
        reader.DisableAutomaticCheckpoint(); reader.Exec("BEGIN");
        Assert.Equal(1, reader.ScalarLong("SELECT revision FROM profile_state"));
        await Commit();
        var pinned = await storage.RequestMaintenance(ProfileMaintenanceReason.LoadingOrSleep).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(pinned.Attempted);
        Assert.True(pinned.CheckpointedFrames < pinned.WalFrames);
        await Commit();
        Assert.Equal(1, reader.ScalarLong("SELECT revision FROM profile_state"));
        reader.Exec("ROLLBACK");
        var released = await storage.RequestMaintenance(ProfileMaintenanceReason.LoadingOrSleep);
        Assert.True(released.Attempted);
        Assert.Equal(released.WalFrames, released.CheckpointedFrames);
        Assert.Equal(3, storage.Load()!.Profile.Revision);
        Assert.Equal("ok", reader.ScalarText("PRAGMA integrity_check"));
    }

    [Fact]
    public async Task SmallWalWaitsForOpportunityAndPreservesTheCommittedRevision()
    {
        var profile = NativeProfileJsonWriterTests.CreateProfile();
        using var storage = new SqliteProfileStorage(Path.Combine(directory, "profile.sqlite"), codec);
        storage.Import(profile, null, null);
        profile.Revision++;
        var changes = new ProfileChangeJournal(profile.GenerationId, codec); changes.Metadata();
        await storage.Commit(changes.Capture(profile));
        Assert.False((await storage.RequestMaintenance(ProfileMaintenanceReason.LongSession)).Attempted);
        Assert.True((await storage.RequestMaintenance(ProfileMaintenanceReason.LoadingOrSleep)).Attempted);
        using var primary = new SqliteStore(storage.Path, readOnly: true);
        Assert.Equal("ok", primary.ScalarText("PRAGMA integrity_check"));
        Assert.Equal(profile.Revision, primary.ScalarLong("SELECT revision FROM profile_state"));
        Assert.Equal(1, primary.ScalarLong("SELECT count(*) FROM receipt"));
        Assert.False(File.Exists(storage.Path + ".recovery"));
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
