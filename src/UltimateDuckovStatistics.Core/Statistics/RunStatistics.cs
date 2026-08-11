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
        totals.TotalRuns++;
        AddOutcome(totals.Outcomes, summary.Outcome);
        totals.PhysicalDistance += summary.PhysicalDistance;
        totals.TeleportDistance += summary.TeleportDistance;
        WeaponStatisticsReducer.Merge(totals.WeaponStatistics, summary.WeaponStatistics);

        if (!totals.Maps.TryGetValue(summary.MapId, out var map))
        {
            map = new MapRunAggregate
            {
                MapId = summary.MapId,
                DisplayName = summary.MapDisplayName,
                IsKnown = summary.MapKnown
            };
            totals.Maps[summary.MapId] = map;
        }

        map.DisplayName = summary.MapDisplayName;
        map.IsKnown |= summary.MapKnown;
        map.TotalRuns++;
        AddOutcome(map.Outcomes, summary.Outcome);
        map.PhysicalDistance += summary.PhysicalDistance;
        map.TeleportDistance += summary.TeleportDistance;
        WeaponStatisticsReducer.Merge(map.WeaponStatistics, summary.WeaponStatistics);
    }

    private static void AddRecord(RunDurationRecords records, RunSummary summary)
    {
        var overall = summary.Outcome == RunOutcome.Extracted ? records.Extraction : records.Death;
        UpdatePair(overall, summary);

        if (!records.Maps.TryGetValue(summary.MapId, out var map))
        {
            map = new MapRunDurationRecords
            {
                MapId = summary.MapId,
                DisplayName = summary.MapDisplayName
            };
            records.Maps[summary.MapId] = map;
        }

        map.DisplayName = summary.MapDisplayName;
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
        outcomes[key] = current + 1;
    }

    private static void Validate(RunSummary summary)
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
            || !IsFiniteNonNegative(summary.TeleportDistance))
        {
            throw new ArgumentException("Run summary is invalid.", nameof(summary));
        }

        if (summary.Outcome == RunOutcome.Interrupted && summary.RecordEligible)
        {
            throw new ArgumentException("Interrupted runs cannot be record eligible.", nameof(summary));
        }
    }

    private static bool IsFiniteNonNegative(double value) =>
        value >= 0 && !double.IsNaN(value) && !double.IsInfinity(value);
}
