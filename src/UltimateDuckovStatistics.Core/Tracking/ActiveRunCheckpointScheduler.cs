namespace UltimateDuckovStatistics.Core.Tracking;

public sealed class ActiveRunCheckpointScheduler
{
    private readonly MonotonicCadenceGate dirtyCadence;
    private readonly CheckpointRetryGate retryGate;

    public ActiveRunCheckpointScheduler(double dirtyIntervalSeconds, double retryIntervalSeconds)
    {
        dirtyCadence = new MonotonicCadenceGate(dirtyIntervalSeconds);
        retryGate = new CheckpointRetryGate(retryIntervalSeconds);
    }

    public bool ShouldAttempt(bool dirty, bool periodicCheckpointDue, double monotonicSeconds)
    {
        var coalescedDirtyCheckpointDue = dirty && dirtyCadence.IsDue(monotonicSeconds);
        return retryGate.ShouldAttempt(coalescedDirtyCheckpointDue, periodicCheckpointDue, monotonicSeconds);
    }

    public void RecordResult(bool succeeded, double monotonicSeconds)
    {
        if (succeeded)
        {
            dirtyCadence.MarkCompleted(monotonicSeconds);
        }

        retryGate.RecordResult(succeeded, monotonicSeconds);
    }

    public void Reset()
    {
        dirtyCadence.Reset();
        retryGate.Reset();
    }
}
