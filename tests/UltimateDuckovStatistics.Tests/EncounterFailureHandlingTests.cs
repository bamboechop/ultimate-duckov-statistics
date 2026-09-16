using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Encounters;
using UltimateDuckovStatistics.Core.Export;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Tracking;
using UltimateDuckovStatistics.Encounters;
using UltimateDuckovStatistics.Sqlite;

namespace UltimateDuckovStatistics.Tests;

public sealed class EncounterFailureHandlingTests
{
    private static void Point(EncounterCapturePipeline pipe, int time, string generation = "generation", string run = "run") =>
        pipe.Record(generation, run, "map", "segment", time, "path-point", new { position = new[] { time, 0, time } });

    private sealed class BrokenPayload
    {
        private readonly IOException failure = new("Injected detached-payload serialization failure");
        public int Observation => throw failure;
    }

    private static void Break(EncounterCapturePipeline pipe, string generation = "generation", string run = "run") =>
        pipe.Record(generation, run, "map", "segment", 4, "path-point", new BrokenPayload());

    [Fact]
    public void QueueLimitDrainsAcceptedEvidenceAndStopsLaterRunsTruthfully()
    {
        var pipe = new EncounterCapturePipeline();
        for (var i = 0; i <= 8192; i++) Point(pipe, i);
        Assert.NotNull(pipe.Failure);
        var saved = new List<EncounterRecord>();
        Assert.True(pipe.Pump((_, row) => { saved.Add(row); return true; }, flush: true));
        Assert.Equal(8192, saved.Where(row => row.Route != null).Sum(row => row.Route!.Points.Count));
        Assert.Equal(EncounterCaptureIssue.QueueLimit, Assert.Single(saved, row => row.Coverage != null).Coverage!.Issue);
        var count = saved.Count;
        Point(pipe, 9000);
        Assert.True(pipe.Pump((_, row) => { saved.Add(row); return true; }, flush: true));
        Assert.Equal(count, saved.Count);
        pipe.ObserveRun("next-generation", "next-run", 0);
        pipe.ObserveRun("next-generation", "next-run", 1);
        Assert.True(pipe.Pump((generation, row) =>
        {
            Assert.Equal("next-generation", generation); Assert.Equal("next-run", row.RunId);
            Assert.True(row.Coverage!.CaptureStopped); Assert.Empty(row.VisitId);
            saved.Add(row); return true;
        }, flush: true));
        Assert.Equal(count + 1, saved.Count);
        EncounterRecordValidation.ValidateHistory(saved, new HashSet<string> { "run", "next-run" });
        Assert.False(pipe.HasPending);
    }

    [Fact]
    public void FailedWorkerIsConsumedOnceAndDoesNotMutatePreviouslyPublishedData()
    {
        var pipe = new EncounterCapturePipeline(); var saved = new List<EncounterRecord>();
        bool Publish(string _, EncounterRecord row) { saved.Add(row); return true; }
        Point(pipe, 1); Assert.True(pipe.Pump(Publish, flush: true));
        var previous = Assert.Single(saved, row => row.Route != null);
        Point(pipe, 2); Break(pipe);
        Assert.True(pipe.Pump(Publish, flush: true));
        Assert.NotNull(pipe.Failure); Assert.False(pipe.HasPending);
        Assert.Equal(1, Assert.Single(previous.Route!.Points).Seconds);
        Assert.Single(saved, row => row.Route != null);
        Assert.Equal(EncounterCaptureIssue.ReductionFailed, Assert.Single(saved, row => row.Coverage != null).Coverage!.Issue);
        var count = saved.Count;
        Assert.True(pipe.Pump(Publish, flush: true)); Assert.Equal(count, saved.Count);
    }

