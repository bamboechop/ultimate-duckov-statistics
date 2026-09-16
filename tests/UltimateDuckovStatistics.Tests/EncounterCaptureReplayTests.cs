using UltimateDuckovStatistics.Encounters.Diagnostics;
#if UDS_ENCOUNTER_DIAGNOSTICS
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UltimateDuckovStatistics.Encounters;
using Xunit;

namespace UltimateDuckovStatistics.Tests;

public sealed class EncounterCaptureReplayTests
{
    [Fact]
    public void NativeEndOfRunGapClosesPriorContinuityWithoutInventingAnotherRun()
    {
        var endGap = Row(3, "path-gap", new { visit = 1, reason = "inactive-paused-loading-or-native-unavailable", sequence = 497 }, "");
        endGap["MapId"] = "";
        endGap["SegmentId"] = "";
        var capture = Replay(Point(1, 1), Point(2, 2), endGap, Point(4, 4));
        Assert.False(capture.IsPartial, string.Join("; ", capture.Issues));
        var group = Assert.Single(capture.Groups);
        Assert.Single(group.PathEdges);
        Assert.Contains(group.Gaps, gap => gap.Sequence == 3 && gap.Reason.StartsWith("outside-run-context:", StringComparison.Ordinal));
        var malformedFatal = Fatal(1, 1);
        malformedFatal["RunId"] = "";
        Assert.True(Replay(malformedFatal).IsPartial);
    }

    [Fact]
    public void SamplesKeepTheirOwnTimesAndDoNotBridgeVisitsGapsOrRuns()
    {
        var capture = Replay(Point(1, 1), Point(2, 2), Row(3, "path-gap", new { visit = 1, reason = "loading" }),
            Point(4, 4), Point(5, 5, visit: 2), Point(6, 6, visit: 2), Point(7, 7, run: "other"));
        Assert.False(capture.IsPartial);
        Assert.Equal(2, capture.Groups.Count);
        var group = capture.Groups[0];
        Assert.Equal(2, group.PathEdges.Count);
        Assert.Equal(1, group.PathEdges[0].FromTime);
        Assert.Equal(2, group.PathEdges[0].ToTime);
        Assert.Equal(2, group.PathEdges[1].Visit);
        Assert.Equal(6, group.PathEdges[1].Sequence);
        Assert.Equal(60, group.PathPoints.Last().ProbeSequence);
        Assert.Empty(capture.Groups[1].PathEdges);
        Assert.Equal("Level", group.NativeMapId);
    }

    [Fact]
    public void OnlyProvenHorizontalPlacementsProduceDottedEdgesAndAllPlacementsSplitWalking()
    {
        var capture = Replay(Point(1, 1), Point(2, 2), Placement(3, 2.8, new[] { 0d, 0, 0 }, new[] { 0d, 5, 0 }),
            Point(4, 4), Placement(5, 4.8, new[] { 1d, 0, 0 }, new[] { 10d, 0, 0 }),
            Point(6, 6), Point(7, 7), Placement(8, 7.8, new[] { 10d, 0, 0 }, new[] { 20d, 0, 0 }, false), Point(9, 9));
        var edges = Assert.Single(capture.Groups).PathEdges;
        Assert.Equal(3, edges.Count);
        var teleport = Assert.Single(edges, edge => edge.Style == EncounterReplayEdgeStyle.Teleport);
        Assert.Equal(4.8, teleport.FromTime);
        Assert.Equal(5.1, teleport.ToTime, 8);
        Assert.DoesNotContain(edges, edge => edge.From.Sequence == 2 && edge.To.Sequence == 4);
        Assert.DoesNotContain(edges, edge => edge.To.Sequence == 9);
        AssertTimeline(edges);
    }

