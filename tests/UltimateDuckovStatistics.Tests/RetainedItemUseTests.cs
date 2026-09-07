using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;
using UltimateDuckovStatistics.UI;

namespace UltimateDuckovStatistics.Tests;

#pragma warning disable CA1861
public sealed class RetainedItemUseTests
{
    private static ProfileDocument Profile(string generation = "g") => new()
    {
        GenerationId = generation, Statistics = new ProfileStatistics { SaveGenerationId = generation },
        Capabilities = new List<CapabilityRecord>
        {
            new() { AdapterId = "native-item-use", State = AdapterCapabilityState.Supported },
            new() { AdapterId = "native-healing-attribution", State = AdapterCapabilityState.Supported },
            new() { AdapterId = ThrowableUseObservation.CapabilityId, State = AdapterCapabilityState.Supported }
        }
    };
    private static StatisticsPanelProjection Project(ProfileDocument profile) => StatisticsPanelProjectionFactory.Create(profile,
        new EconomyMetricCapabilities(), new CraftingMetricCapabilities(), new WorldTimeMetricCapabilities());
    private static ItemUsePresentation Present(ProfileDocument profile) => ItemUsePresentationFactory.Create(Project(profile), profile.GenerationId)!;
    private static ItemUseRecorded Use(ProfileDocument profile, string id, long count = 1, double amount = 1,
        ConsumptionUnit unit = ConsumptionUnit.Item, CanonicalItemGroup group = CanonicalItemGroup.Healing,
        string name = "Same name", GameplayContext context = GameplayContext.Raid, params ItemEffectTag[] effects)
    {
        var value = new ItemUseRecorded { EventId = Guid.NewGuid().ToString("N"), ItemId = id, DisplayName = name,
            ActivationCount = count, AmountConsumed = amount, ConsumptionUnit = unit, Group = group,
            EffectTags = effects.ToList(), SaveGenerationId = profile.GenerationId, GameplayContext = context };
        ItemUseReducer.Apply(profile.Statistics, value); return value;
    }
    private static void Disable(ProfileDocument profile, string id) => profile.Capabilities.Single(cap => cap.AdapterId == id).State = AdapterCapabilityState.DisabledIncompatible;
    private static ItemUseDocument Document(Func<string, string>? text = null) => new(
        (value, width, size) => value.Split('\n').Sum(line => Math.Max(1, MathF.Ceiling(line.Length * size * .5f / Math.Max(1, width)))) * size,
        (value, size) => value.Length * size * .5f, text);
    private static ItemUseSelection Select(ItemUsePresentation presentation) { var result = new ItemUseSelection(); result.Refresh(presentation); return result; }

    [Fact]
    public void SuccessfulRaidUsesKeepUnitsAndActualHealingIndependent()
    {
        var profile = Profile();
        var value = Use(profile, "duckov:item:1", count: 5, amount: 15, unit: ConsumptionUnit.Durability, effects: ItemEffectTag.Healing);
        HealingReducer.Apply(profile.Statistics, new HealingApplied { EventId = "h", ApplicationId = "a", SourceItemUseEventId = value.EventId,
            ItemId = value.ItemId, SaveGenerationId = "g", GameplayContext = GameplayContext.Raid, ActualHealthRestored = 48.84 });
        Use(profile, "duckov:item:2", count: 30, amount: 99, context: GameplayContext.Base);
        var p = Present(profile); var item = Assert.Single(p.Items);
        Assert.Equal("5", p.Uses.Text); Assert.Equal("1", p.DifferentItems.Text); Assert.Equal("48.84", p.Health.Text);
        Assert.Equal("15 durability", item.Amount.Text); Assert.Equal("Healing", item.GroupName); Assert.Equal("Healing", item.Effects);
        Assert.Equal(5, p.Groups.Single(group => group.Group == CanonicalItemGroup.Healing).Count);
        Assert.All(p.Groups.Where(group => group.Group != CanonicalItemGroup.Healing), group => Assert.Equal("0", group.Uses.Text));
    }

