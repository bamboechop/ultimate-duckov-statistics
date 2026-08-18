using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class NativeRunTerminalBoundary
{
    private Func<bool>? terminalObserver;
    private RunLifecycleEvent? pendingTerminalEvent;

    public bool HasPendingTerminal => pendingTerminalEvent != null;

    public RunLifecycleEvent? PendingTerminalEvent => pendingTerminalEvent;

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
            if (!ObserveTerminalCandidate(tracker, lifecycleEvent, diagnosticHandler))
            {
                diagnosticHandler("Run terminalization deferred because queued economy was not accepted.");
                return new RunLifecycleTransition();
            }
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

        if (!ObserveTerminalCandidate(tracker, pendingTerminalEvent, diagnosticHandler))
        {
            diagnosticHandler("Run terminalization remains deferred because queued economy was not accepted.");
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