    [Fact]
    public void EarlierCompletedBatchSurvivesFailureInNextOwner()
    {
        var pipe = new EncounterCapturePipeline(); var saved = new List<(string, EncounterRecord)>();
        Point(pipe, 1, "first-generation", "first-run"); Break(pipe, "second-generation", "second-run");
        Assert.True(pipe.Pump((generation, row) => { saved.Add((generation, row)); return true; }, flush: true));
        Assert.Contains(saved, pair => pair.Item1 == "first-generation" && pair.Item2.Route != null);
        Assert.DoesNotContain(saved, pair => pair.Item1 == "first-generation" && pair.Item2.Coverage != null);
        Assert.Equal("second-generation", Assert.Single(saved, pair => pair.Item2.Coverage != null).Item1);
        Assert.DoesNotContain(saved, pair => pair.Item1 == "second-generation" && pair.Item2.Visit != null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicationFailureRetainsExactHeadAndBacksOffWithoutDisablingCapture(bool throws)
    {
        var clock = 0d; var pipe = new EncounterCapturePipeline(() => clock);
        Point(pipe, 1);
        EncounterRecord? rejected = null; var attempts = 0; var saved = new List<EncounterRecord>();
        bool Publish(string generation, EncounterRecord row)
        {
            Assert.Equal("generation", generation);
            if (++attempts == 1)
            {
                rejected = row;
                if (throws) throw new IOException("Injected unavailable storage");
                return false;
            }
            if (attempts == 2) Assert.Same(rejected, row);
            saved.Add(row); return true;
        }
        Assert.False(pipe.Pump(Publish, flush: true));
        Assert.Null(pipe.Failure); Assert.NotNull(pipe.PublicationFailure); Assert.True(pipe.HasPending);
        Assert.False(pipe.Pump(Publish, flush: true)); Assert.Equal(1, attempts);
        clock = .5;
        Assert.True(pipe.Pump(Publish, flush: true)); Assert.Null(pipe.PublicationFailure);
        Assert.Single(saved, row => row.Route != null); Assert.DoesNotContain(saved, row => row.Coverage != null);
        Point(pipe, 2); Assert.True(pipe.Pump(Publish, flush: true));
        Assert.Equal(2, saved.Last(row => row.Route != null).Route!.Points.Count);
    }

    [Fact]
    public void DegradedFamilyDoesNotDisableIndependentCaptureOrInventVisits()
    {
        var pipe = new EncounterCapturePipeline(); var saved = new List<EncounterRecord>();
        pipe.ReportCoverage("generation", "run", 0, EncounterCaptureIssue.LootIncomplete);
        pipe.ReportCoverage("generation", "run", 1, EncounterCaptureIssue.LootIncomplete);
        Assert.True(pipe.Pump((_, row) => { saved.Add(row); return true; }, flush: true));
        var run = StoredEncounterRun.Build(saved.ToArray());
        Assert.Empty(run.Visits); Assert.Empty(run.Events);
        Assert.Equal("ui.encounters_capture_incomplete", run.CoverageNoticeKey);
        Assert.Single(saved); Assert.Null(pipe.Failure);
        Point(pipe, 2);
        Assert.True(pipe.Pump((_, row) => { saved.Add(row); return true; }, flush: true));
        Assert.Contains(saved, row => row.Route != null);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedCaptureWaitsForStorageThenCompletesRunAndRestoresDurableNotice(bool missingContext)
    {
        using var directory = new TemporaryDirectory();
        SqliteLibrary.Initialize(Path.Combine(AppContext.BaseDirectory, "sqlite3.dll"));
        var now = NativeProfileJsonWriterTests.Now;
        var codec = new ProfileRecordCodec(NativeProfileJsonWriter.WriteRecord);
        var identity = new SaveIdentitySnapshot { Slot = 1, SaveFilePresent = true, SaveFileCreationUtcTicks = now.Ticks,
            ContentSha256 = new string('a', 64), ObservedLength = 1, ObservedWriteUtcTicks = now.Ticks };
        ProfileRepository Open(string name) => new(Path.Combine(directory.Path, name), () => now, () => Guid.NewGuid().ToString("N"),
            createIncrementalStorage: path => new SqliteProfileStorage(path, codec), recordCodec: codec);
        var clock = 0d; var pipe = new EncounterCapturePipeline(() => clock);
        using var source = Open("source"); source.Open(identity);
        var generation = source.CurrentGenerationId;
        var tracker = IncrementalCheckpointProtocolTests.Started(generation, route: true, runId: "run");
        if (missingContext) pipe.Record(generation, "run", "map", "", 4, "combat_fatal", new { });
        else Break(pipe, generation);
        var storageAvailable = false;
        var boundary = new NativeRunTerminalBoundary(() => clock);
        boundary.SetTerminalObserver(() => pipe.Pump((owner, row) =>
        {
            Assert.Equal(generation, owner);
            if (!storageAvailable) throw new IOException("Injected publication failure");
            source.RecordEncounterDeferred(owner, row); return true;
        }, flush: true));
        var terminal = new RunLifecycleEvent { Kind = RunLifecycleEventKind.Extracted, TimestampUtc = now.AddSeconds(10), MonotonicSeconds = 10 };
        Assert.Null(boundary.Apply(tracker, terminal, _ => { }, () => true).Completed);
        Assert.True(boundary.HasPendingTerminal); Assert.True(tracker.IsActive);
        Assert.Equal(!missingContext, pipe.Failure != null);
        Assert.True(pipe.HasPending);
        storageAvailable = true; clock = 61;
        var completed = boundary.Retry(tracker, _ => { }, _ => { source.Flush(); return true; }).Completed;
        Assert.NotNull(completed); Assert.False(boundary.HasPendingTerminal); Assert.False(pipe.HasPending);
        source.CompleteRun(completed!); source.CloseClean();
        using var reopened = Open("source"); reopened.Open(identity);
        var warning = Assert.Single(reopened.ReadEncounters("run"));
        Assert.Equal(EncounterRecordKind.Coverage, warning.Kind);
        Assert.Equal(!missingContext, warning.Coverage!.CaptureStopped);
        Assert.Equal(missingContext ? EncounterCaptureIssue.ContextUnavailable : EncounterCaptureIssue.ReductionFailed, warning.Coverage.Issue);
        using var snapshot = await reopened.CaptureExportSnapshotAsync();
        var export = ProfileExportWriter.WriteToRoot(snapshot, Path.Combine(directory.Path, "exports"), now);
        var preview = StatisticsRestoreReader.Read(Assert.Single(export.Files), 1);
        using var restored = Open("restored"); restored.Open(identity);
        restored.RestoreStatistics(identity, preview, restored.CurrentGenerationId); restored.CloseClean();
        using var verified = Open("restored"); verified.Open(identity);
        var records = verified.ReadEncounters("run").ToArray();
        Assert.Equal(codec.Encode(warning), codec.Encode(Assert.Single(records)));
        Assert.Equal(missingContext ? "ui.encounters_capture_incomplete" : "ui.encounters_capture_stopped", StoredEncounterRun.Build(records).CoverageNoticeKey);
        Assert.Empty(StoredEncounterRun.Build(records).Visits);
        verified.CloseClean(); reopened.CloseClean();
    }

    [Fact]
    public void CoverageCannotBeRemovedReownedOrMadeToLookComplete()
    {
        var pipe = new EncounterCapturePipeline(); EncounterRecord? row = null;
        pipe.StopCapture("generation", "run", 0, EncounterCaptureIssue.NativeCaptureFailed, "Injected native failure");
        Assert.True(pipe.Pump((_, value) => { row = value; return true; }, flush: true));
        var codec = new ProfileRecordCodec();
        var changed = ProfileRecordCodec.Decode<EncounterRecord>(codec.Encode(row!));
        changed.Coverage!.Issue = EncounterCaptureIssue.CombatIncomplete; changed.Coverage.CaptureStopped = false;
        Assert.Throws<ArgumentException>(() => EncounterRecordValidation.ValidateReplacement(row!, changed));
        changed = ProfileRecordCodec.Decode<EncounterRecord>(codec.Encode(row!)); changed.VisitId = "invented";
        Assert.Throws<ArgumentException>(() => EncounterRecordValidation.Validate(changed));
        changed = ProfileRecordCodec.Decode<EncounterRecord>(codec.Encode(row!)); changed.Coverage!.ObservedSeconds = double.NaN;
        Assert.Throws<ArgumentException>(() => EncounterRecordValidation.Validate(changed));
        Assert.Throws<ArgumentException>(() => EncounterRecordValidation.ValidateHistory(new[] { row! }, new HashSet<string> { "other-run" }));
    }
}
