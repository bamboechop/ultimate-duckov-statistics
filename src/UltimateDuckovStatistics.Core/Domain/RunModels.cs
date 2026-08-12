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

    public RunSummary ToInterruptedSummary()
    {
        var endedUtc = EnsureUtc(LastObservedUtc == default ? StartedUtc : LastObservedUtc);
        var startedUtc = EnsureUtc(StartedUtc);
        return new RunSummary
        {
            RunId = RunId,
            SaveGenerationId = SaveGenerationId,
            NativeRaidId = NativeRaidId,
            MapId = MapId,
            MapDisplayName = MapDisplayName,
            MapKnown = MapKnown,
            StartedUtc = startedUtc,
            EndedUtc = endedUtc < startedUtc ? startedUtc : endedUtc,
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
            EquipmentStatistics = EquipmentStatisticsReducer.Clone(EquipmentStatistics)
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
