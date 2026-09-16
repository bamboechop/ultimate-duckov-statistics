using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Encounters;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;
using UltimateDuckovStatistics.Encounters;

namespace UltimateDuckovStatistics.Tests;

public sealed class EncounterMissingContextTests
{
    [Theory]
    [InlineData("combat_fatal")]
    [InlineData("loot.transfer")]
    [InlineData("path-point")]
    public void ExhaustedTrackerRoutePublishesIncompleteCoverageForDiscardedEvidence(string kind)
    {
        var tracker = IncrementalCheckpointProtocolTests.Started("g", route: true, runId: "r");
        var pipe = new EncounterCapturePipeline();
        var time = 0;
        for (var visit = 1; visit <= RouteStatisticsReducer.MaximumSegmentsPerRun; visit++)
        {
            pipe.Record("g", tracker.ActiveRunId!, tracker.ActiveMapId!, tracker.ActiveSegmentId!, time,
                "path-point", Point(visit));
            tracker.Apply(new RunLifecycleEvent { Kind = RunLifecycleEventKind.MapTransitionStarted,
                TimestampUtc = NativeProfileJsonWriterTests.Now.AddSeconds(++time), MonotonicSeconds = time });
            tracker.Apply(new RunLifecycleEvent { Kind = RunLifecycleEventKind.DestinationControlReady,
                TimestampUtc = NativeProfileJsonWriterTests.Now.AddSeconds(++time), MonotonicSeconds = time,
                Map = new MapIdentity { MapId = "map-" + (visit + 1), DisplayName = "Map", IsKnown = true } });
        }
        Assert.True(tracker.IsActive); Assert.False(tracker.IsSuspended); Assert.Null(tracker.ActiveSegmentId);
        pipe.Record("g", tracker.ActiveRunId!, tracker.ActiveMapId!, tracker.ActiveSegmentId ?? "", time, kind, new { });
        pipe.Record("g", tracker.ActiveRunId!, tracker.ActiveMapId!, "", time + 1, kind, new { });
        var records = Drain(pipe);
        Assert.Null(pipe.Failure);
        Assert.Equal(RouteStatisticsReducer.MaximumSegmentsPerRun, records.Count(row => row.Visit != null));
        Assert.Equal(RouteStatisticsReducer.MaximumSegmentsPerRun, records.Where(row => row.Route != null).Sum(row => row.Route!.Points.Count));
        var coverage = Assert.Single(records, row => row.Coverage != null);
        Assert.Empty(coverage.VisitId); Assert.False(coverage.Coverage!.CaptureStopped);
        Assert.Equal(time, coverage.Coverage.ObservedSeconds);
        Assert.Equal("ui.encounters_capture_incomplete", StoredEncounterRun.Build(records).CoverageNoticeKey);
        EncounterRecordValidation.ValidateHistory(records);
    }

    [Theory]
    [InlineData("", "segment")]
    [InlineData("map", "")]
    public void MissingContextEndsPriorVisitAndResumesWithoutJoiningAcrossLostEvidence(string map, string segment)
    {
        var pipe = new EncounterCapturePipeline();
        var saved = new Dictionary<(string Run, string Id), EncounterRecord>();
        bool Publish(string generation, EncounterRecord row)
        { Assert.Equal("g", generation); saved[(row.RunId, row.Id)] = row; return true; }
        pipe.Record("g", "r", "map", "segment", 0, "path-point", Point(0));
        Assert.True(pipe.Pump(Publish, flush: true));
        pipe.Record("g", "r", map, segment, 1, "combat_fatal", new { });
        Assert.True(pipe.Pump(Publish, flush: true));
        pipe.Record("g", "r", map, segment, 2, "loot.transfer", new { });
        pipe.Record("g", "r", "map", "segment", 3, "path-point", Point(3));
        Assert.True(pipe.Pump(Publish, flush: true));
        Assert.Single(saved.Values, row => row.Coverage != null);
        Assert.Equal(2, saved.Values.Count(row => row.Visit != null));
        Assert.All(saved.Values.Where(row => row.Route != null), row =>
        { Assert.Single(row.Route!.Points); Assert.Equal(RouteConnection.Start, row.Route.Points[0].Connection); });
        pipe.Record("g", "next", map, segment, 0, "combat_fatal", new { });
        Assert.True(pipe.Pump(Publish, flush: true));
        Assert.Single(saved.Values, row => row.RunId == "next" && row.Coverage != null);
        EncounterRecordValidation.ValidateHistory(saved.Values);
    }

    [Fact]
    public void LoadingGapWithoutSegmentKeepsVisitAndDoesNotReportLostGameplayEvidence()
    {
        var pipe = new EncounterCapturePipeline();
        pipe.Record("g", "r", "map", "segment", 0, "path-point", Point(0));
        pipe.Record("g", "r", "map", "", 1, "path-gap", new { reason = "loading" });
        pipe.Record("g", "r", "map", "segment", 2, "path-point", Point(2));
        pipe.Record("g", "r", "", "", 3, "session-end", new { });
        var records = Drain(pipe);
        Assert.Single(records, row => row.Visit != null);
        Assert.DoesNotContain(records, row => row.Coverage != null);
        var points = records.Where(row => row.Route != null).SelectMany(row => row.Route!.Points).ToArray();
        Assert.Equal(RouteConnection.Gap, points[1].Connection);
    }

    private static object Point(int position) => new { position = new[] { position, 0, position } };

    private static EncounterRecord[] Drain(EncounterCapturePipeline pipe)
    {
        var result = new Dictionary<(string Run, string Id), EncounterRecord>();
        Assert.True(pipe.Pump((_, row) => { result[(row.RunId, row.Id)] = row; return true; }, flush: true));
        Assert.Null(pipe.Failure);
        return result.Values.ToArray();
    }
}
