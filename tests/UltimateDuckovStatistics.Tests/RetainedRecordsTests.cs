using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.UI;

namespace UltimateDuckovStatistics.Tests;

public sealed class RetainedRecordsTests
{
    [Fact]
    public void CurrentLanguageResolvesRecordRoutesAndOverviewDurationHighlights()
    {
        var run = Run("translated", 60); run.StartingMapId = "duckov:map:A"; run.StartingMapDisplayName = "Lagerbereich";
        run.Segments = new() { Segment("duckov:map:A", "Lagerbereich"), Segment("duckov:map:B", "Keller") };
        run.Segments[1].SegmentIndex = 1;
        var p = WithExtraction(run);
        p.Names = new EntityDisplayNames(id => id == "duckov:map:A" ? "Warehouse" : id == "duckov:map:B" ? "Basement" : null);
        var before = System.Text.Json.JsonSerializer.Serialize(p.Profile);
        var result = Present(p);
        Assert.Equal("Warehouse", Value(result.Overall[0], "Starting map"));
        Assert.Equal("Warehouse - Basement", Value(result.Overall[0], "Route"));
        var highlights = OverviewHighlightsPresentationFactory.Create(p, UiText.Get);
        Assert.Contains("Warehouse", highlights[0].Value, StringComparison.Ordinal);
        Assert.Contains("Basement", highlights[1].Value, StringComparison.Ordinal);
        Assert.Equal(before, System.Text.Json.JsonSerializer.Serialize(p.Profile));
        Assert.DoesNotContain("Warehouse", RecordsPresentationFactory.MapName("duckov:map:A", "Unknown", false, UiText.Get, p.Names), StringComparison.Ordinal);
    }

    private static readonly string[] ExpectedHeadings = ["Fastest extraction", "Longest successful raid", "Shortest death run", "Longest death run"];
    private static readonly string[] ExpectedDurations = ["01:04.083", "1:10:00.999", "00:12.000", "08:20.000"];
    [Fact]
    public void AuthoritativePairsMapAllFourRolesWithoutRecalculatingFromHistory()
    {
        var p = Projection(Run("fast", 64.083), Run("long", 4200.999), Run("short-death", 12, RunOutcome.Died), Run("long-death", 500, RunOutcome.Died));
        var r = p.Runs.Records;
        r.Extraction.Shortest = Reference(p.Runs.Runs.Single(run => run.RunId == "fast"));
        r.Extraction.Longest = Reference(p.Runs.Runs.Single(run => run.RunId == "long"));
        r.Death.Shortest = Reference(p.Runs.Runs.Single(run => run.RunId == "short-death"));
        r.Death.Longest = Reference(p.Runs.Runs.Single(run => run.RunId == "long-death"));
        var result = Present(p);
        Assert.Equal(ExpectedHeadings, result.Overall.Select(card => card.Heading));
        Assert.Equal(ExpectedDurations, result.Overall.Select(card => Value(card, "Time")));
        // A reference is authoritative even when it is not the extreme of this visible list.
        r.Extraction.Shortest = r.Extraction.Longest;
        Assert.Equal("long", Present(p).Overall[0].RunId);
    }

    [Theory]
    [InlineData(RunOutcome.Extracted)]
    [InlineData(RunOutcome.Died)]
    public void SoleEligibleRunFillsBothSemanticRoles(RunOutcome outcome)
    {
        var p = Projection(Run("sole", 0, outcome)); var pair = outcome == RunOutcome.Extracted ? p.Runs.Records.Extraction : p.Runs.Records.Death;
        pair.Shortest = pair.Longest = Reference(p.Runs.Runs[0]);
        var cards = Present(p).Overall.Where(card => card.RunId != null).ToArray();
        Assert.Equal(2, cards.Length); Assert.All(cards, card => { Assert.Equal("sole", card.RunId); Assert.Equal("00:00.000", Value(card, "Time")); });
        Assert.NotEqual(cards[0].Heading, cards[1].Heading);
    }

