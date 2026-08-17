using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class NativeRunTerminalBoundary
{
    private Action? terminalObserver;

    public void SetTerminalObserver(Action? observer) => terminalObserver = observer;

    public RunLifecycleTransition Apply(
        RunLifecycleTracker tracker,
        RunLifecycleEvent lifecycleEvent,
        Action<string> diagnosticHandler)
    {
        if (tracker == null) throw new ArgumentNullException(nameof(tracker));
        if (lifecycleEvent == null) throw new ArgumentNullException(nameof(lifecycleEvent));
        if (diagnosticHandler == null) throw new ArgumentNullException(nameof(diagnosticHandler));

        ObserveTerminalCandidate(tracker, lifecycleEvent.Kind, diagnosticHandler);
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
