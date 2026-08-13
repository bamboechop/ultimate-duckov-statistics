using System.Runtime.Serialization;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Core.Statistics;

[DataContract]
public sealed class ContainerMetricCapabilities
{
    [DataMember(Order = 1)] public MetricAvailability UniqueContainersLooted { get; set; } = new();
}

[DataContract]
public sealed class ContainerStatisticsAggregate
{
    [DataMember(Order = 1)] public ContainerMetricCapabilities Capabilities { get; set; } = new();
    [DataMember(Order = 2)] public long UniqueContainersLooted { get; set; }
    [DataMember(Order = 3)] public bool HistoricalUnavailable { get; set; }
    [DataMember(Order = 4)] public bool WasRepairedFromInvalidState { get; set; }
}

[DataContract]
public sealed class ContainerRunCheckpointState
{
    public const int DeduplicationCapacity = 4096;

    [DataMember(Order = 1)] public ContainerStatisticsAggregate Statistics { get; set; } = new();
    [DataMember(Order = 2)] public List<int> LootedContainerKeys { get; set; } = new();
    [DataMember(Order = 3)] public bool DeduplicationSaturated { get; set; }
    [DataMember(Order = 4)] public bool WasRepairedFromInvalidState { get; set; }
    [DataMember(Order = 5)] public List<string> LootedContainerIdentities { get; set; } = new();
}

public static class ContainerStatisticsReducer
{
    private const string RepairProvenance =
        "Persisted container data was repaired; capability remains unavailable.";

    public static bool Record(ContainerRunCheckpointState state, ContainerLooted value)
    {
        if (state == null) throw new ArgumentNullException(nameof(state));
        ValidateEvent(value);
        NormalizeCheckpoint(state);
        var identity = StableIdentity(value);
        var identityIndex = state.LootedContainerIdentities.BinarySearch(identity, StringComparer.Ordinal);
        if (state.DeduplicationSaturated || identityIndex >= 0) return false;
        if (state.LootedContainerIdentities.Count >= ContainerRunCheckpointState.DeduplicationCapacity)
        {
            state.DeduplicationSaturated = true;
            state.Statistics.Capabilities.UniqueContainersLooted = new MetricAvailability
            {
                State = AdapterCapabilityState.DisabledIncompatible,
                Provenance = "The per-run stable-key deduplication capacity was reached; further containers were not recorded."
            };
            return false;
        }

        state.LootedContainerIdentities.Insert(~identityIndex, identity);
        var keyIndex = state.LootedContainerKeys.BinarySearch(value.ContainerKey);
        if (keyIndex < 0) state.LootedContainerKeys.Insert(~keyIndex, value.ContainerKey);
        state.Statistics.UniqueContainersLooted = SaturatingAdd(state.Statistics.UniqueContainersLooted, 1);
        return true;
    }

    public static void Merge(
        ContainerStatisticsAggregate target,
        ContainerStatisticsAggregate source,
        bool adoptSourceCapability = false)
    {
        if (target == null) throw new ArgumentNullException(nameof(target));
        if (source == null) throw new ArgumentNullException(nameof(source));
        NormalizePersisted(target);
        NormalizePersisted(source);
        var adoptFirstRunCapability = adoptSourceCapability
                                      && IsEmpty(target)
                                      && !target.HistoricalUnavailable;
        target.UniqueContainersLooted = SaturatingAdd(target.UniqueContainersLooted, source.UniqueContainersLooted);
        target.Capabilities.UniqueContainersLooted = adoptFirstRunCapability
            ? Clone(source.Capabilities.UniqueContainersLooted)
            : Restrict(
                target.Capabilities.UniqueContainersLooted,
                source.Capabilities.UniqueContainersLooted,
                preferSourceOnTie: !target.HistoricalUnavailable);
        target.HistoricalUnavailable |= source.HistoricalUnavailable;
        target.WasRepairedFromInvalidState |= source.WasRepairedFromInvalidState;
    }

    public static ContainerStatisticsAggregate Clone(ContainerStatisticsAggregate source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        NormalizePersisted(source);
        return new ContainerStatisticsAggregate
        {
            Capabilities = CloneCapabilities(source.Capabilities),
            UniqueContainersLooted = source.UniqueContainersLooted,
            HistoricalUnavailable = source.HistoricalUnavailable,
            WasRepairedFromInvalidState = source.WasRepairedFromInvalidState
        };
    }

