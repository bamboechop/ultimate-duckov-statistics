using UltimateDuckovStatistics.Core;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.UI;

namespace UltimateDuckovStatistics.Tests;

public sealed class EconomyStatisticsTests
{
    [Fact]
    public void MoneyAndCashRemainSeparateAndNetDerivesFromGrossFlows()
    {
        var aggregate = new EconomyStatisticsAggregate();
        Assert.True(EconomyStatisticsReducer.Record(aggregate, "generation", Flow("money-in", CurrencyKind.Money, CurrencyFlowDirection.Inflow, 100)));
        Assert.True(EconomyStatisticsReducer.Record(aggregate, "generation", Flow("money-out", CurrencyKind.Money, CurrencyFlowDirection.Outflow, 35, CurrencySourceCategory.Purchase, GameplayContext.Shop)));
        Assert.True(EconomyStatisticsReducer.Record(aggregate, "generation", Flow("cash-in", CurrencyKind.Cash, CurrencyFlowDirection.Inflow, 7)));

        Assert.Equal(100, aggregate.Currencies["Money"].Totals.GrossInflow);
        Assert.Equal(35, aggregate.Currencies["Money"].Totals.GrossOutflow);
        Assert.Equal(65, aggregate.Currencies["Money"].Totals.NetFlow);
        Assert.Equal(7, aggregate.Currencies["Cash"].Totals.GrossInflow);
        Assert.Equal(7, aggregate.Currencies["Cash"].Totals.NetFlow);
        EconomyStatisticsReducer.Validate(aggregate);
    }

    [Fact]
    public void SourceAndContextComposeIncludingUnknownAdjustment()
    {
        var aggregate = new EconomyStatisticsAggregate();
        EconomyStatisticsReducer.Record(aggregate, "generation", Flow("unknown", CurrencyKind.Money, CurrencyFlowDirection.Inflow, 10));
        EconomyStatisticsReducer.Record(aggregate, "generation", Flow("reward", CurrencyKind.Money, CurrencyFlowDirection.Inflow, 15, CurrencySourceCategory.Reward, GameplayContext.Reward));
        var money = aggregate.Currencies["Money"];
        Assert.Equal(10, money.Sources["UnknownAdjustment"].GrossInflow);
        Assert.Equal(15, money.Sources["Reward"].GrossInflow);
        Assert.Equal(10, money.Contexts["Unknown"].GrossInflow);
        Assert.Equal(15, money.Contexts["Reward"].GrossInflow);
        EconomyStatisticsReducer.Validate(aggregate);
    }

    [Fact]
    public void DuplicateEventIsRejectedAndBoundedEvidenceDoesNotChangeTotals()
    {
        var aggregate = new EconomyStatisticsAggregate();
        var flow = Flow("same", CurrencyKind.Money, CurrencyFlowDirection.Inflow, 4);
        Assert.True(EconomyStatisticsReducer.Record(aggregate, "generation", flow));
        Assert.False(EconomyStatisticsReducer.Record(aggregate, "generation", flow));
        Assert.Equal(4, aggregate.Currencies["Money"].Totals.GrossInflow);
    }

    [Fact]
    public void InvalidOrContradictoryFlowsAreRejected()
    {
        var aggregate = new EconomyStatisticsAggregate();
        var zero = Flow("zero", CurrencyKind.Money, CurrencyFlowDirection.Inflow, 0);
        Assert.Throws<ArgumentException>(() => EconomyStatisticsReducer.Record(aggregate, "generation", zero));
        var invalidAcquisition = Flow("invalid", CurrencyKind.Cash, CurrencyFlowDirection.Outflow, 1, CurrencySourceCategory.LootOrPickup, GameplayContext.Raid);
        invalidAcquisition.RunId = "run";
        invalidAcquisition.ProvenExternalRaidAcquisition = true;
        Assert.Throws<ArgumentException>(() => EconomyStatisticsReducer.Record(aggregate, "generation", invalidAcquisition));

        var raidWithoutRun = Flow("raid-without-run", CurrencyKind.Money, CurrencyFlowDirection.Inflow, 1, context: GameplayContext.Raid);
        Assert.Throws<ArgumentException>(() => EconomyStatisticsReducer.Record(aggregate, "generation", raidWithoutRun));

        var baseWithRun = Flow("base-with-run", CurrencyKind.Money, CurrencyFlowDirection.Inflow, 1, context: GameplayContext.Base);
        baseWithRun.RunId = "run";
        Assert.Throws<ArgumentException>(() => EconomyStatisticsReducer.Record(aggregate, "generation", baseWithRun));

        var moneyAcquisition = Flow("money-acquisition", CurrencyKind.Money, CurrencyFlowDirection.Inflow, 1, context: GameplayContext.Raid);
        moneyAcquisition.RunId = "run";
        moneyAcquisition.ProvenExternalRaidAcquisition = true;
        Assert.Throws<ArgumentException>(() => EconomyStatisticsReducer.Record(aggregate, "generation", moneyAcquisition));
    }

