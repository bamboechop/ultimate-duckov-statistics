using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Core.Statistics;

public sealed class RunStatisticsViewModel
{
    public long TotalRuns { get; set; }

    public long ExtractedRuns { get; set; }

    public long DiedRuns { get; set; }

    public long InterruptedRuns { get; set; }

    public double PhysicalDistance { get; set; }

    public double TeleportDistance { get; set; }

    public bool MovementSupported { get; set; }

    public IReadOnlyList<RunSummary> Runs { get; set; } = Array.Empty<RunSummary>();

    public RunDurationRecords Records { get; set; } = new();

    public IReadOnlyList<MapRunAggregate> Maps { get; set; } = Array.Empty<MapRunAggregate>();
}

public static class RunStatisticsViewModelFactory
{
    public const string MovementAdapterId = "native-main-duck-movement";

    public static RunStatisticsViewModel Create(ProfileDocument profile)
    {
        if (profile == null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        var totals = profile.Statistics.RunTotals;
        return new RunStatisticsViewModel
        {
            TotalRuns = totals.TotalRuns,
            ExtractedRuns = ReadOutcome(totals, RunOutcome.Extracted),
            DiedRuns = ReadOutcome(totals, RunOutcome.Died),
            InterruptedRuns = ReadOutcome(totals, RunOutcome.Interrupted),
            PhysicalDistance = totals.PhysicalDistance,
            TeleportDistance = totals.TeleportDistance,
            MovementSupported = profile.Capabilities.Any(capability =>
                string.Equals(capability.AdapterId, MovementAdapterId, StringComparison.Ordinal)
                && capability.State == AdapterCapabilityState.Supported),
            Runs = profile.Statistics.Runs
                .OrderByDescending(run => run.StartedUtc)
                .ThenBy(run => run.RunId, StringComparer.Ordinal)
                .ToArray(),
            Records = profile.Statistics.RunRecords,
            Maps = totals.Maps.Values.OrderBy(map => map.MapId, StringComparer.Ordinal).ToArray()
        };
    }

    private static long ReadOutcome(RunAggregateTotals totals, RunOutcome outcome) =>
        totals.Outcomes.TryGetValue(outcome.ToString(), out var value) ? value : 0;
}
