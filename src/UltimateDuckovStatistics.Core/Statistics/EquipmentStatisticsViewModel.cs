using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Core.Statistics;

public sealed class EquipmentStatisticsViewModel
{
    public EquipmentStatisticsAggregate Lifetime { get; set; } = new();
    public EquipmentMetricCapabilities Capabilities { get; set; } = new();
}

public static class EquipmentStatisticsViewModelFactory
{
    public static EquipmentStatisticsViewModel Create(ProfileDocument profile)
    {
        if (profile == null) throw new ArgumentNullException(nameof(profile));
        var aggregate = profile.Statistics.RunTotals.EquipmentStatistics;
        var capabilities = EquipmentStatisticsReducer.CloneCapabilities(aggregate.Capabilities);
        Apply(capabilities.EquipmentSlots, EquipmentCapabilityIds.EquipmentSlots);
        Apply(capabilities.SelectedWeapon, EquipmentCapabilityIds.SelectedWeapon);
        Apply(capabilities.AttachmentMetadata, EquipmentCapabilityIds.AttachmentMetadata);
        Apply(capabilities.DirectTotems, EquipmentCapabilityIds.DirectTotems);
        Apply(capabilities.ToteContents, EquipmentCapabilityIds.ToteContents);
        Apply(capabilities.ToteActivation, EquipmentCapabilityIds.ToteActivation);
        return new EquipmentStatisticsViewModel { Lifetime = aggregate, Capabilities = capabilities };

        void Apply(MetricAvailability recorded, string id)
        {
            var current = profile.Capabilities.FirstOrDefault(value =>
                string.Equals(value.AdapterId, id, StringComparison.Ordinal));
            EquipmentStatisticsReducer.ApplyCurrentAvailability(
                aggregate,
                recorded,
                current?.State ?? AdapterCapabilityState.DisabledIncompatible,
                current?.Detail,
                allowUninitializedFallback: true);
        }
    }
}