    [Fact]
    public void OverlappingPlacementTimingIsOmittedWithoutMovingRecordedPoints()
    {
        var capture = Replay(Point(1, 1), Point(2, 2), Placement(3, 1.9, new[] { 1d, 0, 0 }, new[] { 10d, 0, 0 }), Point(4, 4), Point(5, 5));
        var group = Assert.Single(capture.Groups);
        Assert.True(capture.IsPartial);
        Assert.Equal(4, group.PathPoints.Count);
        Assert.Equal(2, group.PathEdges.Count);
        Assert.Contains(group.Gaps, gap => gap.Reason == "overlapping-edge-times");
        AssertTimeline(group.PathEdges);
    }

    [Fact]
    public void FatalOrderUsesReservedSequenceButDoesNotFabricateCandidateSeconds()
    {
        var later = Fatal(1, 20);
        var earlier = Fatal(2, 10);
        earlier["Data"]!["Candidate"]!["TargetPosition"]!["X"] = "invalid";
        var capture = Replay(later, earlier);
        var fatals = Assert.Single(capture.Groups).Fatals;
        Assert.Equal(new long[] { 10, 20 }, fatals.Select(fatal => fatal.FatalSequence));
        Assert.Equal(2, fatals[0].Sequence);
        Assert.Equal(2.1, fatals[0].Time, 8);
        Assert.Equal(123456, fatals[0].CandidateStopwatchTicks);
        Assert.Null(fatals[0].TargetPosition);
        Assert.Equal("Level", fatals[1].TargetPosition!.LogicalScene);
        Assert.Equal(3, fatals[1].TargetPosition!.Position.X);
        Assert.Contains("completion", fatals[0].Timing);
        Assert.True(capture.IsPartial);
    }

    [Fact]
    public void RetainedPlayerBuffEvidenceDoesNotCreatePlayerKillCredit()
    {
        var direct = Fatal(1, 1);
        var delayed = Fatal(2, 2);
        delayed["Data"]!["Source"]!["Kind"] = "buff";
        delayed["Data"]!["Source"]!["Delayed"] = true;
        var npc = Fatal(3, 3);
        npc["Data"]!["Source"]!["Credited"]!["IsMain"] = false;
        var death = Fatal(4, 4);
        death["Data"]!["Kind"] = "player-death";
        death["Data"]!["Candidate"] = null;
        death["Data"]!["PositionsAvailableBeforeCleanup"] = false;
        var fatals = Assert.Single(Replay(direct, delayed, npc, death).Groups).Fatals;
        Assert.Equal(EncounterReplayFatalKind.PlayerKill, fatals[0].Kind);
        Assert.Equal(EncounterReplayFatalKind.OtherPlayerRelatedDeath, fatals[1].Kind);
        Assert.Equal(EncounterReplayFatalKind.OtherPlayerRelatedDeath, fatals[2].Kind);
        Assert.Equal(EncounterReplayFatalKind.PlayerDeath, fatals[3].Kind);
        Assert.Null(fatals[3].PlayerPosition);
        Assert.Equal(736, fatals[0].Source.WeaponId);
        Assert.Equal(594, fatals[0].Source.LoadedAmmoId);
        Assert.Contains("do not prove causality", fatals[1].Attribution);
    }

    [Fact]
    public void NestedDamageRemainsDiagnosticAndRunLocal()
    {
        var capture = Replay(Row(1, "combat_hurt_begin", new { Transaction = 2, ParentTransaction = 1 }),
            Row(2, "combat_hurt_complete", new { Transaction = 2, NetHpLossAcrossCall = 7, ProposedHpLossOwnedAssignments = 7 }),
            Row(3, "combat_hurt_complete", new { Transaction = 1, NetHpLossAcrossCall = 10, ProposedHpLossOwnedAssignments = 3 }),
            Fatal(4, 1), Row(5, "combat_hurt_complete", new { Transaction = 1, NetHpLossAcrossCall = 4 }, "other"));
        Assert.All(capture.Groups[0].DamageDiagnostics, damage => Assert.True(damage.Nested));
        var diagnostic = Assert.Single(Assert.Single(capture.Groups[0].Fatals).DamageDiagnostics);
        Assert.Equal(10, diagnostic.NetHpLossAcrossCall);
        Assert.Equal(3, diagnostic.ProposedHpLossOwnedAssignments);
        Assert.Contains("not", diagnostic.Interpretation);
        Assert.False(Assert.Single(capture.Groups[1].DamageDiagnostics).Nested);
    }

