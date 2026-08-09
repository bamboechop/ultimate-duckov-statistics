using UltimateDuckovStatistics.Core;
using UltimateDuckovStatistics.Core.Diagnostics;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Tests;

public sealed class PersistenceTests
{
    private static readonly DateTime TestTime = new(2026, 8, 9, 12, 0, 0, DateTimeKind.Utc);
    private static readonly string[] ExpectedDiagnosticMessages = { "two", "three" };

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
    public void ReusedSaveSlotArchivesOldGenerationReadOnlyAndStartsAtZero()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var firstIds = new Queue<string>();
        firstIds.Enqueue("generation-old");
        firstIds.Enqueue("session-old");
        var first = CreateRepository(temporaryDirectory.Path, firstIds);
        first.Open(CreateIdentity(slot: 1, creationTicks: 100));
        first.Record(CreateUse("event-one", "generation-old"));
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
        second.CloseClean();
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

        var secondIds = new Queue<string>();
        secondIds.Enqueue("session-b");
        var second = CreateRepository(temporaryDirectory.Path, secondIds);
        var result = second.Open(CreateIdentity(slot: 1, creationTicks: 100));

        Assert.True(result.InterruptedSessionRecovered);
        Assert.Equal(1, second.Current.InterruptedSessionCount);
        Assert.Equal(1, second.Current.Statistics.Overall.ActivationCount);
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
        GameVersion = "2.3.30"
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
}
