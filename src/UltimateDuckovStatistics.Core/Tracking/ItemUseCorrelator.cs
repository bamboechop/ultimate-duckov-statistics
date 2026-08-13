using UltimateDuckovStatistics.Core.Classification;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Core.Tracking;

public sealed class ItemUseSnapshot
{
    public int RuntimeItemId { get; set; }

    public string ItemId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public ItemClassificationInput Classification { get; set; } = new();

    public bool Stackable { get; set; }

    public int StackCount { get; set; }

    public bool UsesDurability { get; set; }

    public double Durability { get; set; }

    public DateTime TimestampUtc { get; set; }

    public string SaveGenerationId { get; set; } = string.Empty;

    public string? RunId { get; set; }

    public string? MapId { get; set; }

    public string? SegmentId { get; set; }

    public string GameVersion { get; set; } = string.Empty;

    public string GameBuild { get; set; } = string.Empty;

    public GameplayContext GameplayContext { get; set; }

    public IntegrityTags IntegrityTags { get; set; }

    public AdapterCapabilityState AdapterCapability { get; set; } = AdapterCapabilityState.Supported;

    public string AdapterVersion { get; set; } = string.Empty;
}

public enum ItemUseCompletionDisposition
{
    Counted,
    IgnoredOutsideRaid,
    MissingBegin,
    MissingSuccessfulUse
}

public sealed class ItemUseCompletion
{
    public ItemUseCompletion(ItemUseCompletionDisposition disposition, ItemUseRecorded? normalizedEvent)
    {
        Disposition = disposition;
        NormalizedEvent = normalizedEvent;
    }

    public ItemUseCompletionDisposition Disposition { get; }

    public ItemUseRecorded? NormalizedEvent { get; }

    public bool ShouldCount => Disposition == ItemUseCompletionDisposition.Counted;
}

public sealed class ItemUseCorrelator
{
    private const int MaximumPendingUses = 64;
    private readonly Func<string> eventIdFactory;
    private readonly Dictionary<int, PendingUse> pending = new();

    public ItemUseCorrelator(Func<string> eventIdFactory)
    {
        this.eventIdFactory = eventIdFactory ?? throw new ArgumentNullException(nameof(eventIdFactory));
    }

    public int PendingCount => pending.Count;

    public bool Contains(int runtimeItemId) => pending.ContainsKey(runtimeItemId);

