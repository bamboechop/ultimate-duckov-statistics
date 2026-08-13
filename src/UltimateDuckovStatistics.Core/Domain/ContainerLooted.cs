using System.Runtime.Serialization;

namespace UltimateDuckovStatistics.Core.Domain;

[DataContract]
public sealed class ContainerLooted
{
    [DataMember(Order = 1)] public int SchemaVersion { get; set; } = ProductInfo.SchemaVersion;
    [DataMember(Order = 2)] public string EventId { get; set; } = string.Empty;
    [DataMember(Order = 3)] public DateTime TimestampUtc { get; set; }
    [DataMember(Order = 4)] public string SaveGenerationId { get; set; } = string.Empty;
    [DataMember(Order = 5)] public string RunId { get; set; } = string.Empty;
    [DataMember(Order = 6)] public string MapId { get; set; } = MapIdentity.UnknownId;
    [DataMember(Order = 7)] public string GameVersion { get; set; } = string.Empty;
    [DataMember(Order = 8)] public string GameBuild { get; set; } = string.Empty;
    [DataMember(Order = 9)] public GameplayContext GameplayContext { get; set; }
    [DataMember(Order = 10)] public IntegrityTags IntegrityTags { get; set; }
    [DataMember(Order = 11)] public int ContainerKey { get; set; }
    [DataMember(Order = 12)] public string AdapterVersion { get; set; } = string.Empty;
    [DataMember(Order = 13, EmitDefaultValue = false)] public string? SegmentId { get; set; }
}
