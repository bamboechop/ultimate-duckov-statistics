using System.IO.Compression;
using Newtonsoft.Json.Linq;
using UltimateDuckovStatistics.Core.Encounters;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Sqlite;
using UltimateDuckovStatistics.Encounters;

namespace UltimateDuckovStatistics.Tests;

public sealed class EncounterLiveCaptureTests
{
    [Theory]
    [InlineData(59.125, "00:59.125")]
    [InlineData(3599.5, "59:59.500")]
    [InlineData(3600, "1:00:00.000")]
    [InlineData(7425.75, "2:03:45.750")]
    public void EncounterTimesKeepElapsedHours(double seconds, string expected) =>
        Assert.Equal(expected, StoredEncounterMap.FormatEventTime(seconds));

    [Fact]
    public void ClassifiedHeadshotsSurviveBatchesWithoutCountingCriticalOrRejectedDamage()
    {
        var pipe = new EncounterCapturePipeline();
        void Hurt(double amount, bool headshot, bool completed, bool critical) => pipe.Record("generation", "run", "map", "segment", 1,
            "combat_hurt_complete", new { Target = new { Id = 2, PresetKey = "Cname_Scav" },
                Source = new { Credited = new { Id = 1, IsMain = true }, Kind = "projectile", WeaponId = 736 },
                ProposedHpLossOwnedAssignments = amount, Headshot = headshot, NativeCallCompleted = completed, Crit = critical });
        var saved = new Dictionary<string, EncounterRecord>();
        bool Publish(string generation, EncounterRecord record) { saved[record.Id] = record; return true; }
        Hurt(10, true, true, false); // Proven launch-time head target, no native critical.
        Assert.True(pipe.Pump(Publish, flush: true));
        Hurt(10, false, true, true); // Critical alone is not a headshot.
        Hurt(0, true, true, false);
        Hurt(10, true, false, false);
        Assert.True(pipe.Pump(Publish, flush: true));
        var damage = Assert.Single(saved.Values, record => record.Damage != null).Damage!;
        Assert.Equal(20, damage.Amount); Assert.Equal(2, damage.Hits); Assert.Equal(1, damage.Headshots);
    }

    private static JObject[] Fixture(string name)
    {
        var assembly = typeof(EncounterLiveCaptureTests).Assembly;
        using var resource = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames().Single(value => value.EndsWith(name + ".jsonl.gz", StringComparison.Ordinal)))!;
        using var gzip = new GZipStream(resource, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        return reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(JObject.Parse).ToArray();
    }
    private static EncounterRecord[] Replay(string fixture, int cadence = 7)
    {
        var reducer = new EncounterEvidenceProjection("test-session");
        var records = new Dictionary<(EncounterRecordKind, string), EncounterRecord>();
        void Flush()
        {
            foreach (var record in reducer.Flush())
            {
                if (records.TryGetValue((record.Kind, record.Id), out var old)) EncounterRecordValidation.ValidateReplacement(old, record);
                records[(record.Kind, record.Id)] = record;
            }
        }
        var input = Fixture(fixture);
        for (var i = 0; i < input.Length; i++)
        {
            var row = input[i];
            reducer.Apply("test-run", (string)row["MapId"]!, (string)row["SegmentId"]!, (double)row["Time"]!, (string)row["Kind"]!, (JObject)row["Data"]!);
            if (i % cadence == 0) Flush();
        }
        reducer.Apply("test-run", "", "", (double)input.Last()["Time"]!, "session-end", new JObject()); Flush();
        EncounterRecordValidation.ValidateHistory(records.Values.ToList(), new HashSet<string> { "test-run" });
        return records.Values.ToArray();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(5000)]
    public void QualifiedLootSurvivesEveryPublicationCadence(int cadence)
    {
        var records = Replay("loot-observation", cadence);
        Assert.Equal(3, records.Count(record => record.Encounter?.Outcome == EncounterOutcome.PlayerKill));
        var aspirin = Assert.Single(records, record => record.Loot?.ItemTypeId == 20).Loot!;
        Assert.Equal(3, aspirin.TakenToPlayer); Assert.Equal(1, aspirin.ReturnedByPlayer);
        Assert.Equal(19, Assert.Single(records, record => record.Loot?.ItemTypeId == 451).Loot!.TakenToPlayer);
        Assert.DoesNotContain(records, record => record.Loot?.ItemTypeId is 783 or 594);
        Assert.All(records.Where(record => record.Inventory != null).SelectMany(record => record.Inventory!.Slots).Where(slot => !slot.Inspected),
            slot => { Assert.Null(slot.ItemTypeId); Assert.Null(slot.Quantity); });
        Assert.Contains(records, record => record.Inventory?.Slots.Any(slot => slot.ItemTypeId == 20 && slot.Quantity == 3) == true);
        Assert.Equal(3, records.Where(record => record.Inventory != null)
            .Max(record => record.Inventory!.Slots.Where(slot => slot.ItemTypeId == 20).Sum(slot => slot.Quantity ?? 0)));
        Assert.Contains(records, record => record.Damage is { Incoming: true, Amount: > 0, Source.WeaponTypeId: 783 });
        Assert.All(records.Where(record => record.Route != null), record => Assert.True(record.Route!.Sealed));
        var map = Assert.Single(StoredEncounterMap.Build(records));
        Assert.Equal(3, map.Events.Length); Assert.NotEmpty(map.Strokes); Assert.False(map.RouteLimited);
    }

