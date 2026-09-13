using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Export;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;
using UltimateDuckovStatistics.Sqlite;

namespace UltimateDuckovStatistics.Tests;

public sealed class SqliteExportTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "uds-export-sqlite-" + Guid.NewGuid().ToString("N"));
    private readonly ProfileRecordCodec codec = new(NativeProfileJsonWriter.WriteRecord);
    private static readonly DateTime Now = NativeProfileJsonWriterTests.Now;
    public SqliteExportTests()
    { Directory.CreateDirectory(directory); SqliteLibrary.Initialize(Path.Combine(AppContext.BaseDirectory, "sqlite3.dll")); }

    [Fact]
    public async Task DetachedRevisionSurvivesLaterWritesAndOwnerCloseWithAllExportBytesEqual()
    {
        var profile = Profile();
        var database = Path.Combine(directory, "profile.sqlite");
        var storage = new SqliteProfileStorage(database, codec);
        storage.Import(profile, null, null);
        var expected = ProfileExportWriter.Write(profile, Path.Combine(directory, "original", "profile.json"), Now);
        var captured = storage.CaptureExport(profile.GenerationId, profile.Revision);
        profile.Revision++;
        profile.UpdatedUtc = Now.AddMinutes(1);
        var changes = new ProfileChangeJournal(profile.GenerationId, codec);
        changes.Metadata();
        await storage.Commit(changes.Capture(profile));
        using (var snapshot = await captured)
        {
            storage.Dispose();
            Assert.Equal(0, snapshot.Document.Revision);
            var result = ProfileExportWriter.WriteToRoot(snapshot, Path.Combine(directory, "streamed"), Now);
            Assert.Equal(37, result.Files.Count);
            foreach (var file in expected.Files)
                Assert.Equal(File.ReadAllBytes(file), File.ReadAllBytes(Path.Combine(result.Directory, Path.GetFileName(file))));
        }
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(directory, "export-staging")));
    }

    [Fact]
    public async Task FirstExportEstablishesWalBeforeAnyRoutineCommit()
    {
        var profile = Profile();
        using var storage = new SqliteProfileStorage(Path.Combine(directory, "profile.sqlite"), codec);
        storage.Import(profile, null, null);
        using (var imported = new SqliteStore(storage.Path, readOnly: true))
            Assert.Equal("delete", imported.ScalarText("PRAGMA journal_mode"));
        using var snapshot = await storage.CaptureExport(profile.GenerationId, profile.Revision);
        using var live = new SqliteStore(storage.Path, readOnly: true);
        Assert.Equal("wal", live.ScalarText("PRAGMA journal_mode"));
        Assert.Equal(codec.Encode(profile), codec.Encode(snapshot.Document));
    }

    [Fact]
    public async Task WrongRevisionRejectsExportWithoutPoisoningDurableWrites()
    {
        var profile = Profile();
        using var storage = new SqliteProfileStorage(Path.Combine(directory, "profile.sqlite"), codec);
        storage.Import(profile, null, null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.CaptureExport(profile.GenerationId, 42));
        profile.Revision++;
        var changes = new ProfileChangeJournal(profile.GenerationId, codec); changes.Metadata();
        await storage.Commit(changes.Capture(profile));
        using var snapshot = await storage.CaptureExport(profile.GenerationId, profile.Revision);
        Assert.Equal(profile.Revision, snapshot.Document.Revision);
    }

    private static ProfileDocument Profile()
    {
        var profile = NativeProfileJsonWriterTests.CreateProfile();
        for (var index = 0; index < 3; index++)
        {
            var tracker = IncrementalCheckpointProtocolTests.Started(profile.GenerationId, route: true, runId: "run-" + (3 - index));
            var shot = WeaponStatisticsTests.Shot("shot-" + index, "weapon-" + index, "Weapon 雪,\"", "ammo", "Ammo", 2);
            shot.SaveGenerationId = profile.GenerationId; shot.RunId = tracker.ActiveRunId!; shot.SegmentId = tracker.ActiveSegmentId;
            Assert.True(tracker.RecordShot(shot));
            var run = tracker.Apply(new RunLifecycleEvent
            {
                Kind = RunLifecycleEventKind.Extracted,
                TimestampUtc = new DateTime(2026, 8, 10, 10, 0, 0, DateTimeKind.Utc).AddSeconds(10),
                MonotonicSeconds = 10
            }).Completed!;
            Assert.True(RunReducer.Apply(profile.Statistics, run));
        }
        return profile;
    }

    [Fact]
    public async Task NativeFactoryExportRemainsReadableAfterUserResetArchivesTheGeneration()
    {
        var root = Path.Combine(directory, "native-factory");
        using var repository = NativeProfileStorage.Create(root, _ => { });
        var identity = new SaveIdentitySnapshot { Slot = 1, SaveFilePresent = true, ContentSha256 = new string('a', 64), SaveTimeBinary = Now.ToBinary() };
        repository.Open(identity);
        repository.RecordBaseMovementDeferred(new() { GenerationId = repository.CurrentGenerationId, CaptureId = "base", CapturedMeters = 8, CollectionStartedUtc = Now });
        repository.Flush();
        using var snapshot = await repository.CaptureExportSnapshotAsync();
        var previous = repository.CurrentGenerationId;
        repository.Rotate(identity, "UserReset");
        Assert.NotEqual(previous, repository.CurrentGenerationId);
        var exported = ProfileExportWriter.WriteToRoot(snapshot, Path.Combine(directory, "reset-export"), Now);
        Assert.Equal(37, exported.Files.Count);
        Assert.Equal(previous, snapshot.Document.GenerationId);
        Assert.Equal(8, snapshot.Document.Statistics.BaseMovement!.RecordedMeters);
    }

    public void Dispose()
    {
        if (!Directory.Exists(directory)) return;
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories)) File.SetAttributes(file, FileAttributes.Normal);
        Directory.Delete(directory, true);
    }
}
