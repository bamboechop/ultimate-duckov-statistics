using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Tests;

public sealed class CheckpointRetryGateTests
{
    [Fact]
    [Trait("Category", "Run")]
    [Trait("Category", "Persistence")]
    public void FailedCheckpointIsRetriedAtBoundedCadenceAndDirtyPeriodicSignalsCoalesce()
    {
        var gate = new CheckpointRetryGate(1);

        Assert.True(gate.ShouldAttempt(
            combatCheckpointRequired: true,
            periodicCheckpointDue: false,
            monotonicSeconds: 10));
        gate.RecordResult(succeeded: false, monotonicSeconds: 10);

        Assert.False(gate.ShouldAttempt(true, false, 10.01));
        Assert.False(gate.ShouldAttempt(true, true, 10.99));
        Assert.True(gate.ShouldAttempt(true, true, 11));
        gate.RecordResult(succeeded: false, monotonicSeconds: 11);
        Assert.False(gate.ShouldAttempt(true, true, 11.5));

        Assert.True(gate.ShouldAttempt(true, true, 12));
        gate.RecordResult(succeeded: true, monotonicSeconds: 12);
        Assert.True(gate.ShouldAttempt(false, true, 12.01));
    }

    [Fact]
    [Trait("Category", "Run")]
    [Trait("Category", "Persistence")]
    public void NoDirtyOrPeriodicCheckpointDoesNotAttemptAWrite()
    {
        var gate = new CheckpointRetryGate(1);

        Assert.False(gate.ShouldAttempt(false, false, 0));
    }
}
