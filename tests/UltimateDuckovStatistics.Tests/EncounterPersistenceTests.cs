using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Encounters;
using UltimateDuckovStatistics.Core.Export;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;
using UltimateDuckovStatistics.Sqlite;

namespace UltimateDuckovStatistics.Tests;

public sealed class EncounterPersistenceTests : IDisposable
{
    private readonly TemporaryDirectory directory = new();
    private readonly ProfileRecordCodec codec = new(NativeProfileJsonWriter.WriteRecord);
    private static readonly DateTime Now = NativeProfileJsonWriterTests.Now;

    public EncounterPersistenceTests() => SqliteLibrary.Initialize(Path.Combine(AppContext.BaseDirectory, "sqlite3.dll"));
    private string Database => Path.Combine(directory.Path, "profile.sqlite");
    private ProfileRepository Repository(string name = "repository") => new(Path.Combine(directory.Path, name), () => Now, () => Guid.NewGuid().ToString("N"),
        createIncrementalStorage: path => new SqliteProfileStorage(path, codec), recordCodec: codec);
    private static SaveIdentitySnapshot Identity() => new()
    {
        Slot = 1, SaveFilePresent = true, SaveFileCreationUtcTicks = Now.Ticks, GameVersion = "test",
        ContentSha256 = new string('a', 64), SaveTimeBinary = Now.ToBinary(), ObservedWriteUtcTicks = Now.Ticks, ObservedLength = 100
    };

    internal static List<EncounterRecord> Records(string run = "run-1") => new()
    {
        new() { RunId = run, Id = "visit", VisitId = "visit", Kind = EncounterRecordKind.Visit,
            Visit = new() { MapId = "GroundZero", SegmentId = "segment", Ordinal = 0, StartedSeconds = 0, EndedSeconds = 20,
                Calibration = new() { CenterX = 329, CenterZ = 241, Size = 560, ArtworkKey = new string('a', 64) } } },
        new() { RunId = run, Id = "visit/0", VisitId = "visit", Kind = EncounterRecordKind.Route,
            Route = new() { Index = 0, Sealed = true, Points = new()
            {
                new() { Seconds = 0, Connection = RouteConnection.Start, Position = new() { X = 1.2345678f, Y = -0.00000012f, Z = 200.00003f } },
                new() { Seconds = 2, Connection = RouteConnection.Teleport, Position = new() { X = 212.125f, Y = 5, Z = 409.75f } },
                new() { Seconds = 3, Connection = RouteConnection.Gap, Position = new() { X = 230, Y = 6, Z = 400 } }
            } } },
        new() { RunId = run, Id = "actor", VisitId = "visit", Kind = EncounterRecordKind.Encounter,
            Encounter = new() { ActorId = "capture/2", EnemyPresetKey = "Scav", StartedSeconds = 4, EndedSeconds = 8,
                Outcome = EncounterOutcome.PlayerKill, FinalSource = new() { Credit = EncounterCredit.Player, Mechanism = "effect" },
                PlayerPosition = new() { X = 20, Z = 40 }, EnemyPosition = new() { X = 44, Z = 40 } } },
        new() { RunId = run, Id = "outgoing", VisitId = "visit", EncounterId = "actor", Kind = EncounterRecordKind.Damage,
            Damage = new() { Amount = 142.91666793823242, Hits = 12, Headshots = 4, Source = new() { Credit = EncounterCredit.Player, WeaponTypeId = 783 } } },
        new() { RunId = run, Id = "observed", VisitId = "visit", EncounterId = "actor", Kind = EncounterRecordKind.Inventory,
            Inventory = new() { CorpseId = "capture/4", ObservedSeconds = 10, Complete = true, Slots = new()
            { new() { Slot = 0, Inspected = true, ItemTypeId = 783, Quantity = 1 }, new() { Slot = 1, Inspected = false } } } },
        new() { RunId = run, Id = "aspirin", VisitId = "visit", EncounterId = "actor", Kind = EncounterRecordKind.Loot,
            Loot = new() { CorpseId = "capture/4", ItemTypeId = 20, TakenToPlayer = 3, ReturnedByPlayer = 1 } }
    };

