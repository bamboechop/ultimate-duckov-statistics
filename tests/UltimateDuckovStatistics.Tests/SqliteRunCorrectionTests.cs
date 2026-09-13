using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;
using UltimateDuckovStatistics.Sqlite;

namespace UltimateDuckovStatistics.Tests;

public sealed class SqliteRunCorrectionTests : IDisposable
{
    private readonly TemporaryDirectory directory = new();
    private readonly ProfileRecordCodec codec = new(NativeProfileJsonWriter.WriteRecord);
    private readonly ProfileDocument input;
    private readonly ProfileRepository repository;
    private static readonly DateTime Now = NativeProfileJsonWriterTests.Now;
    public SqliteRunCorrectionTests()
    {
        SqliteLibrary.Initialize(Path.Combine(AppContext.BaseDirectory, "sqlite3.dll"));
        input = NativeProfileJsonWriterTests.CreateProfile();
        input.Identity = new SaveIdentitySnapshot { Slot = 1, SaveFilePresent = true, ContentSha256 = new string('a', 64), SaveTimeBinary = Now.ToBinary() };
        foreach (var id in new[] { "first", "second" }) Assert.True(RunReducer.Apply(input.Statistics, Run(input.GenerationId, id)));
        var current = Path.Combine(directory.Path, "profiles", "slot-01", "current"); Directory.CreateDirectory(current);
        new AtomicJsonStore<ProfileDocument>().Save(Path.Combine(current, "profile.json"), input);
        repository = Repository(); repository.Open(input.Identity);
    }
    private ProfileRepository Repository() => new(directory.Path, () => Now, () => Guid.NewGuid().ToString("N"),
        writeProfile: NativeProfileJsonWriter.Write, createIncrementalStorage: path => new SqliteProfileStorage(path, codec), recordCodec: codec);

    [Fact]
    public async Task CorrectionRebuildsAffectedRecordsAndRemovesObsoleteEntriesWithoutRewritingOtherRuns()
    {
        var replacement = CopyFirst(); replacement.WeaponStatistics = new(); replacement.ActiveDurationSeconds = 12;
        var prepared = await repository.PrepareRunCorrection(replacement);
        byte[] previousSecond;
        using (var db = new SqliteStore(repository.CurrentProfilePath!, readOnly: true)) previousSecond = db.Blob("SELECT payload FROM records WHERE kind=17 AND k1='second'")!;
        repository.ApplyRunCorrection(prepared);
        var revision = repository.Current.Revision;
        repository.ApplyRunCorrection(prepared); Assert.Equal(revision, repository.Current.Revision);
        Assert.False(repository.Current.Statistics.RunTotals.WeaponStatistics.Weapons.ContainsKey("first"));
        Assert.True(repository.Current.Statistics.RunTotals.WeaponStatistics.Weapons.ContainsKey("second"));
        Assert.Equal(12, repository.Current.Statistics.RunRecords.Extraction.Longest!.ActiveDurationSeconds);
        var expected = codec.Encode(repository.Current);
        using (var db = new SqliteStore(repository.CurrentProfilePath!, readOnly: true))
            Assert.Equal(previousSecond, db.Blob("SELECT payload FROM records WHERE kind=17 AND k1='second'"));
        repository.CloseClean();
        using var reopened = Repository(); reopened.Open(input.Identity);
        Assert.Equal(expected, codec.Encode(reopened.Current));
        Assert.Null(ProfileFormat.ValidateRecoveryCandidate(reopened.Current));
    }

    [Fact]
    public async Task StalePreparationRejectsWithoutLosingInterveningMovement()
    {
        var replacement = CopyFirst(); replacement.ActiveDurationSeconds = 12;
        var prepared = await repository.PrepareRunCorrection(replacement);
        repository.RecordBaseMovementDeferred(new BaseMovementUpdate { GenerationId = repository.CurrentGenerationId, CaptureId = "new-movement", CapturedMeters = 7, CollectionStartedUtc = Now });
        Assert.Throws<InvalidOperationException>(() => repository.ApplyRunCorrection(prepared));
        Assert.Equal(7, repository.Current.Statistics.BaseMovement!.RecordedMeters);
        Assert.Equal(10, RunHistory.GetById(repository.Current.Statistics.Runs, "first").ActiveDurationSeconds);
    }

    [Fact]
    public async Task FailedPrimaryRetainsCorrectionForNormalRetryAndNextCompletion()
    {
        var replacement = CopyFirst(); replacement.WeaponStatistics = new();
        var prepared = await repository.PrepareRunCorrection(replacement);
        using (var locked = new SqliteStore(repository.CurrentProfilePath!))
        {
            locked.Exec("BEGIN IMMEDIATE");
            Assert.Throws<SqliteFailure>(() => repository.ApplyRunCorrection(prepared));
            locked.Exec("ROLLBACK");
        }
        Assert.True(repository.CompleteRun(Run(input.GenerationId, "third")));
        repository.Flush();
        Assert.False(repository.Current.Statistics.RunTotals.WeaponStatistics.Weapons.ContainsKey("first"));
        Assert.Equal(2, repository.Current.Statistics.RunTotals.WeaponStatistics.Weapons.Count);
        var expected = codec.Encode(repository.Current);
        repository.CloseClean();
        using var reopened = Repository(); reopened.Open(input.Identity);
        Assert.Equal(expected, codec.Encode(reopened.Current));
    }

    private RunSummary CopyFirst() => ProfileRecordCodec.Decode<RunSummary>(codec.Encode(RunHistory.GetById(repository.Current.Statistics.Runs, "first")));

    [Fact]
    public async Task CorrectionRejectsWhenRetainedRunsDoNotExplainIndependentLifetimeEvidence()
    {
        var root = Path.Combine(directory.Path, "independent-evidence");
        var profile = ProfileRecordCodec.Decode<ProfileDocument>(codec.Encode(input));
        profile.Statistics.RunTotals.PhysicalDistance += 1;
        Assert.Null(ProfileFormat.ValidateRecoveryCandidate(profile));
        var current = Path.Combine(root, "profiles", "slot-01", "current");
        Directory.CreateDirectory(current);
        new AtomicJsonStore<ProfileDocument>().Save(Path.Combine(current, "profile.json"), profile);
        using var owner = new ProfileRepository(root, () => Now, () => Guid.NewGuid().ToString("N"),
            writeProfile: NativeProfileJsonWriter.Write, createIncrementalStorage: path => new SqliteProfileStorage(path, codec), recordCodec: codec);
        owner.Open(profile.Identity);
        var before = codec.Encode(owner.Current);
        await Assert.ThrowsAsync<InvalidOperationException>(() => owner.PrepareRunCorrection(CopyFirst()));
        Assert.Equal(before, codec.Encode(owner.Current));
    }
    private static RunSummary Run(string generation, string id)
    {
        var tracker = IncrementalCheckpointProtocolTests.Started(generation, runId: id);
        var shot = WeaponStatisticsTests.Shot("shot-" + id, id, id, "ammo", "Ammo", 2);
        shot.SaveGenerationId = generation; shot.RunId = tracker.ActiveRunId!; shot.SegmentId = tracker.ActiveSegmentId;
        Assert.True(tracker.RecordShot(shot));
        return tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.Extracted,
            TimestampUtc = new DateTime(2026, 8, 10, 10, 0, 0, DateTimeKind.Utc).AddSeconds(10),
            MonotonicSeconds = 10
        }).Completed!;
    }
    public void Dispose() { repository.Dispose(); directory.Dispose(); }
}
