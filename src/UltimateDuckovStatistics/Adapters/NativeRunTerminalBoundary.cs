using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class NativeRunTerminalBoundary
{
    private readonly Func<double> clock;
    private readonly PersistenceRetryBackoff retry;
    private readonly MonotonicCadenceGate diagnosticCadence = new(60);
    private Func<bool>? terminalObserver;
    private RunLifecycleEvent? pendingTerminalEvent;

    public bool IsPreparingTerminal { get; private set; }

    public bool HasPendingTerminal => pendingTerminalEvent != null;

    public RunLifecycleEvent? PendingTerminalEvent => pendingTerminalEvent;

    public NativeRunTerminalBoundary(Func<double>? clock = null)
    {
        this.clock = clock ?? (() => (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency);
        retry = new PersistenceRetryBackoff(this.clock);
    }

    public void SetTerminalObserver(Func<bool>? observer) => terminalObserver = observer;

    public RunLifecycleTransition Apply(
        RunLifecycleTracker tracker,
        RunLifecycleEvent lifecycleEvent,
        Action<string> diagnosticHandler,
        Func<bool> checkpointObserver)
    {
        if (tracker == null) throw new ArgumentNullException(nameof(tracker));
        if (lifecycleEvent == null) throw new ArgumentNullException(nameof(lifecycleEvent));
        if (diagnosticHandler == null) throw new ArgumentNullException(nameof(diagnosticHandler));
        if (checkpointObserver == null) throw new ArgumentNullException(nameof(checkpointObserver));
        if (pendingTerminalEvent != null)
            throw new InvalidOperationException("A pending terminal event must be retried before applying another event.");

        if (tracker.WillComplete(lifecycleEvent))
        {
            pendingTerminalEvent = lifecycleEvent;
            retry.Reset();
            diagnosticCadence.Reset();
            bool observed;
            IsPreparingTerminal = true;
            try { observed = ObserveTerminalCandidate(tracker, lifecycleEvent, diagnosticHandler); }
            finally { IsPreparingTerminal = false; }
            if (!observed)
            {
                Defer("queued economy was not accepted", diagnosticHandler);
                return new RunLifecycleTransition();
            }
            if (!PersistPendingCheckpoint(_ => checkpointObserver(), diagnosticHandler))
            {
                return new RunLifecycleTransition();
            }

            pendingTerminalEvent = null;
        }
        return tracker.Apply(lifecycleEvent);
    }

    public RunLifecycleTransition Retry(
        RunLifecycleTracker tracker,
        Action<string> diagnosticHandler,
        Func<RunLifecycleEvent, bool> checkpointObserver)
    {
        if (tracker == null) throw new ArgumentNullException(nameof(tracker));
        if (diagnosticHandler == null) throw new ArgumentNullException(nameof(diagnosticHandler));
        if (checkpointObserver == null) throw new ArgumentNullException(nameof(checkpointObserver));
        if (pendingTerminalEvent == null) return new RunLifecycleTransition();
        if (!tracker.IsActive)
        {
            pendingTerminalEvent = null;
            return new RunLifecycleTransition();
        }

        if (!retry.IsDue) return new RunLifecycleTransition();

        if (!ObserveTerminalCandidate(tracker, pendingTerminalEvent, diagnosticHandler))
        {
            Defer("queued economy was not accepted", diagnosticHandler);
            return new RunLifecycleTransition();
        }
        if (!PersistPendingCheckpoint(checkpointObserver, diagnosticHandler))
        {
            return new RunLifecycleTransition();
        }

        var lifecycleEvent = pendingTerminalEvent;
        pendingTerminalEvent = null;
        return tracker.Apply(lifecycleEvent);
    }

    public bool PersistPendingCheckpoint(Func<RunLifecycleEvent, bool> checkpointObserver, Action<string> diagnosticHandler)
    {
        if (checkpointObserver == null) throw new ArgumentNullException(nameof(checkpointObserver));
        if (diagnosticHandler == null) throw new ArgumentNullException(nameof(diagnosticHandler));
        if (pendingTerminalEvent == null) return true;
        if (!retry.IsDue) return false;
        try
        {
            if (checkpointObserver(pendingTerminalEvent))
            {
                retry.Reset();
                return true;
            }
            Defer("the refreshed active-run checkpoint was not durable", diagnosticHandler);
        }
        catch (Exception exception)
        {
            Defer($"checkpoint persistence failed: {exception}", diagnosticHandler);
        }
        return false;
    }

    private void Defer(string reason, Action<string> diagnosticHandler)
    {
        retry.Failed();
        var now = clock();
        if (!diagnosticCadence.IsDue(now)) return;
        diagnosticCadence.MarkCompleted(now);
        diagnosticHandler($"Run terminalization deferred because {reason}; pending outcome retained with backoff up to 60s.");
    }

    public bool ObserveTerminalCandidate(
        RunLifecycleTracker tracker,
        RunLifecycleEvent lifecycleEvent,
        Action<string> diagnosticHandler)
    {
        if (tracker == null) throw new ArgumentNullException(nameof(tracker));
        if (lifecycleEvent == null) throw new ArgumentNullException(nameof(lifecycleEvent));
        if (diagnosticHandler == null) throw new ArgumentNullException(nameof(diagnosticHandler));

        if (!tracker.WillComplete(lifecycleEvent)) return true;
        try
        {
            return terminalObserver?.Invoke() != false;
        }
        catch (Exception exception)
        {
            diagnosticHandler($"Pre-terminal observer failed safely: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

}