    public void Begin(ItemUseSnapshot snapshot)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }
        ValidateSnapshot(snapshot);

        if (pending.Count >= MaximumPendingUses && !pending.ContainsKey(snapshot.RuntimeItemId))
        {
            var oldest = pending.OrderBy(entry => entry.Value.Snapshot.TimestampUtc).First();
            pending.Remove(oldest.Key);
        }

        pending[snapshot.RuntimeItemId] = new PendingUse(snapshot);
    }

    public bool MarkSuccessful(int runtimeItemId, double durabilityAfterBehaviors)
    {
        if (!pending.TryGetValue(runtimeItemId, out var use))
        {
            return false;
        }

        use.SuccessObserved = true;
        use.DurabilityAfterBehaviors = durabilityAfterBehaviors;
        return true;
    }

    public ItemUseCompletion CompleteByMainPlayer(
        int runtimeItemId,
        int? finalStackCount,
        double? finalDurability,
        DateTime completedAtUtc)
    {
        if (!pending.TryGetValue(runtimeItemId, out var use))
        {
            return new ItemUseCompletion(ItemUseCompletionDisposition.MissingBegin, normalizedEvent: null);
        }

        pending.Remove(runtimeItemId);
        if (!use.SuccessObserved)
        {
            return new ItemUseCompletion(ItemUseCompletionDisposition.MissingSuccessfulUse, normalizedEvent: null);
        }

        var snapshot = use.Snapshot;
        var classification = ItemClassifier.Classify(snapshot.Classification);
        var consumption = CalculateConsumption(snapshot, use.DurabilityAfterBehaviors, finalStackCount, finalDurability);
        var normalizedEvent = new ItemUseRecorded
        {
            EventId = eventIdFactory(),
            TimestampUtc = EnsureUtc(completedAtUtc),
            SaveGenerationId = snapshot.SaveGenerationId,
            RunId = snapshot.RunId,
            MapId = snapshot.MapId,
            SegmentId = snapshot.SegmentId,
            GameVersion = snapshot.GameVersion,
            GameBuild = snapshot.GameBuild,
            GameplayContext = snapshot.GameplayContext,
            IntegrityTags = snapshot.IntegrityTags,
            AdapterCapability = snapshot.AdapterCapability,
            AdapterVersion = snapshot.AdapterVersion,
            ItemId = snapshot.ItemId,
            DisplayName = snapshot.DisplayName,
            Group = classification.Group,
            EffectTags = classification.EffectTags.ToList(),
            ActivationCount = 1,
            AmountConsumed = consumption.Amount,
            ConsumptionUnit = consumption.Unit
        };

        var disposition = snapshot.GameplayContext == GameplayContext.Raid
            ? ItemUseCompletionDisposition.Counted
            : ItemUseCompletionDisposition.IgnoredOutsideRaid;
        return new ItemUseCompletion(disposition, normalizedEvent);
    }

    public int ExpireBefore(DateTime cutoffUtc)
    {
        cutoffUtc = EnsureUtc(cutoffUtc);
        var expired = pending
            .Where(entry => EnsureUtc(entry.Value.Snapshot.TimestampUtc) < cutoffUtc)
            .Select(entry => entry.Key)
            .ToArray();
        foreach (var runtimeId in expired)
        {
            pending.Remove(runtimeId);
        }

        return expired.Length;
    }

    public void Clear() => pending.Clear();

    private static (double Amount, ConsumptionUnit Unit) CalculateConsumption(
        ItemUseSnapshot snapshot,
        double durabilityAfterBehaviors,
        int? finalStackCount,
        double? finalDurability)
    {
        if (snapshot.Stackable)
        {
            // CA_UseItem decrements exactly one stack unit after the successful
            // UsageUtilities hook. Prefer the observed final delta; the pinned
            // native contract proves the one-unit fallback.
            var amount = finalStackCount.HasValue
                ? Math.Max(0, snapshot.StackCount - finalStackCount.Value)
                : 1;
            if (amount == 0)
            {
                amount = 1;
            }

            return (amount, ConsumptionUnit.StackUnit);
        }

        if (snapshot.UsesDurability)
        {
            var after = finalDurability ?? durabilityAfterBehaviors;
            var amount = Math.Max(0, snapshot.Durability - after);
            return (amount, ConsumptionUnit.Durability);
        }

        return (1, ConsumptionUnit.Item);
    }

    private static void ValidateSnapshot(ItemUseSnapshot snapshot)
    {
        if (snapshot.RuntimeItemId == 0)
        {
            throw new ArgumentException("Runtime item ID must not be zero.", nameof(snapshot));
        }

        if (string.IsNullOrWhiteSpace(snapshot.ItemId))
        {
            throw new ArgumentException("Stable item ID is required.", nameof(snapshot));
        }

        if (string.IsNullOrWhiteSpace(snapshot.SaveGenerationId))
        {
            throw new ArgumentException("Save generation ID is required.", nameof(snapshot));
        }

        snapshot.TimestampUtc = EnsureUtc(snapshot.TimestampUtc);
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private sealed class PendingUse
    {
        public PendingUse(ItemUseSnapshot snapshot)
        {
            Snapshot = snapshot;
            DurabilityAfterBehaviors = snapshot.Durability;
        }

        public ItemUseSnapshot Snapshot { get; }

        public bool SuccessObserved { get; set; }

        public double DurabilityAfterBehaviors { get; set; }
    }
}
