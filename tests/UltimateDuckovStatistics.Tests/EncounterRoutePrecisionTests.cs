using System.Text;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Encounters;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Encounters;
using UltimateDuckovStatistics.Sqlite;

namespace UltimateDuckovStatistics.Tests;

public sealed class EncounterRoutePrecisionTests
{
    private static readonly float[] Position = { 474.438263f, 0.0160068851f, 178.474762f };
    [Fact]
    public void SourceBackedHistoryUsesTheOwnedCodecWhenRememberingItsFirstRead()
    {
        var record = Route();
        var writes = 0;
        byte[]? remembered = null;
        var codec = new ProfileRecordCodec((stream, value, type) =>
        {
            writes++;
            using var encoded = new MemoryStream();
            NativeProfileJsonWriter.WriteRecord(encoded, value, type);
            remembered = encoded.ToArray(); stream.Write(remembered);
        });
        var history = new EncounterHistory(new MemoryEncounterHistorySource(new[] { record }), codec);
        Assert.Same(record, history.Find(record.RunId, record.Kind, record.Id));
        Assert.Equal(1, writes);
        Assert.Contains("29.235181999999998e0", Encoding.UTF8.GetString(remembered!));
        var cached = history.Find(record.RunId, record.Kind, record.Id)!;
        Assert.NotSame(record, cached);
        EncounterRecordValidation.ValidateReplacement(record, cached);
        Assert.Equal(1, writes);
    }

    [Fact]
    public void ReopenedSqliteTailRetainsNativeCodecAndRejectsSingleBitPrefixChanges()
    {
        using var directory = new TemporaryDirectory();
        SqliteLibrary.Initialize(Path.Combine(AppContext.BaseDirectory, "sqlite3.dll"));
        var encounterWrites = 0;
        var codec = new ProfileRecordCodec((stream, value, type) =>
        { if (type == typeof(EncounterRecord)) encounterWrites++; NativeProfileJsonWriter.WriteRecord(stream, value, type); });
        var now = NativeProfileJsonWriterTests.Now;
        var identity = new SaveIdentitySnapshot { Slot = 1, SaveFilePresent = true, SaveFileCreationUtcTicks = now.Ticks,
            ContentSha256 = new string('a', 64), ObservedLength = 1, ObservedWriteUtcTicks = now.Ticks };
        ProfileRepository Open() => new(directory.Path, () => now, () => Guid.NewGuid().ToString("N"),
            createIncrementalStorage: path => new SqliteProfileStorage(path, codec), recordCodec: codec);
        using (var first = Open())
        {
            first.Open(identity);
            first.RecordEncounterDeferred(first.CurrentGenerationId, Visit());
            first.RecordEncounterDeferred(first.CurrentGenerationId, Route());
            first.CloseClean();
        }
        using var reopened = Open(); reopened.Open(identity);
        encounterWrites = 0;
        var history = Assert.IsType<EncounterHistory>(reopened.Current.EncounterHistory);
        history.Find("run", EncounterRecordKind.Route, "visit/0");
        history.Find("run", EncounterRecordKind.Route, "visit/0");
        Assert.Equal(1, encounterWrites); // The SQLite history must propagate its configured codec.
        var next = Route();
        next.Route!.Points.Add(new EncounterRoutePoint { Seconds = 30, Connection = RouteConnection.Walk,
            Position = new EncounterPosition { X = 472.180756f, Y = 0.0160068851f, Z = 179.043045f, MapId = "map" } });
        reopened.RecordEncounterDeferred(reopened.CurrentGenerationId, next);
        reopened.Flush();
        var changedTime = Route(); changedTime.Route!.Points = next.Route.Points.ToList();
        changedTime.Route.Points[0] = new EncounterRoutePoint { Seconds = Math.BitIncrement(29.235182),
            Position = next.Route.Points[0].Position, Connection = RouteConnection.Start };
        Assert.Throws<ArgumentException>(() => reopened.RecordEncounterDeferred(reopened.CurrentGenerationId, changedTime));
        var changedPosition = ProfileRecordCodec.Decode<EncounterRecord>(codec.Encode(next));
        changedPosition.Route!.Points[0].Position.X = MathF.BitIncrement(changedPosition.Route.Points[0].Position.X);
        Assert.Throws<ArgumentException>(() => reopened.RecordEncounterDeferred(reopened.CurrentGenerationId, changedPosition));
        Assert.Equal(2, Assert.Single(reopened.ReadEncounters("run", EncounterRecordKind.Route)).Route!.Points.Count);
        reopened.CloseClean();
    }

    [Fact]
    public void ANewCaptureHostCannotResumeAnOldSessionRouteChunk()
    {
        EncounterRecord[] Capture()
        {
            var pipeline = new EncounterCapturePipeline();
            pipeline.Record("generation", "run", "map", "segment", 29.235182, "path-point",
                new { position = Position });
            var rows = new List<EncounterRecord>();
            Assert.True(pipeline.Pump((_, record) => { rows.Add(record); return true; }, flush: true));
            return rows.ToArray();
        }
        var old = Assert.Single(Capture(), row => row.Route != null);
        var fresh = Assert.Single(Capture(), row => row.Route != null);
        Assert.NotEqual(old.VisitId, fresh.VisitId);
        Assert.NotEqual(old.Id, fresh.Id);
    }

    private static EncounterRecord Visit() => new() { RunId = "run", Id = "visit", VisitId = "visit", Kind = EncounterRecordKind.Visit,
        Visit = new EncounterVisit { MapId = "map", SegmentId = "segment", StartedSeconds = 0 } };

    private static EncounterRecord Route() => new() { RunId = "run", Id = "visit/0", VisitId = "visit", Kind = EncounterRecordKind.Route,
        Route = new EncounterRouteChunk { Points = new() { new EncounterRoutePoint { Seconds = 29.235182,
            Connection = RouteConnection.Start, Position = new EncounterPosition { X = 474.438263f, Y = 0.0160068851f, Z = 178.474762f, MapId = "map" } } } } };
}