    [Fact]
    public void InvalidNumericEvidenceAndMissingRowsAreExplicitAndBreakContinuity()
    {
        var invalid = Point(2, 2);
        invalid["Data"]!["position"]![0] = "0";
        var capture = Replay(Point(1, 1), invalid, Point(3, 3), Point(5, 5));
        Assert.True(capture.IsPartial);
        Assert.Equal(3, Assert.Single(capture.Groups).PathPoints.Count);
        Assert.Empty(capture.Groups[0].PathEdges);
        Assert.Contains(capture.Issues, issue => issue.Contains("Missing envelope sequence"));
        var future = Point(1, 9);
        Assert.Empty(Assert.Single(Replay(future).Groups).PathPoints);
    }

    [Fact]
    public void MissingFooterDroppedRowsAndTrailingDataStayPartial()
    {
        Assert.True(EncounterCaptureReplay.Parse(new StringReader(Point(1, 1).ToString(Formatting.None))).IsPartial);
        var text = Point(1, 1).ToString(Formatting.None) + "\n" + Footer(1, 2).ToString(Formatting.None);
        var lost = EncounterCaptureReplay.Parse(new StringReader(text));
        Assert.Equal(2, lost.DroppedRecords);
        Assert.True(lost.IsPartial);
        var trailing = EncounterCaptureReplay.Parse(new StringReader(text + "\n" + Point(2, 2).ToString(Formatting.None)));
        Assert.Single(Assert.Single(trailing.Groups).PathPoints);
        Assert.Contains(trailing.Issues, issue => issue.Contains("after capture footer"));
    }

    [Fact]
    public void ReaderEnforcesRecordByteLineAndDepthBudgets()
    {
        var text = Point(1, 1).ToString(Formatting.None) + "\n" + Point(2, 2).ToString(Formatting.None);
        foreach (var limits in new[] { new EncounterReplayLimits { MaximumRecords = 1 },
            new EncounterReplayLimits { MaximumBytes = 20 }, new EncounterReplayLimits { MaximumLineCharacters = 20 } })
        {
            var result = EncounterCaptureReplay.Parse(new StringReader(text), limits);
            Assert.True(result.IsPartial);
            Assert.True(result.LimitReached);
        }
        var depth = EncounterCaptureReplay.Parse(new StringReader(text), new EncounterReplayLimits { MaximumDepth = 1 });
        Assert.True(depth.IsPartial);
        Assert.Empty(depth.Groups);
        Assert.Contains(depth.Issues, issue => issue.Contains("Malformed record"));
    }

    [Fact]
    public void MetadataIsDetachedPlainJsonAndCandidateMustMatchFatalIdentity()
    {
        var metadata = new JObject { ["MapId"] = "duckov:map:Level", ["$type"] = "Untrusted.Type, MissingAssembly", ["available"] = false };
        var row = Row(1, "map-calibration", new { calibrationId = new string('a', 64), metadata });
        var fatal = Fatal(2, 1);
        fatal["Data"]!["Candidate"]!["Sequence"] = 99;
        var capture = Replay(row, fatal);
        var group = Assert.Single(capture.Groups);
        Assert.Contains("Untrusted.Type", Assert.Single(group.Calibrations).MetadataJson);
        Assert.False(group.Calibrations[0].Available);
        Assert.Null(Assert.Single(group.Fatals).TargetPosition);
        Assert.True(capture.IsPartial);
    }

