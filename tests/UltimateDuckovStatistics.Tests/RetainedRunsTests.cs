using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.UI;

namespace UltimateDuckovStatistics.Tests;

public sealed class RetainedRunsTests
{
    private static readonly string[] ExpectedRunOrder = ["a", "b", "old"];
    private static readonly string[] ExpectedRouteOrder = ["1  First", "2  Second", "3  First"];
    [Fact]
    public void HistoryOrdersNewestFirstWithStableTieBreakAndNumbers()
    {
        var a = Run("a", 2); var b = Run("b", 2); var old = Run("old", 1);
        var result = Present(b, old, a);
        Assert.Equal(ExpectedRunOrder, result.Runs.Select(run => run.Id));
        Assert.StartsWith("Run 3 ·", result.Runs[0].Metadata);
        Assert.StartsWith("Run 1 ·", result.Runs[2].Metadata);
    }

    [Fact]
    public void InitialNewestAndStableSelectionAcrossRefreshAndDisappearance()
    {
        var state = new RunsSelection();
        Assert.True(state.Refresh(Present(Run("old", 1), Run("new", 2)), "g"));
        Assert.Equal("new", state.SelectedId);
        Assert.True(state.Select("old"));
        state.Refresh(Present(Run("newer", 3), Run("old", 1)), "g");
        Assert.Equal("old", state.SelectedId);
        state.Refresh(Present(Run("newer", 3)), "g");
        Assert.Equal("newer", state.SelectedId);
    }

    [Fact]
    public void OverviewRoutesExactCardIdentityEvenWhenANewerRunExists()
    {
        var cardRun = Run("card", 1);
        var card = RetainedLatestRunViewRunPresentationFactory.Create(
            RetainedRunBadgePresentationFactory.Create(Projection(cardRun), UiText.Get), UiText.Get);
        var state = new RunsSelection();
        state.Refresh(Present(cardRun, Run("new", 2)), "g");
        Assert.True(state.Route(card.LatestRun!.SaveGenerationId, card.LatestRun.RunId));
        Assert.Equal("card", state.Selected!.Id);
    }

    [Theory]
    [InlineData("g", "missing")]
    [InlineData("another-generation", "new")]
    public void FailedExactRoutingNeverPretendsAnotherRunIsTheRequestedRun(string generation, string id)
    {
        var state = new RunsSelection(); state.Refresh(Present(Run("new", 2)), "g");
        Assert.False(state.Route(generation, id)); Assert.Null(state.Selected);
        Assert.True(state.RequestedRunUnavailable);
        Assert.True(state.Select("new")); Assert.False(state.RequestedRunUnavailable);
    }

    [Fact]
    public void ChangedSelectionChangesAllDetailsAndUsesDetachedValues()
    {
        var a = Run("a", 1); var b = Run("b", 2);
        a.CombatStatistics.Totals.MeleeSwings = 42; b.CombatStatistics.Totals.MeleeSwings = 99;
        var snapshot = Present(a, b);
        a.CombatStatistics.Totals.MeleeSwings = 123;
        a.Segments[0].MapDisplayName = "Mutated";
        var state = new RunsSelection(); state.Refresh(snapshot, "g"); state.Select("a");
        Assert.StartsWith("42 swings", state.Selected!.Melee);
        Assert.DoesNotContain("Mutated", state.Selected.Segments[0].Key);
        state.Select("b"); Assert.StartsWith("99 swings", state.Selected!.Melee);
        Assert.IsAssignableFrom<System.Collections.ObjectModel.ReadOnlyCollection<RunDetailPresentation>>(snapshot.Runs);
    }

    [Theory]
    [InlineData(RunOutcome.Extracted, (int)RetainedRunBadgeState.Extracted)]
    [InlineData(RunOutcome.Died, (int)RetainedRunBadgeState.Died)]
    [InlineData(RunOutcome.Interrupted, (int)RetainedRunBadgeState.Unknown)]
    public void OutcomesUseSharedBadgeContract(RunOutcome outcome, int expected)
    {
        var state = (RetainedRunBadgeState)expected;
        var run = Run("r", 1); run.Outcome = outcome;
        Assert.Equal(state, Present(run).Runs[0].Outcome);
        Assert.NotEmpty(UiText.Get(RetainedRunBadgePolicy.ResolveSpecification(state).TextKey));
    }

