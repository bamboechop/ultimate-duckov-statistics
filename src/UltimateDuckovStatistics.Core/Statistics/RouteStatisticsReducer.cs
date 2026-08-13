using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Core.Statistics;

public static class RouteStatisticsReducer
{
    public const int MaximumSegmentsPerRun = 64;
    public const int MaximumEventAssociationsPerRun = 2048;

    public static RouteMetricCapabilities Supported(string provenance) => new()
    {
        OrderedRoute = Availability(AdapterCapabilityState.Supported, provenance),
        Segments = Availability(AdapterCapabilityState.Supported, provenance),
        EventAttribution = Availability(AdapterCapabilityState.Supported, provenance),
        RouteAwareMapTotals = Availability(AdapterCapabilityState.Supported, provenance)
    };

    public static RouteMetricCapabilities Unavailable(string provenance) => new()
    {
        OrderedRoute = Availability(AdapterCapabilityState.DisabledIncompatible, provenance),
        Segments = Availability(AdapterCapabilityState.DisabledIncompatible, provenance),
        EventAttribution = Availability(AdapterCapabilityState.DisabledIncompatible, provenance),
        RouteAwareMapTotals = Availability(AdapterCapabilityState.DisabledIncompatible, provenance)
    };

    public static RouteMetricCapabilities CloneCapabilities(RouteMetricCapabilities source)
    {
        source ??= Unavailable("route capability record missing");
        return new RouteMetricCapabilities
        {
            OrderedRoute = Clone(source.OrderedRoute),
            Segments = Clone(source.Segments),
            EventAttribution = Clone(source.EventAttribution),
            RouteAwareMapTotals = Clone(source.RouteAwareMapTotals)
        };
    }

