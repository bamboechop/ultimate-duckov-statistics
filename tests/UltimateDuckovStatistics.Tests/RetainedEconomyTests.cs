using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.UI;

namespace UltimateDuckovStatistics.Tests;

#pragma warning disable CA1861
public sealed class RetainedEconomyTests
{
    private static readonly DateTime Now = new(2026, 9, 6, 12, 34, 56, DateTimeKind.Utc);
    private static MetricAvailability Supported() => new() { State = AdapterCapabilityState.Supported };
    private static EconomyMetricCapabilities Capabilities() => new()
    {
        MoneyAmountDirection = Supported(),
        MoneySourceAttribution = Supported(),
        MoneyContextAttribution = Supported(),
        CashAmountDirection = Supported(),
        CashExternalAcquisition = Supported(),
        CashContextAttribution = Supported(),
        RouteAttribution = Supported()
    };
    private static ProfileDocument Profile() => new()
    {
        GenerationId = "g",
        Statistics = new ProfileStatistics
        {
            SaveGenerationId = "g",
            Economy = new EconomyStatisticsAggregate { Capabilities = Capabilities() },
            Holdings = new EconomyHoldingsSnapshot { SaveGenerationId = "g", Capabilities = new() { Money = Supported(), Cash = Supported(), LiquidWealth = Supported() } }
        }
    };
    private static StatisticsPanelProjection Projection(ProfileDocument? profile = null, EconomyMetricCapabilities? current = null) =>
        StatisticsPanelProjectionFactory.Create(profile ?? Profile(), current ?? Capabilities(), new(), new());
    private static EconomyPresentation Present(ProfileDocument? profile = null, EconomyMetricCapabilities? current = null) =>
        EconomyPresentationFactory.Create(Projection(profile, current), "g")!;
    private static void Record(EconomyStatisticsAggregate aggregate, CurrencyKind currency, long amount, bool outflow = false,
        CurrencySourceCategory source = CurrencySourceCategory.UnknownAdjustment, GameplayContext context = GameplayContext.Base,
        bool acquired = false, long sequence = 1)
    {
        Assert.True(EconomyStatisticsReducer.Record(aggregate, "g", new CurrencyFlowRecorded
        {
            EventId = "event:" + sequence,
            SaveGenerationId = "g",
            TimestampUtc = Now,
            ProducerActivationId = "activation",
            ProducerSequence = sequence,
            RunId = context == GameplayContext.Raid ? "run" : null,
            Currency = currency,
            Amount = amount,
            Direction = outflow ? CurrencyFlowDirection.Outflow : CurrencyFlowDirection.Inflow,
            Source = source,
            GameplayContext = context,
            ProvenExternalRaidAcquisition = acquired
        }));
    }
    private static RunSummary Run(string id, int index = 0) => new()
    {
        RunId = id,
        SaveGenerationId = "g",
        StartedUtc = Now.AddMinutes(index),
        EndedUtc = Now.AddMinutes(index + 1),
        Outcome = RunOutcome.Extracted,
        MapId = "map:" + id,
        MapDisplayName = "Map " + id,
        MapKnown = true
    };
    private static float Measure(string value, float width, float size) => Math.Max(1, MathF.Ceiling(value.Length * size * .5f / Math.Max(1, width))) * size * 1.2f;
    private static float MeasureWidth(string value, float size) => value.Length * size * .5f;
    private static EconomyDocument Primary(EconomyPresentation p, float width = 1550, bool stacked = false)
    { var result = new EconomyDocument(Measure, MeasureWidth); result.Primary(p, width, stacked); return result; }
    private static EconomyDocument Recent(EconomySelection selection, float width = 780)
    { var result = new EconomyDocument(Measure, MeasureWidth); result.Recent(selection, width); return result; }

