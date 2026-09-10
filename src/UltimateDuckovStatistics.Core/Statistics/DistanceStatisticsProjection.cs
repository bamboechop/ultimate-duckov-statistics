using System.Runtime.Serialization;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Core.Statistics;

[DataContract]
public sealed class DistanceStatisticsProjection
{
    [DataMember(Order = 1)] public double? RaidMeters { get; set; }
    [DataMember(Order = 2)] public double? BaseMeters { get; set; }
    [DataMember(Order = 3)] public double? CombinedMeters { get; set; }
    [DataMember(Order = 4)] public DateTime? BaseCollectionStartedUtc { get; set; }
    [DataMember(Order = 5)] public bool MovementCollectionAvailable { get; set; }
    [DataMember(Order = 6)] public bool RaidPartial { get; set; }
    [DataMember(Order = 7)] public bool BasePartial { get; set; }
    [DataMember(Order = 8)] public bool CombinedPartial { get; set; }
    [DataMember(Order = 9)]
    public string Coverage { get; set; } =
        "Recorded while UDS is active. Earlier base movement is not included. Raid distance includes completed/recovered runs.";

    public static DistanceStatisticsProjection Create(ProfileDocument profile)
    {
        if (profile == null) throw new ArgumentNullException(nameof(profile));
        var available = profile.Capabilities.Count(c => c.AdapterId == RunStatisticsViewModelFactory.MovementAdapterId) == 1
            && profile.Capabilities.Any(c => c.AdapterId == RunStatisticsViewModelFactory.MovementAdapterId
                && c.State == AdapterCapabilityState.Supported);
        var raid = profile.Statistics.RunTotals.PhysicalDistance;
        double? raidMeters = available || raid > 0 || profile.Statistics.Runs.Any(r => r.MovementCapability == AdapterCapabilityState.Supported)
            ? raid : null;
        var recordedBase = profile.Statistics.BaseMovement;
        BaseMovementStatistics.Validate(recordedBase);
        var baseMeters = recordedBase?.RecordedMeters;
        var raidPartial = !available || profile.Statistics.Runs.Any(r => r.MovementCapability != AdapterCapabilityState.Supported);
        var basePartial = recordedBase == null || !available || recordedBase.HasKnownGaps;
        return new DistanceStatisticsProjection
        {
            RaidMeters = raidMeters,
            BaseMeters = baseMeters,
            CombinedMeters = raidMeters.HasValue || baseMeters.HasValue
                ? RouteStatisticsReducer.SaturatingAdd(raidMeters ?? 0, baseMeters ?? 0) : null,
            BaseCollectionStartedUtc = recordedBase?.CollectionStartedUtc,
            MovementCollectionAvailable = available,
            RaidPartial = raidPartial,
            BasePartial = basePartial,
            CombinedPartial = raidPartial || basePartial || !raidMeters.HasValue || !baseMeters.HasValue
        };
    }
}
