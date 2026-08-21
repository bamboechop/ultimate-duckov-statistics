using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class NativeWorldTimeProfileHandoffBoundary
{
    private const string StagedGenerationId = "world-time-profile-handoff";
    private const string PriorClockGenerationId = "world-time-profile-handoff-prior-clock";
    private readonly NativeWorldTimeObservationBoundary stagedBoundary = new();
    private readonly NativeWorldTimeObservationBoundary priorClockBoundary = new();
    private readonly List<ArchivedHandoff> archivedHandoffs = [];
    private HandoffMode mode;
    private long transitionId;
    private object? priorClockInstance;
    private object? loadedClockInstance;
    private WorldClockReading latestPriorClockReading;
    private bool hasPriorClockReading;
    private WorldClockReading latestStagedReading;
    private bool hasStagedReading;
    private bool targetProfileReady;
    private long currentClockReuseDependencyTransitionId;
    private bool currentClockReuseBlocked;
    private long pendingSleepTransitionId;
    private long pendingSleepAdvancedTicks;
    private bool hasPendingSleep;

    public bool IsActive => mode != HandoffMode.None;

    public bool TryGetActiveTransitionId(out long activeTransitionId)
    {
        activeTransitionId = transitionId;
        return mode != HandoffMode.None;
    }

    public bool BeginSleepCompletion(long activeTransitionId, long advancedTicks)
    {
        if (mode == HandoffMode.None
            || activeTransitionId != transitionId
            || advancedTicks < 0
            || hasPendingSleep
            || archivedHandoffs.Any(handoff => handoff.HasPendingSleep))
            return false;

        pendingSleepTransitionId = activeTransitionId;
        pendingSleepAdvancedTicks = advancedTicks;
        hasPendingSleep = true;
        return true;
    }

    public bool CompleteSleep()
    {
        if (hasPendingSleep
            && mode != HandoffMode.None
            && pendingSleepTransitionId == transitionId)
        {
            if (!stagedBoundary.BeginSleepCompletion(StagedGenerationId, pendingSleepAdvancedTicks)
                || !stagedBoundary.CompleteSleep(StagedGenerationId))
                return false;

            ClearPendingSleep();
            return true;
        }

        for (var index = archivedHandoffs.Count - 1; index >= 0; index--)
        {
            var handoff = archivedHandoffs[index];
            if (!handoff.HasPendingSleep) continue;
            handoff.Mutation = Add(
                handoff.Mutation,
                new WorldTimeMutation(0, 0, 1, handoff.PendingSleepAdvancedTicks));
            handoff.ClearPendingSleep();
            return true;
        }

        return false;
    }

    public void BeginAwaitingNativeLoad(
        long activeTransitionId,
        object? currentClockInstance,
        WorldClockReading? currentClockReading = null)
    {
        Begin(activeTransitionId, HandoffMode.AwaitingNativeLoad);
        priorClockInstance = currentClockInstance;
        if (currentClockReading.HasValue) StagePriorClock(currentClockReading.Value);
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
            if (ReferenceEquals(clockInstance, priorClockInstance))
            {
                StagePriorClock(reading);
                return null;
            }
            loadedClockInstance = clockInstance;
            if (targetProfileReady)
            {
                ResetActiveState();
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
        out WorldTimeObservationResult? completionObservation,
        out bool completedSleepTransferred)
    {
        if (currentBoundary == null) throw new ArgumentNullException(nameof(currentBoundary));
        completionObservation = null;
        completedSleepTransferred = false;

        if (mode != HandoffMode.None && completedTransitionId == transitionId)
        {
            var requiresReplacementClock = mode == HandoffMode.AwaitingNativeLoad && !hasStagedReading;
            var completed = CompleteActiveProfileChange(
                generationId,
                currentBoundary,
                currentReading,
                out completionObservation,
                out completedSleepTransferred);
            ResolveCurrentClockReuseDependency(completedTransitionId, requiresReplacementClock);
            return completed;
        }

        var archivedIndex = archivedHandoffs.FindIndex(handoff => handoff.TransitionId == completedTransitionId);
        if (archivedIndex < 0) return false;
        var archived = archivedHandoffs[archivedIndex];
        currentBoundary.Reset();
        if (!archived.HasStagedReading)
        {
            archivedHandoffs.RemoveAt(archivedIndex);
            ResolveCurrentClockReuseDependency(completedTransitionId, requiresReplacementClock: true);
            return true;
        }

        completionObservation = currentBoundary.ResetAndEstablishBaseline(
            generationId,
            archived.LatestStagedReading);
        currentBoundary.RestorePending(archived.Mutation);
        completedSleepTransferred = archived.Mutation.CompletedSleepSessions != 0;
        TransferPendingSleep(
            archived.HasPendingSleep,
            archived.TransitionId,
            completedTransitionId,
            archived.PendingSleepAdvancedTicks,
            generationId,
            currentBoundary);
        archivedHandoffs.RemoveAt(archivedIndex);
        ResolveCurrentClockReuseDependency(completedTransitionId, requiresReplacementClock: false);
        return true;
    }

    public bool CompleteProfileChangeWithCurrentClock(
        long completedTransitionId,
        string generationId,
        NativeWorldTimeObservationBoundary currentBoundary,
        object? currentClockInstance,
        WorldClockReading? currentReading,
        out WorldTimeObservationResult? completionObservation,
        out bool completedSleepTransferred)
    {
        if (currentBoundary == null) throw new ArgumentNullException(nameof(currentBoundary));
        completionObservation = null;
        completedSleepTransferred = false;

        if (mode != HandoffMode.None && completedTransitionId == transitionId)
        {
            if (loadedClockInstance != null
                && currentClockInstance != null
                && ReferenceEquals(currentClockInstance, loadedClockInstance))
            {
                var completed = CompleteActiveProfileChange(
                    generationId,
                    currentBoundary,
                    currentReading,
                    out completionObservation,
                    out completedSleepTransferred);
                ResolveCurrentClockReuseDependency(completedTransitionId, requiresReplacementClock: false);
                return completed;
            }

            if (!CanReuseCapturedCurrentClock())
            {
                var completed = CompleteActiveProfileChange(
                    generationId,
                    currentBoundary,
                    currentReading: null,
                    out completionObservation,
                    out completedSleepTransferred);
                ResolveCurrentClockReuseDependency(completedTransitionId, requiresReplacementClock: true);
                return completed;
            }

            if (currentReading.HasValue
                && currentClockInstance != null
                && ReferenceEquals(currentClockInstance, priorClockInstance))
                StagePriorClock(currentReading.Value);
            var priorMutation = priorClockBoundary.TakePending();
            var stagedMutation = stagedBoundary.TakePending();
            var combined = Add(priorMutation, stagedMutation);
            completionObservation = RestoreSameClockState(
                generationId,
                currentBoundary,
                latestPriorClockReading,
                hasPriorClockReading,
                combined);
            completedSleepTransferred = combined.CompletedSleepSessions != 0;
            TransferPendingSleep(
                hasPendingSleep,
                pendingSleepTransitionId,
                transitionId,
                pendingSleepAdvancedTicks,
                generationId,
                currentBoundary);
            ResolveCurrentClockReuseDependency(completedTransitionId, requiresReplacementClock: false);
            ResetActiveState();
            return true;
        }

        var archivedIndex = archivedHandoffs.FindIndex(handoff => handoff.TransitionId == completedTransitionId);
        if (archivedIndex < 0) return false;
        var archived = archivedHandoffs[archivedIndex];
        if (!CanReuseCapturedCurrentClock(archived))
        {
            currentBoundary.Reset();
            if (archived.HasStagedReading)
            {
                completionObservation = currentBoundary.ResetAndEstablishBaseline(
                    generationId,
                    archived.LatestStagedReading);
                currentBoundary.RestorePending(archived.Mutation);
                completedSleepTransferred = archived.Mutation.CompletedSleepSessions != 0;
                TransferPendingSleep(
                    archived.HasPendingSleep,
                    archived.TransitionId,
                    completedTransitionId,
                    archived.PendingSleepAdvancedTicks,
                    generationId,
                    currentBoundary);
            }
            archivedHandoffs.RemoveAt(archivedIndex);
            ResolveCurrentClockReuseDependency(
                completedTransitionId,
                requiresReplacementClock: !archived.HasStagedReading);
            return true;
        }

        var archivedCombined = Add(archived.PriorClockMutation, archived.Mutation);
        completionObservation = RestoreSameClockState(
            generationId,
            currentBoundary,
            archived.LatestPriorClockReading,
            archived.HasPriorClockReading,
            archivedCombined);
        completedSleepTransferred = archivedCombined.CompletedSleepSessions != 0;
        TransferPendingSleep(
            archived.HasPendingSleep,
            archived.TransitionId,
            completedTransitionId,
            archived.PendingSleepAdvancedTicks,
            generationId,
            currentBoundary);
        archivedHandoffs.RemoveAt(archivedIndex);
        ResolveCurrentClockReuseDependency(completedTransitionId, requiresReplacementClock: false);
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

        Reset();
        return currentReading.HasValue
            ? currentBoundary.ObserveClock(generationId, currentReading.Value)
            : null;
    }

    public void Reset()
    {
        archivedHandoffs.Clear();
        ResetActiveState();
    }

    private bool CompleteActiveProfileChange(
        string generationId,
        NativeWorldTimeObservationBoundary currentBoundary,
        WorldClockReading? currentReading,
        out WorldTimeObservationResult? completionObservation,
        out bool completedSleepTransferred)
    {
        completionObservation = null;
        completedSleepTransferred = false;
        if (mode == HandoffMode.NewGame && currentReading.HasValue)
            completionObservation = Stage(currentReading.Value);

        currentBoundary.Reset();
        targetProfileReady = true;
        if (!hasStagedReading) return true;

        var mutation = stagedBoundary.TakePending();
        var baselineObservation = currentBoundary.ResetAndEstablishBaseline(generationId, latestStagedReading);
        currentBoundary.RestorePending(mutation);
        completedSleepTransferred = mutation.CompletedSleepSessions != 0;
        TransferPendingSleep(
            hasPendingSleep,
            pendingSleepTransitionId,
            transitionId,
            pendingSleepAdvancedTicks,
            generationId,
            currentBoundary);
        if (!completionObservation.HasValue
            || completionObservation.Value.State is not (WorldTimeObservationState.Invalid
                or WorldTimeObservationState.Backward
                or WorldTimeObservationState.Overflow))
            completionObservation = baselineObservation;
        ResetActiveState();
        return true;
    }

    private void Begin(long activeTransitionId, HandoffMode newMode)
    {
        if (activeTransitionId <= 0)
            throw new ArgumentOutOfRangeException(nameof(activeTransitionId));
        if (activeTransitionId == transitionId
            || archivedHandoffs.Any(handoff => handoff.TransitionId == activeTransitionId))
            throw new InvalidOperationException($"World-time profile transition {activeTransitionId} is already staged.");

        var currentClockReuseDependency = 0L;
        var blockCurrentClockReuse = false;
        if (newMode == HandoffMode.AwaitingNativeLoad
            && mode == HandoffMode.AwaitingNativeLoad
            && loadedClockInstance == null)
        {
            if (targetProfileReady)
                blockCurrentClockReuse = true;
            else
                currentClockReuseDependency = transitionId;
        }

        if (mode != HandoffMode.None && !targetProfileReady)
            ArchiveActiveHandoff();
        ResetActiveState();
        transitionId = activeTransitionId;
        mode = newMode;
        currentClockReuseDependencyTransitionId = currentClockReuseDependency;
        currentClockReuseBlocked = blockCurrentClockReuse;
    }

    private void ArchiveActiveHandoff()
    {
        archivedHandoffs.Add(new ArchivedHandoff(
            transitionId,
            stagedBoundary.TakePending(),
            priorClockBoundary.TakePending(),
            latestStagedReading,
            hasStagedReading,
            latestPriorClockReading,
            hasPriorClockReading,
            currentClockReuseDependencyTransitionId,
            currentClockReuseBlocked,
            pendingSleepAdvancedTicks,
            hasPendingSleep));
    }

    private WorldTimeObservationResult Stage(WorldClockReading reading)
    {
        latestStagedReading = reading;
        hasStagedReading = true;
        return stagedBoundary.ObserveClock(StagedGenerationId, reading);
    }

    private WorldTimeObservationResult StagePriorClock(WorldClockReading reading)
    {
        latestPriorClockReading = reading;
        hasPriorClockReading = true;
        return priorClockBoundary.ObserveClock(PriorClockGenerationId, reading);
    }

    private void ResetActiveState()
    {
        stagedBoundary.Reset();
        priorClockBoundary.Reset();
        mode = HandoffMode.None;
        transitionId = 0;
        priorClockInstance = null;
        loadedClockInstance = null;
        latestPriorClockReading = default;
        hasPriorClockReading = false;
        latestStagedReading = default;
        hasStagedReading = false;
        targetProfileReady = false;
        currentClockReuseDependencyTransitionId = 0;
        currentClockReuseBlocked = false;
        ClearPendingSleep();
    }

    private bool CanReuseCapturedCurrentClock() =>
        currentClockReuseDependencyTransitionId == 0 && !currentClockReuseBlocked;

    private static bool CanReuseCapturedCurrentClock(ArchivedHandoff handoff) =>
        handoff.CurrentClockReuseDependencyTransitionId == 0 && !handoff.CurrentClockReuseBlocked;

    private void ResolveCurrentClockReuseDependency(long completedTransitionId, bool requiresReplacementClock)
    {
        if (currentClockReuseDependencyTransitionId == completedTransitionId)
        {
            currentClockReuseDependencyTransitionId = 0;
            currentClockReuseBlocked |= requiresReplacementClock;
        }

        foreach (var handoff in archivedHandoffs)
        {
            if (handoff.CurrentClockReuseDependencyTransitionId != completedTransitionId) continue;
            handoff.CurrentClockReuseDependencyTransitionId = 0;
            handoff.CurrentClockReuseBlocked |= requiresReplacementClock;
        }
    }

    private void ClearPendingSleep()
    {
        pendingSleepTransitionId = 0;
        pendingSleepAdvancedTicks = 0;
        hasPendingSleep = false;
    }

    private static void TransferPendingSleep(
        bool candidatePending,
        long candidateTransitionId,
        long expectedTransitionId,
        long advancedTicks,
        string generationId,
        NativeWorldTimeObservationBoundary currentBoundary)
    {
        if (!candidatePending) return;
        if (candidateTransitionId != expectedTransitionId
            || !currentBoundary.BeginSleepCompletion(generationId, advancedTicks))
            throw new InvalidOperationException(
                "The staged native sleep candidate could not be transferred to the committed profile generation.");
    }

    private static WorldTimeObservationResult? RestoreSameClockState(
        string generationId,
        NativeWorldTimeObservationBoundary currentBoundary,
        WorldClockReading latestReading,
        bool hasReading,
        WorldTimeMutation mutation)
    {
        var retained = currentBoundary.TakePending();
        if (!hasReading)
        {
            currentBoundary.RestorePending(Add(retained, mutation));
            return null;
        }

        var observation = currentBoundary.ResetAndEstablishBaseline(generationId, latestReading);
        currentBoundary.RestorePending(Add(retained, mutation));
        return observation;
    }

    private static WorldTimeMutation Add(WorldTimeMutation left, WorldTimeMutation right) => new(
        checked(left.CalendarDaysAdvanced + right.CalendarDaysAdvanced),
        checked(left.ObservedGameTimeTicks + right.ObservedGameTimeTicks),
        checked(left.CompletedSleepSessions + right.CompletedSleepSessions),
        checked(left.SleepAdvancedTimeTicks + right.SleepAdvancedTimeTicks));

    private sealed class ArchivedHandoff
    {
        public ArchivedHandoff(
            long transitionId,
            WorldTimeMutation mutation,
            WorldTimeMutation priorClockMutation,
            WorldClockReading latestStagedReading,
            bool hasStagedReading,
            WorldClockReading latestPriorClockReading,
            bool hasPriorClockReading,
            long currentClockReuseDependencyTransitionId,
            bool currentClockReuseBlocked,
            long pendingSleepAdvancedTicks,
            bool hasPendingSleep)
        {
            TransitionId = transitionId;
            Mutation = mutation;
            PriorClockMutation = priorClockMutation;
            LatestStagedReading = latestStagedReading;
            HasStagedReading = hasStagedReading;
            LatestPriorClockReading = latestPriorClockReading;
            HasPriorClockReading = hasPriorClockReading;
            CurrentClockReuseDependencyTransitionId = currentClockReuseDependencyTransitionId;
            CurrentClockReuseBlocked = currentClockReuseBlocked;
            PendingSleepAdvancedTicks = pendingSleepAdvancedTicks;
            HasPendingSleep = hasPendingSleep;
        }

        public long TransitionId { get; }
        public WorldTimeMutation Mutation { get; set; }
        public WorldTimeMutation PriorClockMutation { get; }
        public WorldClockReading LatestStagedReading { get; }
        public bool HasStagedReading { get; }
        public WorldClockReading LatestPriorClockReading { get; }
        public bool HasPriorClockReading { get; }
        public long CurrentClockReuseDependencyTransitionId { get; set; }
        public bool CurrentClockReuseBlocked { get; set; }
        public long PendingSleepAdvancedTicks { get; }
        public bool HasPendingSleep { get; private set; }

        public void ClearPendingSleep() => HasPendingSleep = false;
    }

    private enum HandoffMode
    {
        None,
        AwaitingNativeLoad,
        NewGame
    }
}
