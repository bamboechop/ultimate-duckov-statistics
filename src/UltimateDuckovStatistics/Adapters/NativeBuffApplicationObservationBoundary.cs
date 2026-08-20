using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class NativeBuffApplicationObservationBoundary
{
    private readonly CombatBuffOwnershipTracker ownershipTracker = new();
    private volatile bool trusted;

    public bool IsTrusted => trusted;

    public void MarkTrusted() => trusted = true;

    public void MarkUntrusted()
    {
        trusted = false;
        ownershipTracker.Clear();
    }

    public bool Capture(
        object runtimeBuff,
        CombatActorEvidence retainedActor,
        CombatActorEvidence incomingActor)
    {
        if (!trusted) return false;
        ownershipTracker.Observe(runtimeBuff, retainedActor, incomingActor);
        return true;
    }

    public CombatBuffOwnershipResolution Resolve(
        object runtimeBuff,
        CombatActorEvidence retainedActor) =>
        ownershipTracker.Resolve(runtimeBuff, retainedActor, trusted);

    public void Clear() => ownershipTracker.Clear();
}