    [Fact]
    public void NativeWriterPortableReaderAndSqlitePreserveEveryRecordExactly()
    {
        var profile = NativeProfileJsonWriterTests.CreateProfile();
        profile.EncounterHistory = Records();
        foreach (var record in profile.EncounterHistory)
        {
            var portable = new ProfileRecordCodec().Encode(record);
            var nativeRoundTrip = ProfileRecordCodec.Decode<EncounterRecord>(codec.Encode(record));
            Assert.Equal(portable, new ProfileRecordCodec().Encode(nativeRoundTrip));
        }
        using var storage = new SqliteProfileStorage(Database, codec);
        storage.Import(profile, null, null);
        var loaded = storage.Load()!.Profile;
        Assert.Equal(codec.Encode(profile), codec.Encode(loaded));
        var route = loaded.EncounterHistory!.Single(record => record.Kind == EncounterRecordKind.Route).Route!;
        Assert.Equal(BitConverter.SingleToInt32Bits(1.2345678f), BitConverter.SingleToInt32Bits(route.Points[0].Position.X));
        Assert.Equal(RouteConnection.Teleport, route.Points[1].Connection);
        Assert.Null(loaded.EncounterHistory!.Single(record => record.Encounter != null).Encounter!.FinalSource!.WeaponTypeId);
    }

    [Fact]
    public void DeferredMutationOwnsItsBytesAndReopenKeepsLootTotals()
    {
        using var repository = Repository(); repository.Open(Identity());
        var records = Records();
        foreach (var record in records) repository.RecordEncounterDeferred(repository.CurrentGenerationId, record);
        records.Last().Loot!.TakenToPlayer = 999;
        var exposed = repository.ReadEncounters("run-1", EncounterRecordKind.Loot);
        Assert.Equal(3, Assert.Single(exposed).Loot!.TakenToPlayer);
        exposed[0].Loot!.TakenToPlayer = 888;
        repository.CloseClean();
        using var reopened = Repository(); reopened.Open(Identity());
        var loot = Assert.Single(reopened.ReadEncounters("run-1", EncounterRecordKind.Loot)).Loot!;
        Assert.Equal(3, loot.TakenToPlayer); Assert.Equal(1, loot.ReturnedByPlayer);
        Assert.Equal(2, loot.TakenToPlayer - loot.ReturnedByPlayer);
        reopened.CloseClean();
    }

    [Fact]
    public async Task OneChangedLootRowDoesNotRewriteHistoricalRoutesOrRuns()
    {
        using var repository = Repository(); repository.Open(Identity());
        for (var i = 0; i < 200; i++)
            foreach (var record in Records("run-" + i)) repository.RecordEncounterDeferred(repository.CurrentGenerationId, record);
        repository.Flush();
        using var audit = new SqliteStore(repository.CurrentProfilePath!, false);
        audit.Exec("CREATE TABLE writes(kind INTEGER,k1 TEXT,k2 TEXT,k3 TEXT)");
        audit.Exec("CREATE TRIGGER track_updates AFTER UPDATE ON records BEGIN INSERT INTO writes VALUES(new.kind,new.k1,new.k2,new.k3); END");
        var loot = Assert.Single(repository.ReadEncounters("run-199", EncounterRecordKind.Loot));
        loot.Loot!.TakenToPlayer++;
        repository.RecordEncounterDeferred(repository.CurrentGenerationId, loot);
        var snapshot = repository.CapturePersistenceSnapshot();
        await Task.Run(() => repository.SaveSnapshot(snapshot));
        var rows = audit.Rows("SELECT kind,k1,k2,k3 FROM writes WHERE kind<>1");
        var row = Assert.Single(rows);
        Assert.Equal(33L, row[0]); Assert.Equal("run-199", row[1]); Assert.Equal("6", row[2]); Assert.Equal("aspirin", row[3]);
        repository.CloseClean();
    }

