using System.Runtime.Serialization;

namespace UltimateDuckovStatistics.Core.Statistics;

[DataContract]
public sealed class BaseMovementStatistics
{
    [DataMember(Order = 1)] public double RecordedMeters { get; set; }
    [DataMember(Order = 2)] public DateTime CollectionStartedUtc { get; set; }
    [DataMember(Order = 3)] public bool HasKnownGaps { get; set; }

    public static void Validate(BaseMovementStatistics? value)
    {
        if (value == null) return; // Absent means never recorded, including existing current-format profiles.
        if (!double.IsFinite(value.RecordedMeters) || value.RecordedMeters < 0
            || value.CollectionStartedUtc.Kind != DateTimeKind.Utc
            || value.CollectionStartedUtc == DateTime.MinValue)
            throw new ArgumentException("Base movement evidence is invalid.", nameof(value));
    }

    public static BaseMovementStatistics? Clone(BaseMovementStatistics? value) => value == null ? null : new()
    {
        RecordedMeters = value.RecordedMeters,
        CollectionStartedUtc = value.CollectionStartedUtc,
        HasKnownGaps = value.HasKnownGaps
    };
}
