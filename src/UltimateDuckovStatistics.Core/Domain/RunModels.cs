using System.Runtime.Serialization;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Domain;

[DataContract]
public sealed class MapIdentity
{
    public const string UnknownId = "duckov:map:unknown";
    public const string UnknownDisplayName = "Unknown map";

    [DataMember(Order = 1)]
    public string MapId { get; set; } = UnknownId;

    [DataMember(Order = 2)]
    public string DisplayName { get; set; } = UnknownDisplayName;

    [DataMember(Order = 3)]
    public bool IsKnown { get; set; }

    public static bool TryFromNativeStableId(
        string? stableId,
        string? displayName,
        bool isKnown,
        out MapIdentity identity)
    {
        if (string.IsNullOrWhiteSpace(stableId))
        {
            identity = new MapIdentity();
            return false;
        }

        identity = new MapIdentity
        {
            MapId = $"duckov:map:{stableId}",
            DisplayName = string.IsNullOrWhiteSpace(displayName) ? stableId : displayName,
            IsKnown = isKnown
        };
        return true;
    }
}

[DataContract]
public sealed class RunSummary
{
    [DataMember(Order = 1)]
    public int SchemaVersion { get; set; } = ProductInfo.SchemaVersion;

    [DataMember(Order = 2)]
    public string RunId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string SaveGenerationId { get; set; } = string.Empty;

    [DataMember(Order = 4, EmitDefaultValue = false)]
    public string? NativeRaidId { get; set; }

    [DataMember(Order = 5)]
    public string MapId { get; set; } = MapIdentity.UnknownId;

    [DataMember(Order = 6)]
    public string MapDisplayName { get; set; } = MapIdentity.UnknownDisplayName;

    [DataMember(Order = 7)]
    public bool MapKnown { get; set; }

    [DataMember(Order = 8)]
    public DateTime StartedUtc { get; set; }

    [DataMember(Order = 9)]
    public DateTime EndedUtc { get; set; }

    [DataMember(Order = 10)]
    public double ActiveDurationSeconds { get; set; }

    [DataMember(Order = 11)]
    public double WallClockDurationSeconds { get; set; }

    [DataMember(Order = 12)]
    public RunOutcome Outcome { get; set; }

    [DataMember(Order = 13)]
    public double PhysicalDistance { get; set; }

    [DataMember(Order = 14)]
    public double TeleportDistance { get; set; }

    [DataMember(Order = 15)]
    public IntegrityTags IntegrityTags { get; set; }

    [DataMember(Order = 16)]
    public bool RecordEligible { get; set; }

    [DataMember(Order = 17)]
    public string GameVersion { get; set; } = string.Empty;

    [DataMember(Order = 18)]
    public string GameBuild { get; set; } = string.Empty;

    [DataMember(Order = 19)]
    public AdapterCapabilityState LifecycleCapability { get; set; }

    [DataMember(Order = 20)]
    public string LifecycleAdapterVersion { get; set; } = string.Empty;

    [DataMember(Order = 21)]
    public AdapterCapabilityState MovementCapability { get; set; }

    [DataMember(Order = 22)]
    public string MovementAdapterVersion { get; set; } = string.Empty;

    [DataMember(Order = 23)]
    public AdapterCapabilityState MapCapability { get; set; }

    [DataMember(Order = 24)]
    public string MapAdapterVersion { get; set; } = string.Empty;

    [DataMember(Order = 25)]
    public WeaponStatisticsAggregate WeaponStatistics { get; set; } = new();

    [DataMember(Order = 26)]
    public CombatStatisticsAggregate CombatStatistics { get; set; } = new();

    [DataMember(Order = 27)]
    public EquipmentStatisticsAggregate EquipmentStatistics { get; set; } = new();

    [DataMember(Order = 28)]
    public ContainerStatisticsAggregate ContainerStatistics { get; set; } = new();

    [DataMember(Order = 29)]
    public string StartingMapId { get; set; } = MapIdentity.UnknownId;

    [DataMember(Order = 30)]
    public string StartingMapDisplayName { get; set; } = MapIdentity.UnknownDisplayName;

    [DataMember(Order = 31)]
    public bool StartingMapKnown { get; set; }

    [DataMember(Order = 32)]
    public string EndingMapId { get; set; } = MapIdentity.UnknownId;

    [DataMember(Order = 33)]
    public string EndingMapDisplayName { get; set; } = MapIdentity.UnknownDisplayName;

    [DataMember(Order = 34)]
    public bool EndingMapKnown { get; set; }

