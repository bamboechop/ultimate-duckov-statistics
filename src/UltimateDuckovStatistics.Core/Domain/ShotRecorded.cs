using System.Runtime.Serialization;

namespace UltimateDuckovStatistics.Core.Domain;

[DataContract]
public sealed class MetricAvailability
{
    [DataMember(Order = 1)]
    public AdapterCapabilityState State { get; set; } = AdapterCapabilityState.DisabledIncompatible;

    [DataMember(Order = 2)]
    public string Provenance { get; set; } = string.Empty;
}

[DataContract]
public sealed class WeaponMetricCapabilities
{
    [DataMember(Order = 1)]
    public MetricAvailability FiringActions { get; set; } = new();

    [DataMember(Order = 2)]
    public MetricAvailability AmmunitionConsumption { get; set; } = new();

    [DataMember(Order = 3)]
    public MetricAvailability Projectiles { get; set; } = new();

    [DataMember(Order = 4)]
    public MetricAvailability WeaponIdentity { get; set; } = new();

    [DataMember(Order = 5)]
    public MetricAvailability AmmunitionIdentity { get; set; } = new();
}

[DataContract]
public sealed class ShotRecorded
{
    [DataMember(Order = 1)]
    public int SchemaVersion { get; set; } = ProductInfo.SchemaVersion;

    [DataMember(Order = 2)]
    public string EventId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public DateTime TimestampUtc { get; set; }

    [DataMember(Order = 4)]
    public string SaveGenerationId { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public string RunId { get; set; } = string.Empty;

    [DataMember(Order = 6)]
    public string MapId { get; set; } = MapIdentity.UnknownId;

    [DataMember(Order = 7)]
    public GameplayContext GameplayContext { get; set; }

    [DataMember(Order = 8)]
    public IntegrityTags IntegrityTags { get; set; }

    [DataMember(Order = 9)]
    public string GameVersion { get; set; } = string.Empty;

    [DataMember(Order = 10)]
    public string GameBuild { get; set; } = string.Empty;

    [DataMember(Order = 11)]
    public string AdapterVersion { get; set; } = string.Empty;

    [DataMember(Order = 12)]
    public string WeaponId { get; set; } = string.Empty;

    [DataMember(Order = 13)]
    public string WeaponDisplayName { get; set; } = string.Empty;

    [DataMember(Order = 14)]
    public string AmmunitionId { get; set; } = string.Empty;

    [DataMember(Order = 15)]
    public string AmmunitionDisplayName { get; set; } = string.Empty;

    [DataMember(Order = 16, EmitDefaultValue = false)]
    public long? FiringActionCount { get; set; }

    [DataMember(Order = 17, EmitDefaultValue = false)]
    public long? AmmunitionUnitsConsumed { get; set; }

    [DataMember(Order = 18, EmitDefaultValue = false)]
    public long? ProjectileCount { get; set; }

    [DataMember(Order = 19)]
    public WeaponMetricCapabilities Capabilities { get; set; } = new();
}
