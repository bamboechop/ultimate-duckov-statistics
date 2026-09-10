using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Adapters;

internal sealed partial class NativeProfileCoordinator
{
    private Func<bool>? baseMovementBoundaryPublisher;
    private readonly MonotonicCadenceGate baseMovementPersistenceCadence = new(5);
    private bool baseMovementDirty;
    private bool baseMovementWriteQueued;

    public void SetBaseMovementBoundaryPublisher(Func<bool> publisher) =>
        baseMovementBoundaryPublisher = publisher ?? throw new ArgumentNullException(nameof(publisher));

    public bool HandleBaseMovement(BaseMovementUpdate update)
    {
        try
        {
            if (repository == null) return false;
            if (repository.RecordBaseMovementDeferred(update)) baseMovementDirty = true;
            return true;
        }
        catch (Exception exception)
        {
            ReportPersistenceFailure(exception, "Base movement publication failed");
            return false;
        }
    }

    public bool FlushBaseMovement()
    {
        try
        {
            if (baseMovementBoundaryPublisher?.Invoke() == false) return false;
            QueueBaseMovementPersistence(force: true);
            if (!baseMovementWriteQueued) return true;
            DrainProfileWriter();
            baseMovementWriteQueued = false;
            return true;
        }
        catch (Exception exception)
        {
            ReportPersistenceFailure(exception, "Base movement durability remains pending");
            return false;
        }
    }

    private void QueueBaseMovementPersistence(bool force = false)
    {
        var now = monotonicClock();
        if (!baseMovementDirty || (!force && !baseMovementPersistenceCadence.IsDue(now))) return;
        profileWriter.MarkDirty();
        baseMovementWriteQueued = true;
        baseMovementDirty = false;
        baseMovementPersistenceCadence.MarkCompleted(now);
    }
}