    [DataMember(Order = 35)]
    public string RouteSignature { get; set; } = string.Empty;

    [DataMember(Order = 36)]
    public List<MapSegmentSummary> Segments { get; set; } = new();

    [DataMember(Order = 37)]
    public double TransitionExcludedDistance { get; set; }

    [DataMember(Order = 38)]
    public RouteMetricCapabilities RouteCapabilities { get; set; } = new();

    [DataMember(Order = 39)]
    public bool HistoricalRouteUnavailable { get; set; }

    [DataMember(Order = 40)]
    public bool RouteWasRepairedFromInvalidState { get; set; }

    [DataMember(Order = 41)]
    public List<SegmentEventAssociation> SegmentEventAssociations { get; set; } = new();

    [DataMember(Order = 42)]
    public ItemStatisticsAggregate ItemStatistics { get; set; } = new();
}

[DataContract]
public sealed class ActiveRunCheckpoint
{
    [DataMember(Order = 1)]
    public int SchemaVersion { get; set; } = ProductInfo.SchemaVersion;

    [DataMember(Order = 2)]
    public string RunId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string SaveGenerationId { get; set; } = string.Empty;

    [DataMember(Order = 4, EmitDefaultValue = false)]
    public string? NativeRaidId { get; set; }

    [DataMember(Order = 5)]
    public string MapId { get; set; } = MapIdentity.UnknownId;

    [DataMember(Order = 6)]
    public string MapDisplayName { get; set; } = MapIdentity.UnknownDisplayName;

    [DataMember(Order = 7)]
    public bool MapKnown { get; set; }

    [DataMember(Order = 8)]
    public DateTime StartedUtc { get; set; }

    [DataMember(Order = 9)]
    public DateTime LastObservedUtc { get; set; }

    [DataMember(Order = 10)]
    public double ActiveDurationSeconds { get; set; }

    [DataMember(Order = 11)]
    public double PhysicalDistance { get; set; }

    [DataMember(Order = 12)]
    public double TeleportDistance { get; set; }

    [DataMember(Order = 13)]
    public IntegrityTags IntegrityTags { get; set; }

    [DataMember(Order = 14)]
    public string GameVersion { get; set; } = string.Empty;

    [DataMember(Order = 15)]
    public string GameBuild { get; set; } = string.Empty;

    [DataMember(Order = 16)]
    public AdapterCapabilityState LifecycleCapability { get; set; }

    [DataMember(Order = 17)]
    public string LifecycleAdapterVersion { get; set; } = string.Empty;

    [DataMember(Order = 18)]
    public AdapterCapabilityState MovementCapability { get; set; }

    [DataMember(Order = 19)]
    public string MovementAdapterVersion { get; set; } = string.Empty;

    [DataMember(Order = 20)]
    public AdapterCapabilityState MapCapability { get; set; }

    [DataMember(Order = 21)]
    public string MapAdapterVersion { get; set; } = string.Empty;

    [DataMember(Order = 22)]
    public WeaponStatisticsAggregate WeaponStatistics { get; set; } = new();

    [DataMember(Order = 23)]
    public CombatStatisticsAggregate CombatStatistics { get; set; } = new();

    [DataMember(Order = 24)]
    public EquipmentStatisticsAggregate EquipmentStatistics { get; set; } = new();

    [DataMember(Order = 25)]
    public ContainerRunCheckpointState ContainerState { get; set; } = new();

    [DataMember(Order = 26)]
    public string StartingMapId { get; set; } = MapIdentity.UnknownId;

    [DataMember(Order = 27)]
    public string StartingMapDisplayName { get; set; } = MapIdentity.UnknownDisplayName;

    [DataMember(Order = 28)]
    public bool StartingMapKnown { get; set; }

    [DataMember(Order = 29)]
    public List<MapSegmentSummary> Segments { get; set; } = new();

    [DataMember(Order = 30)]
    public double TransitionExcludedDistance { get; set; }

    [DataMember(Order = 31)]
    public RouteMetricCapabilities RouteCapabilities { get; set; } = new();

    [DataMember(Order = 32)]
    public bool HistoricalRouteUnavailable { get; set; }

    [DataMember(Order = 33)]
    public bool RouteWasRepairedFromInvalidState { get; set; }

    [DataMember(Order = 34)]
    public List<SegmentEventAssociation> SegmentEventAssociations { get; set; } = new();

    [DataMember(Order = 35)]
    public ItemStatisticsAggregate ItemStatistics { get; set; } = new();

