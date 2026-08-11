using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Tracking;

public enum RunLifecycleEventKind
{
    RaidInitialized,
    ControlReady,
    PauseStarted,
    PauseEnded,
    LoadingStarted,
    LoadingEnded,
    Extracted,
    Died,
    Interrupted,
    RaidCleared
}

public sealed class RunStartContext
{
    public string SaveGenerationId { get; set; } = string.Empty;

    public string? NativeRaidId { get; set; }

    public MapIdentity Map { get; set; } = new();

    public IntegrityTags IntegrityTags { get; set; }

    public string GameVersion { get; set; } = string.Empty;

    public string GameBuild { get; set; } = string.Empty;

    public AdapterCapabilityState LifecycleCapability { get; set; }

    public string LifecycleAdapterVersion { get; set; } = string.Empty;

    public AdapterCapabilityState MovementCapability { get; set; }

    public string MovementAdapterVersion { get; set; } = string.Empty;

    public AdapterCapabilityState MapCapability { get; set; }

    public string MapAdapterVersion { get; set; } = string.Empty;

    public WeaponMetricCapabilities WeaponCapabilities { get; set; } = new();
}

public sealed class RunLifecycleEvent
{
    public RunLifecycleEventKind Kind { get; set; }

    public DateTime TimestampUtc { get; set; }

    public double MonotonicSeconds { get; set; }

    public string? NativeRaidId { get; set; }

    public RunStartContext? StartContext { get; set; }
}

public sealed class RunLifecycleTransition
{
    public bool Started { get; internal set; }

    public bool StateChanged { get; internal set; }

    public bool CheckpointRequired { get; internal set; }

    public RunSummary? Completed { get; internal set; }
}

public sealed class RunLifecycleTracker
{
    public const double DefaultCheckpointIntervalSeconds = 5;
    private readonly Func<string> runIdFactory;
    private readonly double checkpointIntervalSeconds;
    private readonly HashSet<SuspensionReason> suspensions = new();
    private readonly MovementAccumulator movement;
    private ActiveState? active;
    private bool raidInitialized;
    private string? nativeRaidId;
    private double lastCheckpointMonotonicSeconds;

    public RunLifecycleTracker(
        Func<string> runIdFactory,
        double checkpointIntervalSeconds = DefaultCheckpointIntervalSeconds,
        MovementAccumulator? movement = null)
    {
        this.runIdFactory = runIdFactory ?? throw new ArgumentNullException(nameof(runIdFactory));
        if (checkpointIntervalSeconds <= 0
            || double.IsNaN(checkpointIntervalSeconds)
            || double.IsInfinity(checkpointIntervalSeconds))
        {
            throw new ArgumentOutOfRangeException(nameof(checkpointIntervalSeconds));
        }

        this.checkpointIntervalSeconds = checkpointIntervalSeconds;
        this.movement = movement ?? new MovementAccumulator();
    }

    public bool IsActive => active != null;

    public bool IsSuspended => suspensions.Count > 0;

    public string? ActiveRunId => active?.RunId;

    public string? ActiveMapId => active?.Context.Map.MapId;

