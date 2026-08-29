using System.Globalization;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Tracking;

public readonly struct CraftingResourceCostEvidence
{
    public CraftingResourceCostEvidence(string resourceItemId, string displayName, long consumedQuantity)
    {
        ResourceItemId = resourceItemId ?? string.Empty;
        DisplayName = displayName ?? string.Empty;
        ConsumedQuantity = consumedQuantity;
    }

    public string ResourceItemId { get; }
    public string DisplayName { get; }
    public long ConsumedQuantity { get; }
}

public readonly struct CraftingCompletionEvidence
{
    public CraftingCompletionEvidence(
        string outputItemId,
        string outputDisplayName,
        string recipeId,
        long producedQuantity,
        IReadOnlyList<CraftingResourceCostEvidence>? resources = null,
        long currencyCharged = 0,
        bool resourceEvidenceProven = true,
        bool currencyEvidenceProven = true)
    {
        OutputItemId = outputItemId;
        OutputDisplayName = outputDisplayName;
        RecipeId = recipeId;
        ProducedQuantity = producedQuantity;
        Resources = resources ?? Array.Empty<CraftingResourceCostEvidence>();
        CurrencyCharged = currencyCharged;
        ResourceEvidenceProven = resourceEvidenceProven;
        CurrencyEvidenceProven = currencyEvidenceProven;
    }

    public string OutputItemId { get; }
    public string OutputDisplayName { get; }
    public string RecipeId { get; }
    public long ProducedQuantity { get; }
    public IReadOnlyList<CraftingResourceCostEvidence> Resources { get; }
    public long CurrencyCharged { get; }
    public bool ResourceEvidenceProven { get; }
    public bool CurrencyEvidenceProven { get; }
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
        var canonicalResources = CanonicalizeResources(evidence);
        if (evidence.CurrencyEvidenceProven && evidence.CurrencyCharged < 0)
            throw new ArgumentException("Crafting currency evidence is invalid.", nameof(evidence));
        var immutableEvidence = new CraftingCompletionEvidence(
            evidence.OutputItemId,
            evidence.OutputDisplayName,
            evidence.RecipeId,
            evidence.ProducedQuantity,
            canonicalResources,
            evidence.CurrencyEvidenceProven ? evidence.CurrencyCharged : 0,
            evidence.ResourceEvidenceProven,
            evidence.CurrencyEvidenceProven);
        lock (sync)
        {
            var id = checked(++sequence);
            pending.Add(id, immutableEvidence);
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
                new Dictionary<string, long>(StringComparer.Ordinal) { [batch] = 1 },
                resources: evidence.Resources
                    .Select(resource => new CraftingResourceMutation(
                        resource.ResourceItemId,
                        resource.DisplayName,
                        1,
                        resource.ConsumedQuantity))
                    .ToArray(),
                currencyChargeActions: evidence.CurrencyCharged == 0 ? 0 : 1,
                currencyCharged: evidence.CurrencyCharged,
                resourceEvidenceProven: evidence.ResourceEvidenceProven,
                currencyEvidenceProven: evidence.CurrencyEvidenceProven)]);
        return true;
    }

    private static CraftingResourceCostEvidence[] CanonicalizeResources(CraftingCompletionEvidence evidence)
    {
        if (!evidence.ResourceEvidenceProven) return Array.Empty<CraftingResourceCostEvidence>();
        var resources = new Dictionary<string, CraftingResourceCostEvidence>(StringComparer.Ordinal);
        foreach (var resource in evidence.Resources ?? Array.Empty<CraftingResourceCostEvidence>())
        {
            if (string.IsNullOrWhiteSpace(resource.ResourceItemId) || resource.ConsumedQuantity <= 0)
                throw new ArgumentException("Crafting resource evidence is invalid.", nameof(evidence));
            if (resources.TryGetValue(resource.ResourceItemId, out var current))
            {
                resources[resource.ResourceItemId] = new CraftingResourceCostEvidence(
                    resource.ResourceItemId,
                    string.IsNullOrWhiteSpace(resource.DisplayName) ? current.DisplayName : resource.DisplayName,
                    checked(current.ConsumedQuantity + resource.ConsumedQuantity));
            }
            else
            {
                resources.Add(resource.ResourceItemId, new CraftingResourceCostEvidence(
                    resource.ResourceItemId,
                    resource.DisplayName,
                    resource.ConsumedQuantity));
            }
        }
        return resources.Values.OrderBy(value => value.ResourceItemId, StringComparer.Ordinal).ToArray();
    }

    public bool FinishPublication(CraftingCompletionToken token)
    {
        lock (sync)
            return token.BoundaryId == boundaryId && publishing.Remove(token.Sequence);
    }

    public bool TryInvalidateResourceEvidence(CraftingCompletionToken token)
    {
        lock (sync)
        {
            if (token.BoundaryId != boundaryId || !pending.TryGetValue(token.Sequence, out var evidence))
                return false;
            pending[token.Sequence] = new CraftingCompletionEvidence(
                evidence.OutputItemId,
                evidence.OutputDisplayName,
                evidence.RecipeId,
                evidence.ProducedQuantity,
                resources: Array.Empty<CraftingResourceCostEvidence>(),
                currencyCharged: evidence.CurrencyCharged,
                resourceEvidenceProven: false,
                currencyEvidenceProven: evidence.CurrencyEvidenceProven);
            return true;
        }
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
    private readonly Dictionary<
        (string Output, string Recipe, bool RecipeProven, bool BatchProven, bool ResourceProven, bool CurrencyProven),
        MutableRow> rows = new();
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
                _ = checked((target?.CurrencyChargeActions ?? 0) + entry.Value.CurrencyChargeActions);
                _ = checked((target?.CurrencyCharged ?? 0) + entry.Value.CurrencyCharged);
                foreach (var batch in entry.Value.BatchActions)
                {
                    var current = 0L;
                    if (target != null) target.BatchActions.TryGetValue(batch.Key, out current);
                    _ = checked(current + batch.Value);
                }
                foreach (var resource in entry.Value.Resources)
                {
                    MutableResource? current = null;
                    if (target != null) target.Resources.TryGetValue(resource.Key, out current);
                    _ = checked((current?.ConsumptionActions ?? 0) + resource.Value.ConsumptionActions);
                    _ = checked((current?.ConsumedQuantity ?? 0) + resource.Value.ConsumedQuantity);
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
                        entry.Value.BatchMetadataProven,
                        entry.Value.ResourceEvidenceProven,
                        entry.Value.CurrencyEvidenceProven);
                    rows.Add(entry.Key, target);
                }
                if (!string.IsNullOrWhiteSpace(entry.Value.OutputDisplayName))
                    target.OutputDisplayName = entry.Value.OutputDisplayName;
                target.CompletionActions = checked(target.CompletionActions + entry.Value.CompletionActions);
                target.ProducedQuantity = checked(target.ProducedQuantity + entry.Value.ProducedQuantity);
                target.CurrencyChargeActions = checked(target.CurrencyChargeActions + entry.Value.CurrencyChargeActions);
                target.CurrencyCharged = checked(target.CurrencyCharged + entry.Value.CurrencyCharged);
                foreach (var batch in entry.Value.BatchActions)
                {
                    target.BatchActions.TryGetValue(batch.Key, out var current);
                    target.BatchActions[batch.Key] = checked(current + batch.Value);
                }
                foreach (var resource in entry.Value.Resources)
                    MergeResource(target.Resources, resource.Value);
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
            .ThenBy(value => value.ResourceEvidenceProven)
            .ThenBy(value => value.CurrencyEvidenceProven)
            .Select(value => value.ToImmutable())
            .ToArray());

    private static Dictionary<
        (string Output, string Recipe, bool RecipeProven, bool BatchProven, bool ResourceProven, bool CurrencyProven),
        MutableRow> AggregateRows(
        IEnumerable<CraftingMutationRow> values)
    {
        var rows = new Dictionary<
            (string Output, string Recipe, bool RecipeProven, bool BatchProven, bool ResourceProven, bool CurrencyProven),
            MutableRow>();
        foreach (var row in values)
        {
            var key = (
                row.OutputItemId,
                row.RecipeId,
                row.RecipeIdentityProven,
                row.BatchMetadataProven,
                row.ResourceEvidenceProven,
                row.CurrencyEvidenceProven);
            if (!rows.TryGetValue(key, out var target))
            {
                target = new MutableRow(
                    row.OutputItemId,
                    row.OutputDisplayName,
                    row.RecipeId,
                    row.RecipeIdentityProven,
                    row.BatchMetadataProven,
                    row.ResourceEvidenceProven,
                    row.CurrencyEvidenceProven);
                rows.Add(key, target);
            }
            if (!string.IsNullOrWhiteSpace(row.OutputDisplayName)) target.OutputDisplayName = row.OutputDisplayName;
            target.CompletionActions = checked(target.CompletionActions + row.CompletionActions);
            target.ProducedQuantity = checked(target.ProducedQuantity + row.ProducedQuantity);
            target.CurrencyChargeActions = checked(target.CurrencyChargeActions + row.CurrencyChargeActions);
            target.CurrencyCharged = checked(target.CurrencyCharged + row.CurrencyCharged);
            foreach (var batch in row.BatchActions)
            {
                target.BatchActions.TryGetValue(batch.Key, out var current);
                target.BatchActions[batch.Key] = checked(current + batch.Value);
            }
            foreach (var resource in row.Resources)
                MergeResource(target.Resources, new MutableResource(
                    resource.ResourceItemId,
                    resource.DisplayName,
                    resource.ConsumptionActions,
                    resource.ConsumedQuantity));
        }
        return rows;
    }

    private static void MergeResource(Dictionary<string, MutableResource> resources, MutableResource incoming)
    {
        if (!resources.TryGetValue(incoming.ResourceItemId, out var target))
        {
            resources.Add(incoming.ResourceItemId, new MutableResource(
                incoming.ResourceItemId,
                incoming.DisplayName,
                incoming.ConsumptionActions,
                incoming.ConsumedQuantity));
            return;
        }
        if (!string.IsNullOrWhiteSpace(incoming.DisplayName)) target.DisplayName = incoming.DisplayName;
        target.ConsumptionActions = checked(target.ConsumptionActions + incoming.ConsumptionActions);
        target.ConsumedQuantity = checked(target.ConsumedQuantity + incoming.ConsumedQuantity);
    }

    private sealed class MutableRow
    {
        public MutableRow(
            string outputItemId,
            string outputDisplayName,
            string recipeId,
            bool recipeIdentityProven,
            bool batchMetadataProven,
            bool resourceEvidenceProven,
            bool currencyEvidenceProven)
        {
            OutputItemId = outputItemId;
            OutputDisplayName = outputDisplayName;
            RecipeId = recipeId;
            RecipeIdentityProven = recipeIdentityProven;
            BatchMetadataProven = batchMetadataProven;
            ResourceEvidenceProven = resourceEvidenceProven;
            CurrencyEvidenceProven = currencyEvidenceProven;
        }

        public string OutputItemId { get; }
        public string OutputDisplayName { get; set; }
        public string RecipeId { get; }
        public bool RecipeIdentityProven { get; }
        public bool BatchMetadataProven { get; }
        public bool ResourceEvidenceProven { get; }
        public bool CurrencyEvidenceProven { get; }
        public long CompletionActions { get; set; }
        public long ProducedQuantity { get; set; }
        public long CurrencyChargeActions { get; set; }
        public long CurrencyCharged { get; set; }
        public Dictionary<string, long> BatchActions { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, MutableResource> Resources { get; } = new(StringComparer.Ordinal);

        public CraftingMutationRow ToImmutable() => new(
            OutputItemId,
            OutputDisplayName,
            RecipeId,
            CompletionActions,
            ProducedQuantity,
            new Dictionary<string, long>(BatchActions, StringComparer.Ordinal),
            RecipeIdentityProven,
            BatchMetadataProven,
            Resources.Values
                .OrderBy(value => value.ResourceItemId, StringComparer.Ordinal)
                .Select(value => new CraftingResourceMutation(
                    value.ResourceItemId,
                    value.DisplayName,
                    value.ConsumptionActions,
                    value.ConsumedQuantity))
                .ToArray(),
            CurrencyChargeActions,
            CurrencyCharged,
            ResourceEvidenceProven,
            CurrencyEvidenceProven);
    }

    private sealed class MutableResource
    {
        public MutableResource(string resourceItemId, string displayName, long consumptionActions, long consumedQuantity)
        {
            ResourceItemId = resourceItemId;
            DisplayName = displayName;
            ConsumptionActions = consumptionActions;
            ConsumedQuantity = consumedQuantity;
        }

        public string ResourceItemId { get; }
        public string DisplayName { get; set; }
        public long ConsumptionActions { get; set; }
        public long ConsumedQuantity { get; set; }
    }
}