    [Fact]
    public async Task FailedCommitRetainsRecordsAndOlderAcknowledgementKeepsNewerMutation()
    {
        var profile = NativeProfileJsonWriterTests.CreateProfile();
        using var storage = new SqliteProfileStorage(Database, codec); storage.Import(profile, null, null);
        var journal = new ProfileChangeJournal(profile.GenerationId, codec);
        foreach (var record in Records()) journal.Encounter(record, codec.Encode(record));
        var first = journal.Capture(profile);
        using var blocker = new SqliteStore(Database, false);
        blocker.Exec("CREATE TRIGGER reject_encounter BEFORE INSERT ON records WHEN new.kind=33 BEGIN SELECT RAISE(ABORT,'test failure'); END");
        await Assert.ThrowsAnyAsync<Exception>(() => storage.Commit(first));
        Assert.Equal(0, blocker.ScalarLong("SELECT count(*) FROM records WHERE kind=33"));
        blocker.Exec("DROP TRIGGER reject_encounter");
        var newer = Records().Last(); newer.Loot!.TakenToPlayer = 4;
        journal.Encounter(newer, codec.Encode(newer));
        await storage.Commit(first); journal.Acknowledge(first);
        var second = journal.Capture(profile);
        Assert.Equal(2, second.ChangedRecordCount); // metadata and the newer loot record
        await storage.Commit(second); journal.Acknowledge(second);
        Assert.Equal(4, storage.Load()!.Profile.EncounterHistory!.Single(row => row.Loot != null).Loot!.TakenToPlayer);
        Assert.Single(journal.Capture(profile).Records);
    }

    [Fact]
    public async Task OpeningAndSavingDoesNotDecodeUnselectedHistoricalPayloads()
    {
        var profile = NativeProfileJsonWriterTests.CreateProfile(); profile.EncounterHistory = Records();
        using (var storage = new SqliteProfileStorage(Database, codec)) storage.Import(profile, null, null);
        using (var damage = new SqliteStore(Database, false)) damage.Exec("UPDATE records SET payload=? WHERE kind=33 AND k2='2'", new byte[] { 1, 2, 3 });
        using var reopened = new SqliteProfileStorage(Database, codec);
        var loaded = reopened.Load()!.Profile;
        var journal = new ProfileChangeJournal(loaded.GenerationId, codec);
        await reopened.Commit(journal.Capture(loaded));
        Assert.ThrowsAny<Exception>(() => loaded.EncounterHistory!.Single(record => record.Route != null));
        Assert.True(File.Exists(Database + ".read-failure"));
    }

    [Fact]
    public void CrossGenerationMissingParentsAndSealedRouteEditsAreRejected()
    {
        using var repository = Repository(); repository.Open(Identity());
        var records = Records();
        Assert.Throws<ArgumentException>(() => repository.RecordEncounterDeferred("foreign", records[0]));
        Assert.Throws<ArgumentException>(() => repository.RecordEncounterDeferred(repository.CurrentGenerationId, records.Last()));
        Assert.Null(repository.Current.EncounterHistory);
        foreach (var record in records) repository.RecordEncounterDeferred(repository.CurrentGenerationId, record);
        repository.Flush();
        records[1].Route!.Points[0].Position.X++;
        Assert.Throws<ArgumentException>(() => repository.RecordEncounterDeferred(repository.CurrentGenerationId, records[1]));
        repository.CloseClean();
    }

    [Theory]
    [InlineData("hidden")]
    [InlineData("nan")]
    [InlineData("kind")]
    [InlineData("duplicate")]
    [InlineData("owner")]
    [InlineData("limit")]
    public void MalformedEvidenceCannotBeImported(string failure)
    {
        var records = Records();
        switch (failure)
        {
            case "hidden": records[4].Inventory!.Slots[1].Quantity = 1; records[4].Inventory!.Slots[1].ItemTypeId = 20; break;
            case "nan": records[1].Route!.Points[0].Position.X = float.NaN; break;
            case "kind": records[0].Kind = EncounterRecordKind.Loot; break;
            case "duplicate": records.Add(records[0]); break;
            case "owner": records[5].EncounterId = "missing"; break;
            case "limit": records[1].Route!.Points = Enumerable.Repeat(records[1].Route!.Points[0], 257).ToList(); break;
        }
        var profile = NativeProfileJsonWriterTests.CreateProfile(); profile.EncounterHistory = records;
        using var storage = new SqliteProfileStorage(Database, codec);
        Assert.Throws<ArgumentException>(() => storage.Import(profile, null, null));
        Assert.False(File.Exists(Database));
    }

