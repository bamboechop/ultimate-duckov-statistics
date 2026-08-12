using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Core.Statistics;

public sealed class ContainerStatisticsViewModel
{
    public ContainerStatisticsAggregate Lifetime { get; set; } = new();
    public AdapterCapabilityState CurrentCapability { get; set; }
    public string CapabilityDetail { get; set; } = string.Empty;
}

public static class ContainerStatisticsViewModelFactory
{
    public static ContainerStatisticsViewModel Create(ProfileDocument profile)
    {
        if (profile == null) throw new ArgumentNullException(nameof(profile));
        var aggregate = ContainerStatisticsReducer.Clone(profile.Statistics.RunTotals.ContainerStatistics);
        var current = profile.Capabilities.FirstOrDefault(value =>
            string.Equals(value.AdapterId, ContainerCapabilityIds.UniqueContainersLooted, StringComparison.Ordinal));
        ContainerStatisticsReducer.ApplyCurrentAvailability(
            aggregate,
            current?.State ?? AdapterCapabilityState.DisabledIncompatible,
            current?.Detail);
        return new ContainerStatisticsViewModel
        {
            Lifetime = aggregate,
            CurrentCapability = aggregate.Capabilities.UniqueContainersLooted.State,
            CapabilityDetail = aggregate.Capabilities.UniqueContainersLooted.Provenance
        };
    }
}
