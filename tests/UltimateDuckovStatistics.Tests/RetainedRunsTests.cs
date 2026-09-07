using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.UI;

namespace UltimateDuckovStatistics.Tests;

public sealed class RetainedRunsTests
{
    [Theory]
    [InlineData("g", "g", false)]
    [InlineData("g", "new", true)]
    [InlineData("g", null, true)]
    [InlineData("g", "", true)]
    [InlineData(null, "g", true)]
    [InlineData(null, null, true)]
    public void LiveRefreshRetainsControlsOnlyWithinTheSameAvailableGeneration(string? current, string? next, bool clear)
    {
        Assert.Equal(clear, RetainedRefreshPolicy.RequiresInvalidation(current, next));
    }

    [Fact]
    public void LiveRefreshPreservesAPressButRecyclingStillCancelsIt()
    {
        var binding = new RunsRowBinding(); binding.Bind("g", "run:a"); binding.Press();
        for (var i = 0; i < 120; i++)
        {
            Assert.False(RetainedRefreshPolicy.RequiresInvalidation(binding.Generation, "g"));
            binding.Bind("g", "run:a");
        }
        Assert.True(binding.Release(false));
        binding.Press(); binding.Bind("g", "run:b"); Assert.False(binding.Release(false));
        binding.Press(); binding.Bind("new", "run:b"); Assert.False(binding.Release(false));
    }

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
    public void RunTimestampConvertsUtcBeforeFormatting()
    {
        var run = Run("r", 1);
        var snapshot = RunsPresentationFactory.Create(Projection(run), "g", toLocal: utc => utc.AddHours(2))!;
        Assert.Contains("01.09.2026 - 03:02", snapshot.Runs[0].Metadata);
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
    [Theory]
    [InlineData(0, 0, "No combat or containers")]
    [InlineData(1, 0, "1 firing action · No containers")]
    [InlineData(0, 1, "No combat · 1 container opened")]
    [InlineData(0, 2, "No combat · 2 containers opened")]
    public void SegmentZeroWordingUsesCompleteCombatAndContainerEvidence(int firing, int containers, string expected)
    {
        var run = Run("r", 1);
        run.Segments[0].WeaponStatistics.Totals.FiringActions = firing;
        run.Segments[0].ContainerStatistics.UniqueContainersLooted = containers;
        Assert.EndsWith(expected, Present(run).Runs[0].Segments[0].Value);
    }

    [Theory]
    [InlineData("route")]
    [InlineData("combat")]
    [InlineData("firing")]
    [InlineData("container")]
    [InlineData("attribution")]
    [InlineData("repair")]
    public void IncompleteSegmentEvidenceNeverClaimsProvenEmpty(string boundary)
    {
        var run = Run("r", 1); var segment = run.Segments[0];
        if (boundary == "route") run.RouteCapabilities.Segments.State = AdapterCapabilityState.Experimental;
        if (boundary == "combat") segment.CombatStatistics.Capabilities.MeleeSwings.State = AdapterCapabilityState.Experimental;
        if (boundary == "firing") segment.WeaponStatistics.Capabilities.FiringActions.State = AdapterCapabilityState.Experimental;
        if (boundary == "container") segment.ContainerStatistics.HistoricalUnavailable = true;
        if (boundary == "attribution") run.HistoricalEventAttributionIncomplete = true;
        if (boundary == "repair") segment.WasRepairedFromInvalidState = true;
        var text = Present(run).Runs[0].Segments[0].Value;
        Assert.DoesNotContain("No combat", text); Assert.DoesNotContain("No containers", text);
    }

    [Theory]
    [InlineData("swing")]
    [InlineData("damage")]
    [InlineData("received")]
    [InlineData("hit")]
    [InlineData("projectile")]
    [InlineData("death")]
    public void CombatWithoutKillsCannotBePresentedAsNoCombat(string evidence)
    {
        var run = Run("r", 1); var totals = run.Segments[0].CombatStatistics.Totals;
        if (evidence == "swing") totals.MeleeSwings = 1;
        if (evidence == "damage") totals.DamageDealt = 1;
        if (evidence == "received") totals.DamageReceived = 1;
        if (evidence == "hit") totals.MeleeHits = 1;
        if (evidence == "projectile") totals.CompletedPlayerProjectiles = 1;
        if (evidence == "death") totals.ObservedWorldDeaths = 1;
        var text = Present(run).Runs[0].Segments[0].Value;
        Assert.DoesNotContain("No combat", text); Assert.EndsWith("No containers", text);
    }

    [Fact]
    public void NativeButtonBoundaryHasFullRowHitAndSelectsCurrentVisibleBindingOnce()
    {
        var state = new RunsSelection(); state.Refresh(Present(Run("a", 1), Run("b", 2)), "g");
        var graphic = new UnityEngine.UI.Graphic { raycastTarget = false }; // Decorative parent factory default.
        var button = new RunsHistoryButton(); button.Configure(graphic);
        var activations = 0;
        button.Clicked += () => { if (button.Binding.Activate(state)) activations++; };
        Assert.True(graphic.raycastTarget); Assert.True(button.IsActive()); Assert.True(button.IsInteractable());
        Assert.Same(graphic, button.targetGraphic);
        var pointer = new UnityEngine.EventSystems.PointerEventData();
        button.Binding.Bind("g", "a"); button.OnPointerDown(pointer); button.OnPointerClick(pointer);
        Assert.Equal("a", state.Selected!.Id); Assert.Equal(1, activations);
        button.Binding.Bind("g", "b"); button.OnPointerDown(pointer); button.OnPointerClick(pointer);
        Assert.Equal("b", state.Selected!.Id); Assert.Equal(2, activations);
        // The same single listener survives repeated recycling; release of a stale press is rejected.
        button.OnPointerDown(pointer); button.Binding.Bind("g", "a"); button.OnPointerClick(pointer);
        Assert.Equal("b", state.Selected!.Id); Assert.Equal(2, activations);
        button.OnSubmit(new UnityEngine.EventSystems.BaseEventData());
        Assert.Equal("a", state.Selected!.Id); Assert.Equal(3, activations);
        button.Binding.Bind("other-generation", "b"); button.OnSubmit(new UnityEngine.EventSystems.BaseEventData());
        Assert.Equal("a", state.Selected!.Id); Assert.Equal(3, activations);
    }

    [Theory]
    [InlineData("wheel")]
    [InlineData("drag")]
    [InlineData("disable")]
    [InlineData("right-button")]
    public void ScrollDragDisableAndSecondaryButtonDoNotActivateHistory(string operation)
    {
        var button = new RunsHistoryButton(); button.Configure(new UnityEngine.UI.Graphic());
        button.Binding.Bind("g", "r"); var clicks = 0; button.Clicked += () => clicks++;
        var data = new UnityEngine.EventSystems.PointerEventData(); button.OnPointerDown(data);
        if (operation == "wheel") button.Binding.CancelPointer();
        if (operation == "drag") data.dragging = true;
        if (operation == "disable") button.Disable();
        if (operation == "right-button") data.button = UnityEngine.EventSystems.PointerEventData.InputButton.Right;
        button.OnPointerClick(data); Assert.Equal(0, clicks);
    }

    [Theory]
    [InlineData("duckov:item:1", true, 1)]
    [InlineData("duckov:weapon:141", true, 141)]
    [InlineData("duckov:totem:792", true, 792)]
    [InlineData("mod:weapon:141", false, 0)]
    [InlineData("duckov:weapon:unknown", false, 0)]
    [InlineData("duckov:weapon:141:extra", false, 0)]
    [InlineData("duckov:weapon:-1", false, 0)]
    public void IconLookupAcceptsAllCapturedNativeIdentityKindsOnly(string id, bool expected, int typeId)
    {
        Assert.Equal(expected, NativeItemTypeIdPolicy.TryParse(id, out var parsed));
        if (expected) Assert.Equal(typeId, parsed);
    }

    [Fact]
    public void NativeMetadataMismatchFallbackAndFailureAreNotSuccessfulIcons()
    {
        var icon = new object(); var fallback = new object();
        Assert.Same(icon, NativeItemTypeIdPolicy.Resolve("duckov:weapon:141", id => (id, icon), fallback));
        Assert.Null(NativeItemTypeIdPolicy.Resolve("duckov:weapon:141", _ => (0, icon), fallback));
        Assert.Null(NativeItemTypeIdPolicy.Resolve("duckov:weapon:141", id => (id, fallback), fallback));
        Assert.Null(NativeItemTypeIdPolicy.Resolve<object>("duckov:weapon:141", _ => throw new IOException(), fallback));
        Assert.Null(NativeItemTypeIdPolicy.Resolve<object>("mod:weapon", _ => throw new InvalidOperationException(), fallback));
    }

    [Theory]
    [InlineData("duckov:weapon:356", true)]
    [InlineData("duckov:item:356", true)]
    [InlineData("duckov:totem:356", true)]
    [InlineData("duckov:weapon:357", false)]
    [InlineData("duckov:weapon:1356", false)]
    [InlineData("mod:weapon:356", false)]
    [InlineData("duckov:weapon:356:extra", false)]
    [InlineData("duckov:weapon:unknown", false)]
    public void InvisibleUnarmedUsesEmptyIconOnlyForExactNativeItemIdentity(string id, bool empty)
    {
        Assert.Equal(empty, NativeItemTypeIdPolicy.UseEmptyIcon(id));
        var queries = 0; var knife = new object();
        var result = NativeItemTypeIdPolicy.Resolve(id, typeId => { queries++; return (typeId, knife); }, null);
        if (empty)
        {
            Assert.Null(result);
            Assert.Equal(0, queries); // Never retrieve the player-invisible native weapon sprite.
        }
        else if (NativeItemTypeIdPolicy.TryParse(id, out _)) Assert.Same(knife, result);
    }

    [Fact]
    public void EquipmentOverlayHeightFitsOneThroughSixSlotsAtSupportedResolutions()
    {
        foreach (var (width, height) in new (float, float)[] { (1280, 720), (1680, 1050), (2560, 1440), (1024, 768) })
        {
            var transform = RetainedReferenceTransformPolicy.Create(width, height, 1);
            var shell = RetainedVisualLayoutPolicy.Create(transform);
            var scale = transform.CanvasLength(1);
            var availableHeight = (height - shell.Header.Top - shell.Header.Height) / scale - 110;
            var previousHeight = 0f;
            for (var slots = 1; slots <= 6; slots++)
            {
                // Allow wrapped item names: 60px name + 4px gap + 28px slot label.
                var contentHeight = 12 + slots * (92 + 16);
                var layout = RunsEvidenceLayout.Measure(availableHeight, 32, contentHeight);
                Assert.True(layout.Height > previousHeight);
                Assert.Equal(contentHeight, layout.ContentHeight);
                Assert.True(layout.Height <= availableHeight);
                Assert.Equal(96, layout.ContentTop);
                Assert.Equal(layout.Height - 16, layout.ContentTop + layout.ContentHeight);
                var cues = OverflowCuePolicy.Resolve(layout.ContentHeight, contentHeight, 0);
                Assert.False(cues.ShowLeading); Assert.False(cues.ShowTrailing);
                previousHeight = layout.Height;
            }
        }
    }

    [Fact]
    public void ExceptionallyLongEquipmentTextKeepsHeaderFixedAndBodyReachable()
    {
        var layout = RunsEvidenceLayout.Measure(900, 100, 2000);
        Assert.Equal(900, layout.Height);
        Assert.Equal(100, layout.HeaderHeight);
        Assert.Equal(132, layout.ContentTop);
        Assert.Equal(752, layout.ContentHeight);
        Assert.True(OverflowCuePolicy.Resolve(layout.ContentHeight, 2000, 0).ShowTrailing);
    }

    [Fact]
    public void EquipmentDetailsOpenOnlyForOccupiedItemsWithCapturedOrIncompleteSlots()
    {
        Assert.False(new RunSlotPresentation("empty", EquipmentSlotState.Empty, "", "Empty").CanOpenDetails);
        Assert.False(new RunSlotPresentation("body", EquipmentSlotState.Occupied, "duckov:item:1", "Armor").CanOpenDetails);
        Assert.True(new RunSlotPresentation("weapon", EquipmentSlotState.Occupied, "duckov:weapon:2", "Weapon",
            [EquipmentSlotState.Empty]).CanOpenDetails);
        Assert.True(new RunSlotPresentation("partial", EquipmentSlotState.Occupied, "duckov:weapon:2", "Weapon",
            nestedComplete: false).CanOpenDetails);
        Assert.False(new RunSlotPresentation("empty", EquipmentSlotState.Empty, "", "Empty",
            nestedComplete: false).CanOpenDetails);
    }

    [Fact]
    public void MissingEquipmentNamesDoNotFallBackToInternalIdentifiers()
    {
        var result = RunsPresentationFactory.PresentSlot(new TerminalRootSlot("weapon", "Weapon", EquipmentSlotState.Occupied,
            "duckov:weapon:141", "", EquipmentItemKind.Weapon, true,
            [new("internal/path", "optic", "Optic", EquipmentSlotState.Occupied, "duckov:item:52", "")]), UiText.Get);
        Assert.All(result.Evidence, row => Assert.Equal(UiText.Get("ui.unavailable"), row.ItemName));
        Assert.DoesNotContain("duckov:", result.Text);
        Assert.DoesNotContain("internal/path", result.Text);
        Assert.Equal("duckov:item:52", result.Evidence[1].ItemId);
    }

    [Fact]
    public void EquipmentOverlayKeepsCapturedRootAndAttachmentIconsPairedWithTheirOwnEvidence()
    {
        var nested = new List<TerminalNestedSlot>
        {
            new("weapon/optic", "optic", "Optic", EquipmentSlotState.Occupied, "duckov:item:52", "Captured optic"),
            new("weapon/optic/gem", "gem", "Gem", EquipmentSlotState.Empty, "", ""),
            new("weapon/muzzle", "muzzle", "Muzzle", EquipmentSlotState.Occupied, "mod:missing", "Missing icon")
        };
        var result = RunsPresentationFactory.PresentSlot(new TerminalRootSlot("weapon", "Weapon", EquipmentSlotState.Occupied,
            "duckov:weapon:141", "Captured weapon", EquipmentItemKind.Weapon, false, nested), UiText.Get);
        nested.Clear();
        Assert.Equal(4, result.Evidence.Count);
        Assert.Equal("duckov:weapon:141", result.Evidence[0].ItemId);
        Assert.Contains("Captured weapon", result.Evidence[0].ItemName);
        Assert.Equal("duckov:item:52", result.Evidence[1].ItemId);
        Assert.Contains("Captured optic", result.Evidence[1].ItemName);
        Assert.Equal("Optic", result.Evidence[1].SlotName);
        Assert.Equal("Gem", result.Evidence[2].SlotName);
        Assert.Equal("Empty", result.Evidence[2].ItemName);
        Assert.DoesNotContain("weapon/optic", result.Text);
        Assert.DoesNotContain("duckov:", result.Text);
        var lookups = new List<string>();
        var rootIcon = new object(); var attachmentIcon = new object();
        object? Resolve(string id) { lookups.Add(id); return id == "duckov:weapon:141" ? rootIcon : id == "duckov:item:52" ? attachmentIcon : null; }
        Assert.Same(rootIcon, RunsItemIconPolicy.Resolve(result.Evidence[0], Resolve));
        Assert.Same(attachmentIcon, RunsItemIconPolicy.Resolve(result.Evidence[1], Resolve));
        Assert.Null(RunsItemIconPolicy.Resolve(result.Evidence[2], Resolve));
        Assert.Null(RunsItemIconPolicy.Resolve(result.Evidence[3], Resolve));
        Assert.Equal(["duckov:weapon:141", "duckov:item:52", "mod:missing"], lookups);
        Assert.Contains("Missing icon", result.Evidence[3].ItemName);
        Assert.False(result.NestedComplete);
        Assert.Null(RunsItemIconPolicy.Resolve<object>(result.Evidence[1], _ => throw new IOException()));
        var supplied = result.Evidence.ToList();
        var copy = new RunSlotPresentation("weapon", result.State, result.ItemId, result.Text, evidence: supplied);
        supplied.Clear(); Assert.Equal(4, copy.Evidence.Count);
    }

    [Fact]
    public void AttachmentDotsRetainNativeOrderAndNeverPadIncompleteEvidenceWithEmptyDots()
    {
        var nested = new List<TerminalNestedSlot>
        {
            new("first", "first", "First", EquipmentSlotState.Occupied, "mod:optic", "Modded optic"),
            new("second", "second", "Second", EquipmentSlotState.Empty, "", "")
        };
        var slot = new TerminalRootSlot("weapon", "Weapon", EquipmentSlotState.Occupied, "duckov:weapon:141", "Weapon", EquipmentItemKind.Weapon, false, nested);
        var result = RunsPresentationFactory.PresentSlot(slot, UiText.Get); nested.Clear();
        Assert.Equal(2, result.Attachments.Count); Assert.Equal(EquipmentSlotState.Occupied, result.Attachments[0]);
        Assert.Equal(EquipmentSlotState.Empty, result.Attachments[1]); Assert.False(result.NestedComplete);
        Assert.Equal("mod:optic", result.Evidence[1].ItemId); Assert.Equal("duckov:weapon:141", result.ItemId);
        Assert.DoesNotContain("mod:optic", result.Text); Assert.DoesNotContain("duckov:weapon:141", result.Text);
        Assert.Contains("Additional attachment evidence unavailable", result.Text);
        var noSlots = new TerminalRootSlot("body", "Body", EquipmentSlotState.Occupied, "mod:body", "Body", EquipmentItemKind.Armor, true, []);
        Assert.Empty(RunsPresentationFactory.PresentSlot(noSlots, UiText.Get).Attachments);
    }

    [Fact]
    public void MeasuredDesktopMetadataSitsBesideBadgeAndWrapsInOrderOnlyWhenNecessary()
    {
        (float Width, float Height)[] controls = [(130, 34), (480, 28), (510, 28)];
        var desktop = RunsFlowLayout.Arrange(1500, controls);
        Assert.All(desktop, box => Assert.Equal(0, box.Y));
        Assert.Equal(142, desktop[1].X); Assert.Equal(634, desktop[2].X);
        var narrow = RunsFlowLayout.Arrange(540, controls);
        Assert.True(narrow[1].Y > narrow[0].Y); Assert.True(narrow[2].Y > narrow[1].Y);
        Assert.All(narrow, box => Assert.InRange(box.X + box.Width, 0, 540));
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1680, 1050)]
    [InlineData(2560, 1440)]
    [InlineData(1024, 768)]
    public void LowerViewportsFitReferenceLayoutAndHaveIndependentOverflow(float width, float height)
    {
        var transform = RetainedReferenceTransformPolicy.Create(width, height, 1);
        var shell = RetainedVisualLayoutPolicy.Create(transform);
        var scale = transform.CanvasLength(1);
        var referenceHeight = (height - shell.Header.Top - shell.Header.Height) / scale - 70;
        var stacked = RunsLayoutPolicy.Stack(width);
        var routeHeight = RunsLowerLayout.RouteHeight(stacked, referenceHeight, 360);
        var rightTop = RunsLowerLayout.EquipmentTop(stacked, 310, 360, routeHeight);
        var rightHeight = RunsLowerLayout.EquipmentHeight(stacked, referenceHeight, rightTop, 480);
        Assert.True(routeHeight > 0); Assert.True(rightHeight > 0);
        if (stacked) Assert.True(rightTop > 360 + routeHeight);
        else
        {
            Assert.Equal(310, rightTop);
            Assert.Equal(referenceHeight - 60, 360 + routeHeight, 3);
            Assert.Equal(referenceHeight - 60, rightTop + rightHeight, 3);
        }
        var routeAtBottom = OverflowCuePolicy.Resolve(routeHeight, 2000, 2000 - routeHeight);
        Assert.True(routeAtBottom.ShowLeading); Assert.False(routeAtBottom.ShowTrailing);
        var equipmentFits = OverflowCuePolicy.Resolve(rightHeight, Math.Min(480, rightHeight), 0);
        Assert.False(equipmentFits.ShowLeading); Assert.False(equipmentFits.ShowTrailing);
        // A route offset is not an input to equipment or fixed-header placement.
        Assert.Equal(rightTop, RunsLowerLayout.EquipmentTop(stacked, 310, 360, routeHeight));
    }