    [Fact]
    public async Task DetachedZipExportRestoresHistoryAndExcludesUnfinishedRun()
    {
        using var source = Repository(); source.Open(Identity());
        var tracker = IncrementalCheckpointProtocolTests.Started(source.CurrentGenerationId, route: true, runId: "run-1");
        var run = tracker.Apply(new RunLifecycleEvent { Kind = RunLifecycleEventKind.Extracted, TimestampUtc = Now.AddMinutes(1), MonotonicSeconds = 20 }).Completed!;
        source.CompleteRun(run);
        foreach (var record in Records().Concat(Records("active-run"))) source.RecordEncounterDeferred(source.CurrentGenerationId, record);
        source.Flush();
        using var snapshot = await source.CaptureExportSnapshotAsync();
        var changed = Records().Last(); changed.Loot!.TakenToPlayer = 99;
        source.RecordEncounterDeferred(source.CurrentGenerationId, changed); source.CloseClean();
        var export = ProfileExportWriter.WriteToRoot(snapshot, Path.Combine(directory.Path, "exports"), Now);
        var preview = StatisticsRestoreReader.Read(Assert.Single(export.Files), 1);
        var expected = Records();
        using var target = Repository("target"); target.Open(Identity());
        target.RestoreStatistics(Identity(), preview, target.CurrentGenerationId);
        Assert.Equal(codec.Encode(expected.OrderBy(record => record.Kind).ToList()), codec.Encode(target.Current.EncounterHistory!.OrderBy(record => record.Kind).ToList()));
        Assert.NotEqual(preview.SourceGenerationId, target.CurrentGenerationId);
        target.CloseClean();
        using var reopened = Repository("target"); reopened.Open(Identity());
        Assert.Equal(6, reopened.Current.EncounterHistory!.Count);
        Assert.Equal(3, Assert.Single(reopened.ReadEncounters("run-1", EncounterRecordKind.Loot)).Loot!.TakenToPlayer);
        reopened.CloseClean();
    }

    [Fact]
    public void RouteRecorderBoundsTailsRetainsFailedPublicationAndPreservesTeleports()
    {
        var captured = new List<EncounterRecord>(); var fail = true;
        var recorder = new EncounterRouteRecorder("run", "visit", record => { if (fail) throw new IOException("test"); captured.Add(record); });
        for (var i = 0; i < 256; i++) recorder.Append(i, new() { X = i }, i == 0 ? RouteConnection.Start : RouteConnection.Walk);
        Assert.Throws<IOException>(() => recorder.Flush(seal: true));
        fail = false;
        recorder.Append(256, new() { X = 600 }, RouteConnection.Teleport);
        recorder.Flush(); recorder.Flush();
        Assert.Equal(2, captured.Count);
        Assert.True(captured[0].Route!.Sealed); Assert.Equal(256, captured[0].Route!.Points.Count);
        Assert.Equal(1, captured[1].Route!.Index); Assert.False(captured[1].Route!.Sealed);
        Assert.Equal(RouteConnection.Teleport, Assert.Single(captured[1].Route!.Points).Connection);
        recorder.Flush(seal: true);
        Assert.True(captured[2].Route!.Sealed);
    }

    [Fact]
    public void RestoredEncounterHistoryAndPendingLootSurviveActivationRetry()
    {
        var source = StatisticsRestoreTests.PopulatedProfile(); source.EncounterHistory = Records();
        var path = Path.Combine(directory.Path, "restore.json");
        File.WriteAllText(path, StatisticsExporter.Create(source, Now).Json);
        var preview = StatisticsRestoreReader.Read(path, 1);
        var fail = false;
        using var target = new ProfileRepository(Path.Combine(directory.Path, "target"), () => Now, () => Guid.NewGuid().ToString("N"),
            createIncrementalStorage: storagePath =>
            {
                if (fail && !storagePath.Contains(".uds-reset-", StringComparison.Ordinal)) throw new IOException("Transient post-promotion reopen failure.");
                return new SqliteProfileStorage(storagePath, codec);
            }, recordCodec: codec);
        target.Open(Identity()); var previous = target.CurrentGenerationId;
        fail = true;
        Assert.Throws<IOException>(() => target.RestoreStatistics(Identity(), preview, previous));
        var promoted = target.CurrentGenerationId;
        Assert.NotEqual(previous, promoted);
        var next = Records().Last(); next.Loot!.TakenToPlayer = 4;
        target.RecordEncounterDeferred(promoted, next);
        fail = false;
        target.RestoreStatistics(Identity(), preview, previous);
        Assert.Equal(promoted, target.CurrentGenerationId);
        target.CloseClean();
        using var reopened = Repository("target"); reopened.Open(Identity());
        Assert.Equal(6, reopened.Current.EncounterHistory!.Count);
        Assert.Equal(4, Assert.Single(reopened.ReadEncounters("run-1", EncounterRecordKind.Loot)).Loot!.TakenToPlayer);
        reopened.CloseClean();
    }

