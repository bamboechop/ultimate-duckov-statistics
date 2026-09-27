using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Core.Statistics;

/// <summary>A small, process-local results snapshot. It never owns a profile or run history.</summary>
public sealed class ExtractionSummarySession
{
    private string? generation;
    private string? runId;
    private decimal? startingValue;
    private decimal? endingValue;
    private bool terminalObserved;
    private RunOutcome outcome;

    public ExtractionSummarySnapshot? Completed { get; private set; }

    public void Begin(string saveGenerationId, string id, decimal? carriedValue)
    {
        Clear();
        generation = saveGenerationId;
        runId = id;
        startingValue = carriedValue;
    }

    public void ObserveTerminal(RunOutcome terminalOutcome, decimal? carriedValue)
    {
        if (runId == null || terminalObserved) return;
        terminalObserved = true;
        outcome = terminalOutcome;
        // Death inventory filtering and modded retention rules are not proven here.
        endingValue = terminalOutcome == RunOutcome.Extracted ? carriedValue : null;
    }

    public void Complete(RunSummary run)
    {
        if (run == null) throw new ArgumentNullException(nameof(run));
        if (run.SaveGenerationId != generation || run.RunId != runId || !terminalObserved
            || run.Outcome != outcome || run.Outcome is not (RunOutcome.Extracted or RunOutcome.Died)) return;
        if (Completed != null) return;
        var combat = run.CombatStatistics;
        Completed = new ExtractionSummarySnapshot(
            run.SaveGenerationId, run.RunId, run.NativeRaidId, run.Outcome,
            run.LifecycleCapability == AdapterCapabilityState.Supported
                && double.IsFinite(run.ActiveDurationSeconds) && run.ActiveDurationSeconds >= 0
                    ? run.ActiveDurationSeconds : null,
            combat.Totals.KillsByYou,
            !combat.WasRepairedFromInvalidState
                && combat.Capabilities.KillsByYou.State == AdapterCapabilityState.Supported,
            startingValue.HasValue && endingValue.HasValue ? endingValue - startingValue : null);
    }

    public void Clear()
    {
        generation = null;
        runId = null;
        startingValue = null;
        endingValue = null;
        terminalObserved = false;
        Completed = null;
    }
}

public sealed class ExtractionSummarySnapshot
{
    public ExtractionSummarySnapshot(string generationId, string runId, string? nativeRaidId, RunOutcome outcome,
        double? activeSeconds, long kills, bool killsComplete, decimal? estimatedNetValue)
    {
        GenerationId = generationId;
        RunId = runId;
        NativeRaidId = nativeRaidId;
        Outcome = outcome;
        ActiveSeconds = activeSeconds;
        Kills = kills;
        KillsComplete = killsComplete;
        EstimatedNetValue = estimatedNetValue;
    }

    public string GenerationId { get; }
    public string RunId { get; }
    public string? NativeRaidId { get; }
    public RunOutcome Outcome { get; }
    public double? ActiveSeconds { get; }
    public long Kills { get; }
    public bool KillsComplete { get; }
    public decimal? EstimatedNetValue { get; }
}
