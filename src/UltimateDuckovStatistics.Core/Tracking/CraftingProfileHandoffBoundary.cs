using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Tracking;

public sealed class CraftingProfileHandoffBoundary
{
    public const string StagedGenerationId = "crafting-profile-handoff";

    private readonly object sync = new();
    private readonly Dictionary<long, Handoff> handoffs = new();
    private readonly List<long> activeOrder = [];
    private readonly Queue<long> completedOrder = new();

    public bool HasUncommittedData
    {
        get
        {
            lock (sync) return handoffs.Values.Any(handoff => !handoff.Pending.IsEmpty);
        }
    }

    public bool HasCompletedData
    {
        get { lock (sync) return completedOrder.Count != 0; }
    }

    public void Begin(long transitionId)
    {
        if (transitionId <= 0) throw new ArgumentOutOfRangeException(nameof(transitionId));
        lock (sync)
        {
            if (handoffs.ContainsKey(transitionId))
                throw new InvalidOperationException("The crafting profile transition is already registered.");
            handoffs.Add(transitionId, new Handoff());
            activeOrder.Add(transitionId);
        }
    }

    public bool TryGetActiveTransitionId(out long transitionId)
    {
        lock (sync)
        {
            if (activeOrder.Count == 0)
            {
                transitionId = 0;
                return false;
            }
            transitionId = activeOrder[^1];
            return true;
        }
    }

    public bool Stage(long transitionId, CraftingMutation mutation)
    {
        if (mutation == null) throw new ArgumentNullException(nameof(mutation));
        if (mutation.IsEmpty) return true;
        lock (sync)
        {
            if (!handoffs.TryGetValue(transitionId, out var handoff) || handoff.Completed)
                return false;
            handoff.Pending.Add(new CraftingMutation(
                StagedGenerationId,
                mutation.TimestampUtc,
                mutation.Rows));
            return true;
        }
    }

    public bool Complete(long transitionId, string generationId)
    {
        if (string.IsNullOrWhiteSpace(generationId))
            throw new ArgumentException("The committed crafting generation is required.", nameof(generationId));
        lock (sync)
        {
            if (!handoffs.TryGetValue(transitionId, out var handoff) || handoff.Completed)
                return false;
            handoff.GenerationId = generationId;
            handoff.Completed = true;
            activeOrder.Remove(transitionId);
            if (handoff.Pending.IsEmpty)
            {
                handoffs.Remove(transitionId);
                return true;
            }
            completedOrder.Enqueue(transitionId);
            return true;
        }
    }

    public bool TryFlushCompleted(Func<CraftingMutation, bool> publish)
    {
        if (publish == null) throw new ArgumentNullException(nameof(publish));
        lock (sync)
        {
            while (completedOrder.Count != 0)
            {
                var transitionId = completedOrder.Peek();
                var handoff = handoffs[transitionId];
                if (!handoff.Pending.TryFlush(mutation => publish(new CraftingMutation(
                        handoff.GenerationId,
                        mutation.TimestampUtc,
                        mutation.Rows))))
                    return false;
                completedOrder.Dequeue();
                handoffs.Remove(transitionId);
            }
            return true;
        }
    }

    public void Reset()
    {
        lock (sync)
        {
            handoffs.Clear();
            activeOrder.Clear();
            completedOrder.Clear();
        }
    }

    private sealed class Handoff
    {
        public CraftingPendingAccumulator Pending { get; } = new();
        public string GenerationId { get; set; } = string.Empty;
        public bool Completed { get; set; }
    }
}