    [Fact]
    public void OptionalPreservedQualificationCaptureKeepsKnownObservationsAndFiniteTimeline()
    {
        var path = Environment.GetEnvironmentVariable("UDS_ENCOUNTER_REPLAY_SAMPLE");
        if (string.IsNullOrWhiteSpace(path)) return;
        var capture = EncounterCaptureReplay.Load(path);
        Assert.False(capture.IsPartial, string.Join("; ", capture.Issues));
        Assert.Equal(3, capture.Groups.Sum(group => group.Fatals.Count));
        Assert.Equal(2, capture.Groups.Sum(group => group.PathEdges.Count(edge => edge.Style == EncounterReplayEdgeStyle.Teleport)));
        Assert.Equal(492, capture.Groups.Sum(group => group.PathPoints.Count));
        foreach (var group in capture.Groups) AssertTimeline(group.PathEdges);
    }

    private static void AssertTimeline(IEnumerable<EncounterReplayEdge> edges)
    {
        var array = edges.ToArray();
        var timeline = new EncounterRevealTimeline(array.Select(edge => new EncounterRevealSpan(edge.FromTime, edge.ToTime,
            Math.Sqrt(Math.Pow(edge.To.Position.X - edge.From.Position.X, 2) + Math.Pow(edge.To.Position.Z - edge.From.Position.Z, 2)),
            edge.Style == EncounterReplayEdgeStyle.Teleport)));
        for (var index = 0; index < array.Length; index++)
        {
            Assert.True(double.IsFinite(array[index].FromTime) && double.IsFinite(array[index].ToTime));
            Assert.True(array[index].ToTime >= array[index].FromTime);
            if (index > 0) Assert.True(array[index].FromTime >= array[index - 1].ToTime);
            Assert.InRange(timeline.EdgeFraction(index, 0.5), 0, 1);
        }
    }

    private static EncounterReplayCapture Replay(params JObject[] rows) => EncounterCaptureReplay.Parse(new StringReader(
        string.Join("\n", rows.Append(Footer(rows.Length)).Select(row => row.ToString(Formatting.None)))));
    private static JObject Footer(int written, int dropped = 0) => JObject.FromObject(new { Kind = "capture-footer", Written = written, Dropped = dropped, Complete = true, LimitReached = false });
    private static JObject Row(long sequence, string kind, object data, string run = "run") => JObject.FromObject(new
    { Sequence = sequence, Time = sequence + 0.1, Kind = kind, GenerationId = "generation", RunId = run, MapId = "duckov:map:Level", SegmentId = "segment", Data = data });
    private static JObject Point(long sequence, double time, int visit = 1, string run = "run") => Row(sequence, "path-point", new
    { visit, sequence = sequence * 10, actorId = 1, MonotonicSeconds = time, position = new[] { time, 0, time }, MapId = "duckov:map:Level" }, run);
    private static JObject Placement(long sequence, double time, double[] from, double[] arrival, bool proven = true) => Row(sequence, "path-teleport", new
    { visit = 1, sequence, from, arrival, Time = time, provenSameMap = proven, Map = "duckov:map:Level", NativeMap = "Level", destinationNativeMap = "Level" });
    private static JObject Fatal(long sequence, long fatalSequence) => Row(sequence, "combat_fatal", new
    {
        FatalSequence = fatalSequence, Transaction = 1, Kind = "target-death", HpLoss = 10, PositionsAvailableBeforeCleanup = true,
        Target = new { Id = 3, Kind = "character", IsMain = false, PresetKey = "Cname_ScavRage" },
        Source = new { Kind = "projectile", Delayed = false, OriginallyPlayer = true, WeaponId = 736, LoadedAmmoId = 594,
            Physical = new { Id = 1, Kind = "character", IsMain = true }, Credited = new { Id = 1, Kind = "character", IsMain = true }, NativeDamageActor = new { Id = 1, Kind = "character", IsMain = true } },
        Candidate = new { Sequence = fatalSequence, StopwatchTicks = 123456, Timing = "current-health-setter-prefix-before-cleanup",
            TargetPosition = new { Available = true, X = 3, Y = 2, Z = 1, LogicalScene = "Level" },
            PlayerPosition = new { Available = true, X = 5, Y = 2, Z = 1, LogicalScene = "Level" } }
    });
}
#endif
