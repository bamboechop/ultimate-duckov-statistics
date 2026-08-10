using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Tests;

public sealed class MonotonicCadenceGateTests
{
    [Fact]
    [Trait("Category", "Movement")]
    public void SamplingUsesActualMonotonicDueTimesAtApproximatelyFiveHertz()
    {
        var cadence = new MonotonicCadenceGate(0.2);

        Assert.True(cadence.IsDue(10));
        cadence.MarkCompleted(10);
        Assert.False(cadence.IsDue(10.199));
        Assert.True(cadence.IsDue(10.2));

        cadence.MarkCompleted(10.47);
        Assert.False(cadence.IsDue(10.669));
        Assert.True(cadence.IsDue(10.67));

        cadence.Reset();
        Assert.True(cadence.IsDue(100));
    }
}
