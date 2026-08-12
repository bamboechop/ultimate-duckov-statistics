using System.Runtime.ExceptionServices;

namespace UltimateDuckovStatistics.Adapters;

internal sealed partial class NativeContainerAdapter
{
    internal static void PublishIndependently(
        Action profilePublication,
        Action activeRunPublication)
    {
        if (profilePublication == null) throw new ArgumentNullException(nameof(profilePublication));
        if (activeRunPublication == null) throw new ArgumentNullException(nameof(activeRunPublication));

        Exception? profileFailure = null;
        Exception? activeRunFailure = null;
        try
        {
            profilePublication();
        }
        catch (Exception exception)
        {
            profileFailure = exception;
        }

        try
        {
            activeRunPublication();
        }
        catch (Exception exception)
        {
            activeRunFailure = exception;
        }

        if (profileFailure != null && activeRunFailure != null)
        {
            throw new AggregateException(
                "Container capability publication failed for both profile and active-run destinations.",
                profileFailure,
                activeRunFailure);
        }
        if (profileFailure != null) ExceptionDispatchInfo.Capture(profileFailure).Throw();
        if (activeRunFailure != null) ExceptionDispatchInfo.Capture(activeRunFailure).Throw();
    }
}
