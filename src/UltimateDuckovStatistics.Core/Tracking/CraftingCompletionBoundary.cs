using System.Globalization;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Tracking;

public readonly struct CraftingCompletionEvidence
{
    public CraftingCompletionEvidence(
        string outputItemId,
        string outputDisplayName,
        string recipeId,
        long producedQuantity)
    {
        OutputItemId = outputItemId;
        OutputDisplayName = outputDisplayName;
        RecipeId = recipeId;
        ProducedQuantity = producedQuantity;
    }

    public string OutputItemId { get; }
    public string OutputDisplayName { get; }
    public string RecipeId { get; }
    public long ProducedQuantity { get; }
}

public readonly struct CraftingCompletionToken
{
    public CraftingCompletionToken(Guid boundaryId, long sequence)
    {
        BoundaryId = boundaryId;
        Sequence = sequence;
    }

    public Guid BoundaryId { get; }
    public long Sequence { get; }
}

public sealed class CraftingDeliveryCorrelation
{
    private int deliveryTaskClaimed;
    private int deliveryProven;

    public CraftingDeliveryCorrelation(CraftingCompletionToken token) => Token = token;

    public CraftingCompletionToken Token { get; }

    public bool DeliveryProven => Volatile.Read(ref deliveryProven) != 0;

    public bool TryClaimDeliveryTask() =>
        Interlocked.CompareExchange(ref deliveryTaskClaimed, 1, 0) == 0;

    public bool TryMarkDeliveryProven()
    {
        if (Volatile.Read(ref deliveryTaskClaimed) == 0)
            throw new InvalidOperationException("Crafting delivery cannot be proven before its native return task is correlated.");
        return Interlocked.CompareExchange(ref deliveryProven, 1, 0) == 0;
    }
}

public sealed class CraftingCompletionBoundary
{
    private readonly object sync = new();
    private readonly Guid boundaryId = Guid.NewGuid();
    private readonly Dictionary<long, CraftingCompletionEvidence> pending = new();
    private readonly HashSet<long> publishing = new();
    private long sequence;

    public int PendingCount
    {
        get { lock (sync) return pending.Count; }
    }

    public int OutstandingCount
    {
        get { lock (sync) return checked(pending.Count + publishing.Count); }
    }

    public CraftingCompletionToken Begin(CraftingCompletionEvidence evidence)
    {
        if (string.IsNullOrWhiteSpace(evidence.OutputItemId) || string.IsNullOrWhiteSpace(evidence.RecipeId)
            || evidence.ProducedQuantity <= 0)
            throw new ArgumentException("Crafting completion evidence is invalid.", nameof(evidence));
        lock (sync)
        {
            var id = checked(++sequence);
            pending.Add(id, evidence);
            return new CraftingCompletionToken(boundaryId, id);
        }
    }

    public bool TryComplete(
        CraftingCompletionToken token,
        string saveGenerationId,
        DateTime timestampUtc,
        out CraftingMutation mutation)
    {
        if (string.IsNullOrWhiteSpace(saveGenerationId))
        {
            mutation = CraftingMutation.Empty;
            return false;
        }
        CraftingCompletionEvidence evidence;
        lock (sync)
        {
            if (token.BoundaryId != boundaryId || !pending.Remove(token.Sequence, out evidence))
            {
                mutation = CraftingMutation.Empty;
                return false;
            }
            publishing.Add(token.Sequence);
        }
        var batch = evidence.ProducedQuantity.ToString(CultureInfo.InvariantCulture);
        mutation = new CraftingMutation(
            saveGenerationId,
            timestampUtc,
            [new CraftingMutationRow(
                evidence.OutputItemId,
                evidence.OutputDisplayName,
                evidence.RecipeId,
                1,
                evidence.ProducedQuantity,
                new Dictionary<string, long>(StringComparer.Ordinal) { [batch] = 1 })]);
        return true;
    }

    public bool FinishPublication(CraftingCompletionToken token)
    {
        lock (sync)
            return token.BoundaryId == boundaryId && publishing.Remove(token.Sequence);
    }

    public bool Abandon(CraftingCompletionToken token)
    {
        lock (sync)
            return token.BoundaryId == boundaryId && pending.Remove(token.Sequence);
    }

    public int AbandonUnprovenForTerminalShutdown()
    {
        lock (sync)
        {
            var abandoned = pending.Count;
            pending.Clear();
            return abandoned;
        }
    }
}

public sealed class CraftingPendingAccumulator
{
    private readonly object sync = new();
    private readonly Dictionary<(string Output, string Recipe, bool RecipeProven, bool BatchProven), MutableRow> rows = new();
    private string pendingGenerationId = string.Empty;
    private DateTime pendingTimestampUtc;

    public bool IsEmpty
    {
        get { lock (sync) return rows.Count == 0; }
    }