    [Fact]
    public void TenIconCardsStayCompactRegardlessOfIdentityLengthAndNestedEvidence()
    {
        Assert.Equal(5, RunsViewStyle.SlotColumns); Assert.Equal(90, RunsViewStyle.SlotSize(740));
        Assert.Equal(2, RunsViewStyle.SlotBorder); Assert.Equal(16, RunsViewStyle.SlotRadius);
        Assert.Equal((146, 152, 164), ((int)RunsViewStyle.BorderRed, (int)RunsViewStyle.BorderGreen, (int)RunsViewStyle.BorderBlue));
        for (var index = 0; index < 256; index++)
        {
            var dot = RunsViewStyle.AttachmentDot(90, 256, index);
            Assert.InRange(dot.X + dot.Size, 0, 90); Assert.InRange(dot.Y + dot.Size, 0, 90);
        }
        var slot = new RunSlotPresentation("long", EquipmentSlotState.Occupied, "mod:long", new string('界', 10000));
        Assert.Equal(10000, slot.Text.Length); Assert.Empty(slot.Attachments);
    }

    [Fact]
    public void NativeTypographyKeepsMutedLabelsSmallerAndUsesCultureAwareUppercase()
    {
        Assert.Equal(177, RunsViewStyle.Muted);
        Assert.True(RunsViewStyle.SegmentDetailSize < RunsViewStyle.SegmentTitleSize);
        Assert.True(RunsViewStyle.SummaryLabelSize < RunsViewStyle.SummaryValueSize);
        Assert.Equal("RANGED", RunsViewStyle.Uppercase("Ranged")); Assert.Equal("MELEE", RunsViewStyle.Uppercase("Melee"));
        Assert.Equal("界", RunsViewStyle.Uppercase("界"));
        var before = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("tr-TR");
            Assert.Equal("İ", RunsViewStyle.Uppercase("i"));
        }
        finally { System.Globalization.CultureInfo.CurrentCulture = before; }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OverflowContourReachesBothContainerSidesAndFollowsCornerRadius(bool top)
    {
        var points = RunsRoundedEdgePolicy.Points(700, 400, 20, top);
        Assert.Equal(0, points[0].X, 3); Assert.Equal(700, points[^1].X, 3);
        Assert.Equal(top ? 20 : 380, points[0].Y, 3); Assert.Equal(top ? 20 : 380, points[^1].Y, 3);
        Assert.Equal(top ? 0 : 400, points[8].Y, 3); Assert.Equal(top ? 0 : 400, points[9].Y, 3);
        Assert.All(points, point => Assert.InRange(point.Y, top ? 0 : 380, top ? 20 : 400));
    }

