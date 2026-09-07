using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class NativeProfileTransitionBoundary
{
    private readonly Queue<PendingTransition> pending = new();
    private readonly Func<double> clock;

    public NativeProfileTransitionBoundary(Func<double>? clock = null) =>
        this.clock = clock ?? (() => (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency);

    public bool HasPendingTransition => pending.Count > 0;

    public void Enqueue(string description, params Action[] steps)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("A profile transition description is required.", nameof(description));
        if (steps == null) throw new ArgumentNullException(nameof(steps));
        if (steps.Length == 0 || steps.Any(step => step == null))
            throw new ArgumentException("A profile transition requires at least one non-null step.", nameof(steps));
        pending.Enqueue(new PendingTransition(
            description,
            steps, clock));
    }

    public bool Retry(Func<bool>? boundaryObserver, Action<string> diagnosticHandler)
    {
        if (diagnosticHandler == null) throw new ArgumentNullException(nameof(diagnosticHandler));
        if (pending.Count == 0) return true;

        var current = pending.Peek();
        if (!current.Backoff.IsDue) return false;
        try
        {
            if (boundaryObserver?.Invoke() == false)
            {
                Defer(current, "queued boundary observations were not accepted", diagnosticHandler);
                return false;
            }
        }
        catch (Exception exception)
        {
            Defer(current, $"boundary failed: {exception.GetType().Name}: {exception.Message}", diagnosticHandler);
            return false;
        }

        try
        {
            while (current.NextStep < current.Steps.Count)
            {
                current.Steps[current.NextStep]();
                current.NextStep++;
                current.Backoff.Reset();
                current.DiagnosticCadence.Reset();
            }
            pending.Dequeue();
            return pending.Count == 0;
        }
        catch (Exception exception)
        {
            Defer(current, $"step failed: {exception.GetType().Name}: {exception.Message}", diagnosticHandler);
            return false;
        }
    }

    private void Defer(PendingTransition current, string reason, Action<string> diagnosticHandler)
    {
        current.Backoff.Failed();
        var now = clock();
        if (!current.DiagnosticCadence.IsDue(now)) return;
        current.DiagnosticCadence.MarkCompleted(now);
        diagnosticHandler($"{current.Description} remains queued because {reason}; retry backoff is bounded at 60s.");
    }

    public bool Drain(Func<bool>? boundaryObserver, Action<string> diagnosticHandler)
    {
        if (diagnosticHandler == null) throw new ArgumentNullException(nameof(diagnosticHandler));
        while (pending.Count > 0)
        {
            var current = pending.Peek();
            var pendingCount = pending.Count;
            var nextStep = current.NextStep;
            Retry(boundaryObserver, diagnosticHandler);
            if (pending.Count == 0) return true;
            if (pending.Count < pendingCount || current.NextStep > nextStep) continue;
            return false;
        }
        return true;
    }

    private sealed class PendingTransition
    {
        public PendingTransition(string description, IReadOnlyList<Action> steps, Func<double> clock)
        {
            Description = description;
            Steps = steps;
            Backoff = new PersistenceRetryBackoff(clock);
        }

        public string Description { get; }

        public IReadOnlyList<Action> Steps { get; }

        public int NextStep { get; set; }

        public PersistenceRetryBackoff Backoff { get; }

        public MonotonicCadenceGate DiagnosticCadence { get; } = new(60);
    }
}
