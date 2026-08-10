using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Core.Statistics;

public enum RunRecordEligibilityReason
{
    Eligible,
    Interrupted,
    Integrity,
    LifecycleUnsupported,
    Other
}

public sealed class RunPresentationRow
{
    public RunSummary Run { get; set; } = new();

    public IntegrityTags IntegrityTags { get; set; }

    public bool RecordEligible { get; set; }

    public RunRecordEligibilityReason RecordEligibilityReason { get; set; }
}

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

    public IReadOnlyList<RunPresentationRow> RunRows { get; set; } = Array.Empty<RunPresentationRow>();

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
        var runs = profile.Statistics.Runs
            .OrderByDescending(run => run.StartedUtc)
            .ThenBy(run => run.RunId, StringComparer.Ordinal)
            .ToArray();
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
            Runs = runs,
            RunRows = runs.Select(CreateRunRow).ToArray(),
            Records = profile.Statistics.RunRecords,
            Maps = totals.Maps.Values.OrderBy(map => map.MapId, StringComparer.Ordinal).ToArray()
        };
    }

    private static long ReadOutcome(RunAggregateTotals totals, RunOutcome outcome) =>
        totals.Outcomes.TryGetValue(outcome.ToString(), out var value) ? value : 0;

    private static RunPresentationRow CreateRunRow(RunSummary run) => new()
    {
        Run = run,
        IntegrityTags = run.IntegrityTags,
        RecordEligible = run.RecordEligible,
        RecordEligibilityReason = GetEligibilityReason(run)
    };

    private static RunRecordEligibilityReason GetEligibilityReason(RunSummary run)
    {
        if (run.RecordEligible)
        {
            return RunRecordEligibilityReason.Eligible;
        }

        if (run.Outcome == RunOutcome.Interrupted)
        {
            return RunRecordEligibilityReason.Interrupted;
        }

        if (run.IntegrityTags != IntegrityTags.Normal)
        {
            return RunRecordEligibilityReason.Integrity;
        }

        return run.LifecycleCapability != AdapterCapabilityState.Supported
            ? RunRecordEligibilityReason.LifecycleUnsupported
            : RunRecordEligibilityReason.Other;
    }
}
