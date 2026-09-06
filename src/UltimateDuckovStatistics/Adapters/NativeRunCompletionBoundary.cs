using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class NativeRunCompletionBoundary
{
    private readonly PersistenceRetryBackoff retry;
    private readonly MonotonicCadenceGate diagnosticCadence = new(60);
    private readonly Func<double> clock;

    public NativeRunCompletionBoundary(Func<double>? clock = null)
    {
        this.clock = clock ?? (() => (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency);
        retry = new PersistenceRetryBackoff(this.clock);
    }

    private RunSummary? pendingSummary;
    private string? pendingReason;
    private bool pendingDetailedDiagnostic;

    public bool HasPendingCompletion => pendingSummary != null;

    public RunSummary? PendingSummary => pendingSummary;

    public void Begin(RunSummary summary, string? reason, bool detailedDiagnostic)
    {
        if (summary == null) throw new ArgumentNullException(nameof(summary));
        if (pendingSummary != null)
            throw new InvalidOperationException("A pending completed run must be persisted before accepting another summary.");
        retry.Reset();
        diagnosticCadence.Reset();
        pendingSummary = summary;
        pendingReason = reason;
        pendingDetailedDiagnostic = detailedDiagnostic;
    }

    public bool Retry(Func<RunSummary, bool> completionHandler, Action<string> diagnosticHandler)
    {
        if (completionHandler == null) throw new ArgumentNullException(nameof(completionHandler));
        if (diagnosticHandler == null) throw new ArgumentNullException(nameof(diagnosticHandler));
        if (pendingSummary == null) return true;

        if (!retry.IsDue) return false;
        var summary = pendingSummary;
        Exception? failure = null;
        bool persisted;
        try { persisted = completionHandler(summary); }
        catch (Exception exception) { failure = exception; persisted = false; }
        if (!persisted)
        {
            retry.Failed();
            var now = clock();
            if (diagnosticCadence.IsDue(now))
            {
                diagnosticCadence.MarkCompleted(now);
                diagnosticHandler(
                    $"Completed run persistence remains pending id={summary.RunId} outcome={summary.Outcome}; "
                    + $"retry retained with backoff up to 60s (next delay {retry.DelaySeconds:0}s)."
                    + (failure == null ? string.Empty : $" {failure}"));
            }
            return false;
        }

        diagnosticHandler(pendingDetailedDiagnostic
            ? $"Run finalized id={summary.RunId} outcome={summary.Outcome} "
              + $"active={summary.ActiveDurationSeconds:0.###}s physical={summary.PhysicalDistance:0.###}m "
              + $"teleport={summary.TeleportDistance:0.###}m."
            : $"Run finalized id={summary.RunId} outcome={summary.Outcome} reason={pendingReason ?? "terminal boundary"}.");
        retry.Reset();
        pendingSummary = null;
        pendingReason = null;
        pendingDetailedDiagnostic = false;
        return true;
    }
}
