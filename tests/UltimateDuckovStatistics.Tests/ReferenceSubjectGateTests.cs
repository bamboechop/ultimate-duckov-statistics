using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Tests;

public sealed class ReferenceSubjectGateTests
{
    [Fact]
    [Trait("Category", "Movement")]
    public void OnlyTheExactRetainedMainSubjectIsAcceptedAndReplacementInvalidatesTheOldOne()
    {
        var mainDuck = new object();
        var companion = new object();
        var replacementMainDuck = new object();
        var gate = new ReferenceSubjectGate<object>();

        Assert.True(gate.Replace(mainDuck));
        Assert.True(gate.Accepts(mainDuck));
        Assert.False(gate.Accepts(companion));
        Assert.False(gate.Replace(mainDuck));
        Assert.True(gate.Replace(replacementMainDuck));
        Assert.False(gate.Accepts(mainDuck));
        Assert.True(gate.Accepts(replacementMainDuck));
        gate.Clear();
        Assert.False(gate.Accepts(replacementMainDuck));
    }
}