    [Fact]
    public void ArithmeticOverflowDisablesOnlyTheAffectedCurrencyWithoutApproximation()
    {
        var aggregate = Supported();
        Assert.True(EconomyStatisticsReducer.Record(aggregate, "generation", Flow("max", CurrencyKind.Money, CurrencyFlowDirection.Inflow, long.MaxValue)));
        Assert.False(EconomyStatisticsReducer.Record(aggregate, "generation", Flow("more", CurrencyKind.Money, CurrencyFlowDirection.Inflow, 1)));
        Assert.Equal(long.MaxValue, aggregate.Currencies["Money"].Totals.GrossInflow);
        Assert.Equal(long.MaxValue, aggregate.Currencies["Money"].Totals.NetFlow);
        Assert.True(aggregate.MoneyArithmeticSaturated);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, aggregate.Capabilities.MoneyAmountDirection.State);
        Assert.Equal(AdapterCapabilityState.Supported, aggregate.Capabilities.CashAmountDirection.State);
        Assert.True(EconomyStatisticsReducer.Record(aggregate, "generation", Flow("cash", CurrencyKind.Cash, CurrencyFlowDirection.Inflow, 1)));
        Assert.False(EconomyStatisticsReducer.NormalizePersisted(aggregate));
        EconomyStatisticsReducer.Validate(aggregate);
    }

    [Fact]
    public void AggregateMergeOverflowRetainsThePriorExactValueAndRemainsIdempotentlyUnavailable()
    {
        var target = Supported();
        var source = Supported();
        Assert.True(EconomyStatisticsReducer.Record(
            target,
            "generation",
            Flow("target", CurrencyKind.Money, CurrencyFlowDirection.Inflow, long.MaxValue - 2)));
        Assert.True(EconomyStatisticsReducer.Record(
            source,
            "generation",
            Flow("source", CurrencyKind.Money, CurrencyFlowDirection.Inflow, 5)));

        EconomyStatisticsReducer.Merge(target, source);
        Assert.Equal(long.MaxValue - 2, target.Currencies["Money"].Totals.GrossInflow);
        Assert.True(target.MoneyArithmeticSaturated);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, target.Capabilities.MoneyAmountDirection.State);

        EconomyStatisticsReducer.Merge(target, source);
        Assert.Equal(long.MaxValue - 2, target.Currencies["Money"].Totals.GrossInflow);
        Assert.False(EconomyStatisticsReducer.NormalizePersisted(target));
        EconomyStatisticsReducer.Validate(target);
    }

    [Fact]
    public void CashOutcomeMergeOverflowRetainsAllPriorCashValuesButStillMergesMoney()
    {
        var target = Supported();
        var source = Supported();
        Assert.True(EconomyStatisticsReducer.Record(
            target,
            "generation",
            Flow("target-cash", CurrencyKind.Cash, CurrencyFlowDirection.Inflow, long.MaxValue - 2)));
        target.CashRaidOutcomes.Acquired = long.MaxValue - 2;
        target.CashRaidOutcomes.Unresolved = long.MaxValue - 2;
        Assert.True(EconomyStatisticsReducer.Record(
            source,
            "generation",
            Flow("source-cash", CurrencyKind.Cash, CurrencyFlowDirection.Inflow, 5)));
        source.CashRaidOutcomes.Acquired = 5;
        source.CashRaidOutcomes.Unresolved = 5;
        Assert.True(EconomyStatisticsReducer.Record(
            source,
            "generation",
            Flow("source-money", CurrencyKind.Money, CurrencyFlowDirection.Inflow, 7)));

        EconomyStatisticsReducer.Merge(target, source);

        Assert.Equal(long.MaxValue - 2, target.Currencies["Cash"].Totals.GrossInflow);
        Assert.Equal(long.MaxValue - 2, target.CashRaidOutcomes.Acquired);
        Assert.Equal(long.MaxValue - 2, target.CashRaidOutcomes.Unresolved);
        Assert.True(target.CashArithmeticSaturated);
        Assert.Equal(7, target.Currencies["Money"].Totals.GrossInflow);
        EconomyStatisticsReducer.Validate(target);
    }

    [Fact]
    public void RecoveryCandidateRejectsBreakdownCompositionThatWouldOverflow()
    {
        var aggregate = Supported();
        aggregate.Currencies["Money"] = new CurrencyEconomyAggregate
        {
            Currency = CurrencyKind.Money,
            Totals = new CurrencyFlowTotals { GrossInflow = long.MaxValue },
            Sources = new Dictionary<string, CurrencyFlowTotals>
            {
                ["Sale"] = new() { GrossInflow = long.MaxValue },
                ["Reward"] = new() { GrossInflow = 1 }
            },
            Contexts = new Dictionary<string, CurrencyFlowTotals>
            {
                ["Shop"] = new() { GrossInflow = long.MaxValue }
            }
        };

        Assert.Throws<ArgumentException>(() => EconomyStatisticsReducer.ValidateRecoveryCandidate(aggregate));
    }

    [Fact]
    public void TerminalOutcomeMergeOverflowRetainsThePriorExactOutcome()
    {
        var target = Supported();
        target.CashRaidOutcomes.Acquired = long.MaxValue;
        target.CashRaidOutcomes.Secured = long.MaxValue - 1;
        target.CashTerminalDispositionRecorded = true;
        var run = Supported();
        run.CashRaidOutcomes.Acquired = 2;
        run.CashRaidOutcomes.Secured = 2;
        run.CashTerminalDispositionRecorded = true;

        EconomyStatisticsReducer.MergeTerminalOutcomes(target, run);

        Assert.Equal(long.MaxValue - 1, target.CashRaidOutcomes.Secured);
        Assert.True(target.CashArithmeticSaturated);
        Assert.True(target.CashTerminalDispositionAmbiguous);
        EconomyStatisticsReducer.Validate(target);
    }

    [Theory]
    [InlineData(RunOutcome.Extracted, 12, 0, 0)]
    [InlineData(RunOutcome.Died, 0, 12, 0)]
    [InlineData(RunOutcome.Interrupted, 0, 0, 12)]
    public void ProvenCashAcquisitionGetsTruthfulTerminalDisposition(RunOutcome outcome, long secured, long lost, long unresolved)
    {
        var aggregate = Supported();
        var flow = Flow("pickup", CurrencyKind.Cash, CurrencyFlowDirection.Inflow, 12, CurrencySourceCategory.LootOrPickup, GameplayContext.Raid);
        flow.RunId = "run";
        flow.SegmentId = "segment";
        flow.MapId = "duckov:map:A";
        flow.ProvenExternalRaidAcquisition = true;
        EconomyStatisticsReducer.Record(aggregate, "generation", flow);
        EconomyStatisticsReducer.FinalizeCashRaidOutcome(aggregate, outcome);
        EconomyStatisticsReducer.FinalizeCashRaidOutcome(aggregate, outcome);
        Assert.Equal(12, aggregate.CashRaidOutcomes.Acquired);
        Assert.Equal(secured, aggregate.CashRaidOutcomes.Secured);
        Assert.Equal(lost, aggregate.CashRaidOutcomes.Lost);
        Assert.Equal(unresolved, aggregate.CashRaidOutcomes.Unresolved);
    }

    [Fact]
    public void InterveningCashOutflowMakesOnlyTerminalDispositionUnresolved()
    {
        var aggregate = Supported();
        var pickup = Flow("pickup", CurrencyKind.Cash, CurrencyFlowDirection.Inflow, 12, CurrencySourceCategory.LootOrPickup, GameplayContext.Raid);
        pickup.RunId = "run";
        pickup.ProvenExternalRaidAcquisition = true;
        EconomyStatisticsReducer.Record(aggregate, "generation", pickup);
        var outflow = Flow("drop", CurrencyKind.Cash, CurrencyFlowDirection.Outflow, 2, CurrencySourceCategory.UnknownAdjustment, GameplayContext.Raid);
        outflow.RunId = "run";
        EconomyStatisticsReducer.Record(aggregate, "generation", outflow);
        EconomyStatisticsReducer.FinalizeCashRaidOutcome(aggregate, RunOutcome.Extracted);
        Assert.Equal(12, aggregate.CashRaidOutcomes.Acquired);
        Assert.Equal(0, aggregate.CashRaidOutcomes.Secured);
        Assert.Equal(12, aggregate.CashRaidOutcomes.Unresolved);
        Assert.Equal(2, aggregate.Currencies["Cash"].Totals.GrossOutflow);
    }

    [Fact]
    public void DeferredDifferenceRecoversOnlyUnpersistedEvents()
    {
        var checkpoint = Supported();
        var watermark = Supported();
        EconomyStatisticsReducer.Record(checkpoint, "generation", Flow("persisted", CurrencyKind.Money, CurrencyFlowDirection.Inflow, 5));
        EconomyStatisticsReducer.Record(watermark, "generation", Flow("persisted", CurrencyKind.Money, CurrencyFlowDirection.Inflow, 5));
        EconomyStatisticsReducer.Record(checkpoint, "generation", Flow("pending", CurrencyKind.Money, CurrencyFlowDirection.Outflow, 2));
        Assert.True(EconomyStatisticsReducer.TrySubtract(checkpoint, watermark, out var difference));
        Assert.Equal(0, difference.Currencies["Money"].Totals.GrossInflow);
        Assert.Equal(2, difference.Currencies["Money"].Totals.GrossOutflow);
        Assert.Single(difference.RecentEventIds);
        Assert.Equal("pending", difference.RecentEventIds[0]);
    }

    [Fact]
    public void RepairIsIdempotentAndMakesBrokenCompositionExplicitlyUnknown()
    {
        var aggregate = new EconomyStatisticsAggregate
        {
            Currencies = new Dictionary<string, CurrencyEconomyAggregate>
            {
                ["wrong"] = new()
                {
                    Currency = CurrencyKind.Money,
                    Totals = new CurrencyFlowTotals { GrossInflow = 20 },
                    Sources = new Dictionary<string, CurrencyFlowTotals>(),
                    Contexts = new Dictionary<string, CurrencyFlowTotals>()
                }
            }
        };
        Assert.True(EconomyStatisticsReducer.NormalizePersisted(aggregate));
        Assert.False(EconomyStatisticsReducer.NormalizePersisted(aggregate));
        Assert.Equal(20, aggregate.Currencies["Money"].Sources["UnknownAdjustment"].GrossInflow);
        Assert.Equal(20, aggregate.Currencies["Money"].Contexts["Unknown"].GrossInflow);
        EconomyStatisticsReducer.Validate(aggregate);
    }

    [Fact]
    public void RepairNormalizesInvalidBreakdownAndCapabilityIdentities()
    {
        var aggregate = Supported();
        aggregate.Currencies["Money"] = new CurrencyEconomyAggregate
        {
            Currency = CurrencyKind.Money,
            Totals = new CurrencyFlowTotals { GrossInflow = 9 },
            Sources = new Dictionary<string, CurrencyFlowTotals>
            {
                ["NotASource"] = new() { GrossInflow = 9 }
            },
            Contexts = new Dictionary<string, CurrencyFlowTotals>
            {
                ["NotAContext"] = new() { GrossInflow = 9 }
            }
        };
        aggregate.Capabilities.MoneyAmountDirection.State = (AdapterCapabilityState)99;

        Assert.True(EconomyStatisticsReducer.NormalizePersisted(aggregate));
        Assert.False(EconomyStatisticsReducer.NormalizePersisted(aggregate));
        Assert.Equal(9, aggregate.Currencies["Money"].Sources["UnknownAdjustment"].GrossInflow);
        Assert.Equal(9, aggregate.Currencies["Money"].Contexts["Unknown"].GrossInflow);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, aggregate.Capabilities.MoneyAmountDirection.State);
        Assert.True(aggregate.WasRepairedFromInvalidState);
        EconomyStatisticsReducer.Validate(aggregate);
    }

    [Fact]
    public void PartialCapabilitySupportRetainsExactAmountWithoutInventingASource()
    {
        var aggregate = Supported();
        aggregate.Capabilities.MoneySourceAttribution = new MetricAvailability
        {
            State = AdapterCapabilityState.DisabledIncompatible,
            Provenance = "semantic source unavailable"
        };
        EconomyStatisticsReducer.Record(
            aggregate,
            "generation",
            Flow("partial", CurrencyKind.Money, CurrencyFlowDirection.Outflow, 19));

        Assert.Equal(19, aggregate.Currencies["Money"].Totals.GrossOutflow);
        Assert.Equal(19, aggregate.Currencies["Money"].Sources["UnknownAdjustment"].GrossOutflow);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, aggregate.Capabilities.MoneySourceAttribution.State);
        Assert.Equal(AdapterCapabilityState.Supported, aggregate.Capabilities.MoneyAmountDirection.State);
    }

    [Fact]
    public void UnavailableTerminalOutcomePreservesAcquiredAndUsesUnresolved()
    {
        var aggregate = Supported();
        aggregate.Capabilities.CashTerminalOutcomes = new MetricAvailability
        {
            State = AdapterCapabilityState.DisabledIncompatible,
            Provenance = "fungible terminal disposition unavailable"
        };
        var pickup = Flow(
            "pickup-unavailable",
            CurrencyKind.Cash,
            CurrencyFlowDirection.Inflow,
            6,
            CurrencySourceCategory.LootOrPickup,
            GameplayContext.Raid);
        pickup.RunId = "run";
        pickup.ProvenExternalRaidAcquisition = true;
        EconomyStatisticsReducer.Record(aggregate, "generation", pickup);

        EconomyStatisticsReducer.FinalizeCashRaidOutcome(aggregate, RunOutcome.Extracted);

        Assert.Equal(6, aggregate.CashRaidOutcomes.Acquired);
        Assert.Equal(0, aggregate.CashRaidOutcomes.Secured);
        Assert.Equal(0, aggregate.CashRaidOutcomes.Lost);
        Assert.Equal(6, aggregate.CashRaidOutcomes.Unresolved);
    }

    [Fact]
    public void RepairRejectsOverlappingTerminalBucketsAndIsIdempotent()
    {
        var aggregate = new EconomyStatisticsAggregate
        {
            CashRaidOutcomes = new CashRaidOutcomeAggregate
            {
                Acquired = 10,
                Secured = 6,
                Lost = 0,
                Unresolved = 6
            }
        };

        Assert.True(EconomyStatisticsReducer.NormalizePersisted(aggregate));
        Assert.False(EconomyStatisticsReducer.NormalizePersisted(aggregate));
        Assert.Equal(0, aggregate.CashRaidOutcomes.Secured);
        Assert.Equal(0, aggregate.CashRaidOutcomes.Lost);
        Assert.Equal(10, aggregate.CashRaidOutcomes.Unresolved);
        Assert.True(aggregate.CashTerminalDispositionAmbiguous);
        EconomyStatisticsReducer.Validate(aggregate);
    }

    [Fact]
    public void RecentTransactionEvidenceSaturatesBeforeEvictionCouldPermitDoubleCounting()
    {
        var aggregate = Supported();
        for (var index = 0; index < EconomyStatisticsReducer.MaximumRecentEventIds; index++)
            Assert.True(EconomyStatisticsReducer.Record(
                aggregate,
                "generation",
                Flow($"bounded:{index}", CurrencyKind.Money, CurrencyFlowDirection.Inflow, 1)));

        Assert.Equal(EconomyStatisticsReducer.MaximumRecentEventIds, aggregate.RecentEventIds.Count);
        Assert.Contains("bounded:0", aggregate.RecentEventIds);
        Assert.True(aggregate.DeduplicationSaturated);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, aggregate.Capabilities.MoneyAmountDirection.State);
        Assert.False(EconomyStatisticsReducer.Record(
            aggregate,
            "generation",
            Flow("bounded:overflow", CurrencyKind.Money, CurrencyFlowDirection.Inflow, 1)));
        Assert.False(EconomyStatisticsReducer.Record(
            aggregate,
            "generation",
            Flow("bounded:0", CurrencyKind.Money, CurrencyFlowDirection.Inflow, 1)));
        Assert.Equal(EconomyStatisticsReducer.MaximumRecentEventIds, aggregate.Currencies["Money"].Totals.GrossInflow);

        var later = Supported();
        Assert.True(EconomyStatisticsReducer.Record(
            later,
            "generation",
            Flow("later-cash", CurrencyKind.Cash, CurrencyFlowDirection.Inflow, 3)));
        EconomyStatisticsReducer.Merge(aggregate, later);
        Assert.False(aggregate.Currencies.ContainsKey("Cash"));

        later.CashRaidOutcomes.Acquired = 3;
        later.CashRaidOutcomes.Unresolved = 3;
        later.CashTerminalDispositionRecorded = true;
        EconomyStatisticsReducer.MergeTerminalOutcomes(aggregate, later);
        Assert.Equal(0, aggregate.CashRaidOutcomes.Unresolved);
        Assert.True(aggregate.CashTerminalDispositionAmbiguous);
    }

    [Fact]
    public void EconomyUiProjectionKeepsCurrenciesDirectionsAndUnavailableHistoryDistinct()
    {
        var aggregate = Supported();
        EconomyStatisticsReducer.Record(aggregate, "generation", Flow("ui-money", CurrencyKind.Money, CurrencyFlowDirection.Inflow, 12));
        EconomyStatisticsReducer.Record(aggregate, "generation", Flow("ui-cash", CurrencyKind.Cash, CurrencyFlowDirection.Outflow, 3));
        var compact = UiText.FormatEconomyCompact(aggregate);
        Assert.Contains("Money +12/-0 net 12", compact, StringComparison.Ordinal);
        Assert.Contains("Cash +0/-3 net -3", compact, StringComparison.Ordinal);

        var historical = new EconomyStatisticsAggregate { HistoricalUnavailable = true };
        var unavailable = UiText.FormatEconomyCompact(historical);
        Assert.Contains("Money no recorded M9 flow", unavailable, StringComparison.Ordinal);
        Assert.Contains("Cash no recorded M9 flow", unavailable, StringComparison.Ordinal);
        Assert.Contains("earlier economy history unavailable", unavailable, StringComparison.Ordinal);
        Assert.DoesNotContain("Money 0", unavailable, StringComparison.Ordinal);

        aggregate.Capabilities.MoneyAmountDirection = new MetricAvailability
        {
            State = AdapterCapabilityState.DisabledIncompatible,
            Provenance = "contract drift"
        };
        var disabledAfterCapture = UiText.FormatEconomyCompact(aggregate);
        Assert.Contains("Money +12/-0 net 12 (current capture unavailable)", disabledAfterCapture, StringComparison.Ordinal);
    }

    private static EconomyStatisticsAggregate Supported()
    {
        var supported = new MetricAvailability { State = AdapterCapabilityState.Supported, Provenance = "test" };
        return new EconomyStatisticsAggregate
        {
            Capabilities = new EconomyMetricCapabilities
            {
                MoneyAmountDirection = supported,
                MoneySourceAttribution = supported,
                MoneyContextAttribution = supported,
                CashAmountDirection = supported,
                CashExternalAcquisition = supported,
                CashContextAttribution = supported,
                CashTerminalOutcomes = supported,
                RouteAttribution = supported
            }
        };
    }

    private static CurrencyFlowRecorded Flow(
        string id,
        CurrencyKind currency,
        CurrencyFlowDirection direction,
        long amount,
        CurrencySourceCategory source = CurrencySourceCategory.UnknownAdjustment,
        GameplayContext context = GameplayContext.Unknown) => new()
        {
            EventId = id,
            TimestampUtc = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc),
            SaveGenerationId = "generation",
            MapId = MapIdentity.UnknownId,
            Currency = currency,
            Direction = direction,
            Amount = amount,
            Source = source,
            GameplayContext = context,
            IntegrityTags = IntegrityTags.Normal,
            AdapterVersion = "test"
        };
}
