namespace UltimateDuckovStatistics.Core.Tracking;

public sealed class DeathObservationGate
{
    private bool observed;

    public bool TryObserve(bool runActive)
    {
        if (!runActive || observed) return false;
        observed = true;
        return true;
    }

    public void Reset() => observed = false;
}
