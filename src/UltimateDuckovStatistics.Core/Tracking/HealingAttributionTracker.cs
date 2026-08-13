using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Core.Tracking;

public sealed class HealingUseContext
{
    public string CorrelationId { get; set; } = string.Empty;

    public int RuntimeItemId { get; set; }

    public DateTime StartedUtc { get; set; }

    public string SaveGenerationId { get; set; } = string.Empty;

    public string? RunId { get; set; }

    public string? MapId { get; set; }

    public string? SegmentId { get; set; }

    public string GameVersion { get; set; } = string.Empty;

    public string GameBuild { get; set; } = string.Empty;

    public GameplayContext GameplayContext { get; set; }

    public IntegrityTags IntegrityTags { get; set; }

    public AdapterCapabilityState AdapterCapability { get; set; }

    public string AdapterVersion { get; set; } = string.Empty;

    public string ItemId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public CanonicalItemGroup Group { get; set; }
}

public sealed class HealingObservation
{
    public string ApplicationId { get; set; } = string.Empty;

    public DateTime TimestampUtc { get; set; }

    public double ActualHealthRestored { get; set; }

    public bool IsMainPlayerTarget { get; set; }

    public string? OutcomeMapId { get; set; }

    public string? OutcomeSegmentId { get; set; }
}

public sealed class HealingAttributionTracker
{
    private const int MaximumPendingUses = 64;
    private const int MaximumRecentApplicationIds = 512;
    private readonly Func<string> eventIdFactory;
    private readonly Dictionary<int, SourceState> sourcesByRuntimeItem = new();
    private readonly Dictionary<string, SourceState> sourcesByCorrelation = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> buffSources = new();
    private readonly HashSet<string> recentApplicationIds = new(StringComparer.Ordinal);
    private readonly Queue<string> recentApplicationOrder = new();

    public HealingAttributionTracker(Func<string> eventIdFactory)
    {
        this.eventIdFactory = eventIdFactory ?? throw new ArgumentNullException(nameof(eventIdFactory));
    }

    public int PendingUseCount => sourcesByRuntimeItem.Values.Count(source => !source.Proven);

    public int BuffSourceCount => buffSources.Count;

    public void BeginUse(HealingUseContext context)
    {
        Validate(context);
        if (sourcesByRuntimeItem.TryGetValue(context.RuntimeItemId, out var replaced))
        {
            RemoveSource(replaced);
        }

        while (PendingUseCount >= MaximumPendingUses)
        {
            var oldest = sourcesByRuntimeItem.Values
                .Where(source => !source.Proven)
                .OrderBy(source => source.Context.StartedUtc)
                .First();
            RemoveSource(oldest);
        }

        var source = new SourceState(Clone(context));
        sourcesByRuntimeItem[context.RuntimeItemId] = source;
        sourcesByCorrelation[context.CorrelationId] = source;
    }

    public string? TryGetUseCorrelation(int runtimeItemId) =>
        sourcesByRuntimeItem.TryGetValue(runtimeItemId, out var source)
            ? source.Context.CorrelationId
            : null;

    public string? TryGetBuffCorrelation(int runtimeBuffId) =>
        buffSources.TryGetValue(runtimeBuffId, out var correlationId)
            ? correlationId
            : null;

    public bool BindBuff(int runtimeBuffId, string correlationId)
    {
        if (runtimeBuffId == 0
            || string.IsNullOrWhiteSpace(correlationId)
            || !sourcesByCorrelation.ContainsKey(correlationId))
        {
            return false;
        }

        buffSources[runtimeBuffId] = correlationId;
        return true;
    }

    public bool ReconcileBuff(int runtimeBuffId, string? correlationId)
    {
        if (runtimeBuffId == 0)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(correlationId)
            || !sourcesByCorrelation.ContainsKey(correlationId))
        {
            var removed = buffSources.Remove(runtimeBuffId);
            TrimUnusedProvenSources();
            return removed;
        }

