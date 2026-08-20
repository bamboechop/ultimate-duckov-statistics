using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class NativeWorldTimePersistenceCadence
{
    internal const double PublicationIntervalSeconds = 1;
    internal const double DurablePersistenceIntervalSeconds = 30;
    internal const double PersistenceRetryIntervalSeconds = 1;

    private readonly MonotonicCadenceGate publicationCadence = new(PublicationIntervalSeconds);
    private readonly MonotonicCadenceGate persistenceCadence = new(DurablePersistenceIntervalSeconds);
    private readonly MonotonicCadenceGate persistenceRetryCadence = new(PersistenceRetryIntervalSeconds);
    private bool hasUnpersistedChanges;
    private bool persistenceRetryPending;

    public bool HasUnpersistedChanges => hasUnpersistedChanges;

    public void Start(double monotonicSeconds)
    {
        publicationCadence.Reset();
        publicationCadence.MarkCompleted(monotonicSeconds);
        persistenceCadence.Reset();
        persistenceCadence.MarkCompleted(monotonicSeconds);
        persistenceRetryCadence.Reset();
        hasUnpersistedChanges = false;
        persistenceRetryPending = false;
    }

    public bool ShouldPublish(double monotonicSeconds) => publicationCadence.IsDue(monotonicSeconds);

    public void RecordPublicationAttempt(bool succeeded, bool changed, double monotonicSeconds)
    {
        publicationCadence.MarkCompleted(monotonicSeconds);
        if (succeeded && changed)
        {
            hasUnpersistedChanges = true;
        }
    }

    public bool ShouldSchedulePersistence(double monotonicSeconds, bool force = false) =>
        hasUnpersistedChanges
        && (force
            || (persistenceRetryPending
                ? persistenceRetryCadence.IsDue(monotonicSeconds)
                : persistenceCadence.IsDue(monotonicSeconds)));

    public void RecordPersistenceAttempt(bool succeeded, double monotonicSeconds)
    {
        if (succeeded)
        {
            hasUnpersistedChanges = false;
            persistenceRetryPending = false;
            persistenceCadence.MarkCompleted(monotonicSeconds);
            persistenceRetryCadence.Reset();
            return;
        }

        persistenceRetryPending = true;
        persistenceRetryCadence.MarkCompleted(monotonicSeconds);
    }
}
