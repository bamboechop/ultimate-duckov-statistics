using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class EconomyFlowPublication
{
    private readonly Func<CurrencyFlowRecorded, bool> profilePublisher;
    private readonly Func<CurrencyFlowRecorded, bool> activeRunPublisher;
    private readonly Action<string> diagnosticHandler;
    private readonly Dictionary<string, PublicationState> pending = new(StringComparer.Ordinal);

    public EconomyFlowPublication(
        Func<CurrencyFlowRecorded, bool> profilePublisher,
        Func<CurrencyFlowRecorded, bool> activeRunPublisher,
        Action<string> diagnosticHandler)
    {
        this.profilePublisher = profilePublisher ?? throw new ArgumentNullException(nameof(profilePublisher));
        this.activeRunPublisher = activeRunPublisher ?? throw new ArgumentNullException(nameof(activeRunPublisher));
        this.diagnosticHandler = diagnosticHandler ?? throw new ArgumentNullException(nameof(diagnosticHandler));
    }

    public bool Publish(CurrencyFlowRecorded flow)
    {
        if (flow == null) throw new ArgumentNullException(nameof(flow));
        if (string.IsNullOrWhiteSpace(flow.EventId)) return false;

        pending.TryGetValue(flow.EventId, out var state);
        state ??= new PublicationState();
        if (!state.ProfileAccepted)
            state.ProfileAccepted = TryPublish(profilePublisher, flow, "profile");

        var requiresRun = flow.GameplayContext == GameplayContext.Raid
                          && !string.IsNullOrWhiteSpace(flow.RunId);
        if (requiresRun && !state.RunAccepted)
            state.RunAccepted = TryPublish(activeRunPublisher, flow, "active run");

        if (state.ProfileAccepted && (!requiresRun || state.RunAccepted))
        {
            pending.Remove(flow.EventId);
            return true;
        }

        pending[flow.EventId] = state;
        return false;
    }

    private bool TryPublish(
        Func<CurrencyFlowRecorded, bool> destination,
        CurrencyFlowRecorded flow,
        string destinationName)
    {
        try
        {
            return destination(flow);
        }
        catch (Exception exception)
        {
            diagnosticHandler(
                $"Economy flow publication to {destinationName} failed safely and remains queued: " +
                $"{exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private sealed class PublicationState
    {
        public bool ProfileAccepted { get; set; }

        public bool RunAccepted { get; set; }
    }
}