    public static ContainerRunCheckpointState Clone(ContainerRunCheckpointState source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        NormalizeCheckpoint(source);
        return new ContainerRunCheckpointState
        {
            Statistics = Clone(source.Statistics),
            LootedContainerKeys = source.LootedContainerKeys.ToList(),
            DeduplicationSaturated = source.DeduplicationSaturated,
            WasRepairedFromInvalidState = source.WasRepairedFromInvalidState,
            LootedContainerIdentities = source.LootedContainerIdentities.ToList()
        };
    }

    public static ContainerMetricCapabilities CloneCapabilities(ContainerMetricCapabilities source) => new()
    {
        UniqueContainersLooted = Clone(source?.UniqueContainersLooted)
    };

    public static bool NormalizePersisted(ContainerStatisticsAggregate value)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));
        var repaired = false;
        value.Capabilities ??= Repair(new ContainerMetricCapabilities(), ref repaired);
        value.Capabilities.UniqueContainersLooted ??= Repair(new MetricAvailability(), ref repaired);
        var availability = value.Capabilities.UniqueContainersLooted;
        if (!Enum.IsDefined(typeof(AdapterCapabilityState), availability.State))
        {
            availability.State = AdapterCapabilityState.DisabledIncompatible;
            availability.Provenance = "Capability state was invalid and was disabled during normalization.";
            repaired = true;
        }
        else if (availability.Provenance == null)
        {
            availability.Provenance = string.Empty;
            repaired = true;
        }
        if (value.UniqueContainersLooted < 0)
        {
            value.UniqueContainersLooted = 0;
            repaired = true;
        }
        if (repaired || value.WasRepairedFromInvalidState)
        {
            if (availability.State != AdapterCapabilityState.DisabledIncompatible
                || !string.Equals(availability.Provenance, RepairProvenance, StringComparison.Ordinal))
            {
                availability.State = AdapterCapabilityState.DisabledIncompatible;
                availability.Provenance = RepairProvenance;
                repaired = true;
            }
            value.WasRepairedFromInvalidState = true;
        }
        return repaired;
    }

    public static bool NormalizeCheckpoint(ContainerRunCheckpointState value)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));
        var repaired = false;
        value.Statistics ??= Repair(new ContainerStatisticsAggregate(), ref repaired);
        repaired |= NormalizePersisted(value.Statistics);
        value.LootedContainerKeys ??= Repair(new List<int>(), ref repaired);
        value.LootedContainerIdentities ??= Repair(new List<string>(), ref repaired);
        var normalized = value.LootedContainerKeys.Distinct().OrderBy(key => key).ToList();
        if (!value.LootedContainerKeys.SequenceEqual(normalized))
        {
            value.LootedContainerKeys = normalized;
            repaired = true;
        }
        if (value.LootedContainerIdentities.Count == 0 && value.LootedContainerKeys.Count > 0)
        {
            value.LootedContainerIdentities = value.LootedContainerKeys
                .Select(key => $"legacy:{key}")
                .OrderBy(identity => identity, StringComparer.Ordinal)
                .ToList();
        }
        var identities = value.LootedContainerIdentities
            .Where(identity => !string.IsNullOrWhiteSpace(identity))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(identity => identity, StringComparer.Ordinal)
            .ToList();
        if (!value.LootedContainerIdentities.SequenceEqual(identities, StringComparer.Ordinal))
        {
            value.LootedContainerIdentities = identities;
            repaired = true;
        }
        if (value.LootedContainerIdentities.Count > ContainerRunCheckpointState.DeduplicationCapacity)
        {
            throw new ArgumentException("Container checkpoint exceeds the bounded deduplication capacity.", nameof(value));
        }
        if (value.DeduplicationSaturated
            && value.LootedContainerIdentities.Count != ContainerRunCheckpointState.DeduplicationCapacity)
        {
            throw new ArgumentException("Saturated container checkpoint does not retain the complete bounded key set.", nameof(value));
        }
        if (value.Statistics.UniqueContainersLooted != value.LootedContainerIdentities.Count)
        {
            throw new ArgumentException("Container checkpoint total does not match its unique stable-key set.", nameof(value));
        }
        if (value.DeduplicationSaturated
            && value.Statistics.Capabilities.UniqueContainersLooted.State != AdapterCapabilityState.DisabledIncompatible)
        {
            value.Statistics.Capabilities.UniqueContainersLooted = new MetricAvailability
            {
                State = AdapterCapabilityState.DisabledIncompatible,
                Provenance = "The per-run stable-key deduplication capacity was reached; further containers were not recorded."
            };
            repaired = true;
        }
        value.WasRepairedFromInvalidState |= repaired;
        value.Statistics.WasRepairedFromInvalidState |= repaired;
        if (repaired)
        {
            value.Statistics.Capabilities.UniqueContainersLooted.State =
                AdapterCapabilityState.DisabledIncompatible;
            value.Statistics.Capabilities.UniqueContainersLooted.Provenance = RepairProvenance;
        }
        return repaired;
    }

    public static void ValidateRecoveryCandidate(ContainerRunCheckpointState? value, int schemaVersion)
    {
        if (schemaVersion >= 7 && (value == null
            || value.Statistics == null
            || value.Statistics.Capabilities == null
            || value.Statistics.Capabilities.UniqueContainersLooted == null
            || value.LootedContainerKeys == null
            || (schemaVersion >= 8 && value.LootedContainerIdentities == null)))
        {
            throw new ArgumentException("Current-schema container checkpoint is incomplete.", nameof(value));
        }
    }

    public static void ValidateAggregate(ContainerStatisticsAggregate value)
    {
        if (value == null || value.Capabilities?.UniqueContainersLooted == null || value.UniqueContainersLooted < 0)
            throw new ArgumentException("Container statistics are invalid.", nameof(value));
        if (!Enum.IsDefined(typeof(AdapterCapabilityState), value.Capabilities.UniqueContainersLooted.State))
            throw new ArgumentException("Container capability state is invalid.", nameof(value));
    }

    public static void ApplyCurrentAvailability(
        ContainerStatisticsAggregate aggregate,
        AdapterCapabilityState state,
        string? provenance)
    {
        NormalizePersisted(aggregate);
        if (aggregate.WasRepairedFromInvalidState)
        {
            aggregate.Capabilities.UniqueContainersLooted.State = AdapterCapabilityState.DisabledIncompatible;
            aggregate.Capabilities.UniqueContainersLooted.Provenance = RepairProvenance;
            return;
        }
        if (aggregate.HistoricalUnavailable || aggregate.UniqueContainersLooted > 0) return;
        aggregate.Capabilities.UniqueContainersLooted = new MetricAvailability
        {
            State = state,
            Provenance = provenance ?? string.Empty
        };
    }

    public static void RestrictCapabilities(
        ContainerStatisticsAggregate aggregate,
        ContainerMetricCapabilities capabilities)
    {
        if (aggregate == null) throw new ArgumentNullException(nameof(aggregate));
        if (capabilities == null) throw new ArgumentNullException(nameof(capabilities));
        NormalizePersisted(aggregate);
        aggregate.Capabilities.UniqueContainersLooted = Restrict(
            aggregate.Capabilities.UniqueContainersLooted,
            capabilities.UniqueContainersLooted,
            preferSourceOnTie: true);
    }

    public static bool IsEmpty(ContainerStatisticsAggregate value) => value != null
        && !value.WasRepairedFromInvalidState
        && value.UniqueContainersLooted == 0;

    private static void ValidateEvent(ContainerLooted value)
    {
        if (value == null
            || string.IsNullOrWhiteSpace(value.EventId)
            || string.IsNullOrWhiteSpace(value.SaveGenerationId)
            || string.IsNullOrWhiteSpace(value.RunId)
            || string.IsNullOrWhiteSpace(value.MapId)
            || value.GameplayContext != GameplayContext.Raid)
            throw new ArgumentException("Container-looted event is invalid.", nameof(value));
    }

    private static string StableIdentity(ContainerLooted value) =>
        string.IsNullOrWhiteSpace(value.SegmentId)
            ? $"legacy:{value.ContainerKey}"
            : $"{value.MapId}\u001f{value.ContainerKey}";

    private static MetricAvailability Restrict(MetricAvailability left, MetricAvailability right, bool preferSourceOnTie) =>
        (int)left.State > (int)right.State || (!preferSourceOnTie && left.State == right.State)
            ? Clone(left)
            : Clone(right);

    private static MetricAvailability Clone(MetricAvailability? value) => new()
    {
        State = value?.State ?? AdapterCapabilityState.DisabledIncompatible,
        Provenance = value?.Provenance ?? "Capability metadata was missing."
    };

    private static T Repair<T>(T value, ref bool repaired) { repaired = true; return value; }

    private static long SaturatingAdd(long left, long right)
    {
        left = Math.Max(0, left);
        right = Math.Max(0, right);
        return left > long.MaxValue - right ? long.MaxValue : left + right;
    }
}