    [Theory]
    [InlineData(180)]
    [InlineData(600)]
    public void ValueFirstStatisticsReserveWrappedValueBeforeTheirLabel(float width)
    {
        var document = Document();
        var row = new ItemUseRenderRow { Kind = ItemUseRowKind.Statistic, ValueFirst = true,
            Name = "AMOUNT USED", Value = "322.419 durability" };
        document.Add(row, 0, 0, width);
        Assert.True(row.NameTop >= row.ValueTop + row.ValueHeight);
        Assert.True(row.Height >= row.NameTop + row.NameHeight + 12);
    }

    [Fact]
    public void ExactIdsStaySeparateAndInt64CountsSortWithoutDoubleConversion()
    {
        var profile = Profile();
        Use(profile, "mod:item:a", 9_007_199_254_740_992L);
        Use(profile, "mod:item:z", 9_007_199_254_740_993L);
        Use(profile, "duckov:item:1", 4, name: "Same name");
        Use(profile, "duckov:item:2", 4, name: "Same name");
        var p = Present(profile);
        Assert.Equal(new[] { "mod:item:z", "mod:item:a", "duckov:item:1", "duckov:item:2" }, p.Items.Select(item => item.ItemId));
        Assert.Equal("9,007,199,254,740,993", p.Items[0].Uses.Text); Assert.Equal("4", p.DifferentItems.Text);
    }

    [Fact]
    public void UnknownNamesUseExactStableFallbackWithoutJoining()
    {
        var profile = Profile(); Use(profile, "mod:item:a", name: ""); Use(profile, "mod:item:b", name: " ");
        var p = Present(profile);
        Assert.Equal("Unknown / modded item [mod:item:a]", p.Items[0].Name);
        Assert.Equal("Unknown / modded item [mod:item:b]", p.Items[1].Name);
    }

