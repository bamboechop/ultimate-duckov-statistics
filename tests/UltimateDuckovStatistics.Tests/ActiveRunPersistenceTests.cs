using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Tests;

public sealed class ActiveRunPersistenceTests
{
    private static readonly DateTime TestTime = new(2026, 8, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Run")]
    [Trait("Category", "Weapon")]
    public void AcceptedShotIsRecoveredFromTheCheckpointCreatedAfterTheProductionMutationSequence()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var generation = repository.CurrentGenerationId;
        var tracker = new UltimateDuckovStatistics.Core.Tracking.RunLifecycleTracker(() => "run-live-shot");
        tracker.Apply(LifecycleEvent(RunLifecycleEventKind.RaidInitialized, generation, 0));
        tracker.Apply(LifecycleEvent(RunLifecycleEventKind.ControlReady, generation, 0));
        Assert.True(tracker.RecordShot(LiveShot(generation)));
        Assert.True(tracker.CombatCheckpointRequired);
        repository.SaveActiveRun(tracker.CreateCheckpoint(TestTime.AddSeconds(1), 1)!);
        tracker.MarkCheckpointSaved(1);
        Assert.False(tracker.CombatCheckpointRequired);

        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity()).InterruptedRunRecovered);

        var run = Assert.Single(recovery.Current.Statistics.Runs);
        Assert.Equal("run-live-shot", run.RunId);
        Assert.Equal(1, run.WeaponStatistics.Totals.FiringActions);
        Assert.Equal(1, recovery.Current.Statistics.RunTotals.WeaponStatistics.Totals.FiringActions);
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Run")]
    [Trait("Category", "Weapon")]
    public void PartiallyPopulatedWeaponCheckpointIsNormalizedBeforeInterruptedRecovery()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var checkpoint = Checkpoint(repository.CurrentGenerationId, 3);
        checkpoint.WeaponStatistics.Totals = null!;
        checkpoint.WeaponStatistics.Weapons = null!;
        checkpoint.WeaponStatistics.AmmunitionTypes = null!;
        checkpoint.WeaponStatistics.Capabilities = null!;
        repository.CloseClean();
        new AtomicJsonStore<ActiveRunCheckpoint>().Save(ActiveRunPath(directory.Path), checkpoint);

        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity()).InterruptedRunRecovered);

        var recovered = Assert.Single(recovery.Current.Statistics.Runs);
        Assert.NotNull(recovered.WeaponStatistics.Totals);
        Assert.NotNull(recovered.WeaponStatistics.Capabilities.FiringActions);
        Assert.Empty(recovered.WeaponStatistics.Weapons);
        Assert.Empty(recovered.WeaponStatistics.AmmunitionTypes);
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Run")]
    [Trait("Category", "Weapon")]
    public void NullCheckpointAvailabilityMembersAreNormalizedBeforeInterruptedRecovery()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var checkpoint = Checkpoint(repository.CurrentGenerationId, 3);
        checkpoint.WeaponStatistics.Capabilities = new WeaponMetricCapabilities
        {
            FiringActions = null!,
            AmmunitionConsumption = null!,
            Projectiles = null!,
            WeaponIdentity = null!,
            AmmunitionIdentity = null!
        };
        repository.CloseClean();
        new AtomicJsonStore<ActiveRunCheckpoint>().Save(ActiveRunPath(directory.Path), checkpoint);

        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity()).InterruptedRunRecovered);

        var recovered = Assert.Single(recovery.Current.Statistics.Runs);
        Assert.NotNull(recovered.WeaponStatistics.Capabilities.FiringActions);
        Assert.NotNull(recovered.WeaponStatistics.Capabilities.AmmunitionConsumption);
        Assert.NotNull(recovered.WeaponStatistics.Capabilities.Projectiles);
        Assert.NotNull(recovered.WeaponStatistics.Capabilities.WeaponIdentity);
        Assert.NotNull(recovered.WeaponStatistics.Capabilities.AmmunitionIdentity);
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Run")]
    [Trait("Category", "Weapon")]
    public void NegativeWeaponCheckpointIsArchivedReadOnlyWithoutAbortingProfileOpen()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var checkpoint = Checkpoint(repository.CurrentGenerationId, 3);
        checkpoint.WeaponStatistics.Totals.FiringActions = -1;
        repository.CloseClean();
        new AtomicJsonStore<ActiveRunCheckpoint>().Save(ActiveRunPath(directory.Path), checkpoint);

        var recovery = Repository(directory.Path);
        var result = recovery.Open(Identity());

        Assert.False(result.InterruptedRunRecovered);
        Assert.Empty(recovery.Current.Statistics.Runs);
        var preserved = Directory.GetFiles(
            Path.Combine(Path.GetDirectoryName(ActiveRunPath(directory.Path))!, "checkpoint-recovery"));
        Assert.NotEmpty(preserved);
        Assert.All(preserved, file => Assert.True(File.GetAttributes(file).HasFlag(FileAttributes.ReadOnly)));
        recovery.CloseClean();
    }

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
        Assert.Equal(1, run.WeaponStatistics.Totals.FiringActions);
        Assert.Equal(1, run.WeaponStatistics.Totals.AmmunitionUnitsConsumed);
        Assert.Equal(6, run.WeaponStatistics.Totals.Projectiles);
        Assert.Equal(1, recovery.Current.Statistics.RunTotals.WeaponStatistics.Totals.FiringActions);
        Assert.Null(recovery.Current.Statistics.RunRecords.Extraction.Shortest);
        recovery.CloseClean();

        var repeated = Repository(directory.Path);
        var repeatedResult = repeated.Open(Identity());
        Assert.False(repeatedResult.InterruptedRunRecovered);
        Assert.Single(repeated.Current.Statistics.Runs);
        Assert.Equal(1, repeated.Current.Statistics.RunTotals.WeaponStatistics.Totals.FiringActions);
        repeated.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Run")]
    [Trait("Category", "Combat")]
    public void SchemaFourCheckpointRecoveryRetainsHistoricalCombatUnavailability()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var checkpoint = Checkpoint(repository.CurrentGenerationId, 4);
        checkpoint.SchemaVersion = 4;
        checkpoint.CombatStatistics = null!;
        repository.CloseClean();
        new AtomicJsonStore<ActiveRunCheckpoint>().Save(ActiveRunPath(directory.Path), checkpoint);

        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity()).InterruptedRunRecovered);

        var run = Assert.Single(recovery.Current.Statistics.Runs);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
            run.CombatStatistics.Capabilities.DamageDealt.State);
        Assert.Contains("predates M5", run.CombatStatistics.Capabilities.DamageDealt.Provenance);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible,
            recovery.Current.Statistics.RunTotals.CombatStatistics.Capabilities.DamageDealt.State);
        recovery.CloseClean();
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
    [Trait("Category", "Weapon")]
    public void ActiveRunRecoveryUsesValidBackupWhenPrimaryHasNegativeCombatCounter()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var generation = repository.CurrentGenerationId;
        repository.SaveActiveRun(Checkpoint(generation, 5));
        repository.SaveActiveRun(Checkpoint(generation, 8));
        repository.CloseClean();
        var path = ActiveRunPath(directory.Path);
        var primary = File.ReadAllText(path);
        const string validCounter = "\"FiringActions\":1";
        var counterIndex = primary.IndexOf(validCounter, StringComparison.Ordinal);
        Assert.True(counterIndex >= 0);
        File.WriteAllText(
            path,
            string.Concat(
                primary.AsSpan(0, counterIndex),
                "\"FiringActions\":-1",
                primary.AsSpan(counterIndex + validCounter.Length)));

        var recovery = Repository(directory.Path);
        var result = recovery.Open(Identity());

        Assert.True(result.InterruptedRunRecovered);
        var run = Assert.Single(recovery.Current.Statistics.Runs);
        Assert.Equal(5, run.ActiveDurationSeconds);
        Assert.Equal(1, run.WeaponStatistics.Totals.FiringActions);
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(AtomicJsonPaths.GetBackupPath(path)));
        var recoveryDirectory = Path.Combine(Path.GetDirectoryName(path)!, "checkpoint-recovery");
        Assert.False(Directory.Exists(recoveryDirectory));
        recovery.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Run")]
    [Trait("Category", "Combat")]
    public void ActiveRunRecoveryUsesValidBackupWhenPrimaryHasSemanticallyInvalidCombatState()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(directory.Path);
        repository.Open(Identity());
        var generation = repository.CurrentGenerationId;
        var backup = Checkpoint(generation, 5);
        backup.CombatStatistics.Totals.DamageCaused = 5;
        backup.CombatStatistics.Totals.DamageDealt = 5;
        var primary = Checkpoint(generation, 8);
        primary.CombatStatistics.Totals.DamageCaused = 8;
        primary.CombatStatistics.Totals.DamageDealt = 8;
        repository.SaveActiveRun(backup);
        repository.SaveActiveRun(primary);
        repository.CloseClean();
        var path = ActiveRunPath(directory.Path);
        var json = File.ReadAllText(path);
        const string validCounter = "\"DamageDealt\":8";
        var counterIndex = json.IndexOf(validCounter, StringComparison.Ordinal);
        Assert.True(counterIndex >= 0);
        File.WriteAllText(
            path,
            string.Concat(
                json.AsSpan(0, counterIndex),
                "\"DamageDealt\":-1",
                json.AsSpan(counterIndex + validCounter.Length)));

        var recovery = Repository(directory.Path);
        var result = recovery.Open(Identity());

        Assert.True(result.InterruptedRunRecovered);
        var run = Assert.Single(recovery.Current.Statistics.Runs);
        Assert.Equal(5, run.ActiveDurationSeconds);
        Assert.Equal(5, run.CombatStatistics.Totals.DamageDealt);
        Assert.False(run.CombatStatistics.WasRepairedFromInvalidState);
        Assert.False(File.Exists(path));
        Assert.False(File.Exists(AtomicJsonPaths.GetBackupPath(path)));
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
        Assert.Equal(0, repository.Current.Statistics.RunTotals.WeaponStatistics.Totals.FiringActions);
        repository.CloseClean();

        var recovery = Repository(directory.Path);
        Assert.True(recovery.Open(Identity(slot: 1)).InterruptedRunRecovered);
        Assert.Equal(slotOneGeneration, Assert.Single(recovery.Current.Statistics.Runs).SaveGenerationId);
        Assert.Equal(1, recovery.Current.Statistics.RunTotals.WeaponStatistics.Totals.FiringActions);
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
        MapAdapterVersion = "native-map-identity/2.3.30",
        WeaponStatistics = CombatStatistics()
    };

    private static WeaponStatisticsAggregate CombatStatistics()
    {
        var statistics = new WeaponStatisticsAggregate();
        WeaponStatisticsReducer.Apply(statistics, new ShotRecorded
        {
            EventId = "shot-checkpoint",
            TimestampUtc = TestTime,
            SaveGenerationId = "unused-after-aggregation",
            RunId = "run-checkpoint",
            MapId = "duckov:map:warehouse",
            GameplayContext = GameplayContext.Raid,
            WeaponId = "duckov:weapon:1",
            WeaponDisplayName = "Test shotgun",
            AmmunitionId = "duckov:ammo:2",
            AmmunitionDisplayName = "Test shell",
            FiringActionCount = 1,
            AmmunitionUnitsConsumed = 1,
            ProjectileCount = 6,
            Capabilities = SupportedCapabilities()
        });
        return statistics;
    }

    private static WeaponMetricCapabilities SupportedCapabilities() => new()
    {
        FiringActions = Supported(),
        AmmunitionConsumption = Supported(),
        Projectiles = Supported(),
        WeaponIdentity = Supported(),
        AmmunitionIdentity = Supported()
    };

    private static MetricAvailability Supported() => new()
    {
        State = AdapterCapabilityState.Supported,
        Provenance = "test"
    };

    private static RunLifecycleEvent LifecycleEvent(
        RunLifecycleEventKind kind,
        string generation,
        double seconds) => new()
        {
            Kind = kind,
            TimestampUtc = TestTime.AddSeconds(seconds),
            MonotonicSeconds = seconds,
            NativeRaidId = "42",
            StartContext = kind == RunLifecycleEventKind.ControlReady
                ? new RunStartContext
                {
                    SaveGenerationId = generation,
                    NativeRaidId = "42",
                    Map = new MapIdentity
                    {
                        MapId = "duckov:map:warehouse",
                        DisplayName = "Warehouse",
                        IsKnown = true
                    },
                    IntegrityTags = IntegrityTags.Normal,
                    LifecycleCapability = AdapterCapabilityState.Supported,
                    MovementCapability = AdapterCapabilityState.Supported,
                    MapCapability = AdapterCapabilityState.Supported,
                    WeaponCapabilities = SupportedCapabilities()
                }
                : null
        };

    private static ShotRecorded LiveShot(string generation) => new()
    {
        EventId = "live-shot",
        TimestampUtc = TestTime,
        SaveGenerationId = generation,
        RunId = "run-live-shot",
        MapId = "duckov:map:warehouse",
        GameplayContext = GameplayContext.Raid,
        IntegrityTags = IntegrityTags.Normal,
        WeaponId = "duckov:weapon:1",
        WeaponDisplayName = "Test rifle",
        AmmunitionId = "duckov:ammo:2",
        AmmunitionDisplayName = "Test round",
        FiringActionCount = 1,
        AmmunitionUnitsConsumed = 1,
        ProjectileCount = 1,
        Capabilities = SupportedCapabilities()
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
