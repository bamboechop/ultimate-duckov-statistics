using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Adapters;

internal enum DeferredWriteState
{
    None = 0,
    Pending = 1,
    Succeeded = 2,
    Failed = 3
}

internal sealed class DeferredWriteResult
{
    private DeferredWriteResult(DeferredWriteState state, Exception? exception)
    {
        State = state;
        Exception = exception;
    }

    public DeferredWriteState State { get; }
    public Exception? Exception { get; }

    public static DeferredWriteResult None { get; } = new(DeferredWriteState.None, null);
    public static DeferredWriteResult Pending { get; } = new(DeferredWriteState.Pending, null);
    public static DeferredWriteResult Succeeded { get; } = new(DeferredWriteState.Succeeded, null);
    public static DeferredWriteResult Failed(Exception exception) => new(
        DeferredWriteState.Failed,
        exception ?? throw new ArgumentNullException(nameof(exception)));
}

internal sealed class DeferredCheckpointWriter<T>
    where T : class
{
    private readonly Action<T> write;
    private Task<Exception?>? pending;

    public DeferredCheckpointWriter(Action<T> write)
    {
        this.write = write ?? throw new ArgumentNullException(nameof(write));
    }

    public bool TrySubmit(T value)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));
        if (pending != null) return false;
        pending = Task.Run(() =>
        {
            try
            {
                write(value);
                return null;
            }
            catch (Exception exception)
            {
                return exception;
            }
        });
        return true;
    }

    public DeferredWriteResult Poll()
    {
        if (pending == null) return DeferredWriteResult.None;
        return pending.IsCompleted ? Consume() : DeferredWriteResult.Pending;
    }

    public DeferredWriteResult Wait()
    {
        if (pending == null) return DeferredWriteResult.None;
        var exception = pending.GetAwaiter().GetResult();
        return exception == null ? DeferredWriteResult.Succeeded : DeferredWriteResult.Failed(exception);
    }

    public DeferredWriteResult Flush() => pending == null ? DeferredWriteResult.None : Consume();

    private DeferredWriteResult Consume()
    {
        var current = pending ?? throw new InvalidOperationException("No deferred checkpoint is pending.");
        var exception = current.GetAwaiter().GetResult();
        pending = null;
        return exception == null ? DeferredWriteResult.Succeeded : DeferredWriteResult.Failed(exception);
    }
}

internal sealed class DeferredSnapshotWriter<T>
    where T : class
{
    private readonly Func<T> capture;
    private readonly DeferredCheckpointWriter<T> writer;
    private bool dirty;
    private readonly PersistenceRetryBackoff retry;
    private DeferredWriteResult? lastFailure;

    public DeferredSnapshotWriter(Func<T> capture, Action<T> write, Func<double>? clock = null)
    {
        retry = new PersistenceRetryBackoff(clock);
        this.capture = capture ?? throw new ArgumentNullException(nameof(capture));
        writer = new DeferredCheckpointWriter<T>(write);
    }

    public bool IsDirty => dirty;

    public bool HasFailure => lastFailure != null;

    public void MarkDirty()
    {
        dirty = true;
    }

    public DeferredWriteResult Tick(bool allowSubmit = true)
    {
        var observed = writer.Poll();
        if (observed.State == DeferredWriteState.Pending)
        {
            return observed;
        }

        if (observed.State == DeferredWriteState.Failed)
        {
            RememberFailure(observed);
            return observed;
        }

        if (observed.State == DeferredWriteState.Succeeded) ClearFailure();
        if (!retry.IsDue) return lastFailure ?? observed;
        if (!dirty || !allowSubmit)
        {
            return observed;
        }

        return TryCaptureAndSubmit();
    }

    public DeferredWriteResult Flush()
    {
        // Explicit flushes share the same budget as frame-driven submissions.
        // A delayed failure remains failure, never a successful durability barrier.
        if (!retry.IsDue) return lastFailure ?? DeferredWriteResult.Pending;
        Exception? firstFailure = null;
        var retryUsed = false;
        while (true)
        {
            var observed = writer.Flush();
            if (observed.State == DeferredWriteState.Failed)
            {
                dirty = true;
                firstFailure ??= observed.Exception;
                if (retryUsed)
                {
                    var failed = DeferredWriteResult.Failed(
                        firstFailure == null
                            ? observed.Exception ?? new IOException("Deferred snapshot persistence failed.")
                            : new AggregateException(
                                "Deferred snapshot persistence failed and its bounded retry also failed.",
                                firstFailure,
                                observed.Exception ?? new IOException("Deferred snapshot retry failed.")));
                    RememberFailure(failed);
                    return failed;
                }

                retryUsed = true;
            }

            if (!dirty)
            {
                ClearFailure();
                return observed;
            }

            var submitted = TryCaptureAndSubmit();
            if (submitted.State == DeferredWriteState.Failed)
            {
                return submitted;
            }
        }
    }

    private void RememberFailure(DeferredWriteResult failure)
    {
        dirty = true;
        lastFailure = failure;
        retry.Failed();
    }

    private void ClearFailure()
    {
        lastFailure = null;
        retry.Reset();
    }

    private DeferredWriteResult TryCaptureAndSubmit()
    {
        try
        {
            var snapshot = capture();
            if (!writer.TrySubmit(snapshot))
            {
                return DeferredWriteResult.Pending;
            }

            dirty = false;
            return DeferredWriteResult.Pending;
        }
        catch (Exception exception)
        {
            var failed = DeferredWriteResult.Failed(exception);
            RememberFailure(failed);
            return failed;
        }
    }
}
