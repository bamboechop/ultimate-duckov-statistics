using System.IO.Compression;
using System.Text;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Tracking;
using UltimateDuckovStatistics.Sqlite;
using UltimateDuckovStatistics.Core.Export;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Tests;

public sealed class StatisticsRestoreTests
{
    private static readonly DateTime Now = NativeProfileJsonWriterTests.Now;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void JsonAndZipRestoreRecordedValuesIntoAFreshGenerationAndPreservePriorFiles(bool zip)
    {
        using var directory = new TemporaryDirectory();
        var profile = PopulatedProfile();
        var json = StatisticsExporter.Create(profile, Now).Json;
        var path = Path.Combine(directory.Path, zip ? "statistics.zip" : "statistics.json");
        Write(path, json, zip);
        var original = File.ReadAllBytes(path);
        var preview = StatisticsRestoreReader.Read(path, profile.Slot);
        var sequence = 0;
        using var repository = new ProfileRepository(Path.Combine(directory.Path, "target"), () => Now, () => "new-" + ++sequence);
        var identity = Identity(profile.Slot);
        repository.Open(identity);
        var previous = repository.CurrentGenerationId;
        repository.RestoreStatistics(identity, preview, previous);

        Assert.NotEqual(previous, repository.CurrentGenerationId);
        Assert.NotEqual(profile.GenerationId, repository.CurrentGenerationId);
        Assert.Equal(profile.Statistics.Overall.ActivationCount, repository.Current.Statistics.Overall.ActivationCount);
        Assert.Equal(new ProfileRecordCodec().Encode(profile.Statistics.Crafting), new ProfileRecordCodec().Encode(repository.Current.Statistics.Crafting));
        Assert.Equal(profile.Statistics.Runs.Count, repository.Current.Statistics.Runs.Count);
        Assert.All(repository.Current.Statistics.Runs, run => Assert.Equal(repository.CurrentGenerationId, run.SaveGenerationId));
        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Single(Directory.GetDirectories(Path.Combine(directory.Path, "target", "profiles", $"slot-{profile.Slot:D2}", "archives")));
        repository.CloseClean();
        repository.Open(identity);
        Assert.Equal(profile.Statistics.Overall.ActivationCount, repository.Current.Statistics.Overall.ActivationCount);
    }

    [Fact]
    public void WrongSlotMissingMembersAndUnknownSchemaAreRejected()
    {
        using var directory = new TemporaryDirectory();
        var profile = NativeProfileJsonWriterTests.CreateProfile();
        var path = Path.Combine(directory.Path, "statistics.json");
        var json = StatisticsExporter.Create(profile, Now).Json;
        File.WriteAllText(path, json);
        Assert.Throws<InvalidDataException>(() => StatisticsRestoreReader.Read(path, profile.Slot + 1));
        File.WriteAllText(path, "{}");
        Assert.ThrowsAny<Exception>(() => StatisticsRestoreReader.Read(path, profile.Slot));
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        node["SchemaVersion"] = 99;
        File.WriteAllText(path, node.ToJsonString());
        Assert.Throws<InvalidDataException>(() => StatisticsRestoreReader.Read(path, profile.Slot));
    }

