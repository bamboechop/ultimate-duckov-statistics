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
        profile.Statistics.RunTotals.WeaponStatistics.Totals.AmmunitionUnitsConsumed = 4;
        profile.Statistics.RunTotals.WeaponStatistics.Totals.Projectiles = 9;
        profile.Statistics.RunTotals.WeaponStatistics.Weapons["weapon:b"] = Weapon("weapon:b", "Beta", 1);
        profile.Statistics.RunTotals.WeaponStatistics.Weapons["weapon:a"] = Weapon("weapon:a", "Alpha", 3);
        profile.Capabilities.Add(new CapabilityRecord
        {
            AdapterId = WeaponCapabilityIds.FiringActions,
            State = AdapterCapabilityState.Supported
        });
        profile.Capabilities.Add(new CapabilityRecord
        {
            AdapterId = WeaponCapabilityIds.Projectiles,
            State = AdapterCapabilityState.DisabledIncompatible
        });

        var model = WeaponStatisticsViewModelFactory.Create(profile);

        Assert.Same(profile.Statistics.RunTotals.WeaponStatistics, model.Lifetime);
        Assert.Equal(4, model.Lifetime.Totals.FiringActions);
        Assert.Collection(
            model.Weapons,
            value => Assert.Equal("weapon:a", value.WeaponId),
            value => Assert.Equal("weapon:b", value.WeaponId));
        Assert.Equal(AdapterCapabilityState.Supported, model.Capabilities.FiringActions.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, model.Capabilities.Projectiles.State);
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
        profile.Capabilities.Add(new CapabilityRecord
        {
            AdapterId = WeaponCapabilityIds.AmmunitionConsumption,
            State = AdapterCapabilityState.DisabledIncompatible
        });

        var model = WeaponStatisticsViewModelFactory.Create(profile);

        Assert.Equal(AdapterCapabilityState.Experimental, model.Capabilities.FiringActions.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, model.Capabilities.AmmunitionConsumption.State);
    }

    [Fact]
    [Trait("Category", "Weapon")]
    [Trait("Category", "UI")]
    public void VisibleCombatCopySeparatesActionsAmmunitionProjectilesAndDryFireLimitation()
    {
        Assert.Contains(WeaponCapabilityIds.TriggerAttempts, WeaponCapabilityIds.All);
        Assert.Equal("Firing actions", UI.UiText.Get("ui.firing_actions"));
        Assert.Equal("Loaded ammunition units consumed", UI.UiText.Get("ui.ammunition_consumed"));
        Assert.Equal("Projectiles created", UI.UiText.Get("ui.projectiles"));
        Assert.Contains("actual loaded-ammunition consumption", UI.UiText.Get("ui.metric_contract"), StringComparison.Ordinal);
        Assert.Contains("completed projectile creation", UI.UiText.Get("ui.metric_contract"), StringComparison.Ordinal);
        Assert.Equal("Unsupported", UI.UiText.Get("ui.unsupported"));
    }

    private static WeaponAggregate Weapon(string id, string name, long actions) => new()
    {
        WeaponId = id,
        DisplayName = name,
        Totals = new WeaponMetricTotals { FiringActions = actions }
    };
}
