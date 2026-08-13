using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Tests;

public sealed class ActiveRunCheckpointSchedulerTests
{
    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "Performance")]
    public void SuccessfulHighRateDirtyEventsAreCoalescedToTheConfiguredCadence()
    {
        var scheduler = new ActiveRunCheckpointScheduler(
            dirtyIntervalSeconds: 1,
            retryIntervalSeconds: 1);

        Assert.True(scheduler.ShouldAttempt(dirty: true, periodicCheckpointDue: false, monotonicSeconds: 10));
        scheduler.RecordResult(succeeded: true, monotonicSeconds: 10);

        for (var shot = 1; shot < 100; shot++)
        {
            Assert.False(scheduler.ShouldAttempt(
                dirty: true,
                periodicCheckpointDue: false,
                monotonicSeconds: 10 + shot / 100d));
        }

        Assert.True(scheduler.ShouldAttempt(dirty: true, periodicCheckpointDue: false, monotonicSeconds: 11));
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void FailedWriteRetainsTheBoundedRetryCadence()
    {
        var scheduler = new ActiveRunCheckpointScheduler(1, 1);

        Assert.True(scheduler.ShouldAttempt(true, false, 10));
        scheduler.RecordResult(succeeded: false, monotonicSeconds: 10);

        Assert.False(scheduler.ShouldAttempt(true, true, 10.99));
        Assert.True(scheduler.ShouldAttempt(true, true, 11));
    }
}
