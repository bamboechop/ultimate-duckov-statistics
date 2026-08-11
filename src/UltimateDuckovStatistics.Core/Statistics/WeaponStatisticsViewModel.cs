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
        capabilities.FiringActions.State = ReadState(profile, WeaponCapabilityIds.FiringActions, capabilities.FiringActions.State);
        capabilities.AmmunitionConsumption.State = ReadState(profile, WeaponCapabilityIds.AmmunitionConsumption, capabilities.AmmunitionConsumption.State);
        capabilities.Projectiles.State = ReadState(profile, WeaponCapabilityIds.Projectiles, capabilities.Projectiles.State);
        capabilities.WeaponIdentity.State = ReadState(profile, WeaponCapabilityIds.WeaponIdentity, capabilities.WeaponIdentity.State);
        capabilities.AmmunitionIdentity.State = ReadState(profile, WeaponCapabilityIds.AmmunitionIdentity, capabilities.AmmunitionIdentity.State);
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