    [Fact]
    public void EmptyHistoryHasNoInventedRunOrSelection()
    {
        var state = new RunsSelection(); state.Refresh(Present(), "g");
        Assert.Empty(state.Snapshot!.Runs); Assert.Null(state.Selected); Assert.False(state.RequestedRunUnavailable);
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("statistics")]
    [InlineData("run")]
    [InlineData("expected")]
    public void EveryGenerationBoundaryFailsClosed(string boundary)
    {
        var projection = Projection(Run("r", 1));
        if (boundary == "profile") projection.Profile.GenerationId = "wrong";
        if (boundary == "statistics") projection.Profile.Statistics.SaveGenerationId = "wrong";
        if (boundary == "run") projection.Runs.Runs[0].SaveGenerationId = "wrong";
        Assert.Null(RunsPresentationFactory.Create(projection, boundary == "expected" ? "wrong" : "g"));
    }

    [Fact]
    public void DuplicateRunIdentityFailsClosed()
    {
        Assert.Null(RunsPresentationFactory.Create(Projection(Run("same", 1), Run("same", 2)), "g"));
    }

    [Fact]
    public void RefreshGenerationMismatchClearsBothListAndDetails()
    {
        var state = new RunsSelection(); state.Refresh(Present(Run("r", 1)), "g");
        Assert.False(state.Refresh(Present(Run("r", 1)), "other"));
        Assert.Null(state.Snapshot); Assert.Null(state.Selected);
    }

    [Fact]
    public void NewGenerationDoesNotCarrySelectionAcrossEqualRunIds()
    {
        var state = new RunsSelection(); state.Refresh(Present(Run("old", 1)), "g");
        var run = Run("other", 2); var repeated = Run("old", 1);
        var snapshot = Present(run, repeated);
        state.Refresh(new RunsPresentation("new", snapshot.Runs), "new");
        Assert.Equal("other", state.SelectedId);
    }

    [Fact]
    public void FullTimestampConvertsUtcBeforeFormatting()
    {
        var run = Run("r", 1);
        var snapshot = RunsPresentationFactory.Create(Projection(run), "g", toLocal: utc => utc.AddHours(2))!;
        Assert.Contains("2026-09-01 - 03:02:03", snapshot.Runs[0].Metadata);
    }

    [Fact]
    public void NumericZeroIsExactOnlyWithEvidence()
    {
        var run = Run("r", 1);
        var exact = Present(run).Runs[0];
        Assert.Equal("0", exact.Summary[2].Value);
        Assert.Equal("0", exact.Summary[3].Value);
        Assert.Contains("0 kills", exact.Melee);
        run.CombatStatistics.Capabilities.KillsByYou.State = AdapterCapabilityState.DisabledIncompatible;
        run.ContainerStatistics.HistoricalUnavailable = true;
        var unavailable = Present(run).Runs[0];
        Assert.Equal("Unavailable", unavailable.Summary[2].Value);
        Assert.Equal("Unavailable", unavailable.Summary[3].Value);
        Assert.DoesNotContain("0 kills", unavailable.Melee);
    }