    [Theory]
    [InlineData("../statistics.json")]
    [InlineData("folder/statistics.json")]
    [InlineData("profile.json")]
    public void ZipRejectsOtherEntryPathsWithoutExtractingThem(string name)
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "statistics.zip");
        using (var file = File.Create(path))
        using (var archive = new ZipArchive(file, ZipArchiveMode.Create))
        using (var entry = archive.CreateEntry(name).Open()) entry.Write(Encoding.UTF8.GetBytes("{}"));
        Assert.Throws<InvalidDataException>(() => StatisticsRestoreReader.Read(path, 1));
        Assert.Single(Directory.GetFiles(directory.Path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedPreparationAndChangedDestinationPreserveCurrentStatistics(bool sqlite)
    {
        using var directory = new TemporaryDirectory();
        var source = NativeProfileJsonWriterTests.CreateProfile();
        var path = Path.Combine(directory.Path, "statistics.json");
        File.WriteAllText(path, StatisticsExporter.Create(source, Now).Json);
        var preview = StatisticsRestoreReader.Read(path, source.Slot);
        var fail = false;
        var sequence = 0;
        if (sqlite) SqliteLibrary.Initialize(Path.Combine(AppContext.BaseDirectory, "sqlite3.dll"));
        using var repository = new ProfileRepository(Path.Combine(directory.Path, "target"), () => Now, () => "new-" + ++sequence,
            writeProfile: (stream, profile) =>
            {
                if (fail && profile.GenerationReason == "UserRestore") throw new IOException("Disk failure during preparation.");
                new System.Runtime.Serialization.Json.DataContractJsonSerializer(typeof(ProfileDocument),
                    new System.Runtime.Serialization.Json.DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true }).WriteObject(stream, profile);
            }, createIncrementalStorage: sqlite ? storagePath =>
            {
                if (fail && storagePath.Contains(".uds-reset-", StringComparison.Ordinal)) throw new IOException("Preparation failed.");
                return new SqliteProfileStorage(storagePath, new ProfileRecordCodec());
            }
        : null);
        var identity = Identity(source.Slot);
        repository.Open(identity);
        var generation = repository.CurrentGenerationId;
        Assert.Throws<InvalidOperationException>(() => repository.RestoreStatistics(identity, preview, "stale"));
        fail = true;
        Assert.Throws<UserProfileResetFailedException>(() => repository.RestoreStatistics(identity, preview, generation));
        Assert.Equal(generation, repository.CurrentGenerationId);
        fail = false;
        repository.RestoreStatistics(identity, preview, generation);
        Assert.NotEqual(generation, repository.CurrentGenerationId);
    }

    private static void Write(string path, string json, bool zip)
    {
        if (!zip) { File.WriteAllText(path, json); return; }
        using var file = File.Create(path);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        using var entry = archive.CreateEntry("statistics.json").Open();
        var bytes = Encoding.UTF8.GetBytes(json);
        entry.Write(bytes, 0, bytes.Length);
    }

    internal static ProfileDocument PopulatedProfile()
    {
        var profile = NativeProfileJsonWriterTests.CreateProfile();
        for (var i = 0; i < 3; i++) Assert.True(RunReducer.Apply(profile.Statistics, RecordedRun(profile.GenerationId, "run-" + i)));
        profile.Statistics.BaseMovement = new() { RecordedMeters = 123.456, CollectionStartedUtc = Now, HasKnownGaps = true };
        profile.Statistics.Overall = new() { ActivationCount = 2, ActualHealthRestored = 31.25 };
        profile.Statistics.Groups.Add(CanonicalItemGroup.Healing.ToString(), new() { ActivationCount = 2, ActualHealthRestored = 31.25 });
        profile.Statistics.Items.Add("med", new() { ItemId = "med", DisplayName = "Medicine", Group = CanonicalItemGroup.Healing, Totals = new() { ActivationCount = 2, ActualHealthRestored = 31.25 } });
        WorldTimeStatisticsReducer.InitializeOrRestrictCapabilities(profile.Statistics.WorldTime, WorldTimeNativeContractPolicy.Supported("clock", "sleep"));
        Assert.True(WorldTimeStatisticsReducer.Apply(profile.Statistics.WorldTime, new WorldTimeMutation(2, 1000, 1, 700)));
        Assert.True(CraftingStatisticsReducer.Apply(profile.Statistics.Crafting, new CraftingMutation(profile.GenerationId, Now,
            new[] { new CraftingMutationRow("100", "Bandage", "bandage", 1, 2, new() { ["2"] = 1 }) })));
        profile.Statistics.Holdings.Money = new()
        {
            State = EconomyHoldingObservationState.LastObserved,
            Value = 420,
            ObservedUtc = Now,
            SaveGenerationId = profile.GenerationId,
            ObservationProvenance = "fixture",
            FreshnessProvenance = "fixture"
        };
        return profile;
    }

    private static SaveIdentitySnapshot Identity(int slot) => new()
    {
        Slot = slot,
        SaveFilePresent = true,
        ContentSha256 = new string('a', 64),
        SaveTimeBinary = Now.ToBinary()
    };

    internal static RunSummary RecordedRun(string generation, string id)
    {
        var tracker = IncrementalCheckpointProtocolTests.Started(generation, route: true, runId: id);
        var shot = WeaponStatisticsTests.Shot("shot-" + id, "weapon", "Weapon", "ammo", "Ammo", 2);
        shot.SaveGenerationId = generation; shot.RunId = tracker.ActiveRunId!; shot.SegmentId = tracker.ActiveSegmentId;
        Assert.True(tracker.RecordShot(shot));
        return tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.Extracted,
            TimestampUtc = new DateTime(2026, 8, 10, 10, 0, 0, DateTimeKind.Utc).AddSeconds(10),
            MonotonicSeconds = 10
        }).Completed!;
    }

    [Fact]
    public async Task NativeSqliteRestoreSurvivesSourceChangesReopenAndNewActivityWithoutDuplicatingRuns()
    {
        using var directory = new TemporaryDirectory();
        SqliteLibrary.Initialize(Path.Combine(AppContext.BaseDirectory, "sqlite3.dll"));
        var source = PopulatedProfile();
        var path = Path.Combine(directory.Path, "statistics.zip");
        Write(path, StatisticsExporter.Create(source, Now).Json, true);
        var preview = StatisticsRestoreReader.Read(path, 1);
        File.WriteAllText(path, "changed after preview");
        var root = Path.Combine(directory.Path, "target");
        var identity = Identity(1);
        string restoredGeneration;
        using (var repository = NativeProfileStorage.Create(root, _ => { }))
        {
            repository.Open(identity);
            repository.RecordBaseMovementDeferred(new() { GenerationId = repository.CurrentGenerationId, CaptureId = "old", CapturedMeters = 99, CollectionStartedUtc = Now });
            repository.RestoreStatistics(identity, preview, repository.CurrentGenerationId);
            restoredGeneration = repository.CurrentGenerationId;
            Assert.EndsWith("profile.sqlite", repository.CurrentProfilePath);
            Assert.Equal(123.456, repository.Current.Statistics.BaseMovement!.RecordedMeters);
            Assert.Equal(3, repository.Current.Statistics.Runs.Count);
            Assert.Equal(31.25, repository.Current.Statistics.Overall.ActualHealthRestored);
            Assert.Equal(420, repository.Current.Statistics.Holdings.Money.Value);
            Assert.Equal(restoredGeneration, repository.Current.Statistics.Holdings.Money.SaveGenerationId);
            using var snapshot = await repository.CaptureExportSnapshotAsync();
            var result = ProfileExportWriter.WriteToRoot(snapshot, Path.Combine(directory.Path, "reexport"), Now);
            Assert.Equal(3, StatisticsRestoreReader.Read(result.Files.Single(), 1).RunCount);
            repository.CloseClean();
        }
        using (var reopened = NativeProfileStorage.Create(root, _ => { }))
        {
            reopened.Open(identity);
            Assert.Equal(restoredGeneration, reopened.CurrentGenerationId);
            Assert.Equal(3, reopened.Current.Statistics.RunTotals.TotalRuns);
            Assert.Equal(2, reopened.Current.Statistics.Crafting.ProducedQuantity);
            Assert.Equal(2, reopened.Current.Statistics.WorldTime.CalendarDaysAdvanced);
            reopened.RecordBaseMovementDeferred(new() { GenerationId = restoredGeneration, CaptureId = "new", CapturedMeters = 5, CollectionStartedUtc = Now });
            Assert.True(reopened.CompleteRun(RecordedRun(restoredGeneration, "new-run")));
            reopened.CloseClean();
        }
        using var final = NativeProfileStorage.Create(root, _ => { }); final.Open(identity);
        Assert.Equal(4, final.Current.Statistics.RunTotals.TotalRuns);
        Assert.Equal(128.456, final.Current.Statistics.BaseMovement!.RecordedMeters, 8);
        Assert.Null(ProfileFormat.ValidateRecoveryCandidate(final.Current));
    }

    [Theory]
    [InlineData("duplicate-run")]
    [InlineData("negative-distance")]
    [InlineData("wrong-count")]
    [InlineData("missing-items")]
    [InlineData("truncated")]
    [InlineData("trailing")]
    public void InvalidExportsFailBeforeTheyCanBePresentedForReplacement(string defect)
    {
        using var directory = new TemporaryDirectory();
        var profile = PopulatedProfile();
        var json = StatisticsExporter.Create(profile, Now).Json;
        var node = System.Text.Json.Nodes.JsonNode.Parse(json)!;
        if (defect == "duplicate-run") node["Runs"]![1]!["RunId"] = node["Runs"]![0]!["RunId"]!.GetValue<string>();
        if (defect == "negative-distance") node["RunTotals"]!["PhysicalDistance"] = -1;
        if (defect == "wrong-count") node["RunTotals"]!["TotalRuns"] = 7;
        if (defect == "missing-items") node.AsObject().Remove("Items");
        json = node.ToJsonString();
        if (defect == "truncated") json = json[..^10];
        if (defect == "trailing") json += "{}";
        var path = Path.Combine(directory.Path, "statistics.json"); File.WriteAllText(path, json);
        Assert.ThrowsAny<Exception>(() => StatisticsRestoreReader.Read(path, 1));
    }
}