    [Fact]
    public void FormatSevenUpgradeDoesNotTouchExistingPayloads()
    {
        var profile = NativeProfileJsonWriterTests.CreateProfile();
        using (var storage = new SqliteProfileStorage(Database, codec)) storage.Import(profile, null, null);
        using (var setup = new SqliteStore(Database, false))
        {
            setup.Exec("PRAGMA user_version=7");
            setup.Exec("CREATE TRIGGER no_payload_rewrites BEFORE UPDATE ON records BEGIN SELECT RAISE(ABORT,'history rewrite'); END");
        }
        using var upgraded = new SqliteProfileStorage(Database, codec);
        Assert.Equal(codec.Encode(profile), codec.Encode(upgraded.Load()!.Profile));
        using var reader = new SqliteStore(Database, true);
        Assert.Equal(8, reader.ScalarLong("PRAGMA user_version"));
    }

    [Theory]
    [InlineData(EncounterRecordKind.Loot)]
    [InlineData(EncounterRecordKind.Damage)]
    [InlineData(EncounterRecordKind.Inventory)]
    [InlineData(EncounterRecordKind.Encounter)]
    public void AcceptedEvidenceCannotRegress(EncounterRecordKind kind)
    {
        var previous = Records().Single(record => record.Kind == kind);
        var changed = ProfileRecordCodec.Decode<EncounterRecord>(codec.Encode(previous));
        if (changed.Loot != null) changed.Loot.TakenToPlayer--;
        if (changed.Damage != null) changed.Damage.Hits--;
        if (changed.Inventory != null) changed.Inventory.ObservedSeconds--;
        if (changed.Encounter != null) changed.Encounter.EndedSeconds++;
        Assert.Throws<ArgumentException>(() => EncounterRecordValidation.ValidateReplacement(previous, changed));
    }

    [Fact]
    public void RestoreRejectsOrphanedAndDuplicateEncounterRecords()
    {
        var profile = StatisticsRestoreTests.PopulatedProfile(); profile.EncounterHistory = Records();
        var export = StatisticsExporter.Create(profile, Now).Json;
        var path = Path.Combine(directory.Path, "invalid.json");
        File.WriteAllText(path, export.Replace("\"EncounterId\":\"actor\"", "\"EncounterId\":\"missing\"", StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => StatisticsRestoreReader.Read(path, 1));
        profile.EncounterHistory.Add(profile.EncounterHistory[0]);
        File.WriteAllText(path, StatisticsExporter.Create(profile, Now).Json);
        Assert.Throws<InvalidDataException>(() => StatisticsRestoreReader.Read(path, 1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExportWithoutCompletedEncountersRestoresWithNoInventedHistory(bool explicitEmpty)
    {
        var profile = StatisticsRestoreTests.PopulatedProfile(); profile.EncounterHistory = Records("active-only");
        var node = System.Text.Json.Nodes.JsonNode.Parse(StatisticsExporter.Create(profile, Now).Json)!;
        Assert.Null(node["EncounterHistory"]);
        if (explicitEmpty) node["EncounterHistory"] = new System.Text.Json.Nodes.JsonArray();
        var path = Path.Combine(directory.Path, "empty.json"); File.WriteAllText(path, node.ToJsonString());
        var preview = StatisticsRestoreReader.Read(path, 1);
        using var target = Repository(); target.Open(Identity());
        target.RestoreStatistics(Identity(), preview, target.CurrentGenerationId);
        Assert.Null(target.Current.EncounterHistory);
        target.CloseClean();
    }

    public void Dispose() => directory.Dispose();
}
