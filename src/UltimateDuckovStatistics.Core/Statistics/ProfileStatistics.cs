using System.Runtime.Serialization;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Core.Statistics;

[DataContract]
public sealed class ProfileStatistics
{
    [DataMember(Order = 1)]
    public int SchemaVersion { get; set; } = ProductInfo.SchemaVersion;

    [DataMember(Order = 2)]
    public string SaveGenerationId { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public DateTime CreatedUtc { get; set; }

    [DataMember(Order = 4)]
    public DateTime UpdatedUtc { get; set; }

    [DataMember(Order = 5)]
    public AggregateTotals Overall { get; set; } = new();

    [DataMember(Order = 6)]
    public Dictionary<string, ItemAggregate> Items { get; set; } = new(StringComparer.Ordinal);

    [DataMember(Order = 7)]
    public Dictionary<string, AggregateTotals> Groups { get; set; } = new(StringComparer.Ordinal);

    [DataMember(Order = 8)]
    public List<string> RecentEventIds { get; set; } = new();
}

[DataContract]
public sealed class AggregateTotals
{
    [DataMember(Order = 1)]
    public long ActivationCount { get; set; }

    [DataMember(Order = 2)]
    public Dictionary<string, double> AmountsByUnit { get; set; } = new(StringComparer.Ordinal);

    [DataMember(Order = 3)]
    public double ActualHealthRestored { get; set; }
}

[DataContract]
public sealed class ItemAggregate
{
    [DataMember(Order = 1)]
    public string ItemId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public string DisplayName { get; set; } = string.Empty;

    [DataMember(Order = 3)]
    public CanonicalItemGroup Group { get; set; }

    [DataMember(Order = 4)]
    public List<ItemEffectTag> EffectTags { get; set; } = new();

    [DataMember(Order = 5)]
    public AggregateTotals Totals { get; set; } = new();
}
