using System.Runtime.ExceptionServices;

namespace UltimateDuckovStatistics.Adapters;

internal static class ItemUsePublication
{
    internal static bool PublishIndependently(
        Func<bool> profilePublication,
        Func<bool> activeRunPublication)
    {
        if (profilePublication == null) throw new ArgumentNullException(nameof(profilePublication));
        if (activeRunPublication == null) throw new ArgumentNullException(nameof(activeRunPublication));

        var profilePublished = false;
        var activeRunPublished = false;
        Exception? profileFailure = null;
        Exception? activeRunFailure = null;
        try
        {
            profilePublished = profilePublication();
        }
        catch (Exception exception)
        {
            profileFailure = exception;
        }

        try
        {
            activeRunPublished = activeRunPublication();
        }
        catch (Exception exception)
        {
            activeRunFailure = exception;
        }

        if (profileFailure != null && activeRunFailure != null)
        {
            throw new AggregateException(
                "Item-use publication failed for both profile and active-run destinations.",
                profileFailure,
                activeRunFailure);
        }
        if (profileFailure != null) ExceptionDispatchInfo.Capture(profileFailure).Throw();
        if (activeRunFailure != null) ExceptionDispatchInfo.Capture(activeRunFailure).Throw();
        return profilePublished || activeRunPublished;
    }
}
