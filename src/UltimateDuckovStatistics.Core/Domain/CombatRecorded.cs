using System.Runtime.Serialization;

namespace UltimateDuckovStatistics.Core.Domain;

[DataContract]
public sealed class CombatMetricCapabilities
{
    [DataMember(Order = 1)] public MetricAvailability DamageDealt { get; set; } = new();
    [DataMember(Order = 2)] public MetricAvailability DamageReceived { get; set; } = new();
    [DataMember(Order = 3)] public MetricAvailability RangedHits { get; set; } = new();
    [DataMember(Order = 4)] public MetricAvailability Accuracy { get; set; } = new();
    [DataMember(Order = 5)] public MetricAvailability MeleeSwings { get; set; } = new();
    [DataMember(Order = 6)] public MetricAvailability MeleeHits { get; set; } = new();
    [DataMember(Order = 7)] public MetricAvailability EnemiesKilled { get; set; } = new();
    [DataMember(Order = 8)] public MetricAvailability PlayerDeaths { get; set; } = new();
    [DataMember(Order = 9)] public MetricAvailability Ownership { get; set; } = new();
    [DataMember(Order = 10)] public MetricAvailability EnemyIdentity { get; set; } = new();
    [DataMember(Order = 11)] public MetricAvailability EnemyFamily { get; set; } = new();
    [DataMember(Order = 12)] public MetricAvailability Cause { get; set; } = new();
    [DataMember(Order = 13)] public MetricAvailability WeaponIdentity { get; set; } = new();
    [DataMember(Order = 14)] public MetricAvailability AmmunitionIdentity { get; set; } = new();
    [DataMember(Order = 15)] public MetricAvailability DamageOverTime { get; set; } = new();
    [DataMember(Order = 16)] public MetricAvailability Headshots { get; set; } = new();
    [DataMember(Order = 17)] public MetricAvailability HeadshotFinalBlows { get; set; } = new();
}

[DataContract]
public sealed record class CombatRecorded
{
    [DataMember(Order = 1)] public int SchemaVersion { get; set; } = ProductInfo.SchemaVersion;
    [DataMember(Order = 2)] public string EventId { get; set; } = string.Empty;
    [DataMember(Order = 3)] public DateTime TimestampUtc { get; set; }
    [DataMember(Order = 4)] public string SaveGenerationId { get; set; } = string.Empty;
    [DataMember(Order = 5)] public string RunId { get; set; } = string.Empty;
    [DataMember(Order = 6)] public string MapId { get; set; } = MapIdentity.UnknownId;
    [DataMember(Order = 7)] public GameplayContext GameplayContext { get; set; }
    [DataMember(Order = 8)] public IntegrityTags IntegrityTags { get; set; }
    [DataMember(Order = 9)] public string GameVersion { get; set; } = string.Empty;
    [DataMember(Order = 10)] public string GameBuild { get; set; } = string.Empty;
    [DataMember(Order = 11)] public string AdapterVersion { get; set; } = string.Empty;
    [DataMember(Order = 12)] public CombatOwnership Ownership { get; set; }
    [DataMember(Order = 13)] public CombatAttackKind AttackKind { get; set; }
    [DataMember(Order = 14)] public CombatCauseKind CauseKind { get; set; }
    [DataMember(Order = 15)] public string CauseId { get; set; } = "duckov:cause:unknown";
    [DataMember(Order = 16)] public string CauseDisplayName { get; set; } = "Unknown cause";
    [DataMember(Order = 17)] public string AttackerId { get; set; } = "duckov:attacker:unknown";
    [DataMember(Order = 18)] public string AttackerDisplayName { get; set; } = "Unknown attacker";
    [DataMember(Order = 19)] public string TargetId { get; set; } = "duckov:target:unknown";
    [DataMember(Order = 20)] public string TargetDisplayName { get; set; } = "Unknown target";
    [DataMember(Order = 21)] public string TargetFamilyId { get; set; } = "duckov:family:unknown";
    [DataMember(Order = 22)] public string TargetFamilyDisplayName { get; set; } = "Unknown family";
    [DataMember(Order = 23)] public string WeaponId { get; set; } = "duckov:weapon:unknown";
    [DataMember(Order = 24)] public string WeaponDisplayName { get; set; } = "Unknown weapon";
    [DataMember(Order = 25)] public string AmmunitionId { get; set; } = "duckov:ammo:unknown";
    [DataMember(Order = 26)] public string AmmunitionDisplayName { get; set; } = "Unknown ammunition";
    [DataMember(Order = 27, EmitDefaultValue = false)] public string? ProjectileId { get; set; }
    [DataMember(Order = 28)] public double ActualDamageToTarget { get; set; }
    [DataMember(Order = 29)] public double ActualDamageDealt { get; set; }
    [DataMember(Order = 30)] public double ActualDamageReceived { get; set; }
    [DataMember(Order = 31)] public long CompletedPlayerProjectiles { get; set; }
    [DataMember(Order = 32)] public long RangedHits { get; set; }
    [DataMember(Order = 33)] public long MeleeSwings { get; set; }
    [DataMember(Order = 34)] public long MeleeHits { get; set; }
    [DataMember(Order = 35)] public long EnemiesKilled { get; set; }
    [DataMember(Order = 36)] public long PlayerDeaths { get; set; }
    [DataMember(Order = 37)] public long Headshots { get; set; }
    [DataMember(Order = 38)] public long HeadshotFinalBlows { get; set; }
    [DataMember(Order = 39)] public bool IsFinalBlow { get; set; }
    [DataMember(Order = 40)] public bool IsDamageOverTime { get; set; }
    [DataMember(Order = 41)] public CombatMetricCapabilities Capabilities { get; set; } = new();
    [DataMember(Order = 42)] public bool TargetIsEnemy { get; set; }
    [DataMember(Order = 43)] public EquipmentEventAssociation EquipmentAssociation { get; set; } = new();
}
