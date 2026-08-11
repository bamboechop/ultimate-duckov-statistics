using UltimateDuckovStatistics.Core;
using UltimateDuckovStatistics.Core.Diagnostics;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Tests;

public sealed class PersistenceTests
{
    private static readonly DateTime TestTime = new(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string[] ExpectedDiagnosticMessages = { "two", "three" };
    private static readonly string[] SchemaTwoRecentEventIds = { "use-1", "heal-1" };

    [Fact]
    [Trait("Category", "Persistence")]
    public void AtomicStoreRoundTripsAndRecoversCorruptPrimaryFromBackup()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporaryDirectory.Path, "profile.json");
        var store = new AtomicJsonStore<ProfileDocument>();
        store.Save(path, CreateDocument("generation-a", revision: 1));
        store.Save(path, CreateDocument("generation-a", revision: 2));

        File.WriteAllText(path, "{ definitely-not-json");
        var recovered = store.Load(path);

        Assert.Equal(AtomicJsonLoadSource.Backup, recovered.Source);
        Assert.True(recovered.Recovered);
        Assert.True(recovered.PrimaryRepaired);
        Assert.Equal(1, recovered.Value!.Revision);
        Assert.NotEmpty(recovered.Failures);
        Assert.Equal(1, store.Load(path).Value!.Revision);
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void AtomicStoreRecoversOrphanedTemporarySnapshot()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporaryDirectory.Path, "profile.json");
        var temporaryPath = AtomicJsonPaths.GetTemporaryPath(path);
        var store = new AtomicJsonStore<ProfileDocument>();
        store.Save(path, CreateDocument("generation-a", revision: 7));
        File.Move(path, temporaryPath);

        var recovered = store.Load(path);

        Assert.Equal(AtomicJsonLoadSource.Temporary, recovered.Source);
        Assert.True(recovered.PrimaryRepaired);
        Assert.Equal(7, recovered.Value!.Revision);
        Assert.True(File.Exists(path));
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void RepositoryMigratesLegacySchemaWithoutChangingGeneration()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "current", "profile.json");
        var legacy = CreateDocument("generation-legacy", revision: 4);
        legacy.SchemaVersion = 0;
        legacy.Statistics.SchemaVersion = 0;
        legacy.Statistics.SaveGenerationId = string.Empty;
        new AtomicJsonStore<ProfileDocument>().Save(path, legacy);
        var repository = CreateRepository(temporaryDirectory.Path, "session-new");

        var result = repository.Open(CreateIdentity(slot: 1, creationTicks: 100));

        Assert.True(result.MigratedSchema);
        Assert.Equal(ProductInfo.SchemaVersion, repository.Current.SchemaVersion);
        Assert.Equal("generation-legacy", repository.Current.GenerationId);
        Assert.Equal("generation-legacy", repository.Current.Statistics.SaveGenerationId);
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void RepositoryMigratesEveryV01AggregateWithoutLosingUsageStatistics()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "current", "profile.json");
        var legacy = CreateDocument("generation-v01", revision: 12);
        legacy.SchemaVersion = 1;
        legacy.Statistics.SchemaVersion = 1;
        legacy.Statistics.Overall.ActivationCount = 3;
        legacy.Statistics.Overall.AmountsByUnit[nameof(ConsumptionUnit.StackUnit)] = 3;
        legacy.Statistics.Items["item:a"] = new()
        {
            ItemId = "item:a",
            DisplayName = "Legacy medkit",
            Group = CanonicalItemGroup.Healing,
            Totals = new()
            {
                ActivationCount = 3,
                AmountsByUnit = new() { [nameof(ConsumptionUnit.StackUnit)] = 3 }
            }
        };
        legacy.Statistics.Groups[nameof(CanonicalItemGroup.Healing)] = new()
        {
            ActivationCount = 3,
            AmountsByUnit = new() { [nameof(ConsumptionUnit.StackUnit)] = 3 }
        };
        new AtomicJsonStore<ProfileDocument>().Save(path, legacy);
        var repository = CreateRepository(temporaryDirectory.Path, "session-new");

        var result = repository.Open(CreateIdentity(slot: 1, creationTicks: 100));

        Assert.True(result.MigratedSchema);
        Assert.Equal(4, repository.Current.SchemaVersion);
        Assert.Equal(4, repository.Current.Statistics.SchemaVersion);
        Assert.Equal(3, repository.Current.Statistics.Overall.ActivationCount);
        Assert.Equal(3, repository.Current.Statistics.Overall.AmountsByUnit[nameof(ConsumptionUnit.StackUnit)]);
        Assert.Equal(0, repository.Current.Statistics.Overall.ActualHealthRestored);
        Assert.Equal(3, repository.Current.Statistics.Items["item:a"].Totals.ActivationCount);
        Assert.Empty(repository.Current.Statistics.Runs);
        Assert.Equal(0, repository.Current.Statistics.RunTotals.TotalRuns);
        Assert.Null(repository.Current.Statistics.RunRecords.Extraction.Shortest);
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Migration")]
    public void RepositoryMigratesSchemaTwoWithoutChangingM1M2DataCapabilitiesOrArchives()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var slotDirectory = System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01");
        var path = System.IO.Path.Combine(slotDirectory, "current", "profile.json");
        var archivePath = System.IO.Path.Combine(slotDirectory, "archives", "historical-generation", "profile.json");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(archivePath)!);
        File.WriteAllText(archivePath, "historical archive bytes");
        File.SetAttributes(archivePath, File.GetAttributes(archivePath) | FileAttributes.ReadOnly);
        var archiveHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(archivePath)));

        var legacy = CreateDocument("generation-v02", revision: 41);
        legacy.SchemaVersion = 2;
        legacy.Statistics.SchemaVersion = 2;
        legacy.InterruptedSessionCount = 3;
        legacy.Statistics.Overall.ActivationCount = 4;
        legacy.Statistics.Overall.ActualHealthRestored = 72.5;
        legacy.Statistics.Overall.AmountsByUnit[nameof(ConsumptionUnit.Durability)] = 50;
        legacy.Statistics.Groups[nameof(CanonicalItemGroup.Healing)] = new AggregateTotals
        {
            ActivationCount = 4,
            ActualHealthRestored = 72.5,
            AmountsByUnit = new() { [nameof(ConsumptionUnit.Durability)] = 50 }
        };
        legacy.Statistics.Items["item:medkit"] = new ItemAggregate
        {
            ItemId = "item:medkit",
            DisplayName = "Medkit",
            Group = CanonicalItemGroup.Healing,
            EffectTags = new() { ItemEffectTag.Healing },
            Totals = new AggregateTotals
            {
                ActivationCount = 4,
                ActualHealthRestored = 72.5,
                AmountsByUnit = new() { [nameof(ConsumptionUnit.Durability)] = 50 }
            }
        };
        legacy.Statistics.RecentEventIds.AddRange(SchemaTwoRecentEventIds);
        legacy.Capabilities.Add(new CapabilityRecord
        {
            AdapterId = "native-healing-attribution",
            State = AdapterCapabilityState.Supported,
            Version = "native-healing-attribution/2.3.30"
        });
        new AtomicJsonStore<ProfileDocument>().Save(path, legacy);
        var repository = CreateRepository(temporaryDirectory.Path, "session-new");

        var result = repository.Open(CreateIdentity(slot: 1, creationTicks: 100));

        Assert.True(result.MigratedSchema);
        Assert.Equal(ProductInfo.SchemaVersion, repository.Current.SchemaVersion);
        Assert.Equal("generation-v02", repository.Current.GenerationId);
        Assert.Equal(41, repository.Current.Revision);
        Assert.Equal(3, repository.Current.InterruptedSessionCount);
        Assert.Equal(4, repository.Current.Statistics.Overall.ActivationCount);
        Assert.Equal(72.5, repository.Current.Statistics.Overall.ActualHealthRestored);
        Assert.Equal(50, repository.Current.Statistics.Overall.AmountsByUnit[nameof(ConsumptionUnit.Durability)]);
        Assert.Equal(4, repository.Current.Statistics.Groups[nameof(CanonicalItemGroup.Healing)].ActivationCount);
        Assert.Equal(72.5, repository.Current.Statistics.Items["item:medkit"].Totals.ActualHealthRestored);
        Assert.Equal(SchemaTwoRecentEventIds, repository.Current.Statistics.RecentEventIds);
        Assert.Equal("native-healing-attribution", Assert.Single(repository.Current.Capabilities).AdapterId);
        Assert.Empty(repository.Current.Statistics.Runs);
        Assert.Equal(0, repository.Current.Statistics.RunTotals.TotalRuns);
        Assert.Equal(archiveHash, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(archivePath))));
        Assert.True(File.GetAttributes(archivePath).HasFlag(FileAttributes.ReadOnly));
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Migration")]
    [Trait("Category", "Weapon")]
    public void RepositoryMigratesSchemaThreeWithoutChangingM1ToM3Data()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "current", "profile.json");
        var legacy = CreateDocument("generation-v03", revision: 73);
        legacy.SchemaVersion = 3;
        legacy.Statistics.SchemaVersion = 3;
        legacy.InterruptedSessionCount = 2;
        legacy.Statistics.Overall.ActivationCount = 5;
        legacy.Statistics.Overall.ActualHealthRestored = 24;
        legacy.Statistics.RunTotals.TotalRuns = 1;
        legacy.Statistics.RunTotals.PhysicalDistance = 120;
        legacy.Statistics.RunTotals.TeleportDistance = 3;
        legacy.Statistics.RunTotals.Outcomes[nameof(RunOutcome.Extracted)] = 1;
        legacy.Statistics.RunTotals.WeaponStatistics = null!;
        legacy.Statistics.RunTotals.Maps["duckov:map:test"] = new MapRunAggregate
        {
            MapId = "duckov:map:test",
            DisplayName = "Test map",
            IsKnown = true,
            TotalRuns = 1,
            PhysicalDistance = 120,
            TeleportDistance = 3,
            Outcomes = new() { [nameof(RunOutcome.Extracted)] = 1 },
            WeaponStatistics = null!
        };
        legacy.Statistics.Runs.Add(new RunSummary
        {
            RunId = "run-v03",
            SaveGenerationId = "generation-v03",
            MapId = "duckov:map:test",
            MapDisplayName = "Test map",
            MapKnown = true,
            StartedUtc = TestTime.AddMinutes(-2),
            EndedUtc = TestTime,
            ActiveDurationSeconds = 90,
            WallClockDurationSeconds = 120,
            Outcome = RunOutcome.Extracted,
            PhysicalDistance = 120,
            TeleportDistance = 3,
            RecordEligible = true,
            LifecycleCapability = AdapterCapabilityState.Supported,
            MovementCapability = AdapterCapabilityState.Supported,
            MapCapability = AdapterCapabilityState.Supported,
            WeaponStatistics = null!
        });
        legacy.Capabilities.Add(new CapabilityRecord
        {
            AdapterId = RunStatisticsViewModelFactory.MovementAdapterId,
            State = AdapterCapabilityState.Supported,
            Version = "native-main-duck-movement/2.3.30"
        });
        new AtomicJsonStore<ProfileDocument>().Save(path, legacy);
        var repository = CreateRepository(temporaryDirectory.Path, "session-new");

        var result = repository.Open(CreateIdentity(slot: 1, creationTicks: 100));

        Assert.True(result.MigratedSchema);
        Assert.Equal(4, repository.Current.SchemaVersion);
        Assert.Equal(4, repository.Current.Statistics.SchemaVersion);
        Assert.Equal("generation-v03", repository.Current.GenerationId);
        Assert.Equal(73, repository.Current.Revision);
        Assert.Equal(2, repository.Current.InterruptedSessionCount);
        Assert.Equal(5, repository.Current.Statistics.Overall.ActivationCount);
        Assert.Equal(24, repository.Current.Statistics.Overall.ActualHealthRestored);
        Assert.Equal(1, repository.Current.Statistics.RunTotals.TotalRuns);
        Assert.Equal(120, repository.Current.Statistics.RunTotals.PhysicalDistance);
        Assert.Equal(3, repository.Current.Statistics.RunTotals.TeleportDistance);
        Assert.Equal("run-v03", Assert.Single(repository.Current.Statistics.Runs).RunId);
        Assert.Equal("native-main-duck-movement", Assert.Single(repository.Current.Capabilities).AdapterId);
        Assert.Equal(0, repository.Current.Statistics.RunTotals.WeaponStatistics.Totals.FiringActions);
        Assert.Empty(repository.Current.Statistics.RunTotals.WeaponStatistics.Weapons);
        Assert.Empty(repository.Current.Statistics.RunTotals.WeaponStatistics.AmmunitionTypes);
        Assert.NotNull(repository.Current.Statistics.RunTotals.Maps["duckov:map:test"].WeaponStatistics);
        Assert.NotNull(repository.Current.Statistics.Runs[0].WeaponStatistics);
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Healing")]
    public void RepositoryRepairsPreReleaseDelayedHealingGroupWithoutChangingGenerationOrTotals()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "current", "profile.json");
        var profile = CreateDocument("generation-schema-2", revision: 58);
        profile.Statistics.Overall.ActivationCount = 2;
        profile.Statistics.Overall.AmountsByUnit[nameof(ConsumptionUnit.Durability)] = 50;
        profile.Statistics.Overall.AmountsByUnit[nameof(ConsumptionUnit.StackUnit)] = 1;
        profile.Statistics.Overall.ActualHealthRestored = 60;
        profile.Statistics.Items["item:water"] = new()
        {
            ItemId = "item:water",
            DisplayName = "Water",
            Group = CanonicalItemGroup.Drink,
            EffectTags = new List<ItemEffectTag> { ItemEffectTag.Drink },
            Totals = new()
            {
                ActivationCount = 1,
                AmountsByUnit = new() { [nameof(ConsumptionUnit.Durability)] = 50 }
            }
        };
        profile.Statistics.Items["item:injector"] = new()
        {
            ItemId = "item:injector",
            DisplayName = "Recovery Injector",
            Group = CanonicalItemGroup.Drink,
            EffectTags = new List<ItemEffectTag> { ItemEffectTag.Drink, ItemEffectTag.Buff },
            Totals = new()
            {
                ActivationCount = 1,
                AmountsByUnit = new() { [nameof(ConsumptionUnit.StackUnit)] = 1 },
                ActualHealthRestored = 60
            }
        };
        profile.Statistics.Groups[nameof(CanonicalItemGroup.Drink)] = new()
        {
            ActivationCount = 2,
            AmountsByUnit = new()
            {
                [nameof(ConsumptionUnit.Durability)] = 50,
                [nameof(ConsumptionUnit.StackUnit)] = 1
            },
            ActualHealthRestored = 60
        };
        new AtomicJsonStore<ProfileDocument>().Save(path, profile);
        var repository = CreateRepository(temporaryDirectory.Path, "session-new");

        var result = repository.Open(CreateIdentity(slot: 1, creationTicks: 100));

        Assert.True(result.MigratedSchema);
        Assert.Equal("generation-schema-2", repository.Current.GenerationId);
        Assert.Equal(2, repository.Current.Statistics.Overall.ActivationCount);
        Assert.Equal(60, repository.Current.Statistics.Overall.ActualHealthRestored);
        var injector = repository.Current.Statistics.Items["item:injector"];
        Assert.Equal(CanonicalItemGroup.Healing, injector.Group);
        Assert.Contains(ItemEffectTag.Healing, injector.EffectTags);
        Assert.Equal(1, repository.Current.Statistics.Groups[nameof(CanonicalItemGroup.Drink)].ActivationCount);
        Assert.Equal(0, repository.Current.Statistics.Groups[nameof(CanonicalItemGroup.Drink)].ActualHealthRestored);
        Assert.Equal(1, repository.Current.Statistics.Groups[nameof(CanonicalItemGroup.Healing)].ActivationCount);
        Assert.Equal(60, repository.Current.Statistics.Groups[nameof(CanonicalItemGroup.Healing)].ActualHealthRestored);
        Assert.Equal(
            repository.Current.Statistics.Overall.ActivationCount,
            repository.Current.Statistics.Groups.Values.Sum(group => group.ActivationCount));
        Assert.Equal(
            repository.Current.Statistics.Overall.ActualHealthRestored,
            repository.Current.Statistics.Groups.Values.Sum(group => group.ActualHealthRestored));
        repository.CloseClean();

        var persisted = new AtomicJsonStore<ProfileDocument>().Load(path).Value!;
        Assert.Equal("generation-schema-2", persisted.GenerationId);
        Assert.Equal(CanonicalItemGroup.Healing, persisted.Statistics.Items["item:injector"].Group);
        Assert.Equal(60, persisted.Statistics.Overall.ActualHealthRestored);
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void RepositoryRecoversAndMigratesV01BackupSnapshot()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "current", "profile.json");
        var store = new AtomicJsonStore<ProfileDocument>();
        var legacy = CreateDocument("generation-v01-backup", revision: 8);
        legacy.SchemaVersion = 1;
        legacy.Statistics.SchemaVersion = 1;
        legacy.Statistics.Overall.ActivationCount = 2;
        store.Save(path, legacy);
        store.Save(path, CreateDocument("generation-discarded", revision: 9));
        File.WriteAllText(path, "{ corrupt-primary");
        var repository = CreateRepository(temporaryDirectory.Path, "session-new");

        var result = repository.Open(CreateIdentity(slot: 1, creationTicks: 100));

        Assert.True(result.RecoveredSnapshot);
        Assert.True(result.MigratedSchema);
        Assert.Equal(ProductInfo.SchemaVersion, repository.Current.SchemaVersion);
        Assert.Equal("generation-v01-backup", repository.Current.GenerationId);
        Assert.Equal(2, repository.Current.Statistics.Overall.ActivationCount);
        Assert.Equal(0, repository.Current.Statistics.Overall.ActualHealthRestored);
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void RepositoryRecoversAndMigratesV01TemporarySnapshot()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "current", "profile.json");
        var legacy = CreateDocument("generation-v01-temporary", revision: 6);
        legacy.SchemaVersion = 1;
        legacy.Statistics.SchemaVersion = 1;
        legacy.Statistics.Overall.ActivationCount = 4;
        new AtomicJsonStore<ProfileDocument>().Save(AtomicJsonPaths.GetTemporaryPath(path), legacy);
        var repository = CreateRepository(temporaryDirectory.Path, "session-new");

        var result = repository.Open(CreateIdentity(slot: 1, creationTicks: 100));

        Assert.True(result.RecoveredSnapshot);
        Assert.True(result.MigratedSchema);
        Assert.Equal(ProductInfo.SchemaVersion, repository.Current.SchemaVersion);
        Assert.Equal("generation-v01-temporary", repository.Current.GenerationId);
        Assert.Equal(4, repository.Current.Statistics.Overall.ActivationCount);
        Assert.Equal(0, repository.Current.Statistics.Overall.ActualHealthRestored);
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void RepositoryNormalizesMissingLegacyFieldsBeforeIdentityChecks()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "current", "profile.json");
        var legacy = CreateDocument("generation-legacy", revision: 4);
        legacy.SchemaVersion = 0;
        legacy.Identity = null!;
        legacy.Statistics = null!;
        new AtomicJsonStore<ProfileDocument>().Save(path, legacy);
        var ids = new Queue<string>();
        ids.Enqueue("session-new");
        var repository = CreateRepository(temporaryDirectory.Path, ids);

        var result = repository.Open(new SaveIdentitySnapshot { Slot = 1, SaveFilePresent = false });

        Assert.True(result.MigratedSchema);
        Assert.False(result.RotatedGeneration);
        Assert.Equal("generation-legacy", repository.Current.GenerationId);
        Assert.NotNull(repository.Current.Identity);
        Assert.NotNull(repository.Current.Statistics);
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void UnsupportedProfileSchemaIsArchivedByteForByteWithoutDowngrade()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "current", "profile.json");
        var future = CreateDocument("generation-future", revision: 9);
        future.SchemaVersion = ProductInfo.SchemaVersion + 1;
        new AtomicJsonStore<ProfileDocument>().Save(path, future);
        var raw = File.ReadAllText(path).Insert(1, "\"FutureData\":{\"keep\":\"verbatim\"},");
        File.WriteAllText(path, raw);
        var ids = new Queue<string>();
        ids.Enqueue("generation-safe");
        ids.Enqueue("session-safe");
        var repository = CreateRepository(temporaryDirectory.Path, ids);

        var result = repository.Open(CreateIdentity(slot: 1, creationTicks: 100));

        Assert.True(result.UnsupportedSchemaArchived);
        Assert.True(result.RotatedGeneration);
        Assert.Equal("generation-safe", repository.Current.GenerationId);
        var archive = Assert.Single(Directory.EnumerateDirectories(
            System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "archives")));
        var archivedProfile = System.IO.Path.Combine(archive, "profile.json");
        Assert.Equal(raw, File.ReadAllText(archivedProfile));
        Assert.True((File.GetAttributes(archivedProfile) & FileAttributes.ReadOnly) != 0);
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void UnsupportedStatisticsSchemaIsArchivedWithoutBeingSaved()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "current", "profile.json");
        var future = CreateDocument("generation-future", revision: 9);
        future.Statistics.SchemaVersion = ProductInfo.SchemaVersion + 1;
        new AtomicJsonStore<ProfileDocument>().Save(path, future);
        var raw = File.ReadAllText(path);
        var ids = new Queue<string>();
        ids.Enqueue("generation-safe");
        ids.Enqueue("session-safe");
        var repository = CreateRepository(temporaryDirectory.Path, ids);

        var result = repository.Open(CreateIdentity(slot: 1, creationTicks: 100));

        Assert.True(result.UnsupportedSchemaArchived);
        var archive = Assert.Single(Directory.EnumerateDirectories(
            System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "archives")));
        Assert.Equal(raw, File.ReadAllText(System.IO.Path.Combine(archive, "profile.json")));
        Assert.Equal(ProductInfo.SchemaVersion, repository.Current.Statistics.SchemaVersion);
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void SaveGuardNeverDowngradesAnUnsupportedCurrentDocument()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var ids = new Queue<string>();
        ids.Enqueue("generation-a");
        ids.Enqueue("session-a");
        var repository = CreateRepository(temporaryDirectory.Path, ids);
        repository.Open(CreateIdentity(slot: 1, creationTicks: 100));
        var profilePath = repository.CurrentProfilePath!;
        var before = File.ReadAllText(profilePath);
        repository.Current.SchemaVersion = ProductInfo.SchemaVersion + 1;

        Assert.Throws<NotSupportedException>(() => repository.Flush());

        Assert.Equal(before, File.ReadAllText(profilePath));
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void SaveSlotsRemainIsolated()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var ids = new Queue<string>();
        ids.Enqueue("generation-one");
        ids.Enqueue("session-one");
        ids.Enqueue("generation-two");
        ids.Enqueue("session-two");
        var repository = CreateRepository(temporaryDirectory.Path, () => ids.Dequeue());
        repository.Open(CreateIdentity(slot: 1, creationTicks: 100));
        Assert.True(repository.Record(CreateUse("event-one", "generation-one")));
        repository.CloseClean();

        repository.Open(CreateIdentity(slot: 2, creationTicks: 200));

        Assert.Equal("generation-two", repository.Current.GenerationId);
        Assert.Equal(0, repository.Current.Statistics.Overall.ActivationCount);
        Assert.True(File.Exists(System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "current", "profile.json")));
        Assert.True(File.Exists(System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-02", "current", "profile.json")));
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void RuntimeCapabilitiesFollowSlotTransitionsAndGenerationRotations()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var ids = new Queue<string>();
        ids.Enqueue("generation-one");
        ids.Enqueue("session-one");
        ids.Enqueue("generation-two");
        ids.Enqueue("session-two");
        ids.Enqueue("generation-three");
        ids.Enqueue("session-three");
        var repository = CreateRepository(temporaryDirectory.Path, ids);
        var capabilities = new[]
        {
            new CapabilityRecord
            {
                AdapterId = "native-item-use",
                State = AdapterCapabilityState.Supported,
                Version = "native-item-use/2.3.30",
                Detail = "public native hooks"
            }
        };

        repository.Open(CreateIdentity(slot: 1, creationTicks: 100));
        repository.SetCapabilities(capabilities);
        Assert.Equal("native-item-use", Assert.Single(repository.Current.Capabilities).AdapterId);

        repository.Open(CreateIdentity(slot: 2, creationTicks: 200), "SaveSlotSelected");
        Assert.Equal("native-item-use", Assert.Single(repository.Current.Capabilities).AdapterId);

        repository.Rotate(CreateIdentity(slot: 2, creationTicks: 200), "DuckovNewGame");
        var rotatedCapability = Assert.Single(repository.Current.Capabilities);
        Assert.Equal(AdapterCapabilityState.Supported, rotatedCapability.State);
        Assert.Equal("native-item-use/2.3.30", rotatedCapability.Version);
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void ReusedSaveSlotArchivesOldGenerationReadOnlyAndStartsAtZero()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var firstIds = new Queue<string>();
        firstIds.Enqueue("generation-old");
        firstIds.Enqueue("session-old");
        var first = CreateRepository(temporaryDirectory.Path, firstIds);
        first.Open(CreateIdentity(slot: 1, creationTicks: 100));
        first.Record(CreateUse("event-one", "generation-old"));
        first.Record(CreateHealing("healing-one", "event-one", "generation-old", 12.5));
        var firstGenerationDirectory = System.IO.Path.GetDirectoryName(first.CurrentProfilePath)!;
        new DiagnosticStore(
            System.IO.Path.Combine(firstGenerationDirectory, "diagnostics.json"),
            capacity: 5,
            () => TestTime).Add("old-generation evidence");
        first.CloseClean();

        var secondIds = new Queue<string>();
        secondIds.Enqueue("generation-new");
        secondIds.Enqueue("session-new");
        var second = CreateRepository(temporaryDirectory.Path, secondIds);
        var result = second.Open(CreateIdentity(slot: 1, creationTicks: 999));

        Assert.True(result.RotatedGeneration);
        Assert.Equal("generation-new", second.Current.GenerationId);
        Assert.Equal(0, second.Current.Statistics.Overall.ActivationCount);
        Assert.Equal(0, second.Current.Statistics.Overall.ActualHealthRestored);
        var archive = Assert.Single(Directory.EnumerateDirectories(
            System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "archives")));
        Assert.Contains("generation-old", archive, StringComparison.Ordinal);
        var archivedProfile = System.IO.Path.Combine(archive, "profile.json");
        var archivedDiagnostics = System.IO.Path.Combine(archive, "diagnostics.json");
        Assert.True((File.GetAttributes(archivedProfile) & FileAttributes.ReadOnly) != 0);
        Assert.True((File.GetAttributes(archivedDiagnostics) & FileAttributes.ReadOnly) != 0);
        Assert.Equal(
            "old-generation evidence",
            Assert.Single(new AtomicJsonStore<DiagnosticsDocument>().Load(archivedDiagnostics).Value!.Entries).Message);
        var archived = new AtomicJsonStore<ProfileDocument>().Load(archivedProfile).Value!;
        Assert.Equal(1, archived.Statistics.Overall.ActivationCount);
        Assert.Equal(12.5, archived.Statistics.Overall.ActualHealthRestored, precision: 6);
        second.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void ReusedSaveWithUnchangedCreationTimestampButDifferentFingerprintRotates()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var firstIds = new Queue<string>();
        firstIds.Enqueue("generation-old");
        firstIds.Enqueue("session-old");
        var first = CreateRepository(temporaryDirectory.Path, firstIds);
        var original = CreateIdentity(slot: 1, creationTicks: 100);
        first.Open(original);
        first.Record(CreateUse("event-one", "generation-old"));
        first.PrepareForNativeSave(original);

        var replacement = CreateIdentity(slot: 1, creationTicks: 100);
        replacement.ContentSha256 = new string('f', 64);
        replacement.SaveTimeBinary = original.SaveTimeBinary;
        var secondIds = new Queue<string>();
        secondIds.Enqueue("generation-new");
        secondIds.Enqueue("session-new");
        var second = CreateRepository(temporaryDirectory.Path, secondIds);

        var result = second.Open(replacement);

        Assert.True(result.RotatedGeneration);
        Assert.Equal("generation-new", second.Current.GenerationId);
        Assert.Equal(0, second.Current.Statistics.Overall.ActivationCount);
        second.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void InterruptedSessionSurvivesOneProvenNativeSaveStep()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var firstIds = new Queue<string>();
        firstIds.Enqueue("generation-a");
        firstIds.Enqueue("session-a");
        var first = CreateRepository(temporaryDirectory.Path, firstIds);
        var original = CreateIdentity(slot: 1, creationTicks: 100);
        first.Open(original);
        first.Record(CreateUse("event-one", "generation-a"));
        first.PrepareForNativeSave(original);

        var evolved = CreateIdentity(slot: 1, creationTicks: 100);
        evolved.ContentSha256 = new string('f', 64);
        evolved.SaveTimeBinary = TestTime
            .AddSeconds(1)
            .ToBinary();
        evolved.ObservedWriteUtcTicks++;
        evolved.ObservedLength++;
        var secondIds = new Queue<string>();
        secondIds.Enqueue("session-b");
        var second = CreateRepository(temporaryDirectory.Path, secondIds);

        var result = second.Open(evolved);

        Assert.False(result.RotatedGeneration);
        Assert.False(result.CreatedNew);
        Assert.True(result.InterruptedSessionRecovered);
        Assert.Equal("generation-a", second.Current.GenerationId);
        Assert.Equal(1, second.Current.Statistics.Overall.ActivationCount);
        Assert.Equal(evolved.ContentSha256, second.Current.Identity.ContentSha256);
        Assert.Null(second.Current.PendingSave);
        second.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void SameInstanceSameSlotReopenConsumesNativeSaveProofWithoutRotating()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var ids = new Queue<string>();
        ids.Enqueue("generation-a");
        ids.Enqueue("session-a");
        ids.Enqueue("session-b");
        var repository = CreateRepository(temporaryDirectory.Path, ids);
        var original = CreateIdentity(slot: 1, creationTicks: 100);
        repository.Open(original);
        repository.Record(CreateUse("event-one", "generation-a"));
        repository.PrepareForNativeSave(original);

        var evolved = CreateIdentity(slot: 1, creationTicks: 100);
        evolved.ContentSha256 = new string('f', 64);
        evolved.SaveTimeBinary = TestTime
            .AddSeconds(1)
            .ToBinary();
        evolved.ObservedWriteUtcTicks++;
        evolved.ObservedLength++;

        var result = repository.Open(evolved, "SaveSlotSelected");

        Assert.False(result.RotatedGeneration);
        Assert.False(result.CreatedNew);
        Assert.False(result.InterruptedSessionRecovered);
        Assert.Equal("generation-a", repository.Current.GenerationId);
        Assert.Equal(1, repository.Current.Statistics.Overall.ActivationCount);
        Assert.Equal(0, repository.Current.InterruptedSessionCount);
        Assert.Equal(evolved.ContentSha256, repository.Current.Identity.ContentSha256);
        Assert.Null(repository.Current.PendingSave);
        Assert.False(Directory.Exists(
            System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "archives")));
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void CleanCloseClearsPendingSaveIntentBeforeLaterChange()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var firstIds = new Queue<string>();
        firstIds.Enqueue("generation-old");
        firstIds.Enqueue("session-old");
        var first = CreateRepository(temporaryDirectory.Path, firstIds);
        var original = CreateIdentity(slot: 1, creationTicks: 100);
        first.Open(original);
        first.Record(CreateUse("event-one", "generation-old"));
        first.PrepareForNativeSave(original);
        first.CloseClean();

        var replacement = CreateIdentity(slot: 1, creationTicks: 100);
        replacement.ContentSha256 = new string('f', 64);
        replacement.SaveTimeBinary = TestTime
            .AddSeconds(1)
            .ToBinary();
        var secondIds = new Queue<string>();
        secondIds.Enqueue("generation-new");
        secondIds.Enqueue("session-new");
        var second = CreateRepository(temporaryDirectory.Path, secondIds);

        var result = second.Open(replacement);

        Assert.True(result.RotatedGeneration);
        Assert.Equal("generation-new", second.Current.GenerationId);
        Assert.Equal(0, second.Current.Statistics.Overall.ActivationCount);
        second.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void ExpiredPendingSaveIntentCannotBridgeLaterSlotReuse()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var firstIds = new Queue<string>();
        firstIds.Enqueue("generation-old");
        firstIds.Enqueue("session-old");
        var first = CreateRepository(temporaryDirectory.Path, firstIds);
        var original = CreateIdentity(slot: 1, creationTicks: 100);
        first.Open(original);
        first.Record(CreateUse("event-one", "generation-old"));
        first.PrepareForNativeSave(original);

        var replacement = CreateIdentity(slot: 1, creationTicks: 100);
        replacement.ContentSha256 = new string('f', 64);
        replacement.SaveTimeBinary = TestTime
            .AddMinutes(1)
            .ToBinary();
        var secondIds = new Queue<string>();
        secondIds.Enqueue("generation-new");
        secondIds.Enqueue("session-new");
        var second = CreateRepository(temporaryDirectory.Path, secondIds);

        var result = second.Open(replacement);

        Assert.True(result.RotatedGeneration);
        Assert.Equal("generation-new", second.Current.GenerationId);
        Assert.Equal(0, second.Current.Statistics.Overall.ActivationCount);
        second.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void NonzeroLegacyProfileWithoutFingerprintRotatesConservatively()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var firstIds = new Queue<string>();
        firstIds.Enqueue("generation-old");
        firstIds.Enqueue("session-old");
        var first = CreateRepository(temporaryDirectory.Path, firstIds);
        var legacyIdentity = CreateIdentity(slot: 1, creationTicks: 100);
        legacyIdentity.ContentSha256 = null;
        first.Open(legacyIdentity);
        first.Record(CreateUse("event-one", "generation-old"));
        first.CloseClean();

        var secondIds = new Queue<string>();
        secondIds.Enqueue("generation-new");
        secondIds.Enqueue("session-new");
        var second = CreateRepository(temporaryDirectory.Path, secondIds);

        var result = second.Open(CreateIdentity(slot: 1, creationTicks: 100));

        Assert.True(result.RotatedGeneration);
        Assert.Equal("generation-new", second.Current.GenerationId);
        second.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void TransientFingerprintReadFailureDoesNotEraseStoredContinuityProof()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var ids = new Queue<string>();
        ids.Enqueue("generation-a");
        ids.Enqueue("session-a");
        var repository = CreateRepository(temporaryDirectory.Path, ids);
        var stable = CreateIdentity(slot: 1, creationTicks: 100);
        repository.Open(stable);
        var unavailable = CreateIdentity(slot: 1, creationTicks: 100);
        unavailable.ObservedWriteUtcTicks = 999;
        unavailable.ContentSha256 = null;

        repository.RefreshIdentity(unavailable);

        Assert.Equal(stable.ContentSha256, repository.Current.Identity.ContentSha256);
        Assert.Equal(stable.ObservedWriteUtcTicks, repository.Current.Identity.ObservedWriteUtcTicks);
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void InterruptedSessionPreservesProfileAndIsRecordedOnce()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var firstIds = new Queue<string>();
        firstIds.Enqueue("generation-a");
        firstIds.Enqueue("session-a");
        var first = CreateRepository(temporaryDirectory.Path, firstIds);
        first.Open(CreateIdentity(slot: 1, creationTicks: 100));
        first.Record(CreateUse("event-one", "generation-a"));
        first.Record(CreateHealing("healing-one", "event-one", "generation-a", 9));

        var secondIds = new Queue<string>();
        secondIds.Enqueue("session-b");
        var second = CreateRepository(temporaryDirectory.Path, secondIds);
        var result = second.Open(CreateIdentity(slot: 1, creationTicks: 100));

        Assert.True(result.InterruptedSessionRecovered);
        Assert.Equal(1, second.Current.InterruptedSessionCount);
        Assert.Equal(1, second.Current.Statistics.Overall.ActivationCount);
        Assert.Equal(9, second.Current.Statistics.Overall.ActualHealthRestored, precision: 6);
        second.CloseClean();

        var thirdIds = new Queue<string>();
        thirdIds.Enqueue("session-c");
        var third = CreateRepository(temporaryDirectory.Path, thirdIds);
        var cleanResult = third.Open(CreateIdentity(slot: 1, creationTicks: 100));
        Assert.False(cleanResult.InterruptedSessionRecovered);
        Assert.Equal(1, third.Current.InterruptedSessionCount);
        third.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void UnrecoverableProfileIsArchivedInsteadOfOverwritten()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var currentDirectory = System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "current");
        Directory.CreateDirectory(currentDirectory);
        File.WriteAllText(System.IO.Path.Combine(currentDirectory, "profile.json"), "bad primary");
        File.WriteAllText(System.IO.Path.Combine(currentDirectory, "profile.json.bak"), "bad backup");
        var ids = new Queue<string>();
        ids.Enqueue("generation-safe");
        ids.Enqueue("session-safe");
        var repository = CreateRepository(temporaryDirectory.Path, ids);

        var result = repository.Open(CreateIdentity(slot: 1, creationTicks: 100));

        Assert.True(result.CreatedNew);
        Assert.True(result.RotatedGeneration);
        Assert.Equal("generation-safe", repository.Current.GenerationId);
        Assert.Equal(2, result.LoadFailures.Count);
        Assert.Single(Directory.EnumerateDirectories(
            System.IO.Path.Combine(temporaryDirectory.Path, "profiles", "slot-01", "archives")));
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void DiagnosticsAreBoundedAndRawTraceIsDisabledByDefault()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var path = System.IO.Path.Combine(temporaryDirectory.Path, "diagnostics.json");
        var diagnostics = new DiagnosticStore(path, capacity: 2, () => TestTime);

        diagnostics.Add("one");
        diagnostics.Add("two");
        diagnostics.Add("three");

        Assert.False(diagnostics.RawEventTraceEnabled);
        Assert.Equal(ExpectedDiagnosticMessages, diagnostics.Entries.Select(entry => entry.Message));
        var persisted = new AtomicJsonStore<DiagnosticsDocument>().Load(path).Value!;
        Assert.False(persisted.RawEventTraceEnabled);
        Assert.Equal(2, persisted.Entries.Count);
    }

    private static ProfileRepository CreateRepository(string path, string id) =>
        CreateRepository(path, () => id);

    private static ProfileRepository CreateRepository(string path, Queue<string> ids) =>
        CreateRepository(path, () => ids.Dequeue());

    private static ProfileRepository CreateRepository(string path, Func<string> idFactory) =>
        new(path, () => TestTime, idFactory);

    private static ProfileDocument CreateDocument(string generationId, long revision) => new()
    {
        GenerationId = generationId,
        Slot = 1,
        Revision = revision,
        CreatedUtc = TestTime,
        UpdatedUtc = TestTime,
        Identity = CreateIdentity(slot: 1, creationTicks: 100),
        Statistics = new()
        {
            SaveGenerationId = generationId,
            CreatedUtc = TestTime,
            UpdatedUtc = TestTime
        }
    };

    private static SaveIdentitySnapshot CreateIdentity(int slot, long creationTicks) => new()
    {
        Slot = slot,
        SaveFilePresent = true,
        SaveFileCreationUtcTicks = creationTicks,
        ObservedWriteUtcTicks = creationTicks + 10,
        ObservedLength = 4096,
        GameVersion = "2.3.30",
        ContentSha256 = creationTicks.ToString("x", System.Globalization.CultureInfo.InvariantCulture).PadLeft(64, '0'),
        SaveTimeBinary = TestTime.AddTicks(creationTicks).ToBinary()
    };

    private static ItemUseRecorded CreateUse(string eventId, string generationId) => new()
    {
        EventId = eventId,
        TimestampUtc = TestTime,
        SaveGenerationId = generationId,
        GameplayContext = GameplayContext.Raid,
        ItemId = "duckov:item:42",
        DisplayName = "Test item",
        Group = CanonicalItemGroup.Healing,
        ActivationCount = 1,
        AmountConsumed = 1,
        ConsumptionUnit = ConsumptionUnit.Item
    };

    private static HealingApplied CreateHealing(
        string eventId,
        string sourceItemUseEventId,
        string generationId,
        double amount) => new()
        {
            EventId = eventId,
            ApplicationId = $"application-{eventId}",
            SourceItemUseEventId = sourceItemUseEventId,
            TimestampUtc = TestTime,
            SaveGenerationId = generationId,
            GameplayContext = GameplayContext.Raid,
            ItemId = "duckov:item:42",
            DisplayName = "Test item",
            Group = CanonicalItemGroup.Healing,
            ActualHealthRestored = amount
        };
}
