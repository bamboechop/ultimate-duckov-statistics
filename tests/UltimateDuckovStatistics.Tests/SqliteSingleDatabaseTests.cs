using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Sqlite;

namespace UltimateDuckovStatistics.Tests;

public sealed class SqliteSingleDatabaseTests
{
    private static readonly ProfileRecordCodec Codec = new(NativeProfileJsonWriter.WriteRecord);
    private static readonly DateTime Now = NativeProfileJsonWriterTests.Now;
    private static SaveIdentitySnapshot Identity => new()
    {
        Slot = 1,
        SaveFilePresent = true,
        ContentSha256 = new string('a', 64),
        SaveTimeBinary = Now.ToBinary()
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingPrimaryReopensAndExportsWhileObsoleteRecoveryFilesAreLocked(bool damagedRecovery)
    {
        using var directory = new TemporaryDirectory();
        string path;
        byte[] expected;
        using (var repository = NativeProfileStorage.Create(directory.Path, _ => { }))
        {
            repository.Open(Identity);
            repository.RecordBaseMovementDeferred(new()
            { GenerationId = repository.CurrentGenerationId, CaptureId = "before", CapturedMeters = 7, CollectionStartedUtc = Now });
            repository.CloseClean();
            path = Path.Combine(directory.Path, "profiles", "slot-01", "current", "profile.sqlite");
        }
        // Simulate the previous build's independent copy, including its WAL.
        var oldFiles = CopyRecoveryFiles(path);
        if (damagedRecovery) File.WriteAllText(path + ".recovery", "obsolete damaged recovery");
        var bytes = oldFiles.ToDictionary(file => file, File.ReadAllBytes);
        var locks = oldFiles.Select(file => new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None)).ToArray();
        try
        {
            using var repository = NativeProfileStorage.Create(directory.Path, _ => { });
            repository.Open(Identity);
            Assert.Equal(7, repository.Current.Statistics.BaseMovement!.RecordedMeters);
            Assert.True(repository.RecordBaseMovementDeferred(new()
            { GenerationId = repository.CurrentGenerationId, CaptureId = "after", CapturedMeters = 5, CollectionStartedUtc = Now }));
            repository.Flush();
            using var export = await repository.CaptureExportSnapshotAsync();
            Assert.Equal(12, export.Document.Statistics.BaseMovement!.RecordedMeters);
            Assert.Equal(Codec.Encode(repository.Current), Codec.Encode(export.Document));
            expected = Codec.Encode(repository.Current);
            repository.CloseClean();
        }
        finally { foreach (var handle in locks) handle.Dispose(); }
        foreach (var file in bytes) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
        // Removing the obsolete files requires no schema conversion or recovery step.
        foreach (var file in oldFiles) File.Delete(file);
        using var reopened = NativeProfileStorage.Create(directory.Path, _ => { });
        reopened.Open(Identity);
        Assert.Equal(expected, Codec.Encode(reopened.Current));
        reopened.CloseClean();
        Assert.Empty(Directory.EnumerateFiles(Path.GetDirectoryName(path)!, "*.recovery*"));
        Assert.False(File.Exists(path + ".pair-owner"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrDamagedPrimaryCannotSelectObsoleteRecoveryOrStaleJson(bool missing)
    {
        using var directory = new TemporaryDirectory();
        using (var json = new ProfileRepository(directory.Path, () => Now, () => Guid.NewGuid().ToString("N")))
        { json.Open(Identity); json.CloseClean(); }
        var current = Path.Combine(directory.Path, "profiles", "slot-01", "current");
        var jsonFiles = Directory.GetFiles(current).ToDictionary(file => file, File.ReadAllBytes);
        using (var repository = NativeProfileStorage.Create(directory.Path, _ => { }))
        {
            repository.Open(Identity);
            repository.RecordBaseMovementDeferred(new()
            { GenerationId = repository.CurrentGenerationId, CaptureId = "newer", CapturedMeters = 9, CollectionStartedUtc = Now });
            repository.CloseClean();
        }
        var path = Path.Combine(current, "profile.sqlite");
        var oldFiles = CopyRecoveryFiles(path).ToDictionary(file => file, File.ReadAllBytes);
        foreach (var suffix in new[] { "-wal", "-shm" }) File.Delete(path + suffix);
        if (missing) File.Delete(path); else File.WriteAllText(path, "damaged primary");
        var failed = NativeProfileStorage.Create(directory.Path, _ => { });
        try
        {
            if (missing) Assert.Throws<InvalidDataException>(() => failed.Open(Identity));
            else Assert.Throws<SqliteFailure>(() => failed.Open(Identity));
        }
        finally { try { failed.Dispose(); } catch (IOException) { } }
        foreach (var file in oldFiles.Concat(jsonFiles)) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
        if (missing) Assert.False(File.Exists(path));
        else Assert.Equal("damaged primary", File.ReadAllText(path));
    }

    [Fact]
    public async Task CorruptUnloadedRunBlocksWritesAndReopenWithoutAutomaticRecovery()
    {
        using var directory = new TemporaryDirectory();
        SqliteLibrary.Initialize(Path.Combine(AppContext.BaseDirectory, "sqlite3.dll"));
        var path = Path.Combine(directory.Path, "profile.sqlite");
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
        var storage = new SqliteProfileStorage(path, Codec);
        storage.Import(profile, null, null);
        var state = storage.Load()!;
        using (var corruptor = new SqliteStore(path)) corruptor.Exec("UPDATE records SET payload=? WHERE kind=17", new byte[] { 123, 125 });
        Assert.Throws<InvalidDataException>(() => state.Profile.Statistics.Runs[0]);
        Assert.True(File.Exists(path + ".read-failure"));
        var journal = new ProfileChangeJournal(profile.GenerationId, Codec);
        await Assert.ThrowsAsync<InvalidDataException>(() => storage.Commit(journal.Capture(state.Profile)));
        Assert.Throws<InvalidDataException>(() => storage.Dispose());
        var reopened = new SqliteProfileStorage(path, Codec);
        Assert.Throws<InvalidDataException>(() => reopened.Load());
        Assert.Throws<InvalidDataException>(() => reopened.Dispose());
        Assert.True(File.Exists(path + ".read-failure"));
        using var lease = new FileStream(path + ".owner", FileMode.Open, FileAccess.ReadWrite, FileShare.None);
    }

    [Theory]
    [InlineData("-wal")]
    [InlineData("-shm")]
    [InlineData(".read-failure")]
    public void MissingPrimaryWithRemainingSingleDatabaseEvidenceCannotImportStaleJson(string suffix)
    {
        using var directory = new TemporaryDirectory();
        using (var json = new ProfileRepository(directory.Path, () => Now, () => Guid.NewGuid().ToString("N")))
        { json.Open(Identity); json.CloseClean(); }
        var current = Path.Combine(directory.Path, "profiles", "slot-01", "current");
        var jsonFiles = Directory.GetFiles(current).ToDictionary(file => file, File.ReadAllBytes);
        var path = Path.Combine(current, "profile.sqlite");
        File.WriteAllText(path + suffix, "retained database evidence");
        using var failed = NativeProfileStorage.Create(directory.Path, _ => { });
        Assert.Throws<InvalidDataException>(() => failed.Open(Identity));
        Assert.False(File.Exists(path));
        Assert.Equal("retained database evidence", File.ReadAllText(path + suffix));
        foreach (var file in jsonFiles) Assert.Equal(file.Value, File.ReadAllBytes(file.Key));
    }

    [Fact]
    public void FailedInitialImportLeaseAloneDoesNotPreventJsonImportRetry()
    {
        using var directory = new TemporaryDirectory();
        using (var json = new ProfileRepository(directory.Path, () => Now, () => Guid.NewGuid().ToString("N")))
        { json.Open(Identity); json.CloseClean(); }
        var path = Path.Combine(directory.Path, "profiles", "slot-01", "current", "profile.sqlite");
        File.WriteAllBytes(path + ".owner", []);
        using var repository = NativeProfileStorage.Create(directory.Path, _ => { });
        repository.Open(Identity);
        Assert.Equal(path, repository.CurrentProfilePath);
        repository.CloseClean();
        Assert.True(File.Exists(path));
    }

    private static string[] CopyRecoveryFiles(string path)
    {
        var files = new List<string>();
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            if (!File.Exists(path + suffix)) continue;
            var target = path + ".recovery" + suffix;
            File.Copy(path + suffix, target);
            files.Add(target);
        }
        foreach (var suffix in new[] { ".recovery.owner", ".recovery.read-failure", ".pair-owner" })
        {
            File.WriteAllText(path + suffix, "obsolete ownership evidence");
            files.Add(path + suffix);
        }
        return files.ToArray();
    }
}