    [DataMember(Order = 36)]
    public bool TransitionPending { get; set; }

    [DataMember(Order = 37, EmitDefaultValue = false)]
    public string? CurrentSegmentId { get; set; }

    [DataMember(Order = 38)]
    public MovementBaselineState MovementBaseline { get; set; } = new();

    public RunSummary ToInterruptedSummary()
    {
        var endedUtc = EnsureUtc(LastObservedUtc == default ? StartedUtc : LastObservedUtc);
        var startedUtc = EnsureUtc(StartedUtc);
        if (endedUtc < startedUtc)
        {
            endedUtc = startedUtc;
        }
        var recoveredSegments = Segments
            .Select(segment => RouteStatisticsReducer.CloneSegmentForInterruptedRecovery(segment, endedUtc))
            .ToList();
        if (TransitionPending && recoveredSegments.Count > 0)
        {
            recoveredSegments[^1].ExitReason = MapSegmentExitReason.Interrupted;
        }
        var routeCapabilities = RouteCapabilities
                                ?? RouteStatisticsReducer.Unavailable("Route capability record was missing during interrupted recovery.");
        var routeSupported = !HistoricalRouteUnavailable
                             && routeCapabilities.OrderedRoute?.State == AdapterCapabilityState.Supported
                             && routeCapabilities.Segments?.State == AdapterCapabilityState.Supported;
        return new RunSummary
        {
            RunId = RunId,
            SaveGenerationId = SaveGenerationId,
            NativeRaidId = NativeRaidId,
            MapId = MapId,
            MapDisplayName = MapDisplayName,
            MapKnown = MapKnown,
            StartedUtc = startedUtc,
            EndedUtc = endedUtc,
            ActiveDurationSeconds = FiniteNonNegative(ActiveDurationSeconds),
            WallClockDurationSeconds = Math.Max(0, (endedUtc - startedUtc).TotalSeconds),
            Outcome = RunOutcome.Interrupted,
            PhysicalDistance = FiniteNonNegative(PhysicalDistance),
            TeleportDistance = FiniteNonNegative(TeleportDistance),
            IntegrityTags = IntegrityTags,
            RecordEligible = false,
            GameVersion = GameVersion,
            GameBuild = GameBuild,
            LifecycleCapability = LifecycleCapability,
            LifecycleAdapterVersion = LifecycleAdapterVersion,
            MovementCapability = MovementCapability,
            MovementAdapterVersion = MovementAdapterVersion,
            MapCapability = MapCapability,
            MapAdapterVersion = MapAdapterVersion,
            WeaponStatistics = WeaponStatisticsReducer.Clone(WeaponStatistics),
            CombatStatistics = CombatStatisticsReducer.Clone(CombatStatistics),
            EquipmentStatistics = EquipmentStatisticsReducer.Clone(EquipmentStatistics),
            ContainerStatistics = ContainerStatisticsReducer.Clone(ContainerState.Statistics),
            StartingMapId = StartingMapId,
            StartingMapDisplayName = StartingMapDisplayName,
            StartingMapKnown = StartingMapKnown,
            EndingMapId = routeSupported ? recoveredSegments.LastOrDefault()?.MapId ?? MapIdentity.UnknownId : MapIdentity.UnknownId,
            EndingMapDisplayName = routeSupported
                ? recoveredSegments.LastOrDefault()?.MapDisplayName ?? MapIdentity.UnknownDisplayName
                : MapIdentity.UnknownDisplayName,
            EndingMapKnown = routeSupported && recoveredSegments.LastOrDefault()?.MapKnown == true,
            RouteSignature = routeSupported ? RouteStatisticsReducer.BuildSignature(recoveredSegments) : string.Empty,
            Segments = recoveredSegments,
            TransitionExcludedDistance = FiniteNonNegative(TransitionExcludedDistance),
            RouteCapabilities = RouteStatisticsReducer.CloneCapabilities(routeCapabilities),
            HistoricalRouteUnavailable = HistoricalRouteUnavailable,
            RouteWasRepairedFromInvalidState = RouteWasRepairedFromInvalidState,
            SegmentEventAssociations = SegmentEventAssociations.Select(RouteStatisticsReducer.CloneAssociation).ToList(),
            ItemStatistics = ItemStatisticsAggregateReducer.Clone(ItemStatistics)
        };
    }

    private static double FiniteNonNegative(double value) =>
        double.IsNaN(value) || double.IsInfinity(value) ? 0 : Math.Max(0, value);

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
