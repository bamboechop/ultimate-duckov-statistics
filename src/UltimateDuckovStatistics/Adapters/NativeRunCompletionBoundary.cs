using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class NativeRunCompletionBoundary
{
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
        pendingSummary = summary;
        pendingReason = reason;
        pendingDetailedDiagnostic = detailedDiagnostic;
    }

    public bool Retry(Func<RunSummary, bool> completionHandler, Action<string> diagnosticHandler)
    {
        if (completionHandler == null) throw new ArgumentNullException(nameof(completionHandler));
        if (diagnosticHandler == null) throw new ArgumentNullException(nameof(diagnosticHandler));
        if (pendingSummary == null) return true;

        var summary = pendingSummary;
        if (!completionHandler(summary))
        {
            diagnosticHandler(
                $"Completed run persistence remains pending id={summary.RunId} outcome={summary.Outcome}; retry retained.");
            return false;
        }

        diagnosticHandler(pendingDetailedDiagnostic
            ? $"Run finalized id={summary.RunId} outcome={summary.Outcome} "
              + $"active={summary.ActiveDurationSeconds:0.###}s physical={summary.PhysicalDistance:0.###}m "
              + $"teleport={summary.TeleportDistance:0.###}m."
            : $"Run finalized id={summary.RunId} outcome={summary.Outcome} reason={pendingReason ?? "terminal boundary"}.");
        pendingSummary = null;
        pendingReason = null;
        pendingDetailedDiagnostic = false;
        return true;
    }
}
