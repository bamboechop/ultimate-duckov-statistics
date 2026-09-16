using System.Runtime.Serialization;

namespace UltimateDuckovStatistics.Core.Encounters;

public enum EncounterRecordKind { Visit = 1, Route = 2, Encounter = 3, Damage = 4, Inventory = 5, Loot = 6, Coverage = 7 }
public enum EncounterCaptureIssue { QueueLimit = 1, ReductionFailed = 2, NativeCaptureFailed = 3, CombatIncomplete = 4, LootIncomplete = 5, MapIncomplete = 6 }
public enum RouteConnection { Start = 0, Walk = 1, Teleport = 2, Gap = 3 }
public enum EncounterOutcome { PlayerKill = 1, PlayerDeath = 2, OtherDeath = 3 }
public enum EncounterCredit { Unknown = 0, Player = 1, Other = 2 }

// A discriminated record rather than a polymorphic serialized object: both the
// native JSON writer and the portable reader preserve exactly the same contract.
// Generation ownership lives on the profile; a restore keeps run/actor IDs intact.
[DataContract]
public sealed class EncounterRecord
{
    [DataMember(IsRequired = true, Order = 1)] public int Version { get; set; } = 1;
    [DataMember(IsRequired = true, Order = 2)] public string RunId { get; set; } = string.Empty;
    [DataMember(IsRequired = true, Order = 3)] public string Id { get; set; } = string.Empty;
    [DataMember(IsRequired = true, Order = 4)] public EncounterRecordKind Kind { get; set; }
    [DataMember(IsRequired = true, Order = 5)] public string VisitId { get; set; } = string.Empty;
    [DataMember(Order = 6, EmitDefaultValue = false)] public string? EncounterId { get; set; }
    [DataMember(Order = 7, EmitDefaultValue = false)] public EncounterVisit? Visit { get; set; }
    [DataMember(Order = 8, EmitDefaultValue = false)] public EncounterRouteChunk? Route { get; set; }
    [DataMember(Order = 9, EmitDefaultValue = false)] public EncounterDetail? Encounter { get; set; }
    [DataMember(Order = 10, EmitDefaultValue = false)] public EncounterDamage? Damage { get; set; }
    [DataMember(Order = 11, EmitDefaultValue = false)] public EncounterInventory? Inventory { get; set; }
    [DataMember(Order = 12, EmitDefaultValue = false)] public EncounterLoot? Loot { get; set; }
    [DataMember(Order = 13, EmitDefaultValue = false)] public EncounterCoverage? Coverage { get; set; }
}

// Run-owned evidence: a recorder can fail before it observes any map or actor.
[DataContract]
public sealed class EncounterCoverage
{
    [DataMember(IsRequired = true, Order = 1)] public EncounterCaptureIssue Issue { get; set; }
    [DataMember(IsRequired = true, Order = 2)] public double ObservedSeconds { get; set; }
    [DataMember(IsRequired = true, Order = 3)] public bool CaptureStopped { get; set; }
}

[DataContract]
public sealed class EncounterPosition
{
    // Preserve the native coordinates, including height. Map projection uses X/Z.
    [DataMember(IsRequired = true, Order = 1)] public float X { get; set; }
    [DataMember(IsRequired = true, Order = 2)] public float Y { get; set; }
    [DataMember(IsRequired = true, Order = 3)] public float Z { get; set; }
    [DataMember(Order = 4, EmitDefaultValue = false)] public string? MapId { get; set; }
}

[DataContract]
public sealed class EncounterVisit
{
    [DataMember(IsRequired = true, Order = 1)] public string MapId { get; set; } = string.Empty;
    [DataMember(IsRequired = true, Order = 2)] public string SegmentId { get; set; } = string.Empty;
    [DataMember(IsRequired = true, Order = 3)] public int Ordinal { get; set; }
    [DataMember(IsRequired = true, Order = 4)] public double StartedSeconds { get; set; }
    [DataMember(Order = 5, EmitDefaultValue = false)] public double? EndedSeconds { get; set; }
    [DataMember(IsRequired = true, Order = 6)] public bool HasGaps { get; set; }
    [DataMember(Order = 7, EmitDefaultValue = false)] public EncounterMapCalibration? Calibration { get; set; }
}

[DataContract]
public sealed class EncounterMapCalibration
{
    [DataMember(IsRequired = true, Order = 1)] public float CenterX { get; set; }
    [DataMember(IsRequired = true, Order = 2)] public float CenterZ { get; set; }
    [DataMember(IsRequired = true, Order = 3)] public float Size { get; set; }
    [DataMember(IsRequired = true, Order = 4)] public float OffsetX { get; set; }
    [DataMember(IsRequired = true, Order = 5)] public float OffsetZ { get; set; }
    // Content identifier only; never a machine-local path or embedded artwork.
    [DataMember(Order = 6, EmitDefaultValue = false)] public string? ArtworkKey { get; set; }
    [DataMember(IsRequired = true, Order = 7)] public bool Combined { get; set; }
    [DataMember(IsRequired = true, Order = 8)] public float CombinedCenterX { get; set; }
    [DataMember(IsRequired = true, Order = 9)] public float CombinedCenterZ { get; set; }
    [DataMember(IsRequired = true, Order = 10)] public float CombinedSize { get; set; }
    [DataMember(IsRequired = true, Order = 11)] public bool Hidden { get; set; }
    [DataMember(IsRequired = true, Order = 12)] public bool NoSignal { get; set; }
}

