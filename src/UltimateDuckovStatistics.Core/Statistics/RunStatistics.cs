using System.Runtime.Serialization;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Core.Statistics;

[DataContract]
public sealed class RunAggregateTotals
{
    [DataMember(Order = 1)]
    public long TotalRuns { get; set; }

    [DataMember(Order = 2)]
    public Dictionary<string, long> Outcomes { get; set; } = new(StringComparer.Ordinal);

    [DataMember(Order = 3)]
    public double PhysicalDistance { get; set; }

    [DataMember(Order = 4)]
    public double TeleportDistance { get; set; }

    [DataMember(Order = 5)]
    public Dictionary<string, MapRunAggregate> Maps { get; set; } = new(StringComparer.Ordinal);

    [DataMember(Order = 6)]
    public WeaponStatisticsAggregate WeaponStatistics { get; set; } = new();

    [DataMember(Order = 7)]
    public CombatStatisticsAggregate CombatStatistics { get; set; } = new();

    [DataMember(Order = 8)]
    public EquipmentStatisticsAggregate EquipmentStatistics { get; set; } = new();

    [DataMember(Order = 9)]
    public ContainerStatisticsAggregate ContainerStatistics { get; set; } = new();

    [DataMember(Order = 10)]
    public double TransitionExcludedDistance { get; set; }

    [DataMember(Order = 11)]
    public Dictionary<string, RouteAwareMapAggregate> RouteMaps { get; set; } = new(StringComparer.Ordinal);

    [DataMember(Order = 12)]
    public bool RouteAwareHistoryUnavailable { get; set; }

    [DataMember(Order = 13)]
    public ItemStatisticsAggregate ItemStatistics { get; set; } = new();

    [DataMember(Order = 14)]
    public EconomyStatisticsAggregate Economy { get; set; } = new();
}

[DataContract]
public sealed class MapRunAggregate
{
    [DataMember(Order = 1)]
    public string MapId { get; set; } = MapIdentity.UnknownId;

    [DataMember(Order = 2)]
    public string DisplayName { get; set; } = MapIdentity.UnknownDisplayName;

    [DataMember(Order = 3)]
    public bool IsKnown { get; set; }

    [DataMember(Order = 4)]
    public long TotalRuns { get; set; }

    [DataMember(Order = 5)]
    public Dictionary<string, long> Outcomes { get; set; } = new(StringComparer.Ordinal);

    [DataMember(Order = 6)]
    public double PhysicalDistance { get; set; }

    [DataMember(Order = 7)]
    public double TeleportDistance { get; set; }

    [DataMember(Order = 8)]
    public WeaponStatisticsAggregate WeaponStatistics { get; set; } = new();

    [DataMember(Order = 9)]
    public CombatStatisticsAggregate CombatStatistics { get; set; } = new();

    [DataMember(Order = 10)]
    public EquipmentStatisticsAggregate EquipmentStatistics { get; set; } = new();

    [DataMember(Order = 11)]
    public ContainerStatisticsAggregate ContainerStatistics { get; set; } = new();

    [DataMember(Order = 12)]
    public ItemStatisticsAggregate ItemStatistics { get; set; } = new();

    [DataMember(Order = 13)]
    public EconomyStatisticsAggregate Economy { get; set; } = new();
}

[DataContract]
public sealed class DurationRecordReference
{
    [DataMember(Order = 1)]
    public string RunId { get; set; } = string.Empty;

    [DataMember(Order = 2)]
    public double ActiveDurationSeconds { get; set; }

    [DataMember(Order = 3)]
    public DateTime StartedUtc { get; set; }

    [DataMember(Order = 4)]
    public string MapId { get; set; } = MapIdentity.UnknownId;

    [DataMember(Order = 5)]
    public string MapDisplayName { get; set; } = MapIdentity.UnknownDisplayName;
}

