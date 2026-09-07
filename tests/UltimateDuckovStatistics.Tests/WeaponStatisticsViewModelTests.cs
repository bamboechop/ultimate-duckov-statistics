using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Tests;

public sealed class WeaponStatisticsViewModelTests
{
    [Fact]
    [Trait("Category", "Weapon")]
    [Trait("Category", "UI")]
    public void ViewModelUsesPersistedTotalsAndCurrentExplicitCapabilityStates()
    {
        var profile = new ProfileDocument();
        profile.Statistics.RunTotals.WeaponStatistics.Totals.FiringActions = 4;
        profile.Statistics.RunTotals.WeaponStatistics.Weapons["weapon:b"] = Weapon("weapon:b", "Beta", 1);
        profile.Statistics.RunTotals.WeaponStatistics.Weapons["weapon:a"] = Weapon("weapon:a", "Alpha", 3);
        profile.Statistics.RunTotals.WeaponStatistics.Capabilities.FiringActions = new MetricAvailability
        {
            State = AdapterCapabilityState.Supported,
            Provenance = "recorded firing actions"
        };
        profile.Capabilities.Add(new CapabilityRecord
        {
            AdapterId = WeaponCapabilityIds.FiringActions,
            State = AdapterCapabilityState.Supported
        });

        var model = WeaponStatisticsViewModelFactory.Create(profile);

        Assert.Same(profile.Statistics.RunTotals.WeaponStatistics, model.Lifetime);
        Assert.Equal(4, model.Lifetime.Totals.FiringActions);
        Assert.Collection(
            model.Weapons,
            value => Assert.Equal("weapon:a", value.WeaponId),
            value => Assert.Equal("weapon:b", value.WeaponId));
        Assert.Equal(AdapterCapabilityState.Supported, model.Capabilities.FiringActions.State);
    }

    [Fact]
    [Trait("Category", "Weapon")]
    [Trait("Category", "UI")]
    public void NonemptyLifetimeAggregateWithMissingCapabilityMetadataRemainsUnavailable()
    {
        var profile = new ProfileDocument();
        profile.Statistics.RunTotals.WeaponStatistics.Totals.FiringActions = 7;
        profile.Statistics.RunTotals.WeaponStatistics.Weapons["weapon:observed"] =
            Weapon("weapon:observed", "Observed weapon", 7);
        profile.Capabilities.Add(new CapabilityRecord
        {
            AdapterId = WeaponCapabilityIds.FiringActions,
            State = AdapterCapabilityState.Supported
        });

        var model = WeaponStatisticsViewModelFactory.Create(profile);

        Assert.Equal(7, model.Lifetime.Totals.FiringActions);
        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            model.Capabilities.FiringActions.State);
    }

    [Fact]
    [Trait("Category", "Weapon")]
    [Trait("Category", "UI")]
    public void UnsupportedAndExperimentalSubmetricsRemainExplicitInsteadOfLookingLikeZero()
    {
        var profile = new ProfileDocument();
        profile.Capabilities.Add(new CapabilityRecord
        {
            AdapterId = WeaponCapabilityIds.FiringActions,
            State = AdapterCapabilityState.Experimental
        });

        var model = WeaponStatisticsViewModelFactory.Create(profile);

        Assert.Equal(AdapterCapabilityState.Experimental, model.Capabilities.FiringActions.State);
    }

    private static WeaponAggregate Weapon(string id, string name, long actions) => new()
    {
        WeaponId = id,
        DisplayName = name,
        Totals = new WeaponMetricTotals { FiringActions = actions }
    };
}