[DataContract]
public sealed class EncounterRouteChunk
{
    public const int MaximumPoints = 256;
    [DataMember(IsRequired = true, Order = 1)] public int Index { get; set; }
    [DataMember(IsRequired = true, Order = 2)] public bool Sealed { get; set; }
    [DataMember(IsRequired = true, Order = 3)] public List<EncounterRoutePoint> Points { get; set; } = new();
}

[DataContract]
public sealed class EncounterRoutePoint
{
    [DataMember(IsRequired = true, Order = 1)] public double Seconds { get; set; }
    [DataMember(IsRequired = true, Order = 2)] public EncounterPosition Position { get; set; } = new();
    // Connection from the preceding sample, including across a chunk boundary.
    [DataMember(IsRequired = true, Order = 3)] public RouteConnection Connection { get; set; }
}

[DataContract]
public sealed class EncounterDetail
{
    [DataMember(IsRequired = true, Order = 1)] public string ActorId { get; set; } = string.Empty;
    [DataMember(Order = 2, EmitDefaultValue = false)] public string? EnemyPresetKey { get; set; }
    [DataMember(IsRequired = true, Order = 3)] public double StartedSeconds { get; set; }
    [DataMember(Order = 4, EmitDefaultValue = false)] public double? EndedSeconds { get; set; }
    [DataMember(Order = 5, EmitDefaultValue = false)] public EncounterOutcome? Outcome { get; set; }
    [DataMember(Order = 6, EmitDefaultValue = false)] public EncounterPosition? PlayerPosition { get; set; }
    [DataMember(Order = 7, EmitDefaultValue = false)] public EncounterPosition? EnemyPosition { get; set; }
    [DataMember(Order = 8, EmitDefaultValue = false)] public EncounterSource? FinalSource { get; set; }
    [DataMember(IsRequired = true, Order = 9)] public bool HasGaps { get; set; }
    [DataMember(Order = 10, EmitDefaultValue = false)] public EncounterPosition? SourcePosition { get; set; }
    [DataMember(Order = 11, EmitDefaultValue = false)] public string? OutcomeVisitId { get; set; }
}

[DataContract]
public sealed class EncounterSource
{
    [DataMember(IsRequired = true, Order = 1)] public EncounterCredit Credit { get; set; }
    [DataMember(Order = 2, EmitDefaultValue = false)] public string? PhysicalActorId { get; set; }
    [DataMember(Order = 3, EmitDefaultValue = false)] public string? CreditedActorId { get; set; }
    [DataMember(Order = 4, EmitDefaultValue = false)] public int? WeaponTypeId { get; set; }
    [DataMember(Order = 5, EmitDefaultValue = false)] public int? AmmunitionTypeId { get; set; }
    [DataMember(Order = 6, EmitDefaultValue = false)] public string? Mechanism { get; set; }
}

[DataContract]
public sealed class EncounterDamage
{
    [DataMember(IsRequired = true, Order = 1)] public bool Incoming { get; set; }
    [DataMember(IsRequired = true, Order = 2)] public EncounterSource Source { get; set; } = new();
    [DataMember(IsRequired = true, Order = 3)] public double Amount { get; set; }
    [DataMember(IsRequired = true, Order = 4)] public long Hits { get; set; }
    [DataMember(IsRequired = true, Order = 5)] public long Headshots { get; set; }
    [DataMember(IsRequired = true, Order = 6)] public bool HasGaps { get; set; }
}

[DataContract]
public sealed class EncounterInventory
{
    public const int MaximumSlots = 512;
    [DataMember(IsRequired = true, Order = 1)] public string CorpseId { get; set; } = string.Empty;
    [DataMember(IsRequired = true, Order = 2)] public double ObservedSeconds { get; set; }
    [DataMember(IsRequired = true, Order = 3)] public bool Complete { get; set; }
    [DataMember(IsRequired = true, Order = 4)] public List<EncounterInventorySlot> Slots { get; set; } = new();
}

[DataContract]
public sealed class EncounterInventorySlot
{
    [DataMember(IsRequired = true, Order = 1)] public int Slot { get; set; }
    [DataMember(IsRequired = true, Order = 2)] public bool Inspected { get; set; }
    [DataMember(Order = 3, EmitDefaultValue = false)] public int? ItemTypeId { get; set; }
    [DataMember(Order = 4, EmitDefaultValue = false)] public long? Quantity { get; set; }
}

[DataContract]
public sealed class EncounterLoot
{
    [DataMember(IsRequired = true, Order = 1)] public string CorpseId { get; set; } = string.Empty;
    [DataMember(IsRequired = true, Order = 2)] public int ItemTypeId { get; set; }
    [DataMember(IsRequired = true, Order = 3)] public long TakenToPlayer { get; set; }
    [DataMember(IsRequired = true, Order = 4)] public long TakenToPet { get; set; }
    [DataMember(IsRequired = true, Order = 5)] public long ReturnedByPlayer { get; set; }
    [DataMember(IsRequired = true, Order = 6)] public long ReturnedByPet { get; set; }
    [DataMember(IsRequired = true, Order = 7)] public bool HasGaps { get; set; }
    // Gross directional counts, not unique original units or current possession.
}
