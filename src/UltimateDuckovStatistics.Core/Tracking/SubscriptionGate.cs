namespace UltimateDuckovStatistics.Core.Tracking;

public sealed class SubscriptionGate
{
    private bool active;

    public bool IsActive => active;

    public bool TryActivate()
    {
        if (active)
        {
            return false;
        }

        active = true;
        return true;
    }

    public bool TryDeactivate()
    {
        if (!active)
        {
            return false;
        }

        active = false;
        return true;
    }
}
