using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class NativeWorldTimeProfileHandoffBoundary
{
    private const string StagedGenerationId = "world-time-profile-handoff";
    private readonly NativeWorldTimeObservationBoundary stagedBoundary = new();
    private HandoffMode mode;
    private long transitionId;
    private object? priorClockInstance;
    private object? loadedClockInstance;
    private WorldClockReading latestStagedReading;
    private bool hasStagedReading;
    private bool targetProfileReady;

    public bool IsActive => mode != HandoffMode.None;

    public void BeginAwaitingNativeLoad(long activeTransitionId, object? currentClockInstance)
    {
        Begin(activeTransitionId, HandoffMode.AwaitingNativeLoad);
        priorClockInstance = currentClockInstance;
    }

    public WorldTimeObservationResult BeginNewGame(
        long activeTransitionId,
        WorldClockReading loadedReading)
    {
        Begin(activeTransitionId, HandoffMode.NewGame);
        return Stage(loadedReading);
    }

    public WorldTimeObservationResult? Observe(
        string generationId,
        object clockInstance,
        WorldClockReading reading,
        NativeWorldTimeObservationBoundary currentBoundary)
    {
        if (clockInstance == null) throw new ArgumentNullException(nameof(clockInstance));
        if (currentBoundary == null) throw new ArgumentNullException(nameof(currentBoundary));

        if (mode == HandoffMode.NewGame)
            return Stage(reading);
        if (mode != HandoffMode.AwaitingNativeLoad)
            return currentBoundary.ObserveClock(generationId, reading);

        if (loadedClockInstance == null)
        {
            if (ReferenceEquals(clockInstance, priorClockInstance)) return null;
            loadedClockInstance = clockInstance;
            if (targetProfileReady)
            {
                ResetState();
                return currentBoundary.ObserveClock(generationId, reading);
            }
            return Stage(reading);
        }

        if (!ReferenceEquals(clockInstance, loadedClockInstance)) return null;
        return Stage(reading);
    }

    public bool CompleteProfileChange(
        long completedTransitionId,
        string generationId,
        NativeWorldTimeObservationBoundary currentBoundary,
        WorldClockReading? currentReading,
        out WorldTimeObservationResult? completionObservation)
    {
        if (currentBoundary == null) throw new ArgumentNullException(nameof(currentBoundary));
        completionObservation = null;
        if (mode == HandoffMode.None || completedTransitionId != transitionId) return false;

        if (mode == HandoffMode.NewGame && currentReading.HasValue)
            completionObservation = Stage(currentReading.Value);

        currentBoundary.Reset();
        targetProfileReady = true;
        if (!hasStagedReading) return true;

        var mutation = stagedBoundary.TakePending();
        var baselineObservation = currentBoundary.ResetAndEstablishBaseline(generationId, latestStagedReading);
        currentBoundary.RestorePending(mutation);
        if (!completionObservation.HasValue
            || completionObservation.Value.State is not (WorldTimeObservationState.Invalid
                or WorldTimeObservationState.Backward
                or WorldTimeObservationState.Overflow))
            completionObservation = baselineObservation;
        ResetState();
        return true;
    }

    public WorldTimeObservationResult? ResetCurrentProfile(
        string generationId,
        WorldClockReading? currentReading,
        NativeWorldTimeObservationBoundary currentBoundary,
        out bool awaitingNativeLoad)
    {
        if (currentBoundary == null) throw new ArgumentNullException(nameof(currentBoundary));
        awaitingNativeLoad = mode == HandoffMode.AwaitingNativeLoad;
        currentBoundary.Reset();
        if (awaitingNativeLoad) return null;

        ResetState();
        return currentReading.HasValue
            ? currentBoundary.ObserveClock(generationId, currentReading.Value)
            : null;
    }

    public void Reset()
    {
        ResetState();
    }

    private void Begin(long activeTransitionId, HandoffMode newMode)
    {
        if (activeTransitionId <= 0)
            throw new ArgumentOutOfRangeException(nameof(activeTransitionId));
        ResetState();
        transitionId = activeTransitionId;
        mode = newMode;
    }

    private WorldTimeObservationResult Stage(WorldClockReading reading)
    {
        latestStagedReading = reading;
        hasStagedReading = true;
        return stagedBoundary.ObserveClock(StagedGenerationId, reading);
    }

    private void ResetState()
    {
        stagedBoundary.Reset();
        mode = HandoffMode.None;
        transitionId = 0;
        priorClockInstance = null;
        loadedClockInstance = null;
        latestStagedReading = default;
        hasStagedReading = false;
        targetProfileReady = false;
    }

    private enum HandoffMode
    {
        None,
        AwaitingNativeLoad,
        NewGame
    }
}
