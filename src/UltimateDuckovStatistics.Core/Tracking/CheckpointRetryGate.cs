namespace UltimateDuckovStatistics.Core.Tracking;

public sealed class CheckpointRetryGate
{
    private readonly MonotonicCadenceGate retryCadence;
    private bool retryPending;

    public CheckpointRetryGate(double retryIntervalSeconds)
    {
        retryCadence = new MonotonicCadenceGate(retryIntervalSeconds);
    }

    public bool ShouldAttempt(
        bool combatCheckpointRequired,
        bool periodicCheckpointDue,
        double monotonicSeconds)
    {
        if (!combatCheckpointRequired && !periodicCheckpointDue)
        {
            return false;
        }

        return !retryPending || retryCadence.IsDue(monotonicSeconds);
    }

    public void RecordResult(bool succeeded, double monotonicSeconds)
    {
        if (succeeded)
        {
            Reset();
            return;
        }

        retryPending = true;
        retryCadence.MarkCompleted(monotonicSeconds);
    }

    public void Reset()
    {
        retryPending = false;
        retryCadence.Reset();
    }
}