    [Fact]
    public void QualifiedRouteKeepsBothTeleportsWithoutInventingFourthPlayerKill()
    {
        var records = Replay("route-teleports");
        Assert.Equal(3, records.Count(record => record.Encounter?.Outcome == EncounterOutcome.PlayerKill));
        Assert.Equal(3, records.Count(record => record.Encounter?.Outcome != null));
        Assert.Equal(2, records.Where(record => record.Route != null).SelectMany(record => record.Route!.Points).Count(point => point.Connection == RouteConnection.Teleport));
        Assert.Equal(142.91666793823242, records.Where(record => record.Damage?.Incoming == false).Sum(record => record.Damage!.Amount), 6);
        var map = Assert.Single(StoredEncounterMap.Build(records));
        Assert.Equal(2, map.Strokes.Count(stroke => stroke.Dotted));
        Assert.All(map.Events, record => Assert.NotNull(record.Encounter!.EnemyPosition));
    }

    [Fact]
    public void PipelineRetainsRejectedPublicationWithoutDuplicatingDamage()
    {
        var clock = 0d;
        var pipe = new EncounterCapturePipeline(() => clock);
        foreach (var row in Fixture("loot-observation"))
            pipe.Record("generation", "test-run", (string)row["MapId"]!, (string)row["SegmentId"]!, (double)row["Time"]!, (string)row["Kind"]!, row["Data"]!);
        var accepted = new Dictionary<(EncounterRecordKind, string), EncounterRecord>(); var attempts = 0;
        bool Publish(string gen, EncounterRecord record)
        {
            Assert.Equal("generation", gen);
            if (++attempts == 3) return false;
            accepted[(record.Kind, record.Id)] = record; return true;
        }
        Assert.False(pipe.Pump(Publish, flush: true));
        clock += 0.5;
        Assert.True(pipe.Pump(Publish, flush: true), pipe.Failure);
        Assert.False(pipe.HasPending);
        Assert.Equal(3, Assert.Single(accepted.Values, record => record.Loot?.ItemTypeId == 20).Loot!.TakenToPlayer);
        Assert.Equal(3, accepted.Values.Count(record => record.Encounter?.Outcome == EncounterOutcome.PlayerKill));
    }

    [Fact]
    public void MissingArtworkStillProducesEncounterList()
    {
        var records = Replay("loot-observation");
        foreach (var record in records.Where(record => record.Visit != null)) record.Visit!.Calibration = null;
        var map = Assert.Single(StoredEncounterMap.Build(records));
        Assert.Null(map.Calibration); Assert.Empty(map.Strokes); Assert.Equal(3, map.Events.Length);
        Assert.Contains(map.Records, record => record.Loot?.TakenToPlayer == 19);
    }

