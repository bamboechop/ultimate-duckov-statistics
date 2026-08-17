using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class NativeRunTerminalBoundary
{
    private Action? terminalObserver;
    private RunLifecycleEvent? pendingTerminalEvent;

    public bool HasPendingTerminal => pendingTerminalEvent != null;

    public RunLifecycleEvent? PendingTerminalEvent => pendingTerminalEvent;

    public void SetTerminalObserver(Action? observer) => terminalObserver = observer;

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

        ObserveTerminalCandidate(tracker, lifecycleEvent.Kind, diagnosticHandler);
        if (tracker.IsActive && IsTerminalCandidate(lifecycleEvent.Kind))
        {
            pendingTerminalEvent = lifecycleEvent;
            if (!checkpointObserver())
            {
                diagnosticHandler("Run terminalization deferred because the refreshed active-run checkpoint was not durable.");
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

        if (!checkpointObserver(pendingTerminalEvent))
        {
            diagnosticHandler("Run terminalization remains deferred because the refreshed active-run checkpoint was not durable.");
            return new RunLifecycleTransition();
        }

        var lifecycleEvent = pendingTerminalEvent;
        pendingTerminalEvent = null;
        return tracker.Apply(lifecycleEvent);
    }

    public void ObserveTerminalCandidate(
        RunLifecycleTracker tracker,
        RunLifecycleEventKind kind,
        Action<string> diagnosticHandler)
    {
        if (tracker == null) throw new ArgumentNullException(nameof(tracker));
        if (diagnosticHandler == null) throw new ArgumentNullException(nameof(diagnosticHandler));

        if (tracker.IsActive && IsTerminalCandidate(kind))
        {
            try { terminalObserver?.Invoke(); }
            catch (Exception exception)
            {
                diagnosticHandler($"Pre-terminal observer failed safely: {exception.GetType().Name}: {exception.Message}");
            }
        }
    }

    private static bool IsTerminalCandidate(RunLifecycleEventKind kind) => kind is
        RunLifecycleEventKind.RaidInitialized
        or RunLifecycleEventKind.Extracted
        or RunLifecycleEventKind.Died
        or RunLifecycleEventKind.Interrupted;
}