[DataContract]
public sealed class DurationRecordPair
{
    [DataMember(Order = 1, EmitDefaultValue = false)]
    public DurationRecordReference? Shortest { get; set; }

    [DataMember(Order = 2, EmitDefaultValue = false)]
    public DurationRecordReference? Longest { get; set; }
}

[DataContract]
public sealed class MapRunDurationRecords
{
    [DataMember(Order = 1)]
    public string MapId { get; set; } = MapIdentity.UnknownId;

    [DataMember(Order = 2)]
    public string DisplayName { get; set; } = MapIdentity.UnknownDisplayName;

    [DataMember(Order = 3)]
    public DurationRecordPair Extraction { get; set; } = new();

    [DataMember(Order = 4)]
    public DurationRecordPair Death { get; set; } = new();
}

[DataContract]
public sealed class RunDurationRecords
{
    [DataMember(Order = 1)]
    public DurationRecordPair Extraction { get; set; } = new();

    [DataMember(Order = 2)]
    public DurationRecordPair Death { get; set; } = new();

    [DataMember(Order = 3)]
    public Dictionary<string, MapRunDurationRecords> Maps { get; set; } = new(StringComparer.Ordinal);
}

public static class RunReducer
{
    public static bool Apply(ProfileStatistics profile, RunSummary summary)
    {
        if (profile == null)
        {
            throw new ArgumentNullException(nameof(profile));
        }
        Validate(summary);
        if (!string.Equals(profile.SaveGenerationId, summary.SaveGenerationId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("A run cannot be reduced into a different save generation.");
        }

        if (profile.Runs.Any(run => string.Equals(run.RunId, summary.RunId, StringComparison.Ordinal)))
        {
            return false;
        }

        WeaponStatisticsReducer.ValidateAggregate(profile.RunTotals.WeaponStatistics);
        CombatStatisticsReducer.ValidateAggregate(profile.RunTotals.CombatStatistics);
        EquipmentStatisticsReducer.ValidateAggregate(profile.RunTotals.EquipmentStatistics);
        ContainerStatisticsReducer.ValidateAggregate(profile.RunTotals.ContainerStatistics);
        ItemStatisticsAggregateReducer.Validate(profile.RunTotals.ItemStatistics);
        EconomyStatisticsReducer.Validate(profile.RunTotals.Economy);
        foreach (var map in profile.RunTotals.Maps.Values)
        {
            WeaponStatisticsReducer.ValidateAggregate(map.WeaponStatistics);
            CombatStatisticsReducer.ValidateAggregate(map.CombatStatistics);
            EquipmentStatisticsReducer.ValidateAggregate(map.EquipmentStatistics);
            ContainerStatisticsReducer.ValidateAggregate(map.ContainerStatistics);
            ItemStatisticsAggregateReducer.Validate(map.ItemStatistics);
            EconomyStatisticsReducer.Validate(map.Economy);
        }
        foreach (var map in profile.RunTotals.RouteMaps.Values)
        {
            WeaponStatisticsReducer.ValidateAggregate(map.WeaponStatistics);
            CombatStatisticsReducer.ValidateAggregate(map.CombatStatistics);
            EquipmentStatisticsReducer.ValidateAggregate(map.EquipmentStatistics);
            ContainerStatisticsReducer.ValidateAggregate(map.ContainerStatistics);
            ItemStatisticsAggregateReducer.Validate(map.ItemStatistics);
            EconomyStatisticsReducer.Validate(map.Economy);
        }

        profile.Runs.Add(summary);
        AddTotals(profile.RunTotals, summary);
        if (summary.RecordEligible && summary.Outcome is RunOutcome.Extracted or RunOutcome.Died)
        {
            AddRecord(profile.RunRecords, summary);
        }

        profile.UpdatedUtc = summary.EndedUtc;
        return true;
    }

    private static void AddTotals(RunAggregateTotals totals, RunSummary summary)
    {
        totals.TotalRuns = SaturatingAdd(totals.TotalRuns, 1);
        AddOutcome(totals.Outcomes, summary.Outcome);
        totals.PhysicalDistance = RouteStatisticsReducer.SaturatingAdd(totals.PhysicalDistance, summary.PhysicalDistance);
        totals.TeleportDistance = RouteStatisticsReducer.SaturatingAdd(totals.TeleportDistance, summary.TeleportDistance);
        totals.TransitionExcludedDistance = RouteStatisticsReducer.SaturatingAdd(
            totals.TransitionExcludedDistance,
            summary.TransitionExcludedDistance);
        ItemStatisticsAggregateReducer.Merge(totals.ItemStatistics, summary.ItemStatistics);
        EconomyStatisticsReducer.Merge(totals.Economy, summary.Economy);
        WeaponStatisticsReducer.Merge(totals.WeaponStatistics, summary.WeaponStatistics);
        CombatStatisticsReducer.Merge(totals.CombatStatistics, summary.CombatStatistics);
        EquipmentStatisticsReducer.Merge(
            totals.EquipmentStatistics,
            summary.EquipmentStatistics,
            countRunOccurrence: summary.Outcome != RunOutcome.Interrupted);
        ContainerStatisticsReducer.Merge(
            totals.ContainerStatistics,
            summary.ContainerStatistics,
            adoptSourceCapability: totals.TotalRuns == 1);

        var legacyStartingMap = string.IsNullOrWhiteSpace(summary.StartingMapId)
                                || (string.Equals(summary.StartingMapId, MapIdentity.UnknownId, StringComparison.Ordinal)
                                    && !string.Equals(summary.MapId, MapIdentity.UnknownId, StringComparison.Ordinal));
        var startingMapId = legacyStartingMap ? summary.MapId : summary.StartingMapId;
        var startingMapDisplayName = legacyStartingMap || string.IsNullOrWhiteSpace(summary.StartingMapDisplayName)
            ? summary.MapDisplayName
            : summary.StartingMapDisplayName;
        var startingMapKnown = summary.StartingMapKnown || summary.MapKnown;
        if (!totals.Maps.TryGetValue(startingMapId, out var map))
        {
            map = new MapRunAggregate
            {
                MapId = startingMapId,
                DisplayName = startingMapDisplayName,
                IsKnown = startingMapKnown
            };
            totals.Maps[startingMapId] = map;
        }

        map.DisplayName = startingMapDisplayName;
        map.IsKnown |= startingMapKnown;
        map.TotalRuns = SaturatingAdd(map.TotalRuns, 1);
        AddOutcome(map.Outcomes, summary.Outcome);
        map.PhysicalDistance = RouteStatisticsReducer.SaturatingAdd(map.PhysicalDistance, summary.PhysicalDistance);
        map.TeleportDistance = RouteStatisticsReducer.SaturatingAdd(map.TeleportDistance, summary.TeleportDistance);
        WeaponStatisticsReducer.Merge(map.WeaponStatistics, summary.WeaponStatistics);
        CombatStatisticsReducer.Merge(map.CombatStatistics, summary.CombatStatistics);
        EquipmentStatisticsReducer.Merge(
            map.EquipmentStatistics,
            summary.EquipmentStatistics,
            countRunOccurrence: summary.Outcome != RunOutcome.Interrupted);
        ContainerStatisticsReducer.Merge(
            map.ContainerStatistics,
            summary.ContainerStatistics,
            adoptSourceCapability: map.TotalRuns == 1);
        ItemStatisticsAggregateReducer.Merge(map.ItemStatistics, summary.ItemStatistics);
        EconomyStatisticsReducer.Merge(map.Economy, summary.Economy);

        var routeMapTotalsSupported = summary.RouteCapabilities.RouteAwareMapTotals.State == AdapterCapabilityState.Supported
                                      && !summary.HistoricalRouteUnavailable;
        var economyRouteAttributionSupported = summary.Economy.Capabilities.RouteAttribution.State == AdapterCapabilityState.Supported
                                               && !summary.Economy.HistoricalUnavailable;
        if (routeMapTotalsSupported || economyRouteAttributionSupported)
        {
            foreach (var segmentGroup in summary.Segments.GroupBy(segment => segment.MapId, StringComparer.Ordinal))
            {
                if (!totals.RouteMaps.TryGetValue(segmentGroup.Key, out var routeMap))
                {
                    var first = segmentGroup.First();
                    routeMap = new RouteAwareMapAggregate
                    {
                        MapId = first.MapId,
                        DisplayName = first.MapDisplayName,
                        IsKnown = first.MapKnown
                    };
                    totals.RouteMaps[segmentGroup.Key] = routeMap;
                }
                var routeRunEquipment = new EquipmentStatisticsAggregate();
                if (routeMapTotalsSupported) routeMap.RunsVisited = SaturatingAdd(routeMap.RunsVisited, 1);
                foreach (var segment in segmentGroup)
                {
                    routeMap.DisplayName = segment.MapDisplayName;
                    routeMap.IsKnown |= segment.MapKnown;
                    if (routeMapTotalsSupported)
                    {
                        routeMap.SegmentVisits = SaturatingAdd(routeMap.SegmentVisits, 1);
                        routeMap.ActiveDurationSeconds = RouteStatisticsReducer.SaturatingAdd(
                            routeMap.ActiveDurationSeconds,
                            segment.ActiveDurationSeconds);
                        routeMap.PhysicalDistance = RouteStatisticsReducer.SaturatingAdd(routeMap.PhysicalDistance, segment.PhysicalDistance);
                        routeMap.TeleportDistance = RouteStatisticsReducer.SaturatingAdd(routeMap.TeleportDistance, segment.TeleportDistance);
                        routeMap.TransitionExcludedDistance = RouteStatisticsReducer.SaturatingAdd(
                            routeMap.TransitionExcludedDistance,
                            segment.TransitionExcludedDistance);
                        ItemStatisticsAggregateReducer.Merge(routeMap.ItemStatistics, segment.ItemStatistics);
                        WeaponStatisticsReducer.Merge(routeMap.WeaponStatistics, segment.WeaponStatistics);
                        CombatStatisticsReducer.Merge(routeMap.CombatStatistics, segment.CombatStatistics);
                        EquipmentStatisticsReducer.Merge(routeRunEquipment, segment.EquipmentStatistics, countRunOccurrence: false);
                        ContainerStatisticsReducer.Merge(routeMap.ContainerStatistics, segment.ContainerStatistics);
                    }
                    if (economyRouteAttributionSupported)
                        EconomyStatisticsReducer.Merge(routeMap.Economy, segment.Economy);
                }
                if (routeMapTotalsSupported)
                    EquipmentStatisticsReducer.Merge(
                        routeMap.EquipmentStatistics,
                        routeRunEquipment,
                        countRunOccurrence: summary.Outcome != RunOutcome.Interrupted);
            }
        }
    }

    private static void AddRecord(RunDurationRecords records, RunSummary summary)
    {
        var overall = summary.Outcome == RunOutcome.Extracted ? records.Extraction : records.Death;
        UpdatePair(overall, summary);

        var legacyStartingMap = string.IsNullOrWhiteSpace(summary.StartingMapId)
                                || (string.Equals(summary.StartingMapId, MapIdentity.UnknownId, StringComparison.Ordinal)
                                    && !string.Equals(summary.MapId, MapIdentity.UnknownId, StringComparison.Ordinal));
        var startingMapId = legacyStartingMap ? summary.MapId : summary.StartingMapId;
        var startingMapDisplayName = legacyStartingMap || string.IsNullOrWhiteSpace(summary.StartingMapDisplayName)
            ? summary.MapDisplayName
            : summary.StartingMapDisplayName;
        if (!records.Maps.TryGetValue(startingMapId, out var map))
        {
            map = new MapRunDurationRecords
            {
                MapId = startingMapId,
                DisplayName = startingMapDisplayName
            };
            records.Maps[startingMapId] = map;
        }

        map.DisplayName = startingMapDisplayName;
        UpdatePair(summary.Outcome == RunOutcome.Extracted ? map.Extraction : map.Death, summary);
    }

    private static void UpdatePair(DurationRecordPair pair, RunSummary candidate)
    {
        if (pair.Shortest == null || Compare(candidate, pair.Shortest, preferShortest: true) < 0)
        {
            pair.Shortest = CreateReference(candidate);
        }

        if (pair.Longest == null || Compare(candidate, pair.Longest, preferShortest: false) < 0)
        {
            pair.Longest = CreateReference(candidate);
        }
    }

    private static int Compare(RunSummary candidate, DurationRecordReference current, bool preferShortest)
    {
        var duration = candidate.ActiveDurationSeconds.CompareTo(current.ActiveDurationSeconds);
        if (!preferShortest)
        {
            duration = -duration;
        }

        if (duration != 0)
        {
            return duration;
        }

        var started = candidate.StartedUtc.CompareTo(current.StartedUtc);
        return started != 0
            ? started
            : string.Compare(candidate.RunId, current.RunId, StringComparison.Ordinal);
    }

    private static DurationRecordReference CreateReference(RunSummary summary) => new()
    {
        RunId = summary.RunId,
        ActiveDurationSeconds = summary.ActiveDurationSeconds,
        StartedUtc = summary.StartedUtc,
        MapId = summary.MapId,
        MapDisplayName = summary.MapDisplayName
    };

    private static void AddOutcome(Dictionary<string, long> outcomes, RunOutcome outcome)
    {
        var key = outcome.ToString();
        outcomes.TryGetValue(key, out var current);
        outcomes[key] = SaturatingAdd(current, 1);
    }

    public static void Validate(RunSummary summary)
    {
        if (summary == null)
        {
            throw new ArgumentNullException(nameof(summary));
        }
        if (string.IsNullOrWhiteSpace(summary.RunId)
            || string.IsNullOrWhiteSpace(summary.SaveGenerationId)
            || string.IsNullOrWhiteSpace(summary.MapId)
            || string.IsNullOrWhiteSpace(summary.MapDisplayName)
            || summary.EndedUtc < summary.StartedUtc
            || !IsFiniteNonNegative(summary.ActiveDurationSeconds)
            || !IsFiniteNonNegative(summary.WallClockDurationSeconds)
            || !IsFiniteNonNegative(summary.PhysicalDistance)
            || !IsFiniteNonNegative(summary.TeleportDistance)
            || !IsFiniteNonNegative(summary.TransitionExcludedDistance))
        {
            throw new ArgumentException("Run summary is invalid.", nameof(summary));
        }

        WeaponStatisticsReducer.ValidateAggregate(summary.WeaponStatistics);
        CombatStatisticsReducer.ValidateAggregate(summary.CombatStatistics);
        EquipmentStatisticsReducer.ValidateAggregate(summary.EquipmentStatistics);
        ContainerStatisticsReducer.ValidateAggregate(summary.ContainerStatistics);
        ItemStatisticsAggregateReducer.Validate(summary.ItemStatistics);
        EconomyStatisticsReducer.Validate(summary.Economy);
        RouteStatisticsReducer.ValidateCapabilities(summary.RouteCapabilities);

        if (summary.Segments.Count > 0)
        {
            RouteStatisticsReducer.Validate(summary.Segments, allowOpenLast: false);
            if (!summary.HistoricalRouteUnavailable
                && !string.Equals(summary.StartingMapId, summary.Segments[0].MapId, StringComparison.Ordinal))
                throw new ArgumentException("Run starting map does not match its first retained segment.", nameof(summary));
        }
        RouteStatisticsReducer.ValidateAssociations(summary.Segments, summary.SegmentEventAssociations);

        ValidateRunEconomyComposition(summary);

        if (!summary.HistoricalRouteUnavailable
            && summary.RouteCapabilities.Segments.State == AdapterCapabilityState.Supported)
        {
            if (summary.Segments.Count == 0)
                throw new ArgumentException("Supported run route has no segment.", nameof(summary));
            if (!string.Equals(summary.StartingMapId, summary.Segments[0].MapId, StringComparison.Ordinal)
                || !string.Equals(summary.EndingMapId, summary.Segments[^1].MapId, StringComparison.Ordinal))
                throw new ArgumentException("Run starting or ending map does not match its ordered segments.", nameof(summary));
            if (!string.Equals(summary.RouteSignature, RouteStatisticsReducer.BuildSignature(summary.Segments), StringComparison.Ordinal))
                throw new ArgumentException("Run route signature does not match its ordered segments.", nameof(summary));
            if (!NearlyEqual(summary.ActiveDurationSeconds, RouteStatisticsReducer.SaturatingSum(summary.Segments.Select(segment => segment.ActiveDurationSeconds)))
                || !NearlyEqual(summary.PhysicalDistance, RouteStatisticsReducer.SaturatingSum(summary.Segments.Select(segment => segment.PhysicalDistance)))
                || !NearlyEqual(summary.TeleportDistance, RouteStatisticsReducer.SaturatingSum(summary.Segments.Select(segment => segment.TeleportDistance)))
                || !NearlyEqual(summary.TransitionExcludedDistance, RouteStatisticsReducer.SaturatingSum(summary.Segments.Select(segment => segment.TransitionExcludedDistance))))
                throw new ArgumentException("Run duration or movement totals do not equal the segment composition.", nameof(summary));
        }

        if (summary.Outcome == RunOutcome.Interrupted && summary.RecordEligible)
        {
            throw new ArgumentException("Interrupted runs cannot be record eligible.", nameof(summary));
        }
    }

    public static void ValidateProfileEconomyComposition(ProfileStatistics profile)
    {
        if (profile == null) throw new ArgumentNullException(nameof(profile));
        foreach (var run in profile.Runs) ValidateRunEconomyComposition(run);

        ValidateEconomyFanOut("completed-run totals", profile.RunTotals.Economy, profile.Runs.Select(run => run.Economy));

        var runsByStartingMap = profile.Runs.GroupBy(
            run => ResolveStartingMapId(run),
            StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.ToList(), StringComparer.Ordinal);
        foreach (var entry in profile.RunTotals.Maps)
        {
            runsByStartingMap.TryGetValue(entry.Key, out var runs);
            ValidateEconomyFanOut(
                $"starting-map totals '{entry.Key}'",
                entry.Value.Economy,
                (runs ?? []).Select(run => run.Economy));
        }
        foreach (var entry in runsByStartingMap.Where(entry => !profile.RunTotals.Maps.ContainsKey(entry.Key)))
            ValidateMissingEconomyFanOut($"starting-map totals '{entry.Key}'", entry.Value.Select(run => run.Economy));

        var segmentsByMap = profile.Runs
            .Where(run => !run.HistoricalRouteUnavailable
                          && !run.Economy.HistoricalUnavailable
                          && run.Economy.Capabilities.RouteAttribution.State == AdapterCapabilityState.Supported)
            .SelectMany(run => run.Segments)
            .GroupBy(segment => segment.MapId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(segment => segment.Economy).ToList(), StringComparer.Ordinal);
        foreach (var entry in profile.RunTotals.RouteMaps)
        {
            segmentsByMap.TryGetValue(entry.Key, out var segments);
            ValidateEconomyFanOut(
                $"route-map totals '{entry.Key}'",
                entry.Value.Economy,
                segments ?? []);
        }
        foreach (var entry in segmentsByMap.Where(entry => !profile.RunTotals.RouteMaps.ContainsKey(entry.Key)))
            ValidateMissingEconomyFanOut($"route-map totals '{entry.Key}'", entry.Value);
    }

    private static void ValidateRunEconomyComposition(RunSummary summary)
    {
        if (summary.HistoricalRouteUnavailable
            || summary.Economy.HistoricalUnavailable
            || summary.Economy.Capabilities.RouteAttribution.State != AdapterCapabilityState.Supported)
            return;

        ValidateEconomyFanOut(
            $"run '{summary.RunId}' segment composition",
            summary.Economy,
            summary.Segments.Select(segment => segment.Economy),
            validateTerminalOutcomes: false);
    }

    private static void ValidateEconomyFanOut(
        string scope,
        EconomyStatisticsAggregate total,
        IEnumerable<EconomyStatisticsAggregate> components,
        bool validateTerminalOutcomes = true)
    {
        var materialized = components.ToList();
        foreach (CurrencyKind currency in Enum.GetValues(typeof(CurrencyKind)))
        {
            if (!EconomyStatisticsReducer.IsExactCurrencyComposition(total, materialized, currency))
                throw new ArgumentException($"Current-schema {scope} does not equal its exact {currency} composition.");
        }
        var cashOutcomesCompose = validateTerminalOutcomes
            ? EconomyStatisticsReducer.IsExactCashOutcomeComposition(total, materialized)
            : EconomyStatisticsReducer.IsExactCashAcquisitionComposition(total, materialized);
        if (!cashOutcomesCompose)
            throw new ArgumentException($"Current-schema {scope} does not equal its exact Cash raid-outcome composition.");
    }

    private static void ValidateMissingEconomyFanOut(
        string scope,
        IEnumerable<EconomyStatisticsAggregate> components)
    {
        var materialized = components.ToList();
        foreach (CurrencyKind currency in Enum.GetValues(typeof(CurrencyKind)))
        {
            if (materialized.Any(component =>
                    (EconomyStatisticsReducer.HasExactSupportedCurrency(component, currency)
                     || component.HistoricalUnavailable
                     && EconomyStatisticsReducer.HasExactCapturedCurrency(component, currency))
                    && component.Currencies.TryGetValue(currency.ToString(), out var row)
                    && (row.Totals.GrossInflow != 0 || row.Totals.GrossOutflow != 0)))
                throw new ArgumentException($"Current-schema {scope} is missing exact {currency} contributions.");
        }
        if (materialized.Any(component =>
                !component.CashArithmeticSaturated
                && (component.CashRaidOutcomes.Acquired != 0
                    || component.CashRaidOutcomes.Secured != 0
                    || component.CashRaidOutcomes.Lost != 0
                    || component.CashRaidOutcomes.Unresolved != 0)))
            throw new ArgumentException($"Current-schema {scope} is missing exact Cash raid-outcome contributions.");
    }

    private static string ResolveStartingMapId(RunSummary summary) =>
        string.IsNullOrWhiteSpace(summary.StartingMapId)
        || (string.Equals(summary.StartingMapId, MapIdentity.UnknownId, StringComparison.Ordinal)
            && !string.Equals(summary.MapId, MapIdentity.UnknownId, StringComparison.Ordinal))
            ? summary.MapId
            : summary.StartingMapId;

    private static bool IsFiniteNonNegative(double value) =>
        value >= 0 && !double.IsNaN(value) && !double.IsInfinity(value);

    private static bool NearlyEqual(double left, double right) => Math.Abs(left - right) <= 0.000001;

    private static long SaturatingAdd(long current, long addition)
    {
        if (current < 0 || addition < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(current), "Persisted run counters cannot be negative.");
        }

        return current > long.MaxValue - addition ? long.MaxValue : current + addition;
    }

}
