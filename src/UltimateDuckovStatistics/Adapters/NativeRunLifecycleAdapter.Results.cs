using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Adapters;

internal sealed partial class NativeRunLifecycleAdapter
{
    private Action<string, string, bool>? resultsStarted;
    private Action<RunOutcome>? resultsTerminal;
    private Action<RunSummary>? resultsCompleted;
    private Action? resultsReset;
    private string? newlyObservedRaidId;

    public void ConfigureResults(
        Action<string, string, bool> started, Action<RunOutcome> terminal,
        Action<RunSummary> completed, Action reset)
    {
        resultsStarted = started;
        resultsTerminal = terminal;
        resultsCompleted = completed;
        resultsReset = reset;
    }

    private void NotifyResults(Action notify)
    {
        try { notify(); }
        catch (Exception exception)
        {
            diagnosticHandler($"Results summary observer failed safely: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void ResetResults()
    {
        newlyObservedRaidId = null;
        NotifyResults(() => resultsReset?.Invoke());
    }
}