    public bool Add(CraftingMutation mutation)
    {
        if (mutation == null) throw new ArgumentNullException(nameof(mutation));
        if (mutation.IsEmpty) return false;
        var incoming = AggregateRows(mutation.Rows);
        lock (sync)
        {
            var wasEmpty = rows.Count == 0;
            if (!wasEmpty
                && !string.Equals(pendingGenerationId, mutation.SaveGenerationId, StringComparison.Ordinal))
                throw new InvalidOperationException("Pending crafting publications cannot cross save generations.");
            foreach (var entry in incoming)
            {
                rows.TryGetValue(entry.Key, out var target);
                _ = checked((target?.CompletionActions ?? 0) + entry.Value.CompletionActions);
                _ = checked((target?.ProducedQuantity ?? 0) + entry.Value.ProducedQuantity);
                foreach (var batch in entry.Value.BatchActions)
                {
                    var current = 0L;
                    if (target != null) target.BatchActions.TryGetValue(batch.Key, out current);
                    _ = checked(current + batch.Value);
                }
            }
            if (wasEmpty) pendingGenerationId = mutation.SaveGenerationId;
            if (wasEmpty || mutation.TimestampUtc > pendingTimestampUtc)
                pendingTimestampUtc = mutation.TimestampUtc;
            foreach (var entry in incoming)
            {
                if (!rows.TryGetValue(entry.Key, out var target))
                {
                    target = new MutableRow(
                        entry.Value.OutputItemId,
                        entry.Value.OutputDisplayName,
                        entry.Value.RecipeId,
                        entry.Value.RecipeIdentityProven,
                        entry.Value.BatchMetadataProven);
                    rows.Add(entry.Key, target);
                }
                if (!string.IsNullOrWhiteSpace(entry.Value.OutputDisplayName))
                    target.OutputDisplayName = entry.Value.OutputDisplayName;
                target.CompletionActions = checked(target.CompletionActions + entry.Value.CompletionActions);
                target.ProducedQuantity = checked(target.ProducedQuantity + entry.Value.ProducedQuantity);
                foreach (var batch in entry.Value.BatchActions)
                {
                    target.BatchActions.TryGetValue(batch.Key, out var current);
                    target.BatchActions[batch.Key] = checked(current + batch.Value);
                }
            }
            return wasEmpty;
        }
    }

    public bool TryFlush(Func<CraftingMutation, bool> publish)
    {
        if (publish == null) throw new ArgumentNullException(nameof(publish));
        lock (sync)
        {
            if (rows.Count == 0) return true;
            if (!publish(CreateMutation())) return false;
            rows.Clear();
            pendingGenerationId = string.Empty;
            pendingTimestampUtc = default;
            return true;
        }
    }

    private CraftingMutation CreateMutation() => new(
        pendingGenerationId,
        pendingTimestampUtc,
        rows.Values
            .OrderBy(value => value.OutputItemId, StringComparer.Ordinal)
            .ThenBy(value => value.RecipeId, StringComparer.Ordinal)
            .ThenBy(value => value.RecipeIdentityProven)
            .ThenBy(value => value.BatchMetadataProven)
            .Select(value => value.ToImmutable())
            .ToArray());

    private static Dictionary<(string Output, string Recipe, bool RecipeProven, bool BatchProven), MutableRow> AggregateRows(
        IEnumerable<CraftingMutationRow> values)
    {
        var rows = new Dictionary<(string Output, string Recipe, bool RecipeProven, bool BatchProven), MutableRow>();
        foreach (var row in values)
        {
            var key = (row.OutputItemId, row.RecipeId, row.RecipeIdentityProven, row.BatchMetadataProven);
            if (!rows.TryGetValue(key, out var target))
            {
                target = new MutableRow(
                    row.OutputItemId,
                    row.OutputDisplayName,
                    row.RecipeId,
                    row.RecipeIdentityProven,
                    row.BatchMetadataProven);
                rows.Add(key, target);
            }
            if (!string.IsNullOrWhiteSpace(row.OutputDisplayName)) target.OutputDisplayName = row.OutputDisplayName;
            target.CompletionActions = checked(target.CompletionActions + row.CompletionActions);
            target.ProducedQuantity = checked(target.ProducedQuantity + row.ProducedQuantity);
            foreach (var batch in row.BatchActions)
            {
                target.BatchActions.TryGetValue(batch.Key, out var current);
                target.BatchActions[batch.Key] = checked(current + batch.Value);
            }
        }
        return rows;
    }

    private sealed class MutableRow
    {
        public MutableRow(
            string outputItemId,
            string outputDisplayName,
            string recipeId,
            bool recipeIdentityProven,
            bool batchMetadataProven)
        {
            OutputItemId = outputItemId;
            OutputDisplayName = outputDisplayName;
            RecipeId = recipeId;
            RecipeIdentityProven = recipeIdentityProven;
            BatchMetadataProven = batchMetadataProven;
        }

        public string OutputItemId { get; }
        public string OutputDisplayName { get; set; }
        public string RecipeId { get; }
        public bool RecipeIdentityProven { get; }
        public bool BatchMetadataProven { get; }
        public long CompletionActions { get; set; }
        public long ProducedQuantity { get; set; }
        public Dictionary<string, long> BatchActions { get; } = new(StringComparer.Ordinal);

        public CraftingMutationRow ToImmutable() => new(
            OutputItemId,
            OutputDisplayName,
            RecipeId,
            CompletionActions,
            ProducedQuantity,
            new Dictionary<string, long>(BatchActions, StringComparer.Ordinal),
            RecipeIdentityProven,
            BatchMetadataProven);
    }
}
