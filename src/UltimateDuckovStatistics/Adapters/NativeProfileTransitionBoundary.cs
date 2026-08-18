namespace UltimateDuckovStatistics.Adapters;

internal sealed class NativeProfileTransitionBoundary
{
    private readonly Queue<PendingTransition> pending = new();

    public bool HasPendingTransition => pending.Count > 0;

    public void Enqueue(string description, Action transition)
    {
        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("A profile transition description is required.", nameof(description));
        pending.Enqueue(new PendingTransition(
            description,
            transition ?? throw new ArgumentNullException(nameof(transition))));
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
                diagnosticHandler($"{current.Description} remains deferred because queued economy was not accepted.");
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
            current.Apply();
            pending.Dequeue();
            return pending.Count == 0;
        }
        catch (Exception exception)
        {
            diagnosticHandler($"{current.Description} failed and remains queued for retry: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private sealed class PendingTransition
    {
        public PendingTransition(string description, Action apply)
        {
            Description = description;
            Apply = apply;
        }

        public string Description { get; }

        public Action Apply { get; }
    }
}
