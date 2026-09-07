using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Export;
using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Tests;

public sealed class ProfileUserOperationSafetyTests
{
    private static readonly DateTime TestTime = new(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void ExportSnapshotKeepsCapturedRunsTotalsRecordsAndRevisionAfterAnotherRunCompletes()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        Assert.True(repository.CompleteRun(Run(repository, "first", 30, "warehouse")));
        var snapshot = repository.CaptureExportSnapshot();
        var revision = repository.Current.Revision;

        // A user can close the panel and complete another run before its export
        // worker is scheduled or finishes serializing a large lifetime profile.
        Assert.True(repository.CompleteRun(Run(repository, "later", 90, "farm")));
        var result = ProfileExportWriter.WriteToRoot(snapshot, Path.Combine(directory.Path, "exports"), TestTime);
        var exported = new AtomicJsonStore<StatisticsExportDocument>()
            .Load(Path.Combine(result.Directory, "statistics.json")).Value!;

        Assert.Equal(revision, exported.Revision);
        Assert.True(repository.Current.Revision > exported.Revision);
        Assert.Equal("first", Assert.Single(exported.Runs).RunId);
        Assert.Equal(1, exported.RunTotals.TotalRuns);
        Assert.Single(exported.RunTotals.Maps);
        Assert.Equal("first", exported.RunRecords.Extraction.Longest!.RunId);
        Assert.Single(exported.RunRecords.Maps);
        Assert.Equal(2, repository.Current.Statistics.RunTotals.TotalRuns);
        Assert.Equal("later", repository.Current.Statistics.RunRecords.Extraction.Longest!.RunId);
        Assert.Equal(37, result.Files.Count);
        Assert.All(result.Files, path => Assert.True(File.Exists(path)));
        repository.CloseClean();
    }

    [Fact]
    public void CapturedExportRemainsWritableAtStableRootAfterOriginalGenerationIsArchived()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        Assert.True(repository.CompleteRun(Run(repository, "archived-run", 30, "warehouse")));
        var snapshot = repository.CaptureExportSnapshot();
        repository.Rotate(Identity(), "UserReset");

        var exportRoot = Path.Combine(directory.Path, "exports");
        var result = ProfileExportWriter.WriteToRoot(snapshot, exportRoot, TestTime);
        var exported = new AtomicJsonStore<StatisticsExportDocument>()
            .Load(Path.Combine(result.Directory, "statistics.json")).Value!;
        Assert.Equal(snapshot.GenerationId, exported.GenerationId);
        Assert.NotEqual(repository.CurrentGenerationId, exported.GenerationId);
        Assert.Equal("archived-run", Assert.Single(exported.Runs).RunId);
        Assert.Equal(exportRoot, Path.GetDirectoryName(result.Directory));
        Assert.Empty(repository.Current.Statistics.Runs);
        Assert.All(Directory.EnumerateFiles(Archive(repository), "*", SearchOption.AllDirectories),
            path => Assert.True((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0));
        repository.CloseClean();
    }

    [Fact]
    public void LockedPrimaryRejectsResetWithoutChangingActiveGenerationOrSession()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        Assert.True(repository.CompleteRun(Run(repository, "kept", 30, "warehouse")));
        var generation = repository.CurrentGenerationId;
        var profilePath = repository.CurrentProfilePath!;
        var sessionPath = Path.Combine(Path.GetDirectoryName(profilePath)!, "session.json");
        var session = File.ReadAllBytes(sessionPath);
        using (var held = new FileStream(profilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failure = Assert.Throws<UserProfileResetFailedException>(() => repository.Rotate(Identity(), "UserReset"));
            Assert.Equal(generation, failure.PreservedGenerationId);
            Assert.Equal(generation, repository.CurrentGenerationId);
            Assert.Equal("kept", Assert.Single(repository.Current.Statistics.Runs).RunId);
            Assert.Equal(session, File.ReadAllBytes(sessionPath));
        }
        Assert.Equal(generation, new AtomicJsonStore<ProfileDocument>().Load(profilePath).Value!.GenerationId);
        Assert.Empty(Directory.EnumerateDirectories(Slot(repository), ".uds-reset-*"));
        Assert.False(Directory.Exists(Path.Combine(Slot(repository), "archives")));
        repository.CloseClean();
    }