        buffSources.TryGetValue(runtimeBuffId, out var previous);
        buffSources[runtimeBuffId] = correlationId;
        return !string.Equals(previous, correlationId, StringComparison.Ordinal);
    }

    public void RemoveBuff(int runtimeBuffId)
    {
        buffSources.Remove(runtimeBuffId);
        TrimUnusedProvenSources();
    }

    public IReadOnlyList<HealingApplied> Observe(string? correlationId, HealingObservation observation)
    {
        if (string.IsNullOrWhiteSpace(correlationId)
            || observation == null
            || !observation.IsMainPlayerTarget
            || observation.ActualHealthRestored <= 0
            || double.IsNaN(observation.ActualHealthRestored)
            || double.IsInfinity(observation.ActualHealthRestored)
            || string.IsNullOrWhiteSpace(observation.ApplicationId)
            || !sourcesByCorrelation.TryGetValue(correlationId, out var source))
        {
            return Array.Empty<HealingApplied>();
        }

        if (!RememberApplication(observation.ApplicationId))
        {
            return Array.Empty<HealingApplied>();
        }

        observation.TimestampUtc = EnsureUtc(observation.TimestampUtc);
        if (!source.Proven)
        {
            source.PendingObservations.Add(Clone(observation));
            return Array.Empty<HealingApplied>();
        }

        return new[] { CreateEvent(source, observation) };
    }

    public IReadOnlyList<HealingApplied> CompleteUse(int runtimeItemId, ItemUseRecorded? successfulUse)
    {
        if (!sourcesByRuntimeItem.TryGetValue(runtimeItemId, out var source))
        {
            return Array.Empty<HealingApplied>();
        }

        sourcesByRuntimeItem.Remove(runtimeItemId);
        if (successfulUse == null
            || successfulUse.GameplayContext != GameplayContext.Raid
            || string.IsNullOrWhiteSpace(successfulUse.EventId)
            || !string.Equals(successfulUse.SaveGenerationId, source.Context.SaveGenerationId, StringComparison.Ordinal)
            || !string.Equals(successfulUse.ItemId, source.Context.ItemId, StringComparison.Ordinal))
        {
            RemoveSource(source);
            return Array.Empty<HealingApplied>();
        }

        source.Proven = true;
        source.SourceItemUseEventId = successfulUse.EventId;
        source.Context.DisplayName = successfulUse.DisplayName;
        source.Context.Group = successfulUse.Group;
        source.Context.GameplayContext = successfulUse.GameplayContext;
        var events = source.PendingObservations
            .Select(observation => CreateEvent(source, observation))
            .ToArray();
        source.PendingObservations.Clear();
        TrimUnusedProvenSources();
        return events;
    }

    public int ExpirePendingBefore(DateTime cutoffUtc)
    {
        cutoffUtc = EnsureUtc(cutoffUtc);
        var expired = sourcesByRuntimeItem.Values
            .Where(source => !source.Proven && source.Context.StartedUtc < cutoffUtc)
            .Distinct()
            .ToArray();
        foreach (var source in expired)
        {
            RemoveSource(source);
        }

        return expired.Length;
    }

    public void Clear()
    {
        sourcesByRuntimeItem.Clear();
        sourcesByCorrelation.Clear();
        buffSources.Clear();
        recentApplicationIds.Clear();
        recentApplicationOrder.Clear();
    }

    public static double CalculateActualRestoration(double currentHealth, double maximumHealth, double requestedHealth)
    {
        if (double.IsNaN(currentHealth)
            || double.IsNaN(maximumHealth)
            || double.IsNaN(requestedHealth)
            || double.IsInfinity(currentHealth)
            || double.IsInfinity(maximumHealth)
            || double.IsInfinity(requestedHealth)
            || requestedHealth <= 0
            || maximumHealth <= currentHealth)
        {
            return 0;
        }

        return Math.Max(0, Math.Min(maximumHealth, currentHealth + requestedHealth) - currentHealth);
    }

    public static double CalculateAppliedRestoration(
        double healthBeforeCall,
        double healthAfterCall,
        double maximumHealthBeforeCall)
    {
        if (double.IsNaN(healthBeforeCall)
            || double.IsNaN(healthAfterCall)
            || double.IsNaN(maximumHealthBeforeCall)
            || double.IsInfinity(healthBeforeCall)
            || double.IsInfinity(healthAfterCall)
            || double.IsInfinity(maximumHealthBeforeCall)
            || maximumHealthBeforeCall <= healthBeforeCall
            || healthAfterCall <= healthBeforeCall)
        {
            return 0;
        }

        return Math.Max(
            0,
            Math.Min(maximumHealthBeforeCall, healthAfterCall) - healthBeforeCall);
    }

    private HealingApplied CreateEvent(SourceState source, HealingObservation observation) => new()
    {
        EventId = eventIdFactory(),
        ApplicationId = observation.ApplicationId,
        SourceItemUseEventId = source.SourceItemUseEventId,
        TimestampUtc = observation.TimestampUtc,
        SaveGenerationId = source.Context.SaveGenerationId,
        RunId = source.Context.RunId,
        MapId = source.Context.MapId,
        GameVersion = source.Context.GameVersion,
        GameBuild = source.Context.GameBuild,
        GameplayContext = source.Context.GameplayContext,
        IntegrityTags = source.Context.IntegrityTags,
        AdapterCapability = source.Context.AdapterCapability,
        AdapterVersion = source.Context.AdapterVersion,
        ItemId = source.Context.ItemId,
        DisplayName = source.Context.DisplayName,
        Group = source.Context.Group,
        ActualHealthRestored = observation.ActualHealthRestored,
        SourceSegmentId = source.Context.SegmentId,
        SourceMapId = source.Context.MapId,
        OutcomeSegmentId = observation.OutcomeSegmentId,
        OutcomeMapId = observation.OutcomeMapId
    };

    private bool RememberApplication(string applicationId)
    {
        if (!recentApplicationIds.Add(applicationId))
        {
            return false;
        }

        recentApplicationOrder.Enqueue(applicationId);
        while (recentApplicationOrder.Count > MaximumRecentApplicationIds)
        {
            recentApplicationIds.Remove(recentApplicationOrder.Dequeue());
        }

        return true;
    }

    private void RemoveSource(SourceState source)
    {
        sourcesByRuntimeItem.Remove(source.Context.RuntimeItemId);
        sourcesByCorrelation.Remove(source.Context.CorrelationId);
        foreach (var buffId in buffSources
                     .Where(entry => string.Equals(entry.Value, source.Context.CorrelationId, StringComparison.Ordinal))
                     .Select(entry => entry.Key)
                     .ToArray())
        {
            buffSources.Remove(buffId);
        }
    }

    private void TrimUnusedProvenSources()
    {
        var referenced = buffSources.Values.ToHashSet(StringComparer.Ordinal);
        foreach (var source in sourcesByCorrelation.Values
                     .Where(source => source.Proven
                                      && !sourcesByRuntimeItem.ContainsKey(source.Context.RuntimeItemId)
                                      && !referenced.Contains(source.Context.CorrelationId))
                     .Distinct()
                     .ToArray())
        {
            sourcesByCorrelation.Remove(source.Context.CorrelationId);
        }
    }

    private static void Validate(HealingUseContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (context.RuntimeItemId == 0
            || string.IsNullOrWhiteSpace(context.CorrelationId)
            || string.IsNullOrWhiteSpace(context.SaveGenerationId)
            || string.IsNullOrWhiteSpace(context.ItemId))
        {
            throw new ArgumentException("Healing use context is incomplete.", nameof(context));
        }

        context.StartedUtc = EnsureUtc(context.StartedUtc);
    }

    private static HealingUseContext Clone(HealingUseContext source) => new()
    {
        CorrelationId = source.CorrelationId,
        RuntimeItemId = source.RuntimeItemId,
        StartedUtc = source.StartedUtc,
        SaveGenerationId = source.SaveGenerationId,
        RunId = source.RunId,
        MapId = source.MapId,
        SegmentId = source.SegmentId,
        GameVersion = source.GameVersion,
        GameBuild = source.GameBuild,
        GameplayContext = source.GameplayContext,
        IntegrityTags = source.IntegrityTags,
        AdapterCapability = source.AdapterCapability,
        AdapterVersion = source.AdapterVersion,
        ItemId = source.ItemId,
        DisplayName = source.DisplayName,
        Group = source.Group
    };

    private static HealingObservation Clone(HealingObservation source) => new()
    {
        ApplicationId = source.ApplicationId,
        TimestampUtc = source.TimestampUtc,
        ActualHealthRestored = source.ActualHealthRestored,
        IsMainPlayerTarget = source.IsMainPlayerTarget,
        OutcomeMapId = source.OutcomeMapId,
        OutcomeSegmentId = source.OutcomeSegmentId
    };

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private sealed class SourceState
    {
        public SourceState(HealingUseContext context)
        {
            Context = context;
        }

        public HealingUseContext Context { get; }

        public bool Proven { get; set; }

        public string SourceItemUseEventId { get; set; } = string.Empty;

        public List<HealingObservation> PendingObservations { get; } = new();
    }
}