    [Fact]
    public void LivePipelineRecordsReopenThroughIndexedSqlite()
    {
        using var directory = new TemporaryDirectory();
        SqliteLibrary.Initialize(Path.Combine(AppContext.BaseDirectory, "sqlite3.dll"));
        var codec = new ProfileRecordCodec(NativeProfileJsonWriter.WriteRecord);
        var now = NativeProfileJsonWriterTests.Now;
        var identity = new SaveIdentitySnapshot
        { Slot = 1, SaveFilePresent = true, SaveFileCreationUtcTicks = now.Ticks, ContentSha256 = new string('a', 64), ObservedLength = 1, ObservedWriteUtcTicks = now.Ticks };
        ProfileRepository Open() => new(directory.Path, () => now, () => Guid.NewGuid().ToString("N"),
            createIncrementalStorage: path => new SqliteProfileStorage(path, codec), recordCodec: codec);
        var pipe = new EncounterCapturePipeline();
        using (var repository = Open())
        {
            repository.Open(identity);
            var rows = Fixture("loot-observation");
            for (var i = 0; i < rows.Length; i++)
            {
                var row = rows[i];
                pipe.Record(repository.CurrentGenerationId, "test-run", (string)row["MapId"]!, (string)row["SegmentId"]!, (double)row["Time"]!, (string)row["Kind"]!, row["Data"]!);
                if (i % 61 == 0)
                {
                    Assert.True(pipe.Pump((generation, record) => { repository.RecordEncounterDeferred(generation, record); return true; }, flush: true), pipe.Failure);
                    repository.Flush();
                }
            }
            Assert.True(pipe.Pump((generation, record) => { repository.RecordEncounterDeferred(generation, record); return true; }, flush: true), pipe.Failure);
            repository.CloseClean();
        }
        using var reopened = Open(); reopened.Open(identity);
        var saved = EncounterHistoryReader.Read(reopened.Current.EncounterHistory, "test-run");
        Assert.Equal(3, saved.Count(record => record.Encounter?.Outcome == EncounterOutcome.PlayerKill));
        Assert.Equal(2, Assert.Single(saved, record => record.Loot?.ItemTypeId == 20).Loot!.TakenToPlayer
            - Assert.Single(saved, record => record.Loot?.ItemTypeId == 20).Loot!.ReturnedByPlayer);
        Assert.NotEmpty(Assert.Single(StoredEncounterMap.Build(saved)).Strokes);
        reopened.CloseClean();
    }

    [Fact]
    public void PlayerDeathAfterKillingTheShooterKeepsBothEventsAndOutcomeMap()
    {
        var reducer = new EncounterEvidenceProjection("session");
        var enemy = new { Id = 2, IsMain = false, PresetKey = "Cname_Scav" };
        var player = new { Id = 1, IsMain = true };
        reducer.Apply("run", "map-a", "segment-a", 1, "combat_hurt_complete", JObject.FromObject(new
        { Target = enemy, Source = new { Physical = player, Credited = player, Kind = "projectile", WeaponId = 736 },
            NativeCallCompleted = true, ProposedHpLossOwnedAssignments = 10, Transaction = 1 }));
        reducer.Apply("run", "map-b", "segment-b", 2, "combat_fatal", JObject.FromObject(new
        { Target = enemy, Source = new { Physical = player, Credited = player, Kind = "projectile", WeaponId = 736 }, FatalSequence = 2 }));
        reducer.Apply("run", "map-b", "segment-b", 3, "combat_fatal", JObject.FromObject(new
        { Target = player, Source = new { Physical = enemy, Credited = enemy, Kind = "projectile", WeaponId = 783 }, FatalSequence = 3 }));
        var records = reducer.Flush();
        EncounterRecordValidation.ValidateHistory(records);
        var maps = StoredEncounterMap.Build(records);
        Assert.Empty(maps[0].Events);
        Assert.Equal(new[] { EncounterOutcome.PlayerKill, EncounterOutcome.PlayerDeath }, maps[1].Events.Select(record => record.Encounter!.Outcome!.Value));
        Assert.Contains(maps[1].Records, record => record.Damage?.Amount == 10);
    }

    [Fact]
    public void InspectingReplacementItemCannotRevealTheOriginalHiddenSlot()
    {
        var reducer = new EncounterEvidenceProjection("session");
        reducer.Apply("run", "map", "segment", 1, "combat_fatal", JObject.FromObject(new
        { Target = new { Id = 2 }, Source = new { Credited = new { Id = 1, IsMain = true } }, FatalSequence = 1 }));
        reducer.Apply("run", "map", "segment", 2, "loot.inventory-open", JObject.FromObject(new
        { ActorId = 2, CorpseId = 4, Rows = new[] { new { Index = 0, ItemId = 8, Revealed = false } } }));
        reducer.Apply("run", "map", "segment", 3, "loot.inspection", JObject.FromObject(new
        { ActorId = 2, CorpseId = 4, Row = new { Index = 0, ItemId = 99, Revealed = true, TypeId = 451, Quantity = 99 } }));
        var slot = Assert.Single(Assert.Single(reducer.Flush(), record => record.Inventory != null).Inventory!.Slots);
        Assert.False(slot.Inspected); Assert.Null(slot.ItemTypeId); Assert.Null(slot.Quantity);
    }
}
