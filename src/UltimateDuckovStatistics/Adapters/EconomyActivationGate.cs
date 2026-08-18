namespace UltimateDuckovStatistics.Adapters;

internal sealed class EconomyActivationGate
{
    private readonly Action<string> registration;
    private readonly Action<Exception> failureHandler;
    private string? pendingActivationId;
    private bool failureReported;

    public EconomyActivationGate(Action<string> registration, Action<Exception> failureHandler)
    {
        this.registration = registration ?? throw new ArgumentNullException(nameof(registration));
        this.failureHandler = failureHandler ?? throw new ArgumentNullException(nameof(failureHandler));
    }

    public bool IsReady => pendingActivationId == null;

    public void Begin(string activationId)
    {
        if (string.IsNullOrWhiteSpace(activationId))
            throw new ArgumentException("An economy activation identity is required.", nameof(activationId));
        pendingActivationId = activationId;
        failureReported = false;
        EnsureReady();
    }

    public bool EnsureReady()
    {
        if (pendingActivationId == null) return true;
        try
        {
            registration(pendingActivationId);
            pendingActivationId = null;
            failureReported = false;
            return true;
        }
        catch (Exception exception)
        {
            if (!failureReported)
            {
                failureReported = true;
                failureHandler(exception);
            }
            return false;
        }
    }
}
