using System.Runtime.Serialization;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Domain;

[DataContract]
public enum MapSegmentExitReason
{
    [EnumMember] None = 0,
    [EnumMember] Transition = 1,
    [EnumMember] Extracted = 2,
    [EnumMember] Died = 3,
    [EnumMember] Interrupted = 4
}

[DataContract]
public sealed class RouteMetricCapabilities
{
    [DataMember(Order = 1)] public MetricAvailability OrderedRoute { get; set; } = new();
    [DataMember(Order = 2)] public MetricAvailability Segments { get; set; } = new();
    [DataMember(Order = 3)] public MetricAvailability EventAttribution { get; set; } = new();
    [DataMember(Order = 4)] public MetricAvailability RouteAwareMapTotals { get; set; } = new();
}

[DataContract]
public sealed class ItemStatisticsAggregate
{
    [DataMember(Order = 1)] public AggregateTotals Overall { get; set; } = new();
    [DataMember(Order = 2)] public Dictionary<string, ItemAggregate> Items { get; set; } = new(StringComparer.Ordinal);
    [DataMember(Order = 3)] public Dictionary<string, AggregateTotals> Groups { get; set; } = new(StringComparer.Ordinal);
    [DataMember(Order = 4)] public List<string> RecentEventIds { get; set; } = new();
    [DataMember(Order = 5)] public bool HistoricalUnavailable { get; set; }
    [DataMember(Order = 6)] public bool WasRepairedFromInvalidState { get; set; }
}

[DataContract]
public sealed class MapSegmentSummary
{
    [DataMember(Order = 1)] public string SegmentId { get; set; } = string.Empty;
    [DataMember(Order = 2)] public int SegmentIndex { get; set; }
    [DataMember(Order = 3)] public string MapId { get; set; } = MapIdentity.UnknownId;
    [DataMember(Order = 4)] public string MapDisplayName { get; set; } = MapIdentity.UnknownDisplayName;
    [DataMember(Order = 5)] public bool MapKnown { get; set; }
    [DataMember(Order = 6)] public DateTime EnteredUtc { get; set; }
    [DataMember(Order = 7, EmitDefaultValue = false)] public DateTime? ExitedUtc { get; set; }
    [DataMember(Order = 8)] public double ActiveDurationSeconds { get; set; }
    [DataMember(Order = 9)] public double PhysicalDistance { get; set; }
    [DataMember(Order = 10)] public double TeleportDistance { get; set; }
    [DataMember(Order = 11)] public double TransitionExcludedDistance { get; set; }
    [DataMember(Order = 12)] public MapSegmentExitReason ExitReason { get; set; }
    [DataMember(Order = 13)] public IntegrityTags IntegrityTags { get; set; }
    [DataMember(Order = 14)] public ItemStatisticsAggregate ItemStatistics { get; set; } = new();
    [DataMember(Order = 15)] public WeaponStatisticsAggregate WeaponStatistics { get; set; } = new();
    [DataMember(Order = 16)] public CombatStatisticsAggregate CombatStatistics { get; set; } = new();
    [DataMember(Order = 17)] public EquipmentStatisticsAggregate EquipmentStatistics { get; set; } = new();
    [DataMember(Order = 18)] public ContainerStatisticsAggregate ContainerStatistics { get; set; } = new();
    [DataMember(Order = 19)] public bool WasRepairedFromInvalidState { get; set; }
    [DataMember(Order = 20)] public EconomyStatisticsAggregate Economy { get; set; } = new();
}

[DataContract]
public sealed class SegmentEventAssociation
{
    [DataMember(Order = 1)] public string EventId { get; set; } = string.Empty;
    [DataMember(Order = 2)] public string EventKind { get; set; } = string.Empty;
    [DataMember(Order = 3)] public DateTime TimestampUtc { get; set; }
    [DataMember(Order = 4)] public string SourceSegmentId { get; set; } = string.Empty;
    [DataMember(Order = 5)] public string SourceMapId { get; set; } = MapIdentity.UnknownId;
    [DataMember(Order = 6)] public string OutcomeSegmentId { get; set; } = string.Empty;
    [DataMember(Order = 7)] public string OutcomeMapId { get; set; } = MapIdentity.UnknownId;
}

[DataContract]
public sealed class RouteAwareMapAggregate
{
    [DataMember(Order = 1)] public string MapId { get; set; } = MapIdentity.UnknownId;
    [DataMember(Order = 2)] public string DisplayName { get; set; } = MapIdentity.UnknownDisplayName;
    [DataMember(Order = 3)] public bool IsKnown { get; set; }
    [DataMember(Order = 4)] public long RunsVisited { get; set; }
    [DataMember(Order = 5)] public long SegmentVisits { get; set; }
    [DataMember(Order = 6)] public double ActiveDurationSeconds { get; set; }
    [DataMember(Order = 7)] public double PhysicalDistance { get; set; }
    [DataMember(Order = 8)] public double TeleportDistance { get; set; }
    [DataMember(Order = 9)] public double TransitionExcludedDistance { get; set; }
    [DataMember(Order = 10)] public ItemStatisticsAggregate ItemStatistics { get; set; } = new();
    [DataMember(Order = 11)] public WeaponStatisticsAggregate WeaponStatistics { get; set; } = new();
    [DataMember(Order = 12)] public CombatStatisticsAggregate CombatStatistics { get; set; } = new();
    [DataMember(Order = 13)] public EquipmentStatisticsAggregate EquipmentStatistics { get; set; } = new();
    [DataMember(Order = 14)] public ContainerStatisticsAggregate ContainerStatistics { get; set; } = new();
    [DataMember(Order = 15)] public bool HistoricalUnavailable { get; set; }
    [DataMember(Order = 16)] public bool WasRepairedFromInvalidState { get; set; }
    [DataMember(Order = 17)] public EconomyStatisticsAggregate Economy { get; set; } = new();
}

public sealed class EventAttributionContext
{
    public string RunId { get; set; } = string.Empty;
    public string MapId { get; set; } = MapIdentity.UnknownId;
    public string SegmentId { get; set; } = string.Empty;
    public bool RouteSupported { get; set; }
}

[DataContract]
public sealed class MovementBaselineState
{
    [DataMember(Order = 1)] public bool HasBaseline { get; set; }
    [DataMember(Order = 2)] public double X { get; set; }
    [DataMember(Order = 3)] public double Y { get; set; }
    [DataMember(Order = 4)] public double Z { get; set; }
    [DataMember(Order = 5)] public double MonotonicSeconds { get; set; }
}