    [Fact]
    public void NoRecordsKeepsBothTruthfulEmptyCategoriesEvenWithDeathsAndExtractionsInHistory()
    {
        var result = Present(Projection(Run("e", 100), Run("d", 100, RunOutcome.Died)));
        Assert.Equal(2, result.Overall.Count);
        Assert.Equal("No eligible extraction runs recorded so far", result.Overall[0].Notice);
        Assert.Equal("No eligible death runs recorded so far", result.Overall[1].Notice);
        Assert.All(result.Overall, card => { Assert.Empty(card.Rows); Assert.Null(card.RunId); });
    }

    [Theory]
    [InlineData(RunOutcome.Extracted)]
    [InlineData(RunOutcome.Died)]
    public void MissingHalfOfPairIsUnavailableRatherThanAnEmptyCategory(RunOutcome outcome)
    {
        var p = Projection(Run("run", 100, outcome));
        var pair = outcome == RunOutcome.Extracted ? p.Runs.Records.Extraction : p.Runs.Records.Death;
        pair.Shortest = Reference(p.Runs.Runs[0]);
        var cards = Present(p).Overall.Where(card => card.Rows.Count > 0).ToArray();
        Assert.Equal(2, cards.Length); Assert.Equal("Unavailable", Value(cards[1], "Time")); Assert.Null(cards[1].RunId);
    }

    [Fact]
    public void TimestampUsesStoredUtcConvertedToLocalRunDate()
    {
        var p = WithExtraction(Run("r", 1));
        var result = RecordsPresentationFactory.Create(p, "g", toLocal: utc => { Assert.Equal(DateTimeKind.Utc, utc.Kind); return utc.AddHours(2); })!;
        Assert.Equal("2026-09-01 - 03:02", Value(result.Overall[0], "Date"));
    }

    [Fact]
    public void StartingMapComesFromResolvedRunEvenWhenRecordContainsAnotherMapObservation()
    {
        var p = WithExtraction(Run("r", 1)); p.Runs.Records.Extraction.Shortest!.MapDisplayName = "Ending map";
        p.Runs.Records.Extraction.Shortest.MapId = "ending";
        Assert.Equal("Start", Value(Present(p).Overall[0], "Starting map"));
    }

    [Theory]
    [InlineData(false, "mod:unknown", "Unknown map (mod:unknown)")]
    [InlineData(true, "mod:known", "Start")]
    public void UnknownAndModdedStartingIdentityRemainsVisible(bool known, string id, string expected)
    {
        var run = Run("r", 1); run.StartingMapKnown = known; run.StartingMapId = id;
        Assert.Equal(expected, Value(Present(WithExtraction(run)).Overall[0], "Starting map"));
    }