    public RunLifecycleTransition Apply(RunLifecycleEvent lifecycleEvent)
    {
        if (lifecycleEvent == null)
        {
            throw new ArgumentNullException(nameof(lifecycleEvent));
        }

        ValidateClock(lifecycleEvent);
        var transition = new RunLifecycleTransition();
        switch (lifecycleEvent.Kind)
        {
            case RunLifecycleEventKind.RaidInitialized:
                raidInitialized = true;
                nativeRaidId = lifecycleEvent.NativeRaidId;
                transition.StateChanged = true;
                break;
            case RunLifecycleEventKind.ControlReady:
                if (active == null
                    && raidInitialized
                    && suspensions.Count == 0
                    && lifecycleEvent.StartContext != null)
                {
                    Start(lifecycleEvent.StartContext, lifecycleEvent.TimestampUtc, lifecycleEvent.MonotonicSeconds);
                    transition.Started = true;
                    transition.StateChanged = true;
                    transition.CheckpointRequired = true;
                }
                break;
            case RunLifecycleEventKind.PauseStarted:
                SetSuspension(SuspensionReason.Pause, active: true, lifecycleEvent, transition);
                break;
            case RunLifecycleEventKind.PauseEnded:
                SetSuspension(SuspensionReason.Pause, active: false, lifecycleEvent, transition);
                break;
            case RunLifecycleEventKind.LoadingStarted:
                SetSuspension(SuspensionReason.Loading, active: true, lifecycleEvent, transition);
                break;
            case RunLifecycleEventKind.LoadingEnded:
                SetSuspension(SuspensionReason.Loading, active: false, lifecycleEvent, transition);
                break;
            case RunLifecycleEventKind.Extracted:
                transition.Completed = Complete(RunOutcome.Extracted, lifecycleEvent);
                transition.StateChanged = transition.Completed != null;
                transition.CheckpointRequired = transition.StateChanged;
                break;
            case RunLifecycleEventKind.Died:
                transition.Completed = Complete(RunOutcome.Died, lifecycleEvent);
                transition.StateChanged = transition.Completed != null;
                transition.CheckpointRequired = transition.StateChanged;
                break;
            case RunLifecycleEventKind.Interrupted:
                transition.Completed = Complete(RunOutcome.Interrupted, lifecycleEvent);
                transition.StateChanged = transition.Completed != null;
                transition.CheckpointRequired = transition.StateChanged;
                break;
            case RunLifecycleEventKind.RaidCleared:
                if (active == null)
                {
                    raidInitialized = false;
                    nativeRaidId = null;
                    transition.StateChanged = true;
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(lifecycleEvent));
        }

        return transition;
    }

    public bool Tick(DateTime timestampUtc, double monotonicSeconds)
    {
        if (active == null)
        {
            return false;
        }

        Advance(timestampUtc, monotonicSeconds);
        return monotonicSeconds - lastCheckpointMonotonicSeconds >= checkpointIntervalSeconds;
    }

    public MovementObservationResult ObserveMovement(
        Position3D position,
        double monotonicSeconds,
        double maximumPlausibleSpeed,
        MovementObservationKind kind = MovementObservationKind.Regular)
    {
        if (active == null)
        {
            return new MovementObservationResult(MovementDisposition.InvalidIgnored, 0, 0);
        }

        return movement.Observe(position, monotonicSeconds, maximumPlausibleSpeed, kind);
    }

    public bool ObserveIntegrity(IntegrityTags integrityTags)
    {
        if (active == null)
        {
            return false;
        }

        var accumulated = RunIntegrityPolicy.Accumulate(active.Context.IntegrityTags, integrityTags);
        if (accumulated == active.Context.IntegrityTags)
        {
            return false;
        }

        active.Context.IntegrityTags = accumulated;
        return true;
    }

    public bool RecordShot(ShotRecorded shot)
    {
        if (active == null || shot == null)
        {
            return false;
        }

        if (!string.Equals(active.Context.SaveGenerationId, shot.SaveGenerationId, StringComparison.Ordinal)
            || !string.Equals(active.RunId, shot.RunId, StringComparison.Ordinal)
            || !string.Equals(active.Context.Map.MapId, shot.MapId, StringComparison.Ordinal))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(shot.EventId)
            || !active.RecentShotEventIds.Add(shot.EventId))
        {
            return false;
        }

        try
        {
            WeaponStatisticsReducer.Apply(active.WeaponStatistics, shot);
            active.Context.IntegrityTags = RunIntegrityPolicy.Accumulate(
                active.Context.IntegrityTags,
                shot.IntegrityTags);
        }
        catch
        {
            active.RecentShotEventIds.Remove(shot.EventId);
            throw;
        }

        active.RecentShotEventIdOrder.Enqueue(shot.EventId);
        while (active.RecentShotEventIdOrder.Count > 512)
        {
            active.RecentShotEventIds.Remove(active.RecentShotEventIdOrder.Dequeue());
        }