    public static bool NormalizeCapabilities(RouteMetricCapabilities value)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));
        var repaired = false;
        value.OrderedRoute ??= RepairAvailability(ref repaired);
        value.Segments ??= RepairAvailability(ref repaired);
        value.EventAttribution ??= RepairAvailability(ref repaired);
        value.RouteAwareMapTotals ??= RepairAvailability(ref repaired);
        foreach (var availability in new[] { value.OrderedRoute, value.Segments, value.EventAttribution, value.RouteAwareMapTotals })
        {
            if (!Enum.IsDefined(typeof(AdapterCapabilityState), availability.State))
            {
                availability.State = AdapterCapabilityState.DisabledIncompatible;
                availability.Provenance = "Invalid route capability state was disabled during normalization.";
                repaired = true;
            }
            else if (availability.Provenance == null)
            {
                availability.Provenance = string.Empty;
                repaired = true;
            }
        }
        if (!CapabilitiesAreConsistent(value))
        {
            repaired = true;
        }
        if (repaired) DisableRoute(value, "Persisted route capability data was repaired and remains unavailable.");
        return repaired;
    }

    public static void ValidateCapabilities(RouteMetricCapabilities value)
    {
        if (value?.OrderedRoute == null || value.Segments == null || value.EventAttribution == null || value.RouteAwareMapTotals == null)
            throw new ArgumentException("Route capability record is incomplete.", nameof(value));
        foreach (var availability in new[] { value.OrderedRoute, value.Segments, value.EventAttribution, value.RouteAwareMapTotals })
            if (!Enum.IsDefined(typeof(AdapterCapabilityState), availability.State) || availability.Provenance == null)
                throw new ArgumentException("Route capability record is invalid.", nameof(value));
        if (!CapabilitiesAreConsistent(value))
            throw new ArgumentException("Route capability dependencies are inconsistent.", nameof(value));
    }

    public static void DisableAttribution(RouteMetricCapabilities target, string provenance)
    {
        target.EventAttribution = Availability(AdapterCapabilityState.DisabledIncompatible, provenance);
        target.RouteAwareMapTotals = Availability(AdapterCapabilityState.DisabledIncompatible, provenance);
    }

    public static void DisableRoute(RouteMetricCapabilities target, string provenance)
    {
        target.OrderedRoute = Availability(AdapterCapabilityState.DisabledIncompatible, provenance);
        target.Segments = Availability(AdapterCapabilityState.DisabledIncompatible, provenance);
        DisableAttribution(target, provenance);
    }

    public static MapSegmentSummary CloneSegment(MapSegmentSummary source) => new()
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
        ItemStatistics = ItemStatisticsAggregateReducer.Clone(source.ItemStatistics),
        WeaponStatistics = WeaponStatisticsReducer.Clone(source.WeaponStatistics),
        CombatStatistics = CombatStatisticsReducer.Clone(source.CombatStatistics),
        EquipmentStatistics = EquipmentStatisticsReducer.Clone(source.EquipmentStatistics),
        ContainerStatistics = ContainerStatisticsReducer.Clone(source.ContainerStatistics),
        WasRepairedFromInvalidState = source.WasRepairedFromInvalidState
    };

    public static MapSegmentSummary CloneSegmentForInterruptedRecovery(MapSegmentSummary source, DateTime endedUtc)
    {
        var clone = CloneSegment(source);
        if (clone.ExitReason == MapSegmentExitReason.None)
        {
            clone.ExitedUtc = endedUtc < clone.EnteredUtc ? clone.EnteredUtc : endedUtc;
            clone.ExitReason = MapSegmentExitReason.Interrupted;
        }
        return clone;
    }

    public static SegmentEventAssociation CloneAssociation(SegmentEventAssociation source) => new()
    {
        EventId = source.EventId,
        EventKind = source.EventKind,
        TimestampUtc = source.TimestampUtc,
        SourceSegmentId = source.SourceSegmentId,
        SourceMapId = source.SourceMapId,
        OutcomeSegmentId = source.OutcomeSegmentId,
        OutcomeMapId = source.OutcomeMapId
    };

    public static string BuildSignature(IEnumerable<MapSegmentSummary> segments) =>
        string.Join(">", segments.Select(segment => segment.MapId));

    public static double SaturatingSum(IEnumerable<double> values)
    {
        if (values == null) throw new ArgumentNullException(nameof(values));
        var total = 0d;
        foreach (var value in values)
        {
            total = SaturatingAdd(total, value);
        }
        return total;
    }

    public static double SaturatingAdd(double current, double addition)
    {
        if (!FiniteNonNegative(current) || !FiniteNonNegative(addition))
            throw new ArgumentOutOfRangeException(nameof(current), "Route measurements must be finite and non-negative.");
        return current > double.MaxValue - addition ? double.MaxValue : current + addition;
    }

    public static bool NormalizePersisted(IList<MapSegmentSummary> segments)
    {
        var repaired = false;
        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            var segmentRepaired = false;
            if (segment == null)
            {
                throw new ArgumentException("Route contains a null segment.", nameof(segments));
            }
            if (string.IsNullOrWhiteSpace(segment.SegmentId))
            {
                segment.SegmentId = $"segment-{index + 1}";
                segmentRepaired = true;
            }
            if (segment.SegmentIndex != index)
            {
                segment.SegmentIndex = index;
                segmentRepaired = true;
            }
            if (string.IsNullOrWhiteSpace(segment.MapId))
            {
                segment.MapId = MapIdentity.UnknownId;
                segmentRepaired = true;
            }
            if (string.IsNullOrWhiteSpace(segment.MapDisplayName))
            {
                segment.MapDisplayName = MapIdentity.UnknownDisplayName;
                segmentRepaired = true;
            }
            segment.EnteredUtc = EnsureUtc(segment.EnteredUtc);
            if (segment.ExitedUtc.HasValue)
            {
                segment.ExitedUtc = EnsureUtc(segment.ExitedUtc.Value);
                if (segment.ExitedUtc < segment.EnteredUtc)
                {
                    segment.ExitedUtc = segment.EnteredUtc;
                    segmentRepaired = true;
                }
            }
            segmentRepaired |= NormalizeDistance(segment.ActiveDurationSeconds, value => segment.ActiveDurationSeconds = value);
            segmentRepaired |= NormalizeDistance(segment.PhysicalDistance, value => segment.PhysicalDistance = value);
            segmentRepaired |= NormalizeDistance(segment.TeleportDistance, value => segment.TeleportDistance = value);
            segmentRepaired |= NormalizeDistance(segment.TransitionExcludedDistance, value => segment.TransitionExcludedDistance = value);
            segment.ItemStatistics ??= Repair(new ItemStatisticsAggregate(), ref segmentRepaired);
            segment.WeaponStatistics ??= Repair(new WeaponStatisticsAggregate(), ref segmentRepaired);
            segment.CombatStatistics ??= Repair(new CombatStatisticsAggregate(), ref segmentRepaired);
            segment.EquipmentStatistics ??= Repair(new EquipmentStatisticsAggregate(), ref segmentRepaired);
            segment.ContainerStatistics ??= Repair(new ContainerStatisticsAggregate(), ref segmentRepaired);
            segmentRepaired |= ItemStatisticsAggregateReducer.NormalizePersisted(segment.ItemStatistics);
            segmentRepaired |= WeaponStatisticsReducer.NormalizePersisted(segment.WeaponStatistics).Changed;
            segmentRepaired |= CombatStatisticsReducer.NormalizePersisted(segment.CombatStatistics).Changed;
            segmentRepaired |= EquipmentStatisticsReducer.NormalizePersisted(segment.EquipmentStatistics);
            segmentRepaired |= ContainerStatisticsReducer.NormalizePersisted(segment.ContainerStatistics);
            segment.WasRepairedFromInvalidState |= segmentRepaired;
            repaired |= segmentRepaired;
        }
        return repaired;
    }

    public static void Validate(IReadOnlyList<MapSegmentSummary> segments, bool allowOpenLast)
    {
        if (segments == null || segments.Count == 0 || segments.Count > MaximumSegmentsPerRun)
            throw new ArgumentException("Route segment count is invalid.", nameof(segments));
        var segmentIds = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            if (segment == null || segment.SegmentIndex != index || string.IsNullOrWhiteSpace(segment.SegmentId)
                || !segmentIds.Add(segment.SegmentId)
                || string.IsNullOrWhiteSpace(segment.MapId) || string.IsNullOrWhiteSpace(segment.MapDisplayName)
                || !Enum.IsDefined(typeof(MapSegmentExitReason), segment.ExitReason)
                || segment.EnteredUtc.Kind != DateTimeKind.Utc
                || (segment.ExitedUtc.HasValue && segment.ExitedUtc.Value.Kind != DateTimeKind.Utc)
                || !FiniteNonNegative(segment.ActiveDurationSeconds) || !FiniteNonNegative(segment.PhysicalDistance)
                || !FiniteNonNegative(segment.TeleportDistance) || !FiniteNonNegative(segment.TransitionExcludedDistance))
                throw new ArgumentException("Route contains an invalid segment.", nameof(segments));
            var open = segment.ExitReason == MapSegmentExitReason.None && !segment.ExitedUtc.HasValue;
            var closed = segment.ExitReason != MapSegmentExitReason.None && segment.ExitedUtc.HasValue;
            if ((!open && !closed)
                || (closed && segment.ExitedUtc < segment.EnteredUtc)
                || (open && (!allowOpenLast || index != segments.Count - 1)))
                throw new ArgumentException("Route contains an invalid open or closed segment.", nameof(segments));
            ItemStatisticsAggregateReducer.Validate(segment.ItemStatistics);
            WeaponStatisticsReducer.ValidateAggregate(segment.WeaponStatistics);
            CombatStatisticsReducer.ValidateAggregate(segment.CombatStatistics);
            EquipmentStatisticsReducer.ValidateAggregate(segment.EquipmentStatistics);
            ValidateSegmentEquipmentOccurrences(segment.EquipmentStatistics);
            ContainerStatisticsReducer.ValidateAggregate(segment.ContainerStatistics);
        }
    }

    public static void ValidateAssociations(
        IReadOnlyList<MapSegmentSummary> segments,
        IReadOnlyList<SegmentEventAssociation> associations)
    {
        if (segments == null || associations == null || associations.Count > MaximumEventAssociationsPerRun)
            throw new ArgumentException("Route event association state is invalid.", nameof(associations));
        var segmentMaps = segments.ToDictionary(segment => segment.SegmentId, segment => segment.MapId, StringComparer.Ordinal);
        var eventIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var association in associations)
        {
            if (association == null
                || string.IsNullOrWhiteSpace(association.EventId)
                || !eventIds.Add(association.EventId)
                || string.IsNullOrWhiteSpace(association.EventKind)
                || association.TimestampUtc.Kind != DateTimeKind.Utc
                || string.IsNullOrWhiteSpace(association.SourceSegmentId)
                || string.IsNullOrWhiteSpace(association.OutcomeSegmentId)
                || !ValidEndpoint(segmentMaps, association.SourceSegmentId, association.SourceMapId)
                || !ValidEndpoint(segmentMaps, association.OutcomeSegmentId, association.OutcomeMapId))
                throw new ArgumentException("Route contains an invalid event association.", nameof(associations));
        }
    }

    private static MetricAvailability Availability(AdapterCapabilityState state, string provenance) => new()
    {
        State = state,
        Provenance = provenance ?? string.Empty
    };

    private static bool CapabilitiesAreConsistent(RouteMetricCapabilities value)
    {
        var orderedRouteSupported = value.OrderedRoute.State == AdapterCapabilityState.Supported;
        var segmentsSupported = value.Segments.State == AdapterCapabilityState.Supported;
        var attributionSupported = value.EventAttribution.State == AdapterCapabilityState.Supported;
        var routeMapTotalsSupported = value.RouteAwareMapTotals.State == AdapterCapabilityState.Supported;
        return orderedRouteSupported == segmentsSupported
               && (!attributionSupported || segmentsSupported)
               && (!routeMapTotalsSupported || attributionSupported);
    }

    private static MetricAvailability Clone(MetricAvailability? source) => source == null
        ? Availability(AdapterCapabilityState.DisabledIncompatible, "route capability entry missing")
        : Availability(source.State, source.Provenance);

    private static MetricAvailability RepairAvailability(ref bool repaired)
    {
        repaired = true;
        return Availability(AdapterCapabilityState.DisabledIncompatible, "Route capability entry was missing.");
    }

    private static bool NormalizeDistance(double value, Action<double> replace)
    {
        if (FiniteNonNegative(value)) return false;
        replace(0);
        return true;
    }

    private static bool FiniteNonNegative(double value) => value >= 0 && !double.IsNaN(value) && !double.IsInfinity(value);

    private static bool ValidEndpoint(
        Dictionary<string, string> segmentMaps,
        string? segmentId,
        string? mapId)
    {
        if (string.IsNullOrWhiteSpace(segmentId)) return true;
        return segmentMaps.TryGetValue(segmentId, out var expectedMapId)
               && string.Equals(expectedMapId, mapId, StringComparison.Ordinal);
    }

    private static void ValidateSegmentEquipmentOccurrences(EquipmentStatisticsAggregate equipment)
    {
        foreach (var rows in new[]
                 {
                     equipment.Items,
                     equipment.SelectedWeapons,
                     equipment.Loadouts,
                     equipment.TotemSets,
                     equipment.TotemStates,
                     equipment.Slots,
                     equipment.SlottedWeapons
                 })
        {
            if (rows.Values.Any(row => row.RunOccurrences != 0))
                throw new ArgumentException("Route segment equipment cannot contain completed-run occurrences.", nameof(equipment));
        }
    }

    private static T Repair<T>(T value, ref bool repaired)
    {
        repaired = true;
        return value;
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