    [Fact]
    public void OrderedRouteRetainsRepeatedMapsAndSingleSegmentOmitsRoute()
    {
        var run = Run("r", 1); var p = WithExtraction(run);
        Assert.DoesNotContain(Present(p).Overall[0].Rows, row => row.Key == "Route");
        run.Segments.Add(Segment("other", "Other")); run.Segments.Add(Segment("start", "Start"));
        Assert.Equal("Start - Other - Start", Value(Present(p).Overall[0], "Route"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void IncompleteRoutesRemainExplicitlyUnavailable(int mode)
    {
        var run = Run("r", 1);
        if (mode == 0) run.RouteCapabilities.Segments.State = AdapterCapabilityState.DisabledIncompatible;
        if (mode == 1) run.RouteWasRepairedFromInvalidState = true;
        if (mode == 2) run.RouteCapabilities.OrderedRoute.State = AdapterCapabilityState.DisabledIncompatible;
        if (mode == 3) run.Segments.Clear();
        Assert.Equal("Unavailable (partial; recorded values only)", Value(Present(WithExtraction(run)).Overall[0], "Route"));
    }

    [Fact]
    public void MissingRunKeepsStoredTimeAndDateButNoGuessedMapRouteOrNavigation()
    {
        var p = Projection(); p.Runs.Records.Extraction.Shortest = p.Runs.Records.Extraction.Longest = Reference(Run("missing", 64.083));
        var card = Present(p).Overall[0];
        Assert.Equal("01:04.083", Value(card, "Time")); Assert.Equal("2026-09-01 - 01:02", Value(card, "Date"));
        Assert.Equal("Unavailable", Value(card, "Starting map")); Assert.Equal("Unavailable", Value(card, "Route"));
        Assert.Null(card.RunId); Assert.Contains("exact recorded run", card.Notice);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void MismatchedOrIneligibleRunCannotBecomeActionable(int mode)
    {
        var run = Run("r", 10); var p = WithExtraction(run);
        if (mode == 0) run.StartedUtc = run.StartedUtc.AddDays(1);
        if (mode == 1) run.ActiveDurationSeconds++;
        if (mode == 2) run.RecordEligible = false;
        if (mode == 3) run.Outcome = RunOutcome.Interrupted;
        if (mode == 4) run.LifecycleCapability = AdapterCapabilityState.DisabledIncompatible;
        Assert.Null(Present(p).Overall[0].RunId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void GenerationAndCompositionAmbiguityFailsClosed(int mode)
    {
        var p = WithExtraction(Run("r", 1));
        if (mode == 0) p.Profile.GenerationId = "other";
        if (mode == 1) p.Profile.Statistics.SaveGenerationId = "other";
        if (mode == 2) p.Runs.Runs[0].SaveGenerationId = "other";
        if (mode == 3) p.Runs.Records = new RunDurationRecords();
        if (mode == 4) { p.Profile.Statistics.Runs.Add(Run("r", 3)); p.Runs = RunStatisticsViewModelFactory.Create(p.Profile); }
        Assert.Null(RecordsPresentationFactory.Create(p, "g"));
        var ambiguousMaps = Projection();
        var map = AddMap(ambiguousMaps, "a", "A", 1);
        ambiguousMaps.Profile.Statistics.RunTotals.Maps["b"] = map;
        ambiguousMaps.Runs = RunStatisticsViewModelFactory.Create(ambiguousMaps.Profile);
        Assert.Null(RecordsPresentationFactory.Create(ambiguousMaps, "g"));
    }

    [Fact]
    public void RecordNavigationUsesSameRunsSelectionAndExactIdentity()
    {
        var p = WithExtraction(Run("record", 10), Run("newer", 20));
        var records = Present(p); var runs = RunsPresentationFactory.Create(p, "g")!;
        var selection = new RunsSelection(); selection.Refresh(runs, "g");
        Assert.True(selection.Route(records.GenerationId, records.Overall[0].RunId!)); Assert.Equal("record", selection.SelectedId);
        Assert.False(selection.Route("stale", records.Overall[0].RunId!)); Assert.Null(selection.SelectedId);
    }

    [Fact]
    public void AllStartingMapsContinueInStableOrderWithoutVisitedMapSubstitution()
    {
        var p = Projection();
        for (var i = 29; i >= 0; i--) AddMap(p, "id" + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture), "Same", i);
        AddMap(p, "mod:extra", "", 0, false);
        p.Profile.Statistics.RunTotals.RouteMaps["visited"] = new RouteAwareMapAggregate { MapId = "visited" };
        p.Runs = RunStatisticsViewModelFactory.Create(p.Profile);
        var result = Present(p);
        Assert.Equal(31, result.Maps.Count); Assert.Equal("id00", result.Maps[0].Id); Assert.Equal("id29", result.Maps[29].Id);
        Assert.Equal("Unknown map (mod:extra)", result.Maps[30].Heading);
        Assert.DoesNotContain(result.Maps, map => map.Id == "visited");
        Assert.Contains(result.Maps[30].Rows, row => row.Key == "Longest successful raid");
    }

    [Fact]
    public void CountsKeepStoredTotalAndConditionalInterrupted()
    {
        var p = Projection(); var map = AddMap(p, "start", "Start", 7);
        map.Outcomes["Extracted"] = 2; map.Outcomes["Died"] = 3; map.Outcomes["Interrupted"] = 2;
        p.Runs = RunStatisticsViewModelFactory.Create(p.Profile);
        var card = Present(p).Maps[0];
        Assert.Equal("7", Value(card, "Runs")); Assert.Equal("2", Value(card, "Extracted"));
        Assert.Equal("3", Value(card, "Died")); Assert.Equal("2", Value(card, "Interrupted"));
        map.Outcomes["Interrupted"] = 0;
        Assert.DoesNotContain(Present(p).Maps[0].Rows, row => row.Key == "Interrupted");
    }

    [Theory]
    [InlineData(true, true, 0, "0")]
    [InlineData(false, true, 0, "Unavailable")]
    [InlineData(true, false, 0, "Unavailable")]
    [InlineData(true, true, 12.5, "12.50 m")]
    [InlineData(false, true, 12.5, "12.50 m (partial; recorded values only)")]
    [InlineData(true, false, 12.5, "12.50 m (partial; recorded values only)")]
    public void MovementUsesCurrentAndHistoricalAvailability(bool current, bool history, double n, string expected)
    {
        var run = Run("r", 1); if (!history) run.MovementCapability = AdapterCapabilityState.DisabledIncompatible;
        var p = Projection(run); var map = AddMap(p, "start", "Start", 1);
        map.PhysicalDistance = map.TeleportDistance = n;
        p.Runs = RunStatisticsViewModelFactory.Create(p.Profile); p.Runs.MovementSupported = current;
        var card = Present(p).Maps[0];
        Assert.Equal(expected, Value(card, "Total distance travelled")); Assert.Equal(expected, Value(card, "Total teleport distance"));
    }

    [Fact]
    public void MapRecordsJoinStableIdAndKeepAllDeathAndExtractionRoles()
    {
        var e = Run("e", 64.083); var d = Run("d", 2, RunOutcome.Died); var p = Projection(e, d);
        AddMap(p, "start", "Duplicate name", 2); AddMap(p, "another", "Duplicate name", 1);
        var record = new MapRunDurationRecords { MapId = "start", Extraction = new() { Shortest = Reference(e), Longest = Reference(e) }, Death = new() { Shortest = Reference(d), Longest = Reference(d) } };
        p.Profile.Statistics.RunRecords.Maps["start"] = record; p.Runs = RunStatisticsViewModelFactory.Create(p.Profile);
        var card = Present(p).Maps.Single(map => map.Id == "start");
        Assert.Equal("01:04.083", Value(card, "Fastest extraction")); Assert.Equal("01:04.083", Value(card, "Longest successful raid"));
        Assert.Equal("00:02.000", Value(card, "Shortest death run")); Assert.Equal("00:02.000", Value(card, "Longest death run"));
        Assert.Equal("No eligible extraction run", Value(Present(p).Maps.Single(map => map.Id == "another"), "Fastest extraction"));
        record.MapId = "another";
        Assert.Equal("Unavailable", Value(Present(p).Maps.Single(map => map.Id == "start"), "Fastest extraction"));
    }

    [Fact]
    public void OrphanRecordMapIsVisibleWithUnavailableTotals()
    {
        var p = Projection(); p.Runs.Records.Maps["orphan"] = new MapRunDurationRecords { MapId = "orphan" };
        var card = Assert.Single(Present(p).Maps); Assert.Equal("orphan", card.Id);
        Assert.Equal("Unavailable", Value(card, "Runs")); Assert.Contains("inconsistent", card.Notice);
    }

    [Fact]
    public void SnapshotOwnsOnlyImmutableCopiedFactsAndLongLocalizedContent()
    {
        var run = Run("r", 1); run.StartingMapDisplayName = new string('m', 500);
        run.Segments.Add(Segment("long", new string('r', 1500)));
        var p = WithExtraction(run);
        var snapshot = RecordsPresentationFactory.Create(p, "g", key => UiText.Get(key) + new string('l', 200))!;
        var original = snapshot.Overall[0].Rows.ToArray();
        run.StartingMapDisplayName = "changed"; run.Segments.Clear(); p.Runs.Records.Extraction.Shortest!.ActiveDurationSeconds = 99;
        Assert.Equal(original, snapshot.Overall[0].Rows); Assert.Contains(snapshot.Overall[0].Rows, row => row.Value.Contains(new string('r', 1500), StringComparison.Ordinal));
        Assert.Throws<NotSupportedException>(() => ((IList<RecordsCard>)snapshot.Overall).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<KeyValuePair<string, string>>)snapshot.Overall[0].Rows).Clear());
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1680, 1050)]
    [InlineData(2560, 1440)]
    [InlineData(1024, 768)]
    public void ResponsiveRowsWrapOrStackAndTrueBottomContainsFinalMap(int width, int height)
    {
        var transform = RetainedReferenceTransformPolicy.Create(width, height, 1);
        var inner = width / transform.CanvasLength(1) - 250;
        var columns = RecordsLayoutPolicy.Columns(inner, 1000);
        Assert.True(columns.ValueWidth > 0); Assert.True(columns.LabelWidth > 0);
        Assert.True(columns.ValueLeft + columns.ValueWidth <= inner + .01);
        Assert.True(RecordsLayoutPolicy.RowHeight(120, 200, columns.Stacked) >= 212);
        var document = RecordsLayoutPolicy.DocumentHeight(900, 5000);
        Assert.Equal(5970, document);
        var bottom = OverflowCuePolicy.Resolve(height, document, document - height);
        Assert.True(bottom.ShowLeading); Assert.False(bottom.ShowTrailing);
        Assert.True(OverflowCuePolicy.Resolve(height, document, document - height - 100).ShowTrailing);
    }

    [Theory]
    [InlineData(600, 600, 0, false, false)]
    [InlineData(600, 1800, 0, false, true)]
    [InlineData(600, 1800, 400, true, true)]
    [InlineData(600, 1800, 1200, true, false)]
    public void SharedOverflowStates(float viewport, float content, float offset, bool top, bool bottom)
    {
        var state = OverflowCuePolicy.Resolve(viewport, content, offset);
        Assert.Equal(top, state.ShowLeading); Assert.Equal(bottom, state.ShowTrailing);
    }

    [Fact]
    public void ScrollStateSurvivesTabSwitchAndSameGenerationRefreshButResetsNewGeneration()
    {
        var state = new RecordsScrollState(); state.Refresh("g"); state.Capture(800);
        for (var i = 0; i < 100; i++) { state.Refresh("g"); Assert.Equal(800, state.Offset); }
        state.Refresh(null); Assert.False(state.Available); state.Capture(0);
        state.Refresh("g"); Assert.Equal(800, state.Offset);
        state.Refresh(null); state.Refresh("new"); Assert.Equal(0, state.Offset); Assert.True(state.Available);
        Assert.Equal(600, RunsLayoutPolicy.Reveal(800, 600, 1200, 1100, 100));
    }

    [Fact]
    public void RetainedPoolReusesListenersAndReleasesRemovedCardsAcrossRefreshAndReopen()
    {
        var controls = new List<object>(); var live = new HashSet<object>();
        var created = 0; var released = 0;
        object Create(int _) { created++; var value = new object(); Assert.True(live.Add(value)); return value; }
        void Release(object value) { Assert.True(live.Remove(value)); released++; }
        for (var shell = 0; shell < 10; shell++)
        {
            RecordsControlPool.Synchronize(controls, 30, Create, Release);
            var initial = controls.ToArray(); var createdBeforeRefresh = created;
            for (var refresh = 0; refresh < 100; refresh++) RecordsControlPool.Synchronize(controls, 30, Create, Release);
            Assert.Equal(createdBeforeRefresh, created); Assert.Equal(initial, controls); Assert.Equal(30, live.Count);
            RecordsControlPool.Synchronize(controls, 4, Create, Release); Assert.Equal(4, live.Count);
            RecordsControlPool.Synchronize(controls, 0, Create, Release);
            RecordsControlPool.Synchronize(controls, 0, Create, Release);
            Assert.Empty(live); Assert.Equal(created, released);
        }
    }

    [Fact]
    public void ProductionReducerAndProjectionComposeStartingMapCountsAndRecords()
    {
        var e = Run("e", 64.083); e.PhysicalDistance = 12; e.TeleportDistance = 5;
        var d = Run("d", 100, RunOutcome.Died);
        var interrupted = Run("i", 5, RunOutcome.Interrupted); interrupted.RecordEligible = false;
        var excluded = Run("excluded", 1); excluded.RecordEligible = false; excluded.IntegrityTags = IntegrityTags.CheatOrCustomDifficulty;
        var p = Projection();
        foreach (var run in new[] { e, d, interrupted, excluded })
        {
            run.EndedUtc = run.StartedUtc.AddSeconds(run.ActiveDurationSeconds);
            run.EndingMapId = "start";
            var segment = run.Segments[0]; segment.SegmentId = run.RunId + "-segment";
            segment.EnteredUtc = run.StartedUtc; segment.ExitedUtc = run.EndedUtc;
            segment.ExitReason = run.Outcome == RunOutcome.Died ? MapSegmentExitReason.Died
                : run.Outcome == RunOutcome.Extracted ? MapSegmentExitReason.Extracted : MapSegmentExitReason.Interrupted;
            segment.ActiveDurationSeconds = run.ActiveDurationSeconds;
            segment.PhysicalDistance = run.PhysicalDistance; segment.TeleportDistance = run.TeleportDistance;
            run.RouteSignature = RouteStatisticsReducer.BuildSignature(run.Segments);
            RunReducer.Apply(p.Profile.Statistics, run);
        }
        p.Runs = RunStatisticsViewModelFactory.Create(p.Profile);
        var result = Present(p); var map = Assert.Single(result.Maps);
        Assert.Equal("4", Value(map, "Runs")); Assert.Equal("1", Value(map, "Interrupted"));
        Assert.Equal("2", Value(map, "Extracted")); Assert.Equal("1", Value(map, "Died"));
        Assert.Equal("01:04.083", Value(map, "Fastest extraction")); Assert.Equal("01:40.000", Value(map, "Shortest death run"));
        Assert.Equal("e", result.Overall[0].RunId); Assert.Equal("d", result.Overall[2].RunId);
    }

    [Fact]
    public void MissingPairInsideExistingMapIsUnavailableButAbsentMapRecordsAreEmpty()
    {
        var p = Projection(); AddMap(p, "start", "Start", 0); p.Runs = RunStatisticsViewModelFactory.Create(p.Profile);
        Assert.Equal("No eligible extraction run", Value(Present(p).Maps[0], "Fastest extraction"));
        Assert.DoesNotContain(Present(p).Maps[0].Rows, row => row.Key == "Shortest death run" || row.Key == "Longest death run");
        p.Runs.Records.Maps["start"] = new MapRunDurationRecords { MapId = "start", Extraction = null! };
        Assert.Equal("Unavailable", Value(Present(p).Maps[0], "Fastest extraction"));
    }

    [Fact]
    public void UnityCompositionUsesSharedNativeControlsAndRetainedLifecycle()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "UltimateDuckovStatistics.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var ui = Path.Combine(dir.FullName, "src", "UltimateDuckovStatistics", "UI");
        var view = File.ReadAllText(Path.Combine(ui, "RetainedRecordsView.cs"));
        var shell = File.ReadAllText(Path.Combine(ui, "RetainedStatisticsShell.cs"));
        var shared = File.ReadAllText(Path.Combine(ui, "RetainedRunsView.cs"));
        Assert.Contains("new ScrollRegion(root, \"RecordsPage\", radius: 20)", view);
        Assert.Contains("RunsNativeScrollConfiguration.Apply(Scroll)", shared); Assert.Contains("RunsOverflowEdge Edge", shared);
        Assert.Contains("CreateOverviewLatestRunViewRun(card.Root", view);
        Assert.Contains("target.AddComponent<ButtonAnimation>()", shell);
        Assert.Contains("AddButtonFeedback(card.Button.Button)", view); Assert.Contains("AddComponent<RunsFocusHandler>()", view);
        Assert.Contains("Move = direction => MoveFocus(card, direction)", view); Assert.Contains("route(snapshot.GenerationId, id)", view);
        Assert.Contains("RecordsControlPool.Synchronize(pool, cards.Count", view); Assert.Contains("card.Dispose()", view);
        Assert.Contains("Button?.Button.onClick.RemoveAllListeners()", view); Assert.Contains("page.Dispose()", view);
        Assert.Contains("page.Rect.gameObject.SetActive(next != null)", view);
        Assert.Contains("recordsView?.Refresh(null)", shared); Assert.Contains("recordsView?.Dispose()", shell);
        Assert.DoesNotContain("ProfileDocument", view); Assert.DoesNotContain("RunSummary", view);
        Assert.DoesNotContain("ValidateSurface", view); Assert.DoesNotContain("new Material", view);
    }

    private static RecordsPresentation Present(StatisticsPanelProjection p) => RecordsPresentationFactory.Create(p, "g", toLocal: utc => utc)!;
    private static string Value(RecordsCard card, string key) => card.Rows.Single(row => row.Key == key).Value;
    private static StatisticsPanelProjection Projection(params RunSummary[] runs)
    {
        var profile = new ProfileDocument { GenerationId = "g", Statistics = new ProfileStatistics { SaveGenerationId = "g", Runs = runs.ToList() } };
        return new StatisticsPanelProjection { Profile = profile, Runs = RunStatisticsViewModelFactory.Create(profile) };
    }
    private static StatisticsPanelProjection WithExtraction(params RunSummary[] runs)
    {
        var p = Projection(runs); p.Runs.Records.Extraction.Shortest = p.Runs.Records.Extraction.Longest = Reference(runs[0]); return p;
    }
    private static MapRunAggregate AddMap(StatisticsPanelProjection p, string id, string name, long total, bool known = true)
    {
        var map = new MapRunAggregate { MapId = id, DisplayName = name, IsKnown = known, TotalRuns = total };
        p.Profile.Statistics.RunTotals.Maps[id] = map; return map;
    }
    private static DurationRecordReference Reference(RunSummary run) => new()
    { RunId = run.RunId, ActiveDurationSeconds = run.ActiveDurationSeconds, StartedUtc = run.StartedUtc, MapId = run.StartingMapId, MapDisplayName = run.StartingMapDisplayName };
    private static RunSummary Run(string id, double duration, RunOutcome outcome = RunOutcome.Extracted) => new()
    {
        RunId = id,
        SaveGenerationId = "g",
        StartedUtc = new DateTime(2026, 9, 1, 1, 2, 3, DateTimeKind.Utc),
        ActiveDurationSeconds = duration,
        Outcome = outcome,
        RecordEligible = true,
        IntegrityTags = IntegrityTags.Normal,
        LifecycleCapability = AdapterCapabilityState.Supported,
        MovementCapability = AdapterCapabilityState.Supported,
        StartingMapKnown = true,
        StartingMapId = "start",
        StartingMapDisplayName = "Start",
        RouteCapabilities = new RouteMetricCapabilities { OrderedRoute = new() { State = AdapterCapabilityState.Supported }, Segments = new() { State = AdapterCapabilityState.Supported } },
        Segments = new List<MapSegmentSummary> { Segment("start", "Start") }
    };
    private static MapSegmentSummary Segment(string id, string name) => new() { MapId = id, MapDisplayName = name, MapKnown = true };
}
