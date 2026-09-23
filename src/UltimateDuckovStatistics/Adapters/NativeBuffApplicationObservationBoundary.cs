using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class NativeBuffApplicationObservationBoundary
{
    private readonly CombatBuffOwnershipTracker ownershipTracker = new();
    private volatile bool trusted;
    private Func<bool>? trustValidator;

    public bool IsTrusted => trusted && (trustValidator?.Invoke() ?? true);

    internal void SetTrustValidator(Func<bool>? validator) => trustValidator = validator;

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
        if (!IsTrusted) return false;
        ownershipTracker.Observe(runtimeBuff, retainedActor, incomingActor);
        return true;
    }

    public CombatBuffOwnershipResolution Resolve(
        object runtimeBuff,
        CombatActorEvidence retainedActor) =>
        ownershipTracker.Resolve(runtimeBuff, retainedActor, IsTrusted);

    public void Clear() => ownershipTracker.Clear();
}
