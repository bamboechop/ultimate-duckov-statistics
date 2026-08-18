using System.Runtime.Serialization;

namespace UltimateDuckovStatistics.Core.Domain;

[DataContract]
public sealed class EconomyMetricCapabilities
{
    [DataMember(Order = 1)] public MetricAvailability MoneyAmountDirection { get; set; } = new();
    [DataMember(Order = 2)] public MetricAvailability MoneySourceAttribution { get; set; } = new();
    [DataMember(Order = 3)] public MetricAvailability MoneyContextAttribution { get; set; } = new();
    [DataMember(Order = 4)] public MetricAvailability CashAmountDirection { get; set; } = new();
    [DataMember(Order = 5)] public MetricAvailability CashExternalAcquisition { get; set; } = new();
    [DataMember(Order = 6)] public MetricAvailability CashContextAttribution { get; set; } = new();
    [DataMember(Order = 7)] public MetricAvailability CashTerminalOutcomes { get; set; } = new();
    [DataMember(Order = 8)] public MetricAvailability RouteAttribution { get; set; } = new();
}

[DataContract]
public sealed class CurrencyFlowRecorded
{
    [DataMember(Order = 1)] public int SchemaVersion { get; set; } = ProductInfo.SchemaVersion;
    [DataMember(Order = 2)] public string EventId { get; set; } = string.Empty;
    [DataMember(Order = 3)] public DateTime TimestampUtc { get; set; }
    [DataMember(Order = 4)] public string SaveGenerationId { get; set; } = string.Empty;
    [DataMember(Order = 5, EmitDefaultValue = false)] public string? RunId { get; set; }
    [DataMember(Order = 6, EmitDefaultValue = false)] public string? SegmentId { get; set; }
    [DataMember(Order = 7)] public string MapId { get; set; } = MapIdentity.UnknownId;
    [DataMember(Order = 8)] public CurrencyKind Currency { get; set; }
    [DataMember(Order = 9)] public CurrencyFlowDirection Direction { get; set; }
    [DataMember(Order = 10)] public long Amount { get; set; }
    [DataMember(Order = 11)] public CurrencySourceCategory Source { get; set; }
    [DataMember(Order = 12, EmitDefaultValue = false)] public string? NativeSourceId { get; set; }
    [DataMember(Order = 13, EmitDefaultValue = false)] public string? SourceDisplayName { get; set; }
    [DataMember(Order = 14)] public GameplayContext GameplayContext { get; set; }
    [DataMember(Order = 15)] public IntegrityTags IntegrityTags { get; set; }
    [DataMember(Order = 16)] public string GameVersion { get; set; } = string.Empty;
    [DataMember(Order = 17)] public string GameBuild { get; set; } = string.Empty;
    [DataMember(Order = 18)] public string AdapterVersion { get; set; } = string.Empty;
    [DataMember(Order = 19, EmitDefaultValue = false)] public string? NativeTransactionId { get; set; }
    [DataMember(Order = 20)] public bool ProvenExternalRaidAcquisition { get; set; }
    [DataMember(Order = 21)] public string ProducerActivationId { get; set; } = string.Empty;
    [DataMember(Order = 22)] public long ProducerSequence { get; set; }
}
