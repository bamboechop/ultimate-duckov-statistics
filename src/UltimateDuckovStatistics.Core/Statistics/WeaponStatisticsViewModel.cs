using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Core.Statistics;

public sealed class WeaponStatisticsViewModel
{
    public WeaponStatisticsAggregate Lifetime { get; set; } = new();

    public IReadOnlyList<WeaponAggregate> Weapons { get; set; } = Array.Empty<WeaponAggregate>();

    public IReadOnlyList<AmmunitionAggregate> AmmunitionTypes { get; set; } = Array.Empty<AmmunitionAggregate>();

    public IReadOnlyList<RunSummary> Runs { get; set; } = Array.Empty<RunSummary>();

    public WeaponMetricCapabilities Capabilities { get; set; } = new();

    public IReadOnlyList<WeaponAmmunitionPairView> WeaponAmmunitionPairs { get; set; } =
        Array.Empty<WeaponAmmunitionPairView>();
}

public sealed class WeaponAmmunitionPairView
{
    public WeaponAmmunitionPairAggregate Pair { get; set; } = new();
    public double PercentageWithinObservedWeaponPairs { get; set; }
}

public static class WeaponStatisticsViewModelFactory
{
    public static WeaponStatisticsViewModel Create(ProfileDocument profile)
    {
        if (profile == null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        var lifetime = profile.Statistics.RunTotals.WeaponStatistics;
        var capabilities = WeaponStatisticsReducer.CloneCapabilities(lifetime.Capabilities);
        capabilities.FiringActions.State = WeaponStatisticsReducer.ResolveCurrentAvailability(
            lifetime,
            capabilities.FiringActions,
            ReadState(profile, WeaponCapabilityIds.FiringActions, capabilities.FiringActions.State));
        capabilities.AmmunitionConsumption.State = WeaponStatisticsReducer.ResolveCurrentAvailability(
            lifetime,
            capabilities.AmmunitionConsumption,
            ReadState(profile, WeaponCapabilityIds.AmmunitionConsumption, capabilities.AmmunitionConsumption.State));
        capabilities.Projectiles.State = WeaponStatisticsReducer.ResolveCurrentAvailability(
            lifetime,
            capabilities.Projectiles,
            ReadState(profile, WeaponCapabilityIds.Projectiles, capabilities.Projectiles.State));
        capabilities.WeaponIdentity.State = WeaponStatisticsReducer.ResolveCurrentAvailability(
            lifetime,
            capabilities.WeaponIdentity,
            ReadState(profile, WeaponCapabilityIds.WeaponIdentity, capabilities.WeaponIdentity.State));
        capabilities.AmmunitionIdentity.State = WeaponStatisticsReducer.ResolveCurrentAvailability(
            lifetime,
            capabilities.AmmunitionIdentity,
            ReadState(profile, WeaponCapabilityIds.AmmunitionIdentity, capabilities.AmmunitionIdentity.State));
        capabilities.WeaponAmmunitionPairing.State = WeaponStatisticsReducer.ResolveCurrentAvailability(
            lifetime,
            capabilities.WeaponAmmunitionPairing,
            ReadState(profile, WeaponCapabilityIds.WeaponAmmunitionPairing, capabilities.WeaponAmmunitionPairing.State));
        var pairedByWeapon = lifetime.WeaponAmmunitionPairs.Values
            .GroupBy(value => value.WeaponId, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.Aggregate(0L, (total, pair) => checked(total + pair.FiringActions)),
                StringComparer.Ordinal);
        return new WeaponStatisticsViewModel
        {
            Lifetime = lifetime,
            Capabilities = capabilities,
            Weapons = lifetime.Weapons.Values
                .OrderByDescending(value => value.Totals.FiringActions)
                .ThenBy(value => value.DisplayName, StringComparer.Ordinal)
                .ThenBy(value => value.WeaponId, StringComparer.Ordinal)
                .ToArray(),
            AmmunitionTypes = lifetime.AmmunitionTypes.Values
                .OrderByDescending(value => value.Totals.AmmunitionUnitsConsumed)
                .ThenBy(value => value.DisplayName, StringComparer.Ordinal)
                .ThenBy(value => value.AmmunitionId, StringComparer.Ordinal)
                .ToArray(),
            WeaponAmmunitionPairs = lifetime.WeaponAmmunitionPairs.Values
                .OrderBy(value => value.WeaponDisplayName, StringComparer.Ordinal)
                .ThenBy(value => value.WeaponId, StringComparer.Ordinal)
                .ThenByDescending(value => value.FiringActions)
                .ThenBy(value => value.AmmunitionDisplayName, StringComparer.Ordinal)
                .ThenBy(value => value.AmmunitionId, StringComparer.Ordinal)
                .Select(value => new WeaponAmmunitionPairView
                {
                    Pair = value,
                    PercentageWithinObservedWeaponPairs = pairedByWeapon[value.WeaponId] == 0
                        ? 0
                        : value.FiringActions * 100d / pairedByWeapon[value.WeaponId]
                })
                .ToArray(),
            Runs = profile.Statistics.Runs
                .OrderByDescending(run => run.StartedUtc)
                .ThenBy(run => run.RunId, StringComparer.Ordinal)
                .ToArray()
        };
    }

    private static AdapterCapabilityState ReadState(
        ProfileDocument profile,
        string adapterId,
        AdapterCapabilityState fallback) => profile.Capabilities
            .FirstOrDefault(capability => string.Equals(capability.AdapterId, adapterId, StringComparison.Ordinal))
            ?.State ?? fallback;

}
