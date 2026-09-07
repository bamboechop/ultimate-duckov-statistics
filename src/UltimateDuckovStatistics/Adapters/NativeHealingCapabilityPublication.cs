using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Adapters;

internal static class NativeHealingCapabilityPublication
{
    internal static void Publish(CapabilityRecord capability, NativeRunLifecycleAdapter? lifecycle,
        NativeProfileCoordinator? coordinator)
    {
        // Clear live completeness before any storage operation can interrupt publication.
        lifecycle?.SetHealingCapability(capability);
        coordinator?.SetHealingCapability(capability);
    }
}
