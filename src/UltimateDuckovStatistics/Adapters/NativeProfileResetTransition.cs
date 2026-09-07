using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Adapters;

internal enum NativeUserResetOutcome { Pending, Success, Failure }

internal sealed class NativeUserResetAttempt
{
    internal NativeUserResetAttempt(long transitionId, string requestedGenerationId,
        NativeUserResetOutcome outcome, string completedGenerationId, string detail)
    {
        TransitionId = transitionId;
        RequestedGenerationId = requestedGenerationId;
        Outcome = outcome;
        CompletedGenerationId = completedGenerationId;
        Detail = detail;
    }

    public long TransitionId { get; }
    public string RequestedGenerationId { get; }
    public NativeUserResetOutcome Outcome { get; }
    public string CompletedGenerationId { get; }
    public string Detail { get; }
}

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
        Action writeDiagnostic,
        Action<UserProfileResetFailedException>? resetFailed = null)
    {
        if (transitionId <= 0) throw new ArgumentOutOfRangeException(nameof(transitionId));
        if (craftingProfileChangeStarted == null)
            throw new ArgumentNullException(nameof(craftingProfileChangeStarted));
        if (enqueueTransition == null) throw new ArgumentNullException(nameof(enqueueTransition));
        if (craftingProfileChangeCompleted == null)
            throw new ArgumentNullException(nameof(craftingProfileChangeCompleted));

        UserProfileResetFailedException? failure = null;
        var required = new[]
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
        if (required.Any(step => step == null))
            throw new ArgumentException("User reset transition steps must be non-null.", nameof(profileChanging));

        void BeforeCommit(Action action)
        {
            if (failure != null) return;
            try { action(); }
            catch (UserProfileResetFailedException exception) when (resetFailed != null)
            { failure = exception; }
        }
        void AfterCommit(Action action)
        {
            if (failure == null) action();
        }
        var steps = new Action[]
        {
            profileChanging, waitRunCheckpoint,
            () => BeforeCommit(drainProfileWriter),
            () => BeforeCommit(refreshIdentity),
            () => BeforeCommit(rotateRepository),
            () => AfterCommit(openDiagnostics),
            () => AfterCommit(worldTimeProfileChanged),
            // Every accepted handoff must finish against either the committed
            // generation or the preserved original; a failed reset cannot drop
            // events staged while its persistence boundary was pending.
            () => craftingProfileChangeCompleted(transitionId),
            profileChanged,
            () => AfterCommit(applyCurrentMetricCapabilities),
            () =>
            {
                if (failure == null) writeDiagnostic();
                else resetFailed!(failure);
            }
        };

        craftingProfileChangeStarted(transitionId);
        enqueueTransition("User profile reset", steps);
    }
}
