using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Persistence;

// These objects are encoded while the tracker owns its state. Only bounded
// scalar/capability/snapshot fields remain here; dictionary entries and retained
// segment history have independent records. No reference crosses to a worker.
internal static class CheckpointRecordHeaders
{
    internal static WeaponStatisticsAggregate Weapon(WeaponStatisticsAggregate source) => new()
    {
        Totals = source.Totals,
        Capabilities = source.Capabilities,
        WasRepairedFromInvalidState = source.WasRepairedFromInvalidState,
        UncorrelatedFiringActions = source.UncorrelatedFiringActions
    };
    internal static CombatStatisticsAggregate Combat(CombatStatisticsAggregate source) => new()
    { Totals = source.Totals, Capabilities = source.Capabilities, WasRepairedFromInvalidState = source.WasRepairedFromInvalidState };
    internal static ItemStatisticsAggregate Items(ItemStatisticsAggregate source) => new()
    { Overall = source.Overall, Groups = source.Groups, RecentEventIds = source.RecentEventIds, WasRepairedFromInvalidState = source.WasRepairedFromInvalidState };
    internal static EquipmentStatisticsAggregate Equipment(EquipmentStatisticsAggregate source) => new()
    {
        Capabilities = source.Capabilities,
        TransitionCount = source.TransitionCount,
        TransitionsTruncated = source.TransitionsTruncated,
        ObservedActiveDurationSeconds = source.ObservedActiveDurationSeconds,
        CurrentSnapshot = source.CurrentSnapshot,
        WasRepairedFromInvalidState = source.WasRepairedFromInvalidState,
        Composition = new EquipmentCompositionEvidence { HistoricalUnavailable = source.Composition.HistoricalUnavailable }
    };
    internal static ContainerRunCheckpointState Containers(ContainerRunCheckpointState source) => new()
    { Statistics = source.Statistics, DeduplicationSaturated = source.DeduplicationSaturated, WasRepairedFromInvalidState = source.WasRepairedFromInvalidState };
    internal static MapSegmentSummary Segment(MapSegmentSummary source) => new()
    {
        SegmentId = source.SegmentId,
        SegmentIndex = source.SegmentIndex,
        MapId = source.MapId,
        MapDisplayName = source.MapDisplayName,
        MapKnown = source.MapKnown,
        EnteredUtc = source.EnteredUtc,
        ExitedUtc = source.ExitedUtc,
        ActiveDurationSeconds = source.ActiveDurationSeconds,
        PhysicalDistance = source.PhysicalDistance,
        TeleportDistance = source.TeleportDistance,
        TransitionExcludedDistance = source.TransitionExcludedDistance,
        ExitReason = source.ExitReason,
        IntegrityTags = source.IntegrityTags,
        WasRepairedFromInvalidState = source.WasRepairedFromInvalidState,
        ItemStatistics = Items(source.ItemStatistics),
        WeaponStatistics = Weapon(source.WeaponStatistics),
        CombatStatistics = Combat(source.CombatStatistics),
        EquipmentStatistics = Equipment(source.EquipmentStatistics),
        ContainerStatistics = source.ContainerStatistics,
        Economy = source.Economy
    };
}
