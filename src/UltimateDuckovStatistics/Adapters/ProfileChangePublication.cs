namespace UltimateDuckovStatistics.Adapters;

internal static class ProfileChangePublication
{
    internal static void PublishIndependently(Action? subscribers, Action<Exception> failureHandler)
    {
        if (failureHandler == null) throw new ArgumentNullException(nameof(failureHandler));
        if (subscribers == null) return;

        foreach (Action subscriber in subscribers.GetInvocationList())
        {
            try
            {
                subscriber();
            }
            catch (Exception exception)
            {
                failureHandler(exception);
            }
        }
    }
}
