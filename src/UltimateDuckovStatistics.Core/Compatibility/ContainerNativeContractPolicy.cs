using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Compatibility;

public static class ContainerCapabilityIds
{
    public const string UniqueContainersLooted = "native-container-loot-access";
}

public static class ContainerNativeContractPolicy
{
    public static ContainerMetricCapabilities Supported() => new()
    {
        UniqueContainersLooted = new MetricAvailability
        {
            State = AdapterCapabilityState.Supported,
            Provenance = "InteractableLootbox.OnStartLoot proves successful access; private GetKey supplies the version-checked per-run identity; native death paths mark corpse lootboxes for exclusion."
        }
    };

    public static ContainerMetricCapabilities Unavailable(string detail) => new()
    {
        UniqueContainersLooted = new MetricAvailability
        {
            State = AdapterCapabilityState.DisabledIncompatible,
            Provenance = detail
        }
    };

    public static CapabilityRecord ToRecord(ContainerMetricCapabilities value, string version) => new()
    {
        AdapterId = ContainerCapabilityIds.UniqueContainersLooted,
        State = value.UniqueContainersLooted.State,
        Version = version,
        Detail = value.UniqueContainersLooted.Provenance
    };
}
