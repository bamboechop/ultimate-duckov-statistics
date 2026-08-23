namespace UltimateDuckovStatistics.Adapters;

internal static class NativeProfileResetTransition
{
    internal static void Queue(
        long transitionId,
        Action<long> craftingProfileChangeStarted,
        Action<string, Action[]> enqueueTransition,
        Action profileChanging,
        Action waitRunCheckpoint,
        Action drainProfileWriter,
        Action refreshIdentity,
        Action rotateRepository,
        Action openDiagnostics,
        Action worldTimeProfileChanged,
        Action<long> craftingProfileChangeCompleted,
        Action profileChanged,
        Action applyCurrentMetricCapabilities,
        Action writeDiagnostic)
    {
        if (transitionId <= 0) throw new ArgumentOutOfRangeException(nameof(transitionId));
        if (craftingProfileChangeStarted == null)
            throw new ArgumentNullException(nameof(craftingProfileChangeStarted));
        if (enqueueTransition == null) throw new ArgumentNullException(nameof(enqueueTransition));
        if (craftingProfileChangeCompleted == null)
            throw new ArgumentNullException(nameof(craftingProfileChangeCompleted));

        var steps = new[]
        {
            profileChanging,
            waitRunCheckpoint,
            drainProfileWriter,
            refreshIdentity,
            rotateRepository,
            openDiagnostics,
            worldTimeProfileChanged,
            () => craftingProfileChangeCompleted(transitionId),
            profileChanged,
            applyCurrentMetricCapabilities,
            writeDiagnostic
        };
        if (steps.Any(step => step == null))
            throw new ArgumentException("User reset transition steps must be non-null.", nameof(profileChanging));

        craftingProfileChangeStarted(transitionId);
        enqueueTransition("User profile reset", steps);
    }
}
