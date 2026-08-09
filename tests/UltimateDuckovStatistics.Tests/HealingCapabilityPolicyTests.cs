using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Tests;

public sealed class HealingCapabilityPolicyTests
{
    [Fact]
    [Trait("Category", "Healing")]
    public void AvailableContractsReportSupported()
    {
        Assert.Equal(
            AdapterCapabilityState.Supported,
            HealingCapabilityPolicy.GetState(HealingCapabilityCondition.Available));
    }

    [Theory]
    [Trait("Category", "Healing")]
    [InlineData(HealingCapabilityCondition.MissingContracts)]
    [InlineData(HealingCapabilityCondition.MissingHarmony)]
    [InlineData(HealingCapabilityCondition.IncompatibleHarmony)]
    [InlineData(HealingCapabilityCondition.UnsafeHarmonyPatchSet)]
    [InlineData(HealingCapabilityCondition.ActivationFailure)]
    public void UnsafeOrUnavailableConditionsDisableAttribution(HealingCapabilityCondition condition)
    {
        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            HealingCapabilityPolicy.GetState(condition));
    }
}
