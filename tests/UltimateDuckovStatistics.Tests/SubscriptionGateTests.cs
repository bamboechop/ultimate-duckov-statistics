using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Tests;

public sealed class SubscriptionGateTests
{
    [Fact]
    [Trait("Category", "ItemUse")]
    public void RepeatedSetupSceneChangesAndDeactivationAreIdempotent()
    {
        var gate = new SubscriptionGate();

        Assert.True(gate.TryActivate());
        Assert.False(gate.TryActivate());
        Assert.False(gate.TryActivate());
        Assert.True(gate.IsActive);

        Assert.True(gate.TryDeactivate());
        Assert.False(gate.TryDeactivate());
        Assert.False(gate.IsActive);

        Assert.True(gate.TryActivate());
        Assert.True(gate.IsActive);
    }
}