    [Fact]
    public void NewSessionStorageFailureCleansPreparedFilesAndPreservesActiveSession()
    {
        using var directory = new TemporaryDirectory();
        var sequence = 0;
        var repository = new ProfileRepository(directory.Path, () => TestTime, () =>
        {
            var id = ++sequence;
            if (id == 4)
            {
                // The new session cannot open its temporary file. This targets
                // the actual second staging write, after the new profile exists.
                var prepared = Assert.Single(Directory.EnumerateDirectories(directory.Path, ".uds-reset-*", SearchOption.AllDirectories));
                Directory.CreateDirectory(Path.Combine(prepared, "session.json.tmp"));
            }
            return "identity-" + id;
        });
        repository.Open(Identity());
        var generation = repository.CurrentGenerationId;
        var sessionPath = Path.Combine(Path.GetDirectoryName(repository.CurrentProfilePath!)!, "session.json");
        var session = File.ReadAllBytes(sessionPath);

        Assert.Throws<UserProfileResetFailedException>(() => repository.Rotate(Identity(), "UserReset"));

        Assert.Equal(generation, repository.CurrentGenerationId);
        Assert.Equal(session, File.ReadAllBytes(sessionPath));
        Assert.Empty(Directory.EnumerateDirectories(Slot(repository), ".uds-reset-*"));
        Assert.False(Directory.Exists(Path.Combine(Slot(repository), "archives")));
        repository.CloseClean();
    }

    [Fact]
    public void LockedFileDuringArchiveMoveLeavesAllPriorFilesWritableAndActive()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var generation = repository.CurrentGenerationId;
        var diagnosticsPath = Path.Combine(Path.GetDirectoryName(repository.CurrentProfilePath!)!, "diagnostics.json");
        File.WriteAllText(diagnosticsPath, "{}");
        using (var held = new FileStream(diagnosticsPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Throws<UserProfileResetFailedException>(() => repository.Rotate(Identity(), "UserReset"));

        Assert.Equal(generation, repository.CurrentGenerationId);
        Assert.Empty(Directory.EnumerateDirectories(Path.Combine(Slot(repository), "archives")));
        Assert.Empty(Directory.EnumerateDirectories(Slot(repository), ".uds-reset-*"));
        Assert.All(Directory.EnumerateFiles(Path.GetDirectoryName(repository.CurrentProfilePath!)!),
            path => Assert.False((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0));
        repository.CloseClean();
    }

    private static ProfileRepository Repository(string root) => new(root, () => TestTime, () => Guid.NewGuid().ToString("N"));
    private static string Slot(ProfileRepository repository) => Path.GetDirectoryName(Path.GetDirectoryName(repository.CurrentProfilePath!)!)!;
    private static string Archive(ProfileRepository repository) => Assert.Single(Directory.EnumerateDirectories(Path.Combine(Slot(repository), "archives")));
    private static SaveIdentitySnapshot Identity() => new() { Slot = 1, GameVersion = "2.3.30" };
    private static RunSummary Run(ProfileRepository repository, string id, double seconds, string map) => new()
    {
        RunId = id,
        SaveGenerationId = repository.CurrentGenerationId,
        StartingMapId = "duckov:map:" + map,
        StartingMapDisplayName = map,
        StartingMapKnown = true,
        StartedUtc = TestTime,
        EndedUtc = TestTime.AddSeconds(seconds),
        ActiveDurationSeconds = seconds,
        WallClockDurationSeconds = seconds,
        Outcome = RunOutcome.Extracted,
        RecordEligible = true,
        IntegrityTags = IntegrityTags.Normal,
        LifecycleCapability = AdapterCapabilityState.Supported,
        MovementCapability = AdapterCapabilityState.Supported,
        MapCapability = AdapterCapabilityState.Supported
    };
}