        return true;
    }

    public ActiveRunCheckpoint? CreateCheckpoint(DateTime timestampUtc, double monotonicSeconds)
    {
        if (active == null)
        {
            return null;
        }

        Advance(timestampUtc, monotonicSeconds);
        return new ActiveRunCheckpoint
        {
            RunId = active.RunId,
            SaveGenerationId = active.Context.SaveGenerationId,
            NativeRaidId = active.Context.NativeRaidId,
            MapId = active.Context.Map.MapId,
            MapDisplayName = active.Context.Map.DisplayName,
            MapKnown = active.Context.Map.IsKnown,
            StartedUtc = active.StartedUtc,
            LastObservedUtc = active.LastObservedUtc,
            ActiveDurationSeconds = active.ActiveDurationSeconds,
            PhysicalDistance = movement.PhysicalDistance,
            TeleportDistance = movement.TeleportDistance,
            IntegrityTags = active.Context.IntegrityTags,
            GameVersion = active.Context.GameVersion,
            GameBuild = active.Context.GameBuild,
            LifecycleCapability = active.Context.LifecycleCapability,
            LifecycleAdapterVersion = active.Context.LifecycleAdapterVersion,
            MovementCapability = active.Context.MovementCapability,
            MovementAdapterVersion = active.Context.MovementAdapterVersion,
            MapCapability = active.Context.MapCapability,
            MapAdapterVersion = active.Context.MapAdapterVersion,
            WeaponStatistics = WeaponStatisticsReducer.Clone(active.WeaponStatistics)
        };
    }

    public void MarkCheckpointSaved(double monotonicSeconds)
    {
        if (!double.IsNaN(monotonicSeconds) && !double.IsInfinity(monotonicSeconds))
        {
            lastCheckpointMonotonicSeconds = monotonicSeconds;
        }
    }

    public void DisableMovement()
    {
        if (active != null)
        {
            active.Context.MovementCapability = AdapterCapabilityState.DisabledIncompatible;
        }
    }

    private void Start(RunStartContext context, DateTime timestampUtc, double monotonicSeconds)
    {
        ValidateContext(context);
        var runId = runIdFactory();
        if (string.IsNullOrWhiteSpace(runId))
        {
            throw new InvalidOperationException("Run ID factory returned an empty value.");
        }

        context.NativeRaidId ??= nativeRaidId;
        timestampUtc = EnsureUtc(timestampUtc);
        active = new ActiveState(runId, context, timestampUtc, monotonicSeconds);
        suspensions.Clear();
        movement.Reset();
        lastCheckpointMonotonicSeconds = monotonicSeconds;
    }

    private void SetSuspension(
        SuspensionReason reason,
        bool active,
        RunLifecycleEvent lifecycleEvent,
        RunLifecycleTransition transition)
    {
        var alreadySet = suspensions.Contains(reason);
        if (alreadySet == active)
        {
            return;
        }

        if (this.active != null)
        {
            Advance(lifecycleEvent.TimestampUtc, lifecycleEvent.MonotonicSeconds);
        }
        if (active)
        {
            suspensions.Add(reason);
        }
        else
        {
            suspensions.Remove(reason);
        }

        transition.StateChanged = true;
        transition.CheckpointRequired = this.active != null;
    }

    private RunSummary? Complete(RunOutcome outcome, RunLifecycleEvent lifecycleEvent)
    {
        if (active == null)
        {
            return null;
        }

        Advance(lifecycleEvent.TimestampUtc, lifecycleEvent.MonotonicSeconds);
        var state = active;
        var endedUtc = EnsureUtc(lifecycleEvent.TimestampUtc);
        var recordEligible = outcome != RunOutcome.Interrupted
                             && state.Context.IntegrityTags == IntegrityTags.Normal
                             && state.Context.LifecycleCapability == AdapterCapabilityState.Supported;
        var summary = new RunSummary
        {
            RunId = state.RunId,
            SaveGenerationId = state.Context.SaveGenerationId,
            NativeRaidId = state.Context.NativeRaidId,
            MapId = state.Context.Map.MapId,
            MapDisplayName = state.Context.Map.DisplayName,
            MapKnown = state.Context.Map.IsKnown,
            StartedUtc = state.StartedUtc,
            EndedUtc = endedUtc < state.StartedUtc ? state.StartedUtc : endedUtc,
            ActiveDurationSeconds = state.ActiveDurationSeconds,
            WallClockDurationSeconds = Math.Max(0, (endedUtc - state.StartedUtc).TotalSeconds),
            Outcome = outcome,
            PhysicalDistance = movement.PhysicalDistance,
            TeleportDistance = movement.TeleportDistance,
            IntegrityTags = state.Context.IntegrityTags,
            RecordEligible = recordEligible,
            GameVersion = state.Context.GameVersion,
            GameBuild = state.Context.GameBuild,
            LifecycleCapability = state.Context.LifecycleCapability,
            LifecycleAdapterVersion = state.Context.LifecycleAdapterVersion,
            MovementCapability = state.Context.MovementCapability,
            MovementAdapterVersion = state.Context.MovementAdapterVersion,
            MapCapability = state.Context.MapCapability,
            MapAdapterVersion = state.Context.MapAdapterVersion,
            WeaponStatistics = WeaponStatisticsReducer.Clone(state.WeaponStatistics)
        };

        active = null;
        movement.Reset();
        raidInitialized = false;
        nativeRaidId = null;
        return summary;
    }

    private void Advance(DateTime timestampUtc, double monotonicSeconds)
    {
        if (active == null || monotonicSeconds <= active.LastMonotonicSeconds)
        {
            return;
        }

        if (suspensions.Count == 0)
        {
            active.ActiveDurationSeconds += monotonicSeconds - active.LastMonotonicSeconds;
        }

        active.LastMonotonicSeconds = monotonicSeconds;
        active.LastObservedUtc = EnsureUtc(timestampUtc);
    }

    private static void ValidateClock(RunLifecycleEvent lifecycleEvent)
    {
        if (double.IsNaN(lifecycleEvent.MonotonicSeconds)
            || double.IsInfinity(lifecycleEvent.MonotonicSeconds)
            || lifecycleEvent.MonotonicSeconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(lifecycleEvent), "Monotonic time must be finite and non-negative.");
        }

        lifecycleEvent.TimestampUtc = EnsureUtc(lifecycleEvent.TimestampUtc);
    }

    private static void ValidateContext(RunStartContext context)
    {
        if (string.IsNullOrWhiteSpace(context.SaveGenerationId)
            || context.Map == null
            || string.IsNullOrWhiteSpace(context.Map.MapId)
            || string.IsNullOrWhiteSpace(context.Map.DisplayName))
        {
            throw new ArgumentException("Run start context is incomplete.", nameof(context));
        }
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };

    private enum SuspensionReason
    {
        Pause,
        Loading
    }

    private sealed class ActiveState
    {
        public ActiveState(string runId, RunStartContext context, DateTime startedUtc, double startedMonotonicSeconds)
        {
            RunId = runId;
            Context = context;
            StartedUtc = startedUtc;
            LastObservedUtc = startedUtc;
            LastMonotonicSeconds = startedMonotonicSeconds;
            WeaponStatistics.Capabilities = WeaponStatisticsReducer.CloneCapabilities(context.WeaponCapabilities);
        }

        public string RunId { get; }

        public RunStartContext Context { get; }

        public DateTime StartedUtc { get; }

        public DateTime LastObservedUtc { get; set; }

        public double LastMonotonicSeconds { get; set; }

        public double ActiveDurationSeconds { get; set; }

        public WeaponStatisticsAggregate WeaponStatistics { get; } = new();

        public HashSet<string> RecentShotEventIds { get; } = new(StringComparer.Ordinal);

        public Queue<string> RecentShotEventIdOrder { get; } = new();
    }
}
