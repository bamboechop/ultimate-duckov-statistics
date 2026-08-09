using System.Runtime.Serialization;

namespace UltimateDuckovStatistics.Core.Domain;

[DataContract]
public sealed class ItemUseRecorded
{
    [DataMember(Order = 1)]
    public int SchemaVersion { get; set; } = ProductInfo.SchemaVersion;

    [DataMember(Order = 2)]
    public string EventId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public DateTime TimestampUtc { get; set; }

    [DataMember(Order = 4)]
    public string SaveGenerationId { get; set; } = string.Empty;

    [DataMember(Order = 5, EmitDefaultValue = false)]
    public string? RunId { get; set; }

    [DataMember(Order = 6, EmitDefaultValue = false)]
    public string? MapId { get; set; }

    [DataMember(Order = 7)]
    public string GameVersion { get; set; } = string.Empty;

    [DataMember(Order = 8)]
    public string GameBuild { get; set; } = string.Empty;

    [DataMember(Order = 9)]
    public GameplayContext GameplayContext { get; set; }

    [DataMember(Order = 10)]
    public IntegrityTags IntegrityTags { get; set; }

    [DataMember(Order = 11)]
    public AdapterCapabilityState AdapterCapability { get; set; }

    [DataMember(Order = 12)]
    public string AdapterVersion { get; set; } = string.Empty;

    [DataMember(Order = 13)]
    public string ItemId { get; set; } = string.Empty;

    [DataMember(Order = 14)]
    public string DisplayName { get; set; } = string.Empty;

    [DataMember(Order = 15)]
    public CanonicalItemGroup Group { get; set; }

    [DataMember(Order = 16)]
    public List<ItemEffectTag> EffectTags { get; set; } = new();

    [DataMember(Order = 17)]
    public long ActivationCount { get; set; } = 1;

    [DataMember(Order = 18)]
    public double AmountConsumed { get; set; }

    [DataMember(Order = 19)]
    public ConsumptionUnit ConsumptionUnit { get; set; }
}
