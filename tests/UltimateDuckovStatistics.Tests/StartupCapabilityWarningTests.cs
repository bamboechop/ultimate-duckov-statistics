using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Tests;

public sealed class StartupCapabilityWarningTests
{
    private static readonly string[] ExpectedDisabled = { "headshots", "healing" };
    [Fact]
    public void WaitsForStartupThenReportsAllDisabledCapabilitiesOnce()
    {
        var policy = new StartupCapabilityWarningPolicy();
        CapabilityRecord[] records = [new() { AdapterId = "headshots", State = AdapterCapabilityState.DisabledIncompatible },
            new() { AdapterId = "damage", State = AdapterCapabilityState.Supported },
            new() { AdapterId = "headshots", State = AdapterCapabilityState.DisabledIncompatible },
            new() { AdapterId = "healing", State = AdapterCapabilityState.DisabledIncompatible }];
        Assert.Empty(policy.Check(records, 1));
        Assert.Equal(ExpectedDisabled, policy.Check(records, 2));
        Assert.Empty(policy.Check(records, 3));
        Assert.Equal(2, new StartupCapabilityWarningPolicy().Check(records, 2).Count);
    }
    [Fact]
    public void SupportedAndExperimentalCapabilitiesDoNotWarn()
    {
        CapabilityRecord[] records = [new() { State = AdapterCapabilityState.Supported }, new() { State = AdapterCapabilityState.Experimental }];
        Assert.Empty(new StartupCapabilityWarningPolicy().Check(records, 2));
    }
}
