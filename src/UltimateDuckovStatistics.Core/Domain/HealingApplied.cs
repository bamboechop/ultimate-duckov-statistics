using System.Runtime.Serialization;

namespace UltimateDuckovStatistics.Core.Domain;

[DataContract]
public sealed class HealingApplied
{
    [DataMember(Order = 1)]
    public int SchemaVersion { get; set; } = ProductInfo.SchemaVersion;

    [DataMember(Order = 2)]
    public string EventId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public string ApplicationId { get; set; } = string.Empty;

    [DataMember(Order = 4)]
    public string SourceItemUseEventId { get; set; } = string.Empty;

    [DataMember(Order = 5)]
    public DateTime TimestampUtc { get; set; }

    [DataMember(Order = 6)]
    public string SaveGenerationId { get; set; } = string.Empty;

    [DataMember(Order = 7, EmitDefaultValue = false)]
    public string? RunId { get; set; }

    [DataMember(Order = 8, EmitDefaultValue = false)]
    public string? MapId { get; set; }

    [DataMember(Order = 9)]
    public string GameVersion { get; set; } = string.Empty;

    [DataMember(Order = 10)]
    public string GameBuild { get; set; } = string.Empty;

    [DataMember(Order = 11)]
    public GameplayContext GameplayContext { get; set; }

    [DataMember(Order = 12)]
    public IntegrityTags IntegrityTags { get; set; }

    [DataMember(Order = 13)]
    public AdapterCapabilityState AdapterCapability { get; set; }

    [DataMember(Order = 14)]
    public string AdapterVersion { get; set; } = string.Empty;

    [DataMember(Order = 15)]
    public string ItemId { get; set; } = string.Empty;

    [DataMember(Order = 16)]
    public string DisplayName { get; set; } = string.Empty;

    [DataMember(Order = 17)]
    public CanonicalItemGroup Group { get; set; }

    [DataMember(Order = 18)]
    public double ActualHealthRestored { get; set; }
}