    [Fact]
    public void RetainedCompositionWiresPolicyToNativeControlsWithoutVisualConstructionGates()
    {
        // Source composition assertions cover the Unity-only wiring which cannot execute in net8.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "UltimateDuckovStatistics.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var ui = Path.Combine(directory.FullName, "src", "UltimateDuckovStatistics", "UI");
        var view = File.ReadAllText(Path.Combine(ui, "RetainedRunsView.cs"));
        var nativeControls = File.ReadAllText(Path.Combine(ui, "RunsNativeControls.cs"));
        Assert.Contains("viewport.gameObject.AddComponent<RectMask2D>()", view);
        Assert.Contains("RunsHistoryClipping.Attach(history.Scroll.viewport)", view);
        Assert.Contains("viewport.gameObject.AddComponent<Image>()", nativeControls);
        Assert.Contains("stencil.color = Color.white", nativeControls);
        Assert.Contains("stencil.raycastTarget = false", nativeControls);
        Assert.Contains("viewport.gameObject.AddComponent<Mask>().showMaskGraphic = false", nativeControls);
        Assert.Contains("row.Button.Configure(row.Background)", view);
        Assert.Contains("row.Button.Binding.Activate(selection)", view);
        Assert.Contains("control.Button.Binding.Bind(selection.Snapshot!.GenerationId, run.Id)", view);
        Assert.Contains("target.AddComponent<ButtonAnimation>()", view);
        Assert.Contains("graphic.raycastTarget = false", view); // Interaction tint cannot steal hits or replace orange base.
        Assert.Contains("control.Background.color = run.Id == selection.SelectedId", view);
        Assert.Contains("label.alignment = value.alignment = TextAlignmentOptions.Top", view);
        Assert.Contains("summary[i].Label.text", view); Assert.Contains("summary[i].Value.text", view);
        Assert.Contains("routeSummary.color = Muted", view); Assert.Contains("secondary.color = Muted", view);
        Assert.Contains("new ScrollRegion(fixedDetail, \"EquipmentCombatScroll\")", view);
        Assert.Contains("Text(fixedDetail, \"RunTitle\"", view);
        Assert.Contains("Text(equipmentCombat.Content, \"EquipmentHeading\"", view);
        Assert.DoesNotContain("RunDetailsScroll", view); Assert.DoesNotContain("Text(slot, \"Identity\"", view);
        Assert.Contains("RunsNativeScrollConfiguration.Apply(Scroll)", view);
        Assert.Contains("RunsOverflowEdge Edge", view);
        Assert.Contains("control.Button.onClick.AddListener(() => ShowEvidence(control))", view);
        Assert.DoesNotContain("AttachTooltip(equipmentCard)", view);
        Assert.DoesNotContain("equipmentCard.GetComponent<Button>()", view);
        Assert.DoesNotContain("ShowEvidence(null)", view);
        Assert.Contains("RunsItemIconPolicy.Resolve(captured, icons.ResolveAvailable)", view);
        Assert.Contains("row.Name.text = captured.ItemName", view);
        Assert.Contains("Node(evidencePanel, \"EquipmentHeaderIcon\")", view);
        Assert.Contains("Text(evidencePanel, \"EquipmentHeaderName\", 24)", view);
        Assert.Contains("evidenceItemName.text = rootItem.ItemName", view);
        Assert.Contains("var captured = rows[i + 1]", view);
        Assert.DoesNotContain("rootItem.SlotName", view);
        Assert.Contains("RunsEvidenceLayout.Measure(height - 40, titleHeight, y)", view);
        Assert.Contains("evidence.Scroll.vertical = y > layout.ContentHeight", view);
        Assert.Contains("row.Slot.text = RunsViewStyle.Uppercase(captured.SlotName)", view);
        Assert.Contains("Text(row, \"SlotName\", 20); slotName.color = Muted", view);
        Assert.Contains("Put(row.Slot, 80, nameHeight + 4, w - 144)", view);
        Assert.Contains("control.Button.interactable = item?.CanOpenDetails == true", view);
        Assert.Contains("if (item?.CanOpenDetails != true) return", view);
        Assert.DoesNotContain("evidenceText", view);
        Assert.Contains("Put(summary[i].Value, (i - start) * (cellWidth + 20), y, cellWidth)", view);
        Assert.Contains("Put(summary[i].Label, (i - start) * (cellWidth + 20), y + valueHeight + 4, cellWidth)", view);
        Assert.Contains("evidenceClose.onClick.AddListener(HideEvidence)", view);
        Assert.Contains("evidenceClose.onClick.RemoveAllListeners()", view);
        Assert.Contains("evidence.Dispose()", view);
        Assert.Contains("!focused.transform.IsChildOf(evidencePanel)", view);
        Assert.DoesNotContain("ValidateSurface", view);
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