    [Fact]
    public void FactoryRejectsLostAndMixedGeneration()
    {
        var p = Projection(); Assert.NotNull(EconomyPresentationFactory.Create(p, "g"));
        Assert.Null(EconomyPresentationFactory.Create(p, "other"));
        p.Profile.Statistics.SaveGenerationId = "other"; Assert.Null(EconomyPresentationFactory.Create(p, "g"));
    }
    [Fact]
    public void ExperimentalSourcesRemainLimitedInsteadOfClaimingDisabledTracking()
    {
        var profile = Profile(); var current = Capabilities();
        Record(profile.Statistics.Economy, CurrencyKind.Money, 17);
        current.MoneySourceAttribution.State = AdapterCapabilityState.Experimental;
        var flow = EconomyPresentationFactory.Flow(profile.Statistics.Economy, CurrencyKind.Money, current, key => key);
        Assert.Equal("ui.economy_current_limited", flow.SourceNotice);
        Assert.Equal(17, flow.Totals.Inflow);
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void ReplacedPublicationMemberFailsBinding(int member)
    {
        var p = Projection();
        switch (member)
        {
            case 0: p.Profile = Profile(); break;
            case 1: p.Economy = new(); break;
            case 2: p.Holdings = new(); break;
            case 3: p.CurrentEconomyCapabilities = new(); break;
            case 4: p.RecentEconomyRuns = new List<RunSummary>(); break;
            case 5: p.Runs = new(); break;
            case 6: p.Holdings.Money = new(); break;
            case 7: p.Economy.Currencies = new(); break;
        }
        Assert.Null(EconomyPresentationFactory.Create(p, "g"));
    }
    [Fact]
    public void RecentRunIdentityMustBelongToExactPublication()
    {
        var profile = Profile(); profile.Statistics.Runs.Add(Run("a")); profile.Statistics.Runs.Add(Run("a", 1));
        Assert.Null(EconomyPresentationFactory.Create(Projection(profile), "g"));
        profile.Statistics.Runs.RemoveAt(1); profile.Statistics.Runs[0].SaveGenerationId = "other";
        Assert.Null(EconomyPresentationFactory.Create(Projection(profile), "g"));
    }
    [Fact]
    public void CurrentZeroHoldingsRemainCurrentAndComparable()
    {
        var p = Profile(); EconomyHoldingsReducer.Apply(p.Statistics.Holdings, new EconomyHoldingsMutation("g", Now, 0, 0, "test"));
        var result = Present(p);
        Assert.All(result.Holdings, holding => { Assert.Equal(0, holding.Value); Assert.Equal(EconomyHoldingObservationState.Current, holding.State); });
        Assert.Equal("Current ATM balance", result.Holdings[1].Caption);
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LastObservedComponentRetainsValueIndependentlyAndInvalidatesCombinedWealth(bool money)
    {
        var p = Profile(); EconomyHoldingsReducer.Apply(p.Statistics.Holdings, new EconomyHoldingsMutation("g", Now, 100, 25, "test"));
        EconomyHoldingsReducer.MarkNotCurrent(p.Statistics.Holdings, "g", money, !money, "transition");
        var result = Present(p); var stale = result.Holdings[money ? 1 : 2]; var sibling = result.Holdings[money ? 2 : 1];
        Assert.Equal(EconomyHoldingObservationState.LastObserved, stale.State); Assert.Contains("Last observed:", stale.Caption);
        Assert.Contains(Now.ToLocalTime().ToString("yyyy-MM-dd - HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture), stale.Caption);
        Assert.Equal(EconomyHoldingObservationState.Current, sibling.State); Assert.NotNull(sibling.Value);
        Assert.Null(result.Holdings[0].Value); Assert.Equal(EconomyHoldingObservationState.Unavailable, result.Holdings[0].State);
    }
    [Fact]
    public void MissingCashDoesNotEraseCurrentMoneyOrUseFlowsForHoldings()
    {
        var p = Profile(); EconomyHoldingsReducer.Apply(p.Statistics.Holdings, new EconomyHoldingsMutation("g", Now, 125, null, "test"));
        Record(p.Statistics.Economy, CurrencyKind.Cash, 999);
        var result = Present(p); Assert.Equal(125, result.Holdings[1].Value); Assert.Null(result.Holdings[2].Value); Assert.Null(result.Holdings[0].Value);
        Assert.Equal(999, result.Cash.Totals.Net);
    }
    [Fact]
    public void UnsupportedComparabilityAndOverflowLeaveBothComponentsVisible()
    {
        var p = Profile(); EconomyHoldingsReducer.Apply(p.Statistics.Holdings, new EconomyHoldingsMutation("g", Now, long.MaxValue, 1, "test"));
        var result = Present(p); Assert.Null(result.Holdings[0].Value); Assert.Equal(long.MaxValue, result.Holdings[1].Value); Assert.Equal(1, result.Holdings[2].Value);
        p.Statistics.Holdings.Money.Value = 100; p.Statistics.Holdings.Capabilities.LiquidWealth.State = AdapterCapabilityState.DisabledIncompatible;
        result = Present(p); Assert.Null(result.Holdings[0].Value); Assert.Equal(100, result.Holdings[1].Value); Assert.Equal(1, result.Holdings[2].Value);
    }
    [Fact]
    public void FlowGrossAndNetValuesStaySeparateAcrossCurrencies()
    {
        var p = Profile(); var a = p.Statistics.Economy;
        Record(a, CurrencyKind.Money, 1800, source: CurrencySourceCategory.Sale, context: GameplayContext.Shop);
        Record(a, CurrencyKind.Money, 500, source: CurrencySourceCategory.Reward, context: GameplayContext.Reward, sequence: 2);
        Record(a, CurrencyKind.Money, 206, true, sequence: 3);
        Record(a, CurrencyKind.Cash, 1000, source: CurrencySourceCategory.LootOrPickup, context: GameplayContext.Raid, acquired: true, sequence: 4);
        Record(a, CurrencyKind.Cash, 1436, context: GameplayContext.Raid, sequence: 5);
        Record(a, CurrencyKind.Cash, 12180, true, sequence: 6);
        var result = Present(p);
        Assert.Equal(2300, result.Money.Totals.Inflow); Assert.Equal(206, result.Money.Totals.Outflow); Assert.Equal(2094, result.Money.Totals.Net);
        Assert.Equal(2436, result.Cash.Totals.Inflow); Assert.Equal(12180, result.Cash.Totals.Outflow); Assert.Equal(-9744, result.Cash.Totals.Net);
        Assert.Equal(1000, result.Cash.ProvenRaidAcquired); Assert.Null(result.Money.ProvenRaidAcquired);
    }
    [Fact]
    public void SupportedNoChangesIsZeroButUnavailableCurrencyDoesNotInventZero()
    {
        var current = Capabilities(); current.CashAmountDirection.State = AdapterCapabilityState.DisabledIncompatible;
        var result = Present(current: current);
        Assert.Equal(0, result.Money.Totals.Net); Assert.Null(result.Cash.Totals.Net);
        Assert.Equal("", result.Money.Notice); Assert.Contains("unavailable", result.Cash.Notice, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void NetZeroWithRecordedChangesSurvivesCurrentFailure()
    {
        var p = Profile(); Record(p.Statistics.Economy, CurrencyKind.Money, 50); Record(p.Statistics.Economy, CurrencyKind.Money, 50, true, sequence: 2);
        var current = Capabilities(); current.MoneyAmountDirection.State = AdapterCapabilityState.DisabledIncompatible;
        var result = Present(p, current);
        Assert.Equal(50, result.Money.Totals.Inflow); Assert.Equal(50, result.Money.Totals.Outflow); Assert.Equal(0, result.Money.Totals.Net);
        Assert.NotEmpty(result.Money.Notice); Assert.Equal(0, result.Cash.Totals.Net); Assert.Empty(result.Cash.Notice);
    }
    [Fact]
    public void MissingEarlierEvidenceDoesNotBecomeZeroOrAddDevelopmentHistoryBoilerplate()
    {
        var p = Profile(); p.Statistics.Economy.HistoricalUnavailable = true; Record(p.Statistics.Economy, CurrencyKind.Money, 20);
        var result = Present(p); Assert.Equal(20, result.Money.Totals.Net); Assert.Null(result.Cash.Totals.Net);
        Assert.Empty(result.Money.Notice);
        Assert.DoesNotContain(Primary(result).Elements, e => e.Text.Contains("earlier", StringComparison.OrdinalIgnoreCase));
    }
    [Fact]
    public void ActualPartialEvidenceRemainsQualified()
    {
        var p = Profile(); Record(p.Statistics.Economy, CurrencyKind.Money, 20); p.Statistics.Economy.MoneyArithmeticSaturated = true;
        var result = Present(p); Assert.Equal(20, result.Money.Totals.Net); Assert.Contains("incomplete", result.Money.Notice);
        Assert.Equal("", result.Cash.Notice);
    }
    [Fact]
    public void SourceFailureDoesNotEraseAmountOrContexts()
    {
        var p = Profile(); Record(p.Statistics.Economy, CurrencyKind.Money, 100, source: CurrencySourceCategory.Sale, context: GameplayContext.Shop);
        var current = Capabilities(); current.MoneySourceAttribution.State = AdapterCapabilityState.DisabledIncompatible;
        var result = Present(p, current); Assert.Equal(100, result.Money.Totals.Net); Assert.Empty(result.Money.Notice);
        Assert.Equal(100, result.Money.Sources.Single().Net); Assert.NotEmpty(result.Money.SourceNotice);
        Assert.Equal(100, result.Money.Contexts.Single().Net); Assert.Empty(result.Money.ContextNotice);
    }
    [Fact]
    public void SourcesAndContextsHaveDeterministicSemanticOrderWithoutJoiningNames()
    {
        var p = Profile(); var a = p.Statistics.Economy;
        Record(a, CurrencyKind.Money, 1, source: CurrencySourceCategory.UnknownAdjustment, context: GameplayContext.Reward);
        Record(a, CurrencyKind.Money, 2, source: CurrencySourceCategory.Reward, context: GameplayContext.Shop, sequence: 2);
        Record(a, CurrencyKind.Money, 3, source: CurrencySourceCategory.Sale, context: GameplayContext.Base, sequence: 3);
        var result = Present(p);
        Assert.Equal(new[] { "Sale", "Reward", "UnknownAdjustment" }, result.Money.Sources.Select(r => r.Id));
        Assert.Equal(new[] { "Base", "Shop", "Reward" }, result.Money.Contexts.Select(r => r.Id));
        Assert.Equal(new long?[] { 3, 2, 1 }, result.Money.Sources.Select(r => r.Net));
    }
    [Fact]
    public void RaidAcquisitionIsOnlyAnInflowSubordinateAndNeverAnotherNet()
    {
        var p = Profile(); Record(p.Statistics.Economy, CurrencyKind.Cash, 100, source: CurrencySourceCategory.LootOrPickup, context: GameplayContext.Raid, acquired: true);
        var d = Primary(Present(p));
        var label = Assert.Single(d.Elements, e => e.Id == "Cash:contexts:acquired:label");
        var value = Assert.Single(d.Elements, e => e.Id == "Cash:contexts:acquired:value");
        var raid = Assert.Single(d.Elements, e => e.Id == "Cash:contexts:Raid:label");
        Assert.True(label.Y >= raid.Y + raid.Height); Assert.True(label.X > raid.X); Assert.Equal("+100", value.Text);
        Assert.DoesNotContain(d.Elements, e => e.Id.StartsWith("Cash:contexts:acquired", StringComparison.Ordinal) && e.Text.Contains("net", StringComparison.OrdinalIgnoreCase));
    }
    [Fact]
    public void AcquisitionDoesNotReconstructMissingRaidContext()
    {
        var a = new EconomyStatisticsAggregate(); a.CashAcquired = 10;
        var result = EconomyPresentationFactory.Flow(a, CurrencyKind.Cash, new());
        Assert.Equal(10, result.ProvenRaidAcquired); Assert.Null(result.Contexts.Single().Inflow);
    }
    [Fact]
    public void RecentRunsRetainExactIdentityAndOwnValuesEvenWithSameDisplayNames()
    {
        var p = Profile(); var older = Run("a"); var newest = Run("b", 1); older.MapDisplayName = newest.MapDisplayName = "Same map name";
        Record(older.Economy, CurrencyKind.Money, 10); Record(newest.Economy, CurrencyKind.Money, 25);
        p.Statistics.Runs.Add(older); p.Statistics.Runs.Add(newest);
        var result = Present(p); Assert.Equal(new[] { "b", "a" }, result.RecentRuns.Select(r => r.RunId));
        Assert.Equal(new long?[] { 25, 10 }, result.RecentRuns.Select(r => r.Money.Totals.Net));
        Assert.True(result.CanRoute("g", "a")); Assert.False(result.CanRoute("other", "a")); Assert.False(result.CanRoute("g", "missing"));
        Assert.StartsWith("Run 2", result.RecentRuns[0].Metadata); Assert.StartsWith("Run 1", result.RecentRuns[1].Metadata);
    }
    [Fact]
    public void PresentationIsDetachedFromRecordedDictionariesAndHoldings()
    {
        var p = Profile(); Record(p.Statistics.Economy, CurrencyKind.Money, 10);
        EconomyHoldingsReducer.Apply(p.Statistics.Holdings, new EconomyHoldingsMutation("g", Now, 50, 5, "test"));
        var run = Run("a"); p.Statistics.Runs.Add(run); var result = Present(p);
        p.Statistics.Economy.Currencies["Money"].Totals.GrossInflow = 999; p.Statistics.Economy.Currencies["Money"].Sources.Clear();
        p.Statistics.Holdings.Money.Value = 500; run.MapDisplayName = "Changed";
        Assert.Equal(10, result.Money.Totals.Inflow); Assert.Single(result.Money.Sources); Assert.Equal(50, result.Holdings[1].Value);
        Assert.Equal("Map a", result.RecentRuns[0].Title);
    }
    [Fact]
    public void RecentRunPublicationRetainsExistingBound()
    {
        var p = Profile(); for (var i = 0; i < 100; i++) p.Statistics.Runs.Add(Run("run-" + i, i));
        var result = Present(p); Assert.Equal(12, result.RecentRuns.Count); Assert.Equal("run-99", result.RecentRuns[0].RunId);
        Assert.Equal("run-88", result.RecentRuns[11].RunId);
    }
    [Fact]
    public void SelectionRefreshNeverSubstitutesAReplacementRun()
    {
        var p = Profile(); p.Statistics.Runs.Add(Run("a")); p.Statistics.Runs.Add(Run("b", 1));
        var s = new EconomySelection(); s.Refresh(Present(p)); Assert.Equal("b", s.ExpandedRunId);
        Assert.True(s.Toggle("g", "a")); s.Capture("Recent", 500); s.Refresh(Present(p)); Assert.Equal("a", s.ExpandedRunId);
        Assert.Equal(500, s.Offset("Recent", 100, 1000)); Assert.False(s.Toggle("other", "b")); Assert.False(s.Toggle("g", "missing"));
        p.Statistics.Runs.RemoveAt(0); s.Refresh(Present(p)); Assert.Null(s.ExpandedRunId); Assert.Equal(10, s.Offset("Recent", 100, 110));
        s.Refresh(null); Assert.Null(s.ExpandedRunId); Assert.False(s.Toggle("g", "b")); Assert.Equal(0, s.Offset("Recent", 100, 1000));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BothLayoutsKeepAllHoldingsFlowsAndRecordedBreakdowns(bool stacked)
    {
        var p = Profile(); Record(p.Statistics.Economy, CurrencyKind.Cash, 100, source: CurrencySourceCategory.LootOrPickup, context: GameplayContext.Raid, acquired: true);
        var d = Primary(Present(p), 1550, stacked);
        Assert.Equal(3, d.Elements.Count(e => e.Id.StartsWith("holding:", StringComparison.Ordinal) && e.Id.EndsWith(":value", StringComparison.Ordinal)));
        Assert.Contains(d.Elements, e => e.Text == "Money flow"); Assert.Contains(d.Elements, e => e.Text == "Cash flow");
        Assert.Contains(d.Elements, e => e.Text == "Sources"); Assert.Contains(d.Elements, e => e.Text == "Contexts");
        Assert.All(d.Elements, e => { Assert.True(e.Width > 0); Assert.True(e.Height > 0); Assert.True(e.X >= 0); Assert.True(e.X + e.Width <= 1550.01f); Assert.True(e.Y + e.Height <= d.Height); });
        var money = d.Elements.Single(e => e.Text == "Money flow"); var cash = d.Elements.Single(e => e.Text == "Cash flow");
        if (stacked) Assert.True(cash.Y > money.Y + money.Height); else Assert.Equal(money.Y, cash.Y);
    }
    [Fact]
    public void ExpandedRecentCardHasPeerNetsAndSeparateBottomRightRoute()
    {
        var p = Profile(); p.Statistics.Runs.Add(Run("a")); var s = new EconomySelection(); s.Refresh(Present(p)); var d = Recent(s);
        var route = Assert.Single(d.Elements, e => e.Kind == EconomyElementKind.Route);
        var title = d.Elements.Single(e => e.Id == "run:a:title");
        Assert.True(route.Y >= d.Elements.Single(e => e.Id == "run:a:cash:label").Y + d.Elements.Single(e => e.Id == "run:a:cash:label").Height);
        Assert.Equal(title.Y + title.Height / 2, d.Elements.Single(e => e.Kind == EconomyElementKind.Badge).Y + d.Elements.Single(e => e.Kind == EconomyElementKind.Badge).Height / 2, 3);
        Assert.Equal(20, 780 - 30 - route.X - route.Width, 3);
        Assert.Equal(2, d.Elements.Count(e => e.Id is "run:a:money:value" or "run:a:cash:value"));
        Assert.DoesNotContain(d.Elements, e => e.Text.Contains("acquired", StringComparison.OrdinalIgnoreCase));
        Assert.True(s.Toggle("g", "a")); var collapsed = Recent(s);
        Assert.DoesNotContain(collapsed.Elements, e => e.Kind == EconomyElementKind.Route); Assert.True(collapsed.Height < d.Height);
    }
    [Fact]
    public void ExpandedCardsKeepTenPixelGapAndEnclosingBackground()
    {
        var p = Profile(); p.Statistics.Runs.Add(Run("a")); p.Statistics.Runs.Add(Run("b", 1)); var s = new EconomySelection(); s.Refresh(Present(p)); var d = Recent(s);
        var group = d.Surfaces.Single(surface => surface.X == 30);
        var next = d.Elements.Single(e => e.Kind == EconomyElementKind.RunToggle && e.Id == "a");
        Assert.Equal(10, next.Y - group.Y - group.Height, 3);
        Assert.All(d.Elements.Where(e => e.Id.StartsWith("run:b:", StringComparison.Ordinal)), e => Assert.True(e.Y + e.Height <= group.Y + group.Height));
    }
    [Fact]
    public void LongNamesAndTableFallbackRemainInsideTheirMeasuredRegions()
    {
        var rows = new[] { new EconomyFlowRow("a", new string('L', 180), long.MaxValue, 0, long.MaxValue) };
        Assert.Empty(EconomyLayoutPolicy.TableColumns(650, new[] { "Zufluss", "Abfluss", "Netto" }, rows, MeasureWidth));
        var p = Profile(); var run = Run("long"); run.MapDisplayName = new string('L', 300); p.Statistics.Runs.Add(run);
        var s = new EconomySelection(); s.Refresh(Present(p)); var d = Recent(s, 600);
        Assert.All(d.Elements, e => { Assert.True(e.X + e.Width <= 600.01); Assert.True(e.Y + e.Height <= d.Height); });
        var title = d.Elements.Single(e => e.Id == "run:long:title"); Assert.True(title.Height > 100);
    }
    [Fact]
    public void EmptyStateContainsNoAnonymousLegacyHistoryCard()
    {
        var s = new EconomySelection(); s.Refresh(Present()); var d = Recent(s);
        Assert.Empty(d.Surfaces); Assert.DoesNotContain(d.Elements, e => e.Actionable);
        Assert.Contains(d.Elements, e => e.Text == UiText.Get("ui.runs_empty"));
    }
}