    [Theory]
    [InlineData(0, ConsumptionUnit.StackUnit, "0 stack units", (int)ItemUseEvidence.Supported)]
    [InlineData(1, ConsumptionUnit.StackUnit, "1 stack unit", (int)ItemUseEvidence.Supported)]
    [InlineData(0, ConsumptionUnit.UnknownAmount, "Unavailable", (int)ItemUseEvidence.Unavailable)]
    public void ThrowableReleaseNeverImpliesItemConsumption(double amount, ConsumptionUnit unit, string expected, int evidence)
    {
        var profile = Profile(); Use(profile, "duckov:item:100", 3, amount, unit, CanonicalItemGroup.Special, effects: ItemEffectTag.Throwable);
        profile.Statistics.RunTotals.ItemStatistics.HistoricalUnavailable = true;
        var p = Present(profile); var item = Assert.Single(p.Items);
        Assert.Equal("3", item.Uses.Text); Assert.Equal(ItemUseEvidence.Supported, item.Uses.Evidence);
        Assert.Equal(expected, item.Amount.Text); Assert.Equal((ItemUseEvidence)evidence, item.Amount.Evidence);
        Assert.Equal("Special", item.GroupName); Assert.Equal("Throwable", item.Effects); Assert.Empty(p.Notice);
        Assert.DoesNotContain("Partial", item.Uses.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeReleaseObservationFlowsIntoPresentationWithoutInflatingRecoveredThrow()
    {
        var profile = Profile();
        var observation = ThrowableUseObservation.Begin(new ItemUseSnapshot { SaveGenerationId = "g", RunId = "run", ItemId = "duckov:item:17",
            DisplayName = "Recoverable", GameplayContext = GameplayContext.Raid, Stackable = true, StackCount = 1 }, true, true)!;
        observation.MarkReleased(); var value = observation.Complete("g", "run", null, 1, false, DateTime.UtcNow)!;
        Assert.True(ItemUseReducer.Apply(profile.Statistics, value));
        var item = Assert.Single(Present(profile).Items);
        Assert.Equal("1", item.Uses.Text); Assert.Equal("0 stack units", item.Amount.Text);
        Assert.Null(observation.Complete("g", "run", null, 0, true, DateTime.UtcNow));
    }

    [Fact]
    public void UnknownConsumptionDoesNotEraseObservedSiblingUnit()
    {
        var profile = Profile(); Use(profile, "x", amount: 3.5, unit: ConsumptionUnit.Durability); Use(profile, "x", amount: 0, unit: ConsumptionUnit.UnknownAmount);
        var item = Assert.Single(Present(profile).Items);
        Assert.Equal("2", item.Uses.Text); Assert.Equal("3.5 durability, Unavailable", item.Amount.Text);
        Assert.Equal(ItemUseEvidence.Partial, item.Amount.Evidence);
    }

    [Fact]
    public void MissingConsumptionIsUnavailableWhileExplicitZeroRemainsZero()
    {
        var profile = Profile(); Use(profile, "a", amount: 0); Use(profile, "b"); profile.Statistics.Items["b"].Totals.AmountsByUnit.Clear();
        var p = Present(profile);
        Assert.Equal("0 items", p.Items.Single(item => item.ItemId == "a").Amount.Text);
        Assert.Equal("Unavailable", p.Items.Single(item => item.ItemId == "b").Amount.Text);
    }

    [Theory]
    [InlineData("native-item-use")]
    [InlineData("native-healing-attribution")]
    [InlineData("throwable-releases")]
    public void CurrentFailureNeverBecomesAnEmptyZeroPage(string id)
    {
        var profile = Profile(); Disable(profile, id); var p = Present(profile);
        Assert.False(p.Empty); Assert.NotEmpty(p.Notice);
        Assert.Equal(ItemUseEvidence.Unavailable, id == "native-healing-attribution" ? p.Health.Evidence : p.Uses.Evidence);
    }

    [Fact]
    public void FreshSupportedProfileHasObservedEmptyState()
    {
        var p = Present(Profile()); Assert.True(p.Empty); Assert.Equal("0", p.Uses.Text); Assert.Equal("0", p.Health.Text);
        Assert.All(p.Groups, group => Assert.Equal(ItemUseEvidence.Supported, group.Uses.Evidence));
    }

    [Fact]
    public void MissingAndDuplicateCapabilitiesFailClosed()
    {
        var profile = Profile(); profile.Capabilities.RemoveAt(0);
        Assert.Equal(ItemUseEvidence.Unavailable, Present(profile).Uses.Evidence);
        profile = Profile(); profile.Capabilities.Add(new CapabilityRecord { AdapterId = "native-item-use", State = AdapterCapabilityState.Supported });
        Assert.Equal(ItemUseEvidence.Unavailable, Present(profile).Uses.Evidence);
    }

    [Fact]
    public void RepairedCurrentEvidenceIsQualifiedWithoutInventingZeros()
    {
        var profile = Profile(); Use(profile, "x", count: 4);
        profile.Statistics.RunTotals.ItemStatistics.WasRepairedFromInvalidState = true;
        var p = Present(profile);
        Assert.Equal("4 (Partial)", p.Uses.Text); Assert.Equal(ItemUseEvidence.Unavailable, p.Health.Evidence);
        Assert.Contains(UiText.Get("ui.item_use_repaired"), p.Notice, StringComparison.Ordinal);
        Assert.Equal(ItemUseEvidence.Unavailable, p.Groups.Single(group => group.Group == CanonicalItemGroup.Drink).Uses.Evidence);
        Assert.Equal(ItemUseEvidence.Partial, p.Items[0].Uses.Evidence);
    }

    [Fact]
    public void ThrowableFailureKeepsFoodAndHealthIndependent()
    {
        var profile = Profile(); Use(profile, "food", 4, group: CanonicalItemGroup.Food, effects: ItemEffectTag.Food);
        Use(profile, "throw", 2, group: CanonicalItemGroup.Special, effects: ItemEffectTag.Throwable);
        Disable(profile, ThrowableUseObservation.CapabilityId); var p = Present(profile);
        Assert.Equal(ItemUseEvidence.Partial, p.Uses.Evidence);
        Assert.Equal(ItemUseEvidence.Supported, p.Items.Single(item => item.ItemId == "food").Uses.Evidence);
        Assert.Equal(ItemUseEvidence.Partial, p.Items.Single(item => item.ItemId == "throw").Uses.Evidence);
        Assert.Equal(ItemUseEvidence.Supported, p.Groups.Single(group => group.Group == CanonicalItemGroup.Food).Uses.Evidence);
        Assert.Equal(ItemUseEvidence.Partial, p.Groups.Single(group => group.Group == CanonicalItemGroup.Special).Uses.Evidence);
        Assert.Equal(ItemUseEvidence.Supported, p.Health.Evidence);
    }

    [Fact]
    public void GenericUseFailureDoesNotHideSupportedThrowableUses()
    {
        var profile = Profile(); Use(profile, "throw", 2, group: CanonicalItemGroup.Special, effects: ItemEffectTag.Throwable);
        Disable(profile, "native-item-use"); var p = Present(profile);
        Assert.Equal("2", Assert.Single(p.Items).Uses.Text); Assert.Equal(ItemUseEvidence.Supported, p.Items[0].Uses.Evidence);
        Assert.Equal(ItemUseEvidence.Partial, p.Uses.Evidence);
    }

    [Fact]
    public void HealingFailureLeavesUseAndConsumptionAvailable()
    {
        var profile = Profile(); Use(profile, "x"); Disable(profile, "native-healing-attribution"); var p = Present(profile);
        var item = Assert.Single(p.Items);
        Assert.Equal(ItemUseEvidence.Supported, item.Uses.Evidence); Assert.Equal(ItemUseEvidence.Supported, item.Amount.Evidence);
        Assert.Equal(ItemUseEvidence.Unavailable, item.Health.Evidence);
    }

    [Fact]
    public void PrimaryGroupDoesNotDoubleCountMultiEffectItems()
    {
        var profile = Profile(); Use(profile, "x", 5, group: CanonicalItemGroup.Food, effects: new[] { ItemEffectTag.Drink, ItemEffectTag.Food, ItemEffectTag.Buff });
        var p = Present(profile); Assert.Equal(5, p.Groups.Sum(group => group.Count));
        Assert.Equal("Food, Drink, Buff", Assert.Single(p.Items).Effects);
        var selection = Select(p); selection.SelectFilter("g", CanonicalItemGroup.Drink); Assert.Single(selection.VisibleItems);
        selection.SelectFilter("g", CanonicalItemGroup.StimulantBuff); Assert.Single(selection.VisibleItems);
        selection.SelectFilter("g", CanonicalItemGroup.Food); Assert.Single(selection.VisibleItems);
    }

    [Theory]
    [InlineData(CanonicalItemGroup.Healing, ItemEffectTag.Healing)]
    [InlineData(CanonicalItemGroup.RemedyDebuffRemoval, ItemEffectTag.DebuffRemoval)]
    [InlineData(CanonicalItemGroup.Food, ItemEffectTag.Food)]
    [InlineData(CanonicalItemGroup.Drink, ItemEffectTag.Drink)]
    [InlineData(CanonicalItemGroup.StimulantBuff, ItemEffectTag.Buff)]
    [InlineData(CanonicalItemGroup.Special, ItemEffectTag.Special)]
    [InlineData(CanonicalItemGroup.Special, ItemEffectTag.Throwable)]
    public void EffectFiltersUseRecordedTagsIndependentlyOfLocalizedText(CanonicalItemGroup group, ItemEffectTag tag)
    {
        var profile = Profile();
        Use(profile, "effect", group: CanonicalItemGroup.OtherUnknown, effects: tag);
        Use(profile, "name-only", group: CanonicalItemGroup.OtherUnknown, name: "Buff Healing Food Drink");
        var p = ItemUsePresentationFactory.Create(Project(profile), "g", _ => "Localized label")!;
        var selection = Select(p); selection.SelectFilter("g", group);
        Assert.Equal("effect", Assert.Single(selection.VisibleItems).ItemId);
        Assert.Equal(2, p.Groups.Sum(value => value.Count));
        Assert.Equal(2, p.Groups.Single(value => value.Group == CanonicalItemGroup.OtherUnknown).Count);
    }

    [Fact]
    public void EffectFilterSnapshotIsDetachedFromLaterRecordedTagChanges()
    {
        var profile = Profile(); Use(profile, "effect", group: CanonicalItemGroup.Food, effects: ItemEffectTag.Buff);
        var selection = Select(Present(profile)); selection.SelectFilter("g", CanonicalItemGroup.StimulantBuff);
        profile.Statistics.Items["effect"].EffectTags.Clear();
        Assert.Single(selection.VisibleItems);
        selection.Refresh(Present(profile));
        Assert.Empty(selection.VisibleItems);
    }

    [Fact]
    public void SnapshotCopiesValuesAndRejectsDetachedOrWrongGenerationData()
    {
        var profile = Profile(); Use(profile, "x", 5); var projection = Project(profile);
        var p = ItemUsePresentationFactory.Create(projection, "g")!;
        Assert.Null(ItemUsePresentationFactory.Create(projection, "elsewhere"));
        profile.Statistics.Items["x"].Totals.ActivationCount = 999;
        Assert.Equal("5", p.Items[0].Uses.Text);
        projection.ItemUse = Project(Profile()).ItemUse; Assert.Null(ItemUsePresentationFactory.Create(projection, "g"));
        projection = Project(Profile()); projection.Profile = Profile(); Assert.Null(ItemUsePresentationFactory.Create(projection, "g"));
    }

    [Fact]
    public void RecentRunsUseTheirOwnExactRecordedItemsAndStableNavigation()
    {
        var profile = Profile(); var use = Use(profile, "x", 5);
        var run = new RunSummary { RunId = "exact", SaveGenerationId = "g", StartingMapId = "map:a", StartingMapDisplayName = "Map", StartingMapKnown = true,
            StartedUtc = new DateTime(2026, 9, 7, 1, 2, 3, DateTimeKind.Utc), EndedUtc = new DateTime(2026, 9, 7, 2, 0, 0, DateTimeKind.Utc) };
        ItemStatisticsAggregateReducer.Record(run.ItemStatistics, "g", use); profile.Statistics.Runs.Add(run);
        var p = ItemUsePresentationFactory.Create(Project(profile), "g", toLocal: value => value)!;
        var row = Assert.Single(p.RecentRuns);
        Assert.Equal("exact", row.RunId); Assert.Equal("5", Assert.Single(row.Items).Uses.Text); Assert.Contains("2026-09-07 - 01:02", row.Caption, StringComparison.Ordinal);
        Assert.True(p.CanRoute("g", "exact")); Assert.False(p.CanRoute("other", "exact")); Assert.False(p.CanRoute("g", "nearby"));
        run.ItemStatistics.Items["x"].DisplayName = "Changed"; Assert.Equal("Same name", row.Items[0].Name);
    }

    private static RunSummary RoutedRun(params string[] maps) => new()
    {
        RunId = "route", SaveGenerationId = "g", RouteCapabilities = RouteStatisticsReducer.Supported("native route"),
        Segments = maps.Select((map, index) => new MapSegmentSummary { SegmentId = "segment:" + index,
            SegmentIndex = index, MapKnown = true, MapId = "map:" + map, MapDisplayName = map }).ToList()
    };

    [Fact]
    public void ReturnVisitsCountDistinctMapsAndUseTheSameEndpointSummaryAsRuns()
    {
        var profile = Profile(); Use(profile, "x"); var run = RoutedRun("A", "B", "A"); profile.Statistics.Runs.Add(run);
        var projection = Project(profile); var row = Assert.Single(ItemUsePresentationFactory.Create(projection, "g")!.RecentRuns);
        var detail = Assert.Single(RunsPresentationFactory.Create(projection, "g")!.Runs);
        Assert.Equal("A", row.Title); Assert.Equal(detail.Title, row.Title);
        Assert.Contains(" · 2 maps · ", row.Caption, StringComparison.Ordinal); Assert.DoesNotContain("3 maps", row.Caption, StringComparison.Ordinal);
        Assert.StartsWith("Run 1 · ", row.Caption, StringComparison.Ordinal);
        run.Segments[2].MapId = "map:C"; run.Segments[2].MapDisplayName = "C";
        Assert.Equal("A - C", Assert.Single(Present(profile).RecentRuns).Title);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("repaired-route")]
    [InlineData("repaired-segment")]
    [InlineData("missing-route")]
    [InlineData("missing-segments")]
    public void UnknownOrIncompleteRoutesNeverClaimAnExactMapCount(string state)
    {
        var profile = Profile(); Use(profile, "x"); var run = RoutedRun("A", "B"); profile.Statistics.Runs.Add(run);
        if (state == "unknown") { run.Segments[1].MapKnown = false; run.Segments[1].MapId = MapIdentity.UnknownId; }
        else if (state == "repaired-route") run.RouteWasRepairedFromInvalidState = true;
        else if (state == "repaired-segment") run.Segments[1].WasRepairedFromInvalidState = true;
        else if (state == "missing-route") run.RouteCapabilities.OrderedRoute.State = AdapterCapabilityState.DisabledIncompatible;
        else run.RouteCapabilities.Segments.State = AdapterCapabilityState.DisabledIncompatible;
        var row = Assert.Single(Present(profile).RecentRuns);
        Assert.Contains(" · Unavailable · ", row.Caption, StringComparison.Ordinal);
        Assert.DoesNotContain("2 maps", row.Caption, StringComparison.Ordinal);
        if (state == "unknown") Assert.Equal("A - " + UiText.Get("ui.overview_latest_run_unknown_map"), row.Title);
    }

    [Fact]
    public void RecentWindowUsesFullHistoryRunOrdinalsWithExactIdTieBreaks()
    {
        var profile = Profile(); Use(profile, "x");
        for (var i = 0; i < 15; i++) profile.Statistics.Runs.Add(new RunSummary {
            RunId = "run:" + i.ToString("D2", System.Globalization.CultureInfo.InvariantCulture), SaveGenerationId = "g",
            StartedUtc = new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Utc).AddMinutes(i / 2),
            EndedUtc = new DateTime(2026, 9, 7, 1, 0, 0, DateTimeKind.Utc).AddMinutes(i) });
        var projection = Project(profile); var items = ItemUsePresentationFactory.Create(projection, "g")!;
        var runs = RunsPresentationFactory.Create(projection, "g")!;
        Assert.Equal(12, items.RecentRuns.Count); Assert.Equal("run:14", items.RecentRuns[0].RunId);
        Assert.StartsWith("Run 15 · ", items.RecentRuns[0].Caption, StringComparison.Ordinal);
        Assert.All(items.RecentRuns, row => Assert.StartsWith(runs.Runs.Single(run => run.Id == row.RunId).Metadata + " · ", row.Caption, StringComparison.Ordinal));
    }

    [Fact]
    public void EmptyRunDistinguishesRecordedEmptyFromAbsentHistory()
    {
        var profile = Profile(); Use(profile, "x");
        profile.Statistics.Runs.Add(new RunSummary { RunId = "current", SaveGenerationId = "g" });
        profile.Statistics.Runs.Add(new RunSummary { RunId = "absent", SaveGenerationId = "g", ItemStatistics = new ItemStatisticsAggregate { HistoricalUnavailable = true } });
        var p = Present(profile);
        Assert.Equal(UiText.Get("ui.item_use_no_run_uses"), p.RecentRuns.Single(run => run.RunId == "current").EmptyText);
        Assert.Equal(UiText.Get("ui.item_use_run_unavailable"), p.RecentRuns.Single(run => run.RunId == "absent").EmptyText);
    }

    [Fact]
    public void SelectionPreservesExpansionByExactIdAndClearsOnGenerationLoss()
    {
        var profile = Profile(); Use(profile, "a"); Use(profile, "b", group: CanonicalItemGroup.Food); var selection = Select(Present(profile));
        Assert.True(selection.Expanded("item:a")); Assert.True(selection.Toggle("g", "item:b"));
        Assert.True(selection.SelectFilter("g", CanonicalItemGroup.Food)); selection.Capture("left", 750);
        selection.Refresh(Present(profile)); Assert.True(selection.Expanded("item:b")); Assert.Equal(CanonicalItemGroup.Food, selection.Filter);
        Assert.Equal(500, selection.Offset("left", 100, 600));
        Assert.False(selection.Toggle("stale", "item:b")); Assert.False(selection.Toggle("g", "item:no"));
        selection.Refresh(null); Assert.Null(selection.Snapshot); Assert.Null(selection.Filter); Assert.False(selection.Expanded("item:b"));
        selection.Refresh(Present(Profile("next"))); Assert.Equal(0, selection.Offset("left", 100, 600));
    }

    [Fact]
    public void FilterOffsetsStayIndependentAndRejectInvalidGroups()
    {
        var selection = Select(Present(Profile())); selection.Capture("left", 100); selection.Capture("right", 200);
        Assert.True(selection.SelectFilter("g", CanonicalItemGroup.Drink)); Assert.Equal(0, selection.Offset("left", 100, 1000));
        selection.Capture("left", 300); Assert.Equal(200, selection.Offset("right", 100, 1000));
        Assert.True(selection.SelectFilter("g", null)); Assert.Equal(100, selection.Offset("left", 100, 1000));
        Assert.False(selection.SelectFilter("g", (CanonicalItemGroup)999)); Assert.Null(selection.Filter);
    }

    [Theory]
    [InlineData(1176)]
    [InlineData(600)]
    [InlineData(320)]
    public void ItemDetailsAndFiltersWrapWithinMeasuredWidth(float width)
    {
        var profile = Profile(); Use(profile, "duckov:item:356", name: new string('W', 180), effects: new[] { ItemEffectTag.Healing, ItemEffectTag.Food, ItemEffectTag.Drink });
        var selection = Select(Present(profile)); var document = Document(); document.Left(selection, width);
        var item = Assert.Single(document.Rows, row => row.Kind == ItemUseRowKind.Item); Assert.True(item.EmptyIcon); Assert.Equal("—", item.IconFallback);
        Assert.Equal(8, document.Rows.Count(row => row.Kind == ItemUseRowKind.Filter));
        Assert.Equal(7, document.Rows.Count(row => row.Kind == ItemUseRowKind.Statistic));
        Assert.All(document.Rows, row => { Assert.True(row.X >= 0); Assert.True(row.X + row.Width <= width + .01f); Assert.True(row.Height > 0); });
        Assert.All(document.Rows.Where(row => row.Kind == ItemUseRowKind.Filter), row => Assert.True(row.NameWidth > 0));
        Assert.Contains(document.Surfaces, surface => surface.Radius == 10 && surface.Height > item.Height);
    }

    [Fact]
    public void ExpandedDetailsUseTwoRowsAndKeepNextItemGap()
    {
        var profile = Profile(); Use(profile, "a", 2); Use(profile, "b"); var selection = Select(Present(profile));
        var document = Document(); document.Left(selection, 1176);
        var metrics = document.Rows.Where(row => row.Kind == ItemUseRowKind.Statistic).Skip(3).ToArray();
        Assert.Equal(metrics[0].Y, metrics[1].Y); Assert.Equal(metrics[2].Y, metrics[3].Y); Assert.True(metrics[2].Y > metrics[0].Y);
        var surface = document.Surfaces.Single(box => box.Radius == 10); var next = document.Rows.Single(row => row.Id == "item:b");
        Assert.Equal(surface.Y + surface.Height + 10, next.Y);
        selection.Toggle("g", "item:a"); document = Document(); document.Left(selection, 1176);
        Assert.Equal(3, document.Rows.Count(row => row.Kind == ItemUseRowKind.Statistic));
        Assert.DoesNotContain(document.Surfaces, box => box.Radius == 10);
    }

    [Fact]
    public void NoMatchingFilterKeepsKpisAndOtherColumnSections()
    {
        var profile = Profile(); Use(profile, "a"); var selection = Select(Present(profile)); selection.SelectFilter("g", CanonicalItemGroup.Drink);
        var left = Document(); left.Left(selection, 1176); var right = Document(); right.Right(selection, 1176);
        Assert.DoesNotContain(left.Rows, row => row.Kind == ItemUseRowKind.Item);
        Assert.Contains(left.Rows, row => row.Name == UiText.Get("ui.item_use_filter_empty"));
        Assert.Equal(3, left.Rows.Count(row => row.Kind == ItemUseRowKind.Statistic));
        Assert.Equal(7, right.Rows.Count(row => row.Kind == ItemUseRowKind.Group));
        Assert.Contains(right.Rows, row => row.Name == UiText.Get("ui.recent_runs"));
    }

    [Fact]
    public void SingularUnitsAndRunHealingCaptionsMatchTheMeasuredFacts()
    {
        var profile = Profile(); var use = Use(profile, "a");
        var run = new RunSummary { HealingCaptureComplete = true, RunId = "r", SaveGenerationId = "g" };
        ItemStatisticsAggregateReducer.Record(run.ItemStatistics, "g", use); profile.Statistics.Runs.Add(run);
        var p = Present(profile); Assert.Equal("1 item", p.Items[0].Amount.Text);
        var document = Document(); document.Right(Select(p), 1176);
        var item = Assert.Single(document.Rows, row => row.Kind == ItemUseRowKind.Item);
        Assert.Equal("1 use", item.Value); Assert.Empty(item.Caption);
        Disable(profile, "native-healing-attribution"); document = Document(); document.Right(Select(Present(profile)), 1176);
        Assert.Contains("Unavailable", Assert.Single(document.Rows, row => row.Kind == ItemUseRowKind.Item).Caption, StringComparison.Ordinal);
    }

    [Fact]
    public void RecentRunDocumentHasNativeBadgeExactRouteAndReadOnlyDetails()
    {
        var profile = Profile(); var use = Use(profile, "a");
        var run = new RunSummary { RunId = "r", SaveGenerationId = "g", StartingMapId = "m", StartingMapDisplayName = new string('W', 120), StartingMapKnown = true };
        ItemStatisticsAggregateReducer.Record(run.ItemStatistics, "g", use); profile.Statistics.Runs.Add(run);
        var document = Document(); document.Right(Select(Present(profile)), 1176);
        var header = document.Rows.Single(row => row.Id == "run:r"); var route = document.Rows.Single(row => row.Id == "route:r");
        Assert.Equal(RetainedRunBadgeState.Extracted, header.Outcome); Assert.True(header.Expandable); Assert.True(route.Actionable);
        Assert.True(route.X + route.Width < header.X + header.Width);
        Assert.True(route.Y >= header.Y + header.Height);
        Assert.Equal(header.NameTop + header.NameHeight / 2, header.BadgeTop + header.BadgeHeight / 2, precision: 3);
        Assert.All(document.Rows.Where(row => row.Kind == ItemUseRowKind.Item), row => Assert.True(route.Y >= row.Y + row.Height));
        Assert.All(document.Rows.Where(row => row.Kind == ItemUseRowKind.Item), row => Assert.False(row.Actionable));
    }

    [Fact]
    public void LargeInventoryHasBoundedVisibleRowsAtEitherEnd()
    {
        var profile = Profile();
        for (var i = 0; i < 2500; i++) Use(profile, "mod:item:" + i.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var document = Document(); document.Left(Select(Present(profile)), 1176);
        Assert.InRange(document.Visible(0, 800).Count, 1, 40);
        var end = document.Visible(document.Height - 800, 800); Assert.InRange(end.Count, 1, 20);
        Assert.Contains(document.Rows.Count - 1, end);
        Assert.True(document.Height > 250_000);
    }

    [Theory]
    [InlineData(2560, false)]
    [InlineData(1920, false)]
    [InlineData(1024, true)]
    public void ResponsiveColumnsUseBoundedLeftFirstStacking(float pixels, bool stacked)
    {
        Assert.Equal(stacked, CombatLayoutPolicy.Stack(pixels));
        Assert.Equal(stacked ? 2392 : 1176, ItemUseLayoutPolicy.ColumnWidth(2392, stacked));
        Assert.InRange(ItemUseLayoutPolicy.BoundedHeight(stacked, 900, 10000), 1, 900);
        Assert.Equal(stacked ? 200 : 900, ItemUseLayoutPolicy.BoundedHeight(stacked, 900, 200));
    }
}
#pragma warning restore CA1861
