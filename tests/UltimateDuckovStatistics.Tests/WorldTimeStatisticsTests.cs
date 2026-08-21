using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Tests;

public sealed class WorldTimeStatisticsTests
{
    [Fact]
    public void ReducerAppliesIndependentCheckedTotals()
    {
        var aggregate = new WorldTimeStatisticsAggregate();
        WorldTimeStatisticsReducer.InitializeOrRestrictCapabilities(
            aggregate,
            WorldTimeNativeContractPolicy.Supported("clock", "sleep"));

        Assert.True(WorldTimeStatisticsReducer.Apply(
            aggregate,
            new WorldTimeMutation(2, 1000, 1, 700)));
        Assert.Equal(2, aggregate.CalendarDaysAdvanced);
        Assert.Equal(1000, aggregate.ObservedGameTimeTicks);
        Assert.Equal(1, aggregate.CompletedSleepSessions);
        Assert.Equal(700, aggregate.SleepAdvancedTimeTicks);
        WorldTimeStatisticsReducer.Validate(aggregate);
    }

    [Fact]
    public void SleepCompletionCanPublishAfterItsObservedClockSliceWasAlreadyFlushed()
    {
        var aggregate = new WorldTimeStatisticsAggregate();
        WorldTimeStatisticsReducer.InitializeOrRestrictCapabilities(
            aggregate,
            WorldTimeNativeContractPolicy.Supported("clock", "sleep"));
        var sleepDuration = TimeSpan.FromHours(8).Ticks;

        Assert.True(WorldTimeStatisticsReducer.Apply(
            aggregate,
            new WorldTimeMutation(0, sleepDuration, 0, 0)));
        Assert.True(WorldTimeStatisticsReducer.Apply(
            aggregate,
            new WorldTimeMutation(0, TimeSpan.FromSeconds(1).Ticks, 1, sleepDuration)));

        Assert.Equal(sleepDuration + TimeSpan.FromSeconds(1).Ticks, aggregate.ObservedGameTimeTicks);
        Assert.Equal(1, aggregate.CompletedSleepSessions);
        Assert.Equal(sleepDuration, aggregate.SleepAdvancedTimeTicks);
    }

    [Fact]
    public void OverflowPreservesPriorExactTotalAndDisablesOnlyAffectedMetric()
    {
        var aggregate = new WorldTimeStatisticsAggregate
        {
            CalendarDaysAdvanced = long.MaxValue,
            Capabilities = WorldTimeNativeContractPolicy.Supported("clock", "sleep")
        };

        Assert.True(WorldTimeStatisticsReducer.Apply(aggregate, new WorldTimeMutation(1, 10, 0, 0)));

        Assert.Equal(long.MaxValue, aggregate.CalendarDaysAdvanced);
        Assert.True(aggregate.CalendarArithmeticUnavailable);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, aggregate.Capabilities.CalendarDays.State);
        Assert.Equal(10, aggregate.ObservedGameTimeTicks);
        Assert.Equal(AdapterCapabilityState.Supported, aggregate.Capabilities.ObservedElapsed.State);
    }

    [Fact]
    public void InvalidPersistedCounterRepairsWithoutInventingHistory()
    {
        var aggregate = new WorldTimeStatisticsAggregate { SleepAdvancedTimeTicks = -42 };

        Assert.True(WorldTimeStatisticsReducer.NormalizePersisted(aggregate));

        Assert.Equal(0, aggregate.SleepAdvancedTimeTicks);
        Assert.True(aggregate.SleepElapsedArithmeticUnavailable);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, aggregate.Capabilities.SleepAdvancedTime.State);
    }

    [Fact]
    public void CapabilityDegradationRemainsNarrowAndMonotonic()
    {
        var aggregate = new WorldTimeStatisticsAggregate();
        WorldTimeStatisticsReducer.InitializeOrRestrictCapabilities(
            aggregate,
            WorldTimeNativeContractPolicy.ClockSupportedSleepUnavailable("clock", "no sleep patch"));

        Assert.Equal(AdapterCapabilityState.Supported, aggregate.Capabilities.CalendarDays.State);
        Assert.Equal(AdapterCapabilityState.Supported, aggregate.Capabilities.ObservedElapsed.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, aggregate.Capabilities.CompletedSleepSessions.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, aggregate.Capabilities.SleepAdvancedTime.State);
    }

    [Fact]
    public void ClonePreservesHistoricalAndCapabilityProvenance()
    {
        var source = new WorldTimeStatisticsAggregate
        {
            CalendarDaysAdvanced = 3,
            HistoricalUnavailable = true,
            HistoricalProvenance = "pre-M12"
        };
        WorldTimeStatisticsReducer.InitializeOrRestrictCapabilities(
            source,
            WorldTimeNativeContractPolicy.Supported("clock", "sleep"));

        var clone = WorldTimeStatisticsReducer.Clone(source);

        Assert.Equal(3, clone.CalendarDaysAdvanced);
        Assert.True(clone.HistoricalUnavailable);
        Assert.Equal("pre-M12", clone.HistoricalProvenance);
        Assert.Equal(AdapterCapabilityState.Supported, clone.Capabilities.CalendarDays.State);
    }
}
