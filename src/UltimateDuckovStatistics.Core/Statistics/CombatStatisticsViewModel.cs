using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Core.Statistics;

public sealed class CombatStatisticsViewModel
{
    public CombatStatisticsAggregate Lifetime { get; set; } = new();
    public CombatMetricCapabilities Capabilities { get; set; } = new();
    public IReadOnlyList<CombatBreakdownAggregate> Enemies { get; set; } = Array.Empty<CombatBreakdownAggregate>();
    public IReadOnlyList<CombatBreakdownAggregate> Killers { get; set; } = Array.Empty<CombatBreakdownAggregate>();
    public IReadOnlyList<RunSummary> Runs { get; set; } = Array.Empty<RunSummary>();
    public double? Accuracy => Capabilities.Accuracy.State == AdapterCapabilityState.Supported
        && Lifetime.Totals.CompletedPlayerProjectiles > 0
            ? (double)Lifetime.Totals.RangedHits / Lifetime.Totals.CompletedPlayerProjectiles
            : null;
}

public static class CombatStatisticsViewModelFactory
{
    public static CombatStatisticsViewModel Create(ProfileDocument profile)
    {
        if (profile == null) throw new ArgumentNullException(nameof(profile));
        var lifetime = CombatStatisticsReducer.Clone(profile.Statistics.RunTotals.CombatStatistics);
        var capabilities = CombatStatisticsReducer.CloneCapabilities(lifetime.Capabilities);
        ApplyCurrentStates(lifetime, capabilities, profile.Capabilities);
        return new CombatStatisticsViewModel
        {
            Lifetime = lifetime,
            Capabilities = capabilities,
            Enemies = lifetime.Enemies.Values.OrderByDescending(x => x.Totals.DamageCaused)
                .ThenBy(x => x.DisplayName, StringComparer.Ordinal).ToArray(),
            Killers = lifetime.Killers.Values.OrderByDescending(x => x.Totals.PlayerDeaths)
                .ThenBy(x => x.DisplayName, StringComparer.Ordinal).ToArray(),
            Runs = profile.Statistics.Runs.OrderByDescending(x => x.StartedUtc).ToArray()
        };
    }

    private static void ApplyCurrentStates(
        CombatStatisticsAggregate aggregate,
        CombatMetricCapabilities values,
        IReadOnlyList<CapabilityRecord> current)
    {
        Set(values.DamageDealt, CombatCapabilityIds.DamageDealt, current);
        Set(values.DamageReceived, CombatCapabilityIds.DamageReceived, current);
        Set(values.RangedHits, CombatCapabilityIds.RangedHits, current);
        Set(values.Accuracy, CombatCapabilityIds.Accuracy, current);
        Set(values.MeleeSwings, CombatCapabilityIds.MeleeSwings, current);
        Set(values.MeleeHits, CombatCapabilityIds.MeleeHits, current);
        Set(values.EnemiesKilled, CombatCapabilityIds.EnemiesKilled, current);
        Set(values.PlayerDeaths, CombatCapabilityIds.PlayerDeaths, current);
        Set(values.Ownership, CombatCapabilityIds.Ownership, current);
        Set(values.EnemyIdentity, CombatCapabilityIds.EnemyIdentity, current);
        Set(values.EnemyFamily, CombatCapabilityIds.EnemyFamily, current);
        Set(values.Cause, CombatCapabilityIds.Cause, current);
        Set(values.WeaponIdentity, CombatCapabilityIds.WeaponIdentity, current);
        Set(values.AmmunitionIdentity, CombatCapabilityIds.AmmunitionIdentity, current);
        Set(values.DamageOverTime, CombatCapabilityIds.DamageOverTime, current);
        Set(values.Headshots, CombatCapabilityIds.Headshots, current);
        Set(values.HeadshotFinalBlows, CombatCapabilityIds.HeadshotFinalBlows, current);

        void Set(MetricAvailability value, string id, IReadOnlyList<CapabilityRecord> observed)
        {
            var state = observed.FirstOrDefault(x => string.Equals(x.AdapterId, id, StringComparison.Ordinal))?.State
                        ?? AdapterCapabilityState.DisabledIncompatible;
            value.State = CombatStatisticsReducer.ResolveCurrentAvailability(aggregate, value, state);
        }
    }
}
