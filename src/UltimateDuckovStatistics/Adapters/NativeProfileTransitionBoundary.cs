namespace UltimateDuckovStatistics.Adapters;

internal sealed class NativeProfileTransitionBoundary
{
    private readonly Queue<PendingTransition> pending = new();

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
            steps));
    }

    public bool Retry(Func<bool>? boundaryObserver, Action<string> diagnosticHandler)
    {
        if (diagnosticHandler == null) throw new ArgumentNullException(nameof(diagnosticHandler));
        if (pending.Count == 0) return true;

        var current = pending.Peek();
        try
        {
            if (boundaryObserver?.Invoke() == false)
            {
                diagnosticHandler($"{current.Description} remains deferred because queued boundary observations were not accepted.");
                return false;
            }
        }
        catch (Exception exception)
        {
            diagnosticHandler($"{current.Description} boundary failed safely and remains queued: {exception.GetType().Name}: {exception.Message}");
            return false;
        }

        try
        {
            while (current.NextStep < current.Steps.Count)
            {
                current.Steps[current.NextStep]();
                current.NextStep++;
            }
            pending.Dequeue();
            return pending.Count == 0;
        }
        catch (Exception exception)
        {
            diagnosticHandler($"{current.Description} failed and remains queued for retry: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
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
        public PendingTransition(string description, IReadOnlyList<Action> steps)
        {
            Description = description;
            Steps = steps;
        }

        public string Description { get; }

        public IReadOnlyList<Action> Steps { get; }

        public int NextStep { get; set; }
    }
}