    [Fact]
    public void HistoricalPartialFieldsKeepUsefulProvenValues()
    {
        var run = Run("r", 1);
        run.ContainerStatistics.HistoricalUnavailable = true; run.ContainerStatistics.UniqueContainersLooted = 7;
        run.ItemStatistics.HistoricalUnavailable = true; run.ItemStatistics.Overall.ActualHealthRestored = 23;
        run.HistoricalRouteUnavailable = true;
        var result = Present(run).Runs[0];
        Assert.Contains("7 (partial", result.Summary[3].Value);
        Assert.Contains("23 (partial", result.Summary[9].Value);
        Assert.Contains("partial", result.RouteSummary);
        Assert.Contains("unavailable", result.EquipmentState, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TerminalEmptyAndMissingEvidenceRemainDistinctWithoutInventedSlots()
    {
        var run = Run("r", 1);
        run.TerminalLoadout = new TerminalLoadout
        {
            State = TerminalLoadoutState.Partial,
            Snapshot = new EquipmentSnapshot
            {
                CharacterSlotStateComplete = false,
                CharacterSlots = new List<CharacterEquipmentSlotSnapshot>
                {
                    new() { SlotId = "empty", SlotDisplayName = "Empty slot", State = EquipmentSlotState.Empty }
                }
            }
        };
        var result = Present(run).Runs[0];
        Assert.Single(result.Slots); Assert.Equal(EquipmentSlotState.Empty, result.Slots[0].State);
        Assert.Contains("Empty", result.Slots[0].Text);
        Assert.Contains("unavailable", result.EquipmentState);
        Assert.DoesNotContain("attachment", result.Slots[0].Text);
    }

    [Fact]
    public void CapturedRootIdentityAndIncompleteNestedEvidenceAreDetached()
    {
        var nested = new List<TerminalNestedSlot>
        {
            new("path", "scope", "Scope", EquipmentSlotState.Occupied, "mod:scope", "Mod scope")
        };
        var slot = new TerminalRootSlot("native:weapon", "Weapon", EquipmentSlotState.Occupied,
            "mod:gun", "Mod gun", EquipmentItemKind.Weapon, false, nested);
        var result = RunsPresentationFactory.PresentSlot(slot, UiText.Get);
        nested.Clear();
        Assert.Equal("mod:gun", result.ItemId); Assert.Contains("Mod scope", result.Text);
        Assert.Contains("Additional attachment evidence unavailable", result.Text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrThrowingIconResolverPreservesModdedIdentity(bool throws)
    {
        var slot = new RunSlotPresentation("slot", EquipmentSlotState.Occupied, "mod:gun", "Mod gun");
        Assert.Null(RunsItemIconPolicy.Resolve<object>(slot, _ => throws ? throw new InvalidOperationException() : null));
        Assert.Equal("mod:gun", slot.ItemId); Assert.Equal("Mod gun", slot.Text);
        var fallback = new object(); Assert.Same(fallback, RunsItemIconPolicy.Resolve(slot, _ => fallback));
    }

    [Fact]
    public void EmptySlotNeverUsesAnItemIcon()
    {
        var empty = new RunSlotPresentation("slot", EquipmentSlotState.Empty, string.Empty, "Empty");
        Assert.Null(RunsItemIconPolicy.Resolve<object>(empty, _ => throw new Xunit.Sdk.XunitException("Must not resolve an item for empty evidence")));
    }

    [Fact]
    public void RangedAndMeleeUseIndependentClassifiedCounts()
    {
        var run = Run("r", 1);
        run.CombatStatistics.Totals.KillsByYou = 15;
        run.CombatStatistics.Totals.PlayerKills = new PlayerKillPartition { Ranged = 3, Melee = 2, Effect = 10 };
        var result = Present(run).Runs[0];
        Assert.Contains("3 kills", result.Ranged); Assert.Contains("2 kills", result.Melee);
        Assert.DoesNotContain("12 kills", result.Melee);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncompleteClassificationsDoNotCreateExactZeros(bool historical)
    {
        var run = Run("r", 1);
        run.CombatStatistics.Totals.KillsByYou = 5;
        run.CombatStatistics.Totals.PlayerKills = historical ? PlayerKillPartition.Historical(5) : new PlayerKillPartition { Unknown = 5 };
        var result = Present(run).Runs[0];
        Assert.Contains("Unavailable kills", result.Melee);
        Assert.DoesNotContain("0 kills", result.Ranged);
        Assert.Contains(historical ? "Historical" : "incomplete", result.Ranged);
    }

    [Fact]
    public void NewlyExactRunIsNotContaminatedByHistoricalSibling()
    {
        var old = Run("old", 1); old.CombatStatistics.Totals.PlayerKills = PlayerKillPartition.Historical(0);
        var current = Run("current", 2);
        var result = Present(old, current);
        Assert.Contains("0 kills", result.Runs[0].Melee);
        Assert.DoesNotContain("0 kills", result.Runs[1].Melee);
    }

    [Fact]
    public void RouteKeepsRepeatedMapsInStoredOrderAndNaturalUnits()
    {
        var run = Run("r", 1);
        run.Segments.Add(Segment("b", "Second")); run.Segments.Add(Segment("a", "First"));
        run.Segments[0].CombatStatistics.Totals.KillsByYou = 1;
        var result = Present(run).Runs[0];
        Assert.Equal(ExpectedRouteOrder, result.Segments.Select(segment => segment.Key));
        Assert.Contains("1 kill ·", result.Segments[0].Value);
        Assert.Equal("3 segments · 2 maps", result.RouteSummary);
    }

    [Theory]
    [InlineData(100, 100, 0, false, false)]
    [InlineData(100, 800, 0, false, true)]
    [InlineData(100, 800, 300, true, true)]
    [InlineData(100, 800, 698, true, true)]
    [InlineData(100, 800, 700, true, false)]
    public void RouteAndHistoryOverflowReflectTrueExtents(float viewport, float content, float offset, bool above, bool below)
    {
        var state = OverflowCuePolicy.Resolve(viewport, content, offset);
        Assert.Equal(above, state.ShowLeading); Assert.Equal(below, state.ShowTrailing);
    }

    [Theory]
    [InlineData(1280, 720, false)]
    [InlineData(1680, 1050, false)]
    [InlineData(2560, 1440, false)]
    [InlineData(1024, 768, true)]
    public void ResponsiveColumnsKeepSpacingAndAllSummaryCells(float width, float height, bool stacked)
    {
        var transform = RetainedReferenceTransformPolicy.Create(width, height, 1);
        var layout = RetainedVisualLayoutPolicy.Create(transform);
        Assert.Equal(stacked, RunsLayoutPolicy.Stack(width));
        var contentWidth = layout.Header.Width / transform.CanvasLength(1);
        var historyWidth = RunsLayoutPolicy.HistoryWidth(contentWidth, stacked);
        Assert.True(historyWidth > 0);
        if (stacked) Assert.Equal(contentWidth, historyWidth);
        else Assert.Equal(contentWidth, historyWidth * 3 + RunsLayoutPolicy.Gap, 3);
        var columns = RunsLayoutPolicy.SummaryColumns(stacked);
        Assert.Equal(10, columns * (10 / columns));
    }

    [Fact]
    public void LongHistoryKeepsVisibleControlWindowBoundedAndCanReachLastRow()
    {
        var tops = Enumerable.Range(0, 10000).Select(index => index * 125f).ToArray();
        foreach (var offset in new[] { 0f, 10000f, 500000f, tops[^1] - 600 })
        {
            var window = RunsHistoryWindow.Visible(tops, offset, 700);
            Assert.InRange(window.End - window.First, 1, 9);
        }
        var bottom = RunsHistoryWindow.Visible(tops, tops[^1] - 600, 700);
        Assert.Equal(tops.Length, bottom.End);
    }

    [Fact]
    public void FocusRevealScrollsBothWaysAndClampsAtBottom()
    {
        Assert.Equal(500, RunsLayoutPolicy.Reveal(0, 200, 1000, 600, 100));
        Assert.Equal(20, RunsLayoutPolicy.Reveal(500, 200, 1000, 20, 100));
        Assert.Equal(800, RunsLayoutPolicy.Reveal(0, 200, 1000, 950, 100));
    }

    [Fact]
    public void LongLocalizedAndStoredStringsRemainUntruncated()
    {
        var run = Run("r", 1); var longName = new string('界', 2000);
        run.Segments[0].MapDisplayName = longName;
        var result = RunsPresentationFactory.Create(Projection(run), "g", key => "Langer lokalisierter Text " + UiText.Get(key))!;
        Assert.Equal(longName, result.Runs[0].Title);
        Assert.Contains(longName, result.Runs[0].Segments[0].Key);
        Assert.Contains("Langer lokalisierter Text", result.Runs[0].Summary[0].Key);
    }

    [Fact]
    public void LongRouteRetainsEverySegmentAndOutcome()
    {
        var run = Run("r", 1); run.Segments = Enumerable.Range(0, 300).Select(index => Segment(index.ToString(System.Globalization.CultureInfo.InvariantCulture), "Map " + index)).ToList();
        var result = Present(run).Runs[0];
        Assert.Equal(300, result.Segments.Count); Assert.Equal("300  Map 299", result.Segments[^1].Key);
        Assert.Equal(RetainedRunBadgeState.Extracted, result.Outcome);
    }

    [Fact]
    public void RepeatedOpenRefreshSelectionAndInvalidationDoNotAccumulateHistory()
    {
        var snapshot = Present(Run("a", 1), Run("b", 2));
        for (var opening = 0; opening < 100; opening++)
        {
            var state = new RunsSelection();
            for (var refresh = 0; refresh < 50; refresh++)
            {
                state.Refresh(snapshot, "g"); state.Select("a");
                Assert.Same(snapshot, state.Snapshot); Assert.Equal(2, state.Snapshot!.Runs.Count);
            }
            state.Invalidate(); Assert.Null(state.Selected); Assert.Null(state.Snapshot);
        }
    }

    private static RunsPresentation Present(params RunSummary[] runs) => RunsPresentationFactory.Create(Projection(runs), "g")!;

    [Fact]
    public void SameGenerationHandoffRestoresSelectionOnlyAfterAValidRefresh()
    {
        var snapshot = Present(Run("new", 2), Run("old", 1));
        var state = new RunsSelection(); state.Refresh(snapshot, "g"); state.Select("old");
        state.Invalidate(); Assert.Null(state.Selected); Assert.Null(state.Snapshot);
        state.Refresh(snapshot, "g"); Assert.Equal("old", state.SelectedId);
    }

    [Theory]
    [InlineData(100, 100, 0, 1, true)]
    [InlineData(100, 800, 0, -1, true)]
    [InlineData(100, 800, 0, 1, false)]
    [InlineData(100, 800, 300, 1, false)]
    [InlineData(100, 800, 700, 1, true)]
    [InlineData(100, 800, 700, -1, false)]
    public void NestedScrollingPassesUnusedMotionToParent(float viewport, float content, float offset, float direction, bool forward)
        => Assert.Equal(forward, RunsScrollPolicy.Forward(viewport, content, offset, direction));

    [Theory]
    [InlineData(700, 120, 30, 32, 24, false)]
    [InlineData(700, 600, 60, 96, 48, true)]
    [InlineData(320, 270, 120, 240, 96, true)]
    public void MeasuredNativeRowWrapsLongLabelsWithoutClippingOrRejecting(float width, float badgeWidth,
        float badgeHeight, float titleHeight, float metadataHeight, bool stacked)
    {
        var layout = RunsHistoryRowLayout.Create(width, badgeWidth, badgeHeight, _ => titleHeight, metadataHeight);
        Assert.True(layout.TitleWidth > 0);
        Assert.Equal(stacked, layout.TitleTop > 10);
        Assert.True(layout.MetadataTop >= layout.TitleTop + titleHeight);
        Assert.True(layout.Height >= layout.MetadataTop + metadataHeight + 12);
    }

    [Fact]
    public void RepeatedVirtualizationAttachesNativeFeedbackOnceAndDisposesEachControlOnce()
    {
        var created = 0; var attached = 0; var disposed = 0;
        for (var opening = 0; opening < 10; opening++)
        {
            var pool = new RunsControlPool<FakeRow>(() =>
            {
                created++;
                var row = new FakeRow(() => disposed++);
                for (var retry = 0; retry < 2; retry++)
                    NativeButtonInteractionFeedbackPolicy.AttachIfMissing(row, target => target.Attached,
                        target => { target.Attached = true; attached++; });
                return row;
            });
            for (var refresh = 0; refresh < 100; refresh++)
            { pool.Ensure(12); pool.Ensure(5); Assert.Equal(12, pool.Items.Count); }
            pool.Dispose(); pool.Dispose(); pool.Ensure(12);
            Assert.Empty(pool.Items);
        }
        Assert.Equal(120, created); Assert.Equal(created, attached); Assert.Equal(created, disposed);
    }

    private sealed class FakeRow(Action dispose) : IDisposable
    {
        public bool Attached { get; set; }
        public void Dispose() => dispose();
    }

    [Fact]
    public void CompleteTerminalRootAndNestedTreeSurvivesSourceMutation()
    {
        var run = Run("r", 1);
        run.TerminalLoadout = new TerminalLoadout
        {
            State = TerminalLoadoutState.Complete,
            Snapshot = new EquipmentSnapshot
            {
                CharacterSlotStateComplete = true,
                NestedSlotStateComplete = true,
                CharacterSlots = [new() { SlotId = "weapon", SlotDisplayName = "Weapon", State = EquipmentSlotState.Occupied,
                    ItemId = "mod:weapon", ItemDisplayName = "Captured weapon", ItemKind = EquipmentItemKind.Weapon }],
                Items = [new() { SlotId = "weapon", ItemId = "mod:weapon", NestedSlotStateComplete = true,
                    NestedSlots = [new() { Path = "scope", SlotKey = "scope", SlotDisplayName = "Scope", State = EquipmentSlotState.Empty }] }]
            }
        };
        var result = Present(run).Runs[0];
        run.TerminalLoadout.Snapshot.CharacterSlots.Clear(); run.TerminalLoadout.Snapshot.Items.Clear();
        Assert.Equal("Captured terminal equipment", result.EquipmentState);
        Assert.Equal("mod:weapon", Assert.Single(result.Slots).ItemId);
        Assert.Contains("Scope: Empty", result.Slots[0].Text);
        Assert.DoesNotContain("unavailable", result.Slots[0].Text);
    }
    private static StatisticsPanelProjection Projection(params RunSummary[] runs)
    {
        var profile = new ProfileDocument { GenerationId = "g", Statistics = new ProfileStatistics { SaveGenerationId = "g", Runs = runs.ToList() } };
        return new StatisticsPanelProjection { Profile = profile, Runs = RunStatisticsViewModelFactory.Create(profile) };
    }
    private static RunSummary Run(string id, int hour)
    {
        var run = new RunSummary
        {
            RunId = id,
            SaveGenerationId = "g",
            StartedUtc = new DateTime(2026, 9, 1, hour, 2, 3, DateTimeKind.Utc),
            Outcome = RunOutcome.Extracted,
            LifecycleCapability = AdapterCapabilityState.Supported,
            MovementCapability = AdapterCapabilityState.Supported,
            MapKnown = true,
            MapId = "a",
            MapDisplayName = "First",
            StartingMapKnown = true,
            StartingMapId = "a",
            StartingMapDisplayName = "First",
            RecordEligible = true,
            Segments = new List<MapSegmentSummary> { Segment("a", "First") }
        };
        Support(run.CombatStatistics.Capabilities); Support(run.WeaponStatistics.Capabilities);
        Support(run.ContainerStatistics.Capabilities); Support(run.RouteCapabilities); Support(run.Economy.Capabilities);
        return run;
    }
    private static MapSegmentSummary Segment(string id, string name)
    {
        var segment = new MapSegmentSummary { MapId = id, MapDisplayName = name, MapKnown = true };
        Support(segment.CombatStatistics.Capabilities); Support(segment.WeaponStatistics.Capabilities); Support(segment.ContainerStatistics.Capabilities);
        return segment;
    }
    private static void Support(object capabilities)
    {
        foreach (var property in capabilities.GetType().GetProperties())
            if (property.GetValue(capabilities) is MetricAvailability metric) metric.State = AdapterCapabilityState.Supported;
    }
}
