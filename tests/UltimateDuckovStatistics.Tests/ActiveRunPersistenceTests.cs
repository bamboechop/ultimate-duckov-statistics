using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Tests;

public sealed class ActiveRunPersistenceTests
{
    private static readonly DateTime TestTime = new(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Run")]
    public void ActiveRunCheckpointBecomesOneInterruptedSummaryAcrossRepeatedRestarts()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var generation = repository.CurrentGenerationId;
        repository.SaveActiveRun(Checkpoint(generation, 12));
        repository.CloseClean();

        var recovery = Repository(directory.Path);
        var result = recovery.Open(Identity());

        Assert.True(result.InterruptedRunRecovered);
        var run = Assert.Single(recovery.Current.Statistics.Runs);
        Assert.Equal(RunOutcome.Interrupted, run.Outcome);
        Assert.Equal(12, run.ActiveDurationSeconds);
        Assert.Equal(4, run.PhysicalDistance);
        Assert.Equal(9, run.TeleportDistance);
        Assert.Null(recovery.Current.Statistics.RunRecords.Extraction.Shortest);
        recovery.CloseClean();

        var repeated = Repository(directory.Path);
        var repeatedResult = repeated.Open(Identity());
        Assert.False(repeatedResult.InterruptedRunRecovered);
        Assert.Single(repeated.Current.Statistics.Runs);
        repeated.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Run")]
    public void ActiveRunRecoveryUsesBackupWhenPrimaryIsCorrupt()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var generation = repository.CurrentGenerationId;
        repository.SaveActiveRun(Checkpoint(generation, 5));
        repository.SaveActiveRun(Checkpoint(generation, 8));
        repository.CloseClean();
        File.WriteAllText(ActiveRunPath(directory.Path), "{invalid");

        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity()).InterruptedRunRecovered);
        Assert.Equal(5, Assert.Single(recovery.Current.Statistics.Runs).ActiveDurationSeconds);
        Assert.False(File.Exists(ActiveRunPath(directory.Path)));
        Assert.False(File.Exists(AtomicJsonPaths.GetBackupPath(ActiveRunPath(directory.Path))));
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Run")]
    public void ActiveRunRecoveryUsesOrphanedTemporarySnapshot()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var generation = repository.CurrentGenerationId;
        repository.SaveActiveRun(Checkpoint(generation, 7));
        repository.CloseClean();
        File.Move(ActiveRunPath(directory.Path), AtomicJsonPaths.GetTemporaryPath(ActiveRunPath(directory.Path)));

        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity()).InterruptedRunRecovered);
        Assert.Equal(7, Assert.Single(recovery.Current.Statistics.Runs).ActiveDurationSeconds);
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Run")]
    public void ActiveRunCheckpointIsIsolatedBySaveSlotAndGeneration()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity(slot: 1));
        var slotOneGeneration = repository.CurrentGenerationId;
        repository.SaveActiveRun(Checkpoint(slotOneGeneration, 6));
        Assert.Throws<ArgumentException>(() => repository.SaveActiveRun(Checkpoint("different-generation", 7)));

        repository.Open(Identity(slot: 2));
        Assert.Equal(2, repository.Current.Slot);
        Assert.Empty(repository.Current.Statistics.Runs);
        repository.CloseClean();

        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity(slot: 1)).InterruptedRunRecovered);
        Assert.Equal(slotOneGeneration, Assert.Single(recovery.Current.Statistics.Runs).SaveGenerationId);
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Run")]
    public void IdentityRotationRecoversInterruptedStateIntoTheOldGenerationBeforeArchiving()
    {
        using var directory = new TemporaryDirectory();
        var interrupted = Repository(directory.Path);
        interrupted.Open(Identity());
        var oldGeneration = interrupted.CurrentGenerationId;
        interrupted.SaveActiveRun(Checkpoint(oldGeneration, 11));

        var replacement = Repository(directory.Path);
        var result = replacement.Open(Identity(creationTicks: 999, hashCharacter: 'f'));

        Assert.True(result.RotatedGeneration);
        Assert.True(result.InterruptedRunRecovered);
        Assert.True(result.InterruptedSessionRecovered);
        Assert.NotEqual(oldGeneration, replacement.CurrentGenerationId);
        Assert.Empty(replacement.Current.Statistics.Runs);
        Assert.Equal(0, replacement.Current.InterruptedSessionCount);

        var archive = Assert.Single(Directory.EnumerateDirectories(Path.Combine(
            directory.Path,
            "profiles",
            "slot-01",
            "archives")));
        var archived = new AtomicJsonStore<ProfileDocument>().Load(Path.Combine(archive, "profile.json")).Value!;
        var recovered = Assert.Single(archived.Statistics.Runs);
        Assert.Equal(oldGeneration, archived.GenerationId);
        Assert.Equal(oldGeneration, recovered.SaveGenerationId);
        Assert.Equal(RunOutcome.Interrupted, recovered.Outcome);
        Assert.Equal(11, recovered.ActiveDurationSeconds);
        Assert.False(recovered.RecordEligible);
        Assert.Equal(1, archived.InterruptedSessionCount);
        Assert.Empty(Directory.EnumerateFiles(archive, "active-run.json*", SearchOption.TopDirectoryOnly));
        Assert.False(File.Exists(Path.Combine(archive, "session.json")));
        replacement.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Run")]
    public void UnrecoverableCheckpointArtifactsArePreservedReadOnlyWithoutInventingARun()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var generation = repository.CurrentGenerationId;
        repository.SaveActiveRun(Checkpoint(generation, 5));
        repository.SaveActiveRun(Checkpoint(generation, 8));
        repository.CloseClean();
        var path = ActiveRunPath(directory.Path);
        File.WriteAllText(path, "{invalid-primary");
        File.WriteAllText(AtomicJsonPaths.GetBackupPath(path), "{invalid-backup");
        File.WriteAllText(AtomicJsonPaths.GetTemporaryPath(path), "{invalid-temporary");

        var recovery = Repository(directory.Path);
        var result = recovery.Open(Identity());

        Assert.False(result.InterruptedRunRecovered);
        Assert.Empty(recovery.Current.Statistics.Runs);
        Assert.False(File.Exists(path));
        var preserved = Directory.GetFiles(
            Path.Combine(Path.GetDirectoryName(path)!, "checkpoint-recovery"));
        Assert.Equal(3, preserved.Length);
        Assert.All(preserved, file => Assert.True(File.GetAttributes(file).HasFlag(FileAttributes.ReadOnly)));
        recovery.CloseClean();
    }

    private static ProfileRepository Repository(string path) => new(
        path,
        () => TestTime.AddMinutes(1),
        () => Guid.NewGuid().ToString("N"));

    private static ActiveRunCheckpoint Checkpoint(string generation, double activeSeconds) => new()
    {
        RunId = "run-checkpoint",
        SaveGenerationId = generation,
        NativeRaidId = "42",
        MapId = "duckov:map:warehouse",
        MapDisplayName = "Warehouse",
        MapKnown = true,
        StartedUtc = TestTime,
        LastObservedUtc = TestTime.AddSeconds(20),
        ActiveDurationSeconds = activeSeconds,
        PhysicalDistance = 4,
        TeleportDistance = 9,
        IntegrityTags = IntegrityTags.Normal,
        GameVersion = "2.3.30",
        GameBuild = "24013657",
        LifecycleCapability = AdapterCapabilityState.Supported,
        LifecycleAdapterVersion = "native-run-lifecycle/2.3.30",
        MovementCapability = AdapterCapabilityState.Supported,
        MovementAdapterVersion = "native-main-duck-movement/2.3.30",
        MapCapability = AdapterCapabilityState.Supported,
        MapAdapterVersion = "native-map-identity/2.3.30"
    };

    private static SaveIdentitySnapshot Identity(
        int slot = 1,
        long creationTicks = 100,
        char hashCharacter = 'a') => new()
        {
            Slot = slot,
            SaveFilePresent = true,
            SaveFileCreationUtcTicks = creationTicks,
            ObservedWriteUtcTicks = 110,
            ObservedLength = 4096,
            GameVersion = "2.3.30",
            ContentSha256 = new string(slot == 1 ? hashCharacter : 'b', 64),
            SaveTimeBinary = TestTime.ToBinary()
        };

    private static string ActiveRunPath(string root) => Path.Combine(
        root,
        "profiles",
        "slot-01",
        "current",
        "active-run.json");
}
