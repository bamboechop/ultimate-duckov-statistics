using System.Diagnostics;
using System.Globalization;
using Duckov;
using Duckov.Rules;
using Duckov.Scenes;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class NativeRunLifecycleAdapter : IDisposable, IRetryableCleanup
{
    internal const string LifecycleAdapterId = "native-run-lifecycle";
    internal const string LifecycleAdapterVersion = "native-run-lifecycle/2.3.30";
    internal const string MovementAdapterId = "native-main-duck-movement";
    internal const string MovementAdapterVersion = "native-main-duck-movement/2.3.30";
    internal const string MapAdapterId = "native-map-identity";
    internal const string MapAdapterVersion = "native-map-identity/2.3.30";
    internal const string RouteAdapterId = "native-multi-map-route";
    internal const string RouteAdapterVersion = "native-multi-map-route/2.3.30";
    private const string SupportedGameVersion = "2.3.30";
    private const string SupportedGameBuild = "24013657";
    private const double SampleIntervalSeconds = 0.2;
    private const double CheckpointRetryIntervalSeconds = 1;
    private const double CombatCheckpointIntervalSeconds = 1;
    private readonly Func<string> saveGenerationIdProvider;
    private readonly Func<ActiveRunCheckpoint, bool> checkpointHandler;
    private readonly Func<RunSummary, bool> completionHandler;
    private readonly Action<IReadOnlyList<CapabilityRecord>> capabilityHandler;
    private readonly Action<string> diagnosticHandler;
    private readonly Func<WeaponMetricCapabilities> weaponCapabilitiesProvider;
    private readonly Func<CombatMetricCapabilities> combatCapabilitiesProvider;
    private readonly Func<EquipmentMetricCapabilities> equipmentCapabilitiesProvider;
    private readonly Func<ContainerMetricCapabilities> containerCapabilitiesProvider;
    private readonly Func<EconomyMetricCapabilities> economyCapabilitiesProvider;
    private readonly Func<DeferredWriteState>? checkpointCompletionPoller;
    private readonly Func<DeferredWriteState>? checkpointCompletionFlusher;
    private readonly Stopwatch monotonicClock = Stopwatch.StartNew();
    private readonly RunLifecycleTracker tracker;
    private readonly MonotonicCadenceGate sampleCadence = new(SampleIntervalSeconds);
    private readonly ActiveRunCheckpointScheduler checkpointScheduler = new(
        CombatCheckpointIntervalSeconds,
        CheckpointRetryIntervalSeconds);
    private readonly ReferenceSubjectGate<CharacterMainControl> mainCharacterGate = new();
    private readonly NativeCallbackLifetime callbackLifetime = new();
    private readonly DeathObservationGate deathObservationGate = new();
    private readonly NativeRunTerminalBoundary terminalBoundary = new();
    private readonly List<CapabilityRecord> capabilities = new();
    private CharacterMainControl? mainCharacter;
    private bool paused;
    private bool loading;
    private MovementObservationKind? pendingBoundary;
    private string? movementMapId;
    private Action<DamageInfo>? playerDeathObserver;
    private bool pendingDeathTerminal;
    private bool routeTransitionPending;
    private bool destinationPlacementObserved;
    private Action? destinationReadyObserver;
    private bool checkpointWritePending;
    private double pendingCheckpointMonotonicSeconds;
    private long pendingCheckpointMutationRevision;

    public NativeRunLifecycleAdapter(
        Func<string> saveGenerationIdProvider,
        Func<ActiveRunCheckpoint, bool> checkpointHandler,
        Func<RunSummary, bool> completionHandler,
        Action<IReadOnlyList<CapabilityRecord>> capabilityHandler,
        Action<string> diagnosticHandler,
        Func<WeaponMetricCapabilities>? weaponCapabilitiesProvider = null,
        Func<CombatMetricCapabilities>? combatCapabilitiesProvider = null,
        Func<EquipmentMetricCapabilities>? equipmentCapabilitiesProvider = null,
        Func<ContainerMetricCapabilities>? containerCapabilitiesProvider = null,
        Func<EconomyMetricCapabilities>? economyCapabilitiesProvider = null,
        Func<DeferredWriteState>? checkpointCompletionPoller = null,
        Func<DeferredWriteState>? checkpointCompletionFlusher = null)
    {
        this.saveGenerationIdProvider = saveGenerationIdProvider
            ?? throw new ArgumentNullException(nameof(saveGenerationIdProvider));
        this.checkpointHandler = checkpointHandler ?? throw new ArgumentNullException(nameof(checkpointHandler));
        this.completionHandler = completionHandler ?? throw new ArgumentNullException(nameof(completionHandler));
        this.capabilityHandler = capabilityHandler ?? throw new ArgumentNullException(nameof(capabilityHandler));
        this.diagnosticHandler = diagnosticHandler ?? throw new ArgumentNullException(nameof(diagnosticHandler));
        this.weaponCapabilitiesProvider = weaponCapabilitiesProvider ?? (() => new WeaponMetricCapabilities());
        this.combatCapabilitiesProvider = combatCapabilitiesProvider ?? (() => new CombatMetricCapabilities());
        this.equipmentCapabilitiesProvider = equipmentCapabilitiesProvider ?? (() => new EquipmentMetricCapabilities());
        this.containerCapabilitiesProvider = containerCapabilitiesProvider ?? (() => new ContainerMetricCapabilities());
        this.economyCapabilitiesProvider = economyCapabilitiesProvider ?? (() => new EconomyMetricCapabilities());
        this.checkpointCompletionPoller = checkpointCompletionPoller;
        this.checkpointCompletionFlusher = checkpointCompletionFlusher;
        if ((checkpointCompletionPoller == null) != (checkpointCompletionFlusher == null))
            throw new ArgumentException("Deferred checkpoint polling and flushing must be configured together.");
        tracker = new RunLifecycleTracker(() => Guid.NewGuid().ToString("N"));
        SetAllCapabilities(
            AdapterCapabilityState.DisabledIncompatible,
            "Run lifecycle and movement have not been initialized.");
    }

    public bool IsActive => tracker.IsActive;

    public string? CurrentRunId => tracker.ActiveRunId;

    public string? CurrentMapId => tracker.ActiveMapId;

    public string? CurrentSegmentId => tracker.ActiveSegmentId;

    public EventAttributionContext? CurrentEventContext => tracker.ActiveEventContext;

    public bool HasUncheckpointedRunMutations => checkpointWritePending || tracker.CombatCheckpointRequired;

    public bool RecordShot(ShotRecorded shot)
    {
        var recorded = callbackLifetime.CanHandleCallbacks && tracker.RecordShot(shot);
        if (recorded) NativeHotPathDiagnostics.CountTrackerShotMutation();
        return recorded;
    }

    public bool RecordCombat(CombatRecorded value)
    {
        var recorded = callbackLifetime.CanHandleCallbacks && tracker.RecordCombat(value);
        if (recorded) NativeHotPathDiagnostics.CountTrackerCombatMutation();
        return recorded;
    }

    public bool RecordContainer(ContainerLooted value) =>
        callbackLifetime.CanHandleCallbacks && tracker.RecordContainer(value);

    public bool RecordItemUse(ItemUseRecorded value) =>
        callbackLifetime.CanHandleCallbacks && tracker.RecordItemUse(value);

    public bool RecordHealing(HealingApplied value) =>
        callbackLifetime.CanHandleCallbacks && tracker.RecordHealing(value);

    public bool RecordCurrencyFlow(CurrencyFlowRecorded value) =>
        callbackLifetime.CanHandleCallbacks && tracker.RecordCurrencyFlow(value);

    public bool UpdateCombatCapabilities(CombatMetricCapabilities capabilities) =>
        callbackLifetime.CanHandleCallbacks && tracker.UpdateCombatCapabilities(capabilities);

    public bool UpdateContainerCapabilities(ContainerMetricCapabilities capabilities) =>
        callbackLifetime.CanHandleCallbacks && tracker.UpdateContainerCapabilities(capabilities);

    public bool UpdateEconomyCapabilities(EconomyMetricCapabilities capabilities) =>
        callbackLifetime.CanHandleCallbacks && tracker.UpdateEconomyCapabilities(capabilities);

    public bool ObserveEquipment(EquipmentSnapshot snapshot) =>
        callbackLifetime.CanHandleCallbacks
        && tracker.ObserveEquipment(snapshot, DateTime.UtcNow, NowMonotonic());

    public bool InvalidateEquipmentObservation() =>
        callbackLifetime.CanHandleCallbacks
        && tracker.SuspendEquipment(DateTime.UtcNow, NowMonotonic());

    public bool FlushCheckpoint()
    {
        if (!tracker.IsActive) return DrainPendingCheckpoint();
        return SaveCheckpoint(DateTime.UtcNow, NowMonotonic(), awaitPersistence: true);
    }

    public void SetPlayerDeathObserver(Action<DamageInfo>? observer) => playerDeathObserver = observer;

    public void SetDestinationReadyObserver(Action? observer) => destinationReadyObserver = observer;

    public void SetTerminalObserver(Action? observer) => terminalBoundary.SetTerminalObserver(observer);

    public IReadOnlyList<CapabilityRecord> Initialize()
    {
        if (callbackLifetime.DisposalStarted)
        {
            throw new ObjectDisposedException(nameof(NativeRunLifecycleAdapter));
        }

        if (callbackLifetime.IsActive)
        {
            return capabilities;
        }

        var gameVersion = Application.version ?? string.Empty;
        if (!RunCapabilityPolicy.IsSupportedGameVersion(gameVersion, SupportedGameVersion))
        {
            SetAllCapabilities(
                AdapterCapabilityState.DisabledIncompatible,
                $"Installed Duckov version '{gameVersion}' does not match verified version '{SupportedGameVersion}'.");
            diagnosticHandler(capabilities[0].Detail!);
            return capabilities;
        }

        try
        {
            callbackLifetime.Activate(CreateSubscriptions());
            SetAllCapabilities(
                AdapterCapabilityState.Supported,
                "Verified Duckov 2.3.30 public lifecycle, map, main-duck position, and movement-speed contracts.");
            SynchronizeRaidInitialization();
            SynchronizeNativeStates();
            diagnosticHandler("Native run-lifecycle and main-duck movement hooks subscribed; sampling interval is 0.2 seconds.");
        }
        catch (Exception exception)
        {
            TryCleanup();
            SetAllCapabilities(
                AdapterCapabilityState.DisabledIncompatible,
                $"Run-lifecycle activation failed: {exception.GetType().Name}: {exception.Message}");
            diagnosticHandler(capabilities[0].Detail!);
        }

        return capabilities;
    }

    public void Tick()
    {
        PollPendingCheckpoint();
        if (!callbackLifetime.CanHandleCallbacks || LifecycleCapability.State != AdapterCapabilityState.Supported)
        {
            return;
        }

        try
        {
            SynchronizeMainCharacter();
            SynchronizeNativeStates();
            SynchronizeRaidInitialization();
            TryResumeDestination();
            if (pendingDeathTerminal && tracker.IsActive)
            {
                pendingDeathTerminal = false;
                ApplyTerminal(RunLifecycleEventKind.Died);
                return;
            }
            TryStartRun();
            var now = NowMonotonic();
            var utcNow = DateTime.UtcNow;
            var periodicCheckpointDue = tracker.IsActive && tracker.Tick(utcNow, now);
            if (periodicCheckpointDue)
            {
                tracker.ObserveIntegrity(NativeIntegrityProbe.Read());
            }

            if (checkpointScheduler.ShouldAttempt(
                    tracker.CombatCheckpointRequired,
                    periodicCheckpointDue,
                    now))
            {
                SaveCheckpoint(utcNow, now, awaitPersistence: false);
            }

            if (tracker.IsActive
                && !tracker.IsSuspended
                && MovementCapability.State == AdapterCapabilityState.Supported
                && sampleCadence.IsDue(now))
            {
                SampleMainDuck(utcNow, now);
                sampleCadence.MarkCompleted(now);
            }
        }
        catch (Exception exception)
        {
            diagnosticHandler($"Run-lifecycle tick failed safely: {exception.GetType().Name}: {exception.Message}");
        }
    }

    public void InterruptForProfileTransition()
    {
        if (callbackLifetime.DisposalStarted)
        {
            return;
        }

        if (tracker.IsActive)
        {
            ApplyTerminal(RunLifecycleEventKind.Interrupted);
        }

        DetachMainCharacter();
        sampleCadence.Reset();
        checkpointScheduler.Reset();
        movementMapId = null;
        routeTransitionPending = false;
        destinationPlacementObserved = false;
        pendingDeathTerminal = false;
        deathObservationGate.Reset();
        tracker.Apply(Event(RunLifecycleEventKind.RaidCleared));
    }

    public bool TryCleanup()
    {
        var firstAttempt = callbackLifetime.BeginDisposal();
        if (firstAttempt && tracker.IsActive)
        {
            ApplyTerminal(RunLifecycleEventKind.Interrupted);
        }

        sampleCadence.Reset();
        checkpointScheduler.Reset();
        movementMapId = null;
        routeTransitionPending = false;
        destinationPlacementObserved = false;
        pendingDeathTerminal = false;
        deathObservationGate.Reset();
        var cleaned = callbackLifetime.TryCleanup(TryDetachMainCharacter, out var staticCleanupFailure);
        if (staticCleanupFailure != null)
        {
            diagnosticHandler(
                $"Run-lifecycle subscription cleanup failed; cleanup remains retryable: "
                + $"{staticCleanupFailure.GetType().Name}: {staticCleanupFailure.Message}");
        }

        if (cleaned)
        {
            diagnosticHandler("Native run-lifecycle and movement hooks unsubscribed; sampler stopped and main-duck reference released.");
        }

        return cleaned;
    }

    public void Dispose() => TryCleanup();

    private CapabilityRecord LifecycleCapability => capabilities[0];

    private CapabilityRecord MovementCapability => capabilities[1];

    private CapabilityRecord MapCapability => capabilities[2];

    private CapabilityRecord RouteCapability => capabilities[3];

    private void TryStartRun()
    {
        if (tracker.IsActive
            || loading
            || paused
            || !NativeRaidContext.IsRaidMap()
            || !InputManager.InputActived
            || mainCharacter == null
            || !mainCharacter.IsMainCharacter
            || mainCharacter.Health == null
            || mainCharacter.Health.IsDead)
        {
            return;
        }

        var generationId = saveGenerationIdProvider();
        if (string.IsNullOrWhiteSpace(generationId))
        {
            return;
        }

        var now = NowMonotonic();
        var utcNow = DateTime.UtcNow;
        var transition = tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.ControlReady,
            TimestampUtc = utcNow,
            MonotonicSeconds = now,
            StartContext = new RunStartContext
            {
                SaveGenerationId = generationId,
                NativeRaidId = ReadNativeRaidId(),
                Map = ReadMapIdentity(),
                IntegrityTags = NativeIntegrityProbe.Read(),
                GameVersion = Application.version ?? string.Empty,
                GameBuild = SupportedGameBuild,
                LifecycleCapability = LifecycleCapability.State,
                LifecycleAdapterVersion = LifecycleAdapterVersion,
                MovementCapability = MovementCapability.State,
                MovementAdapterVersion = MovementAdapterVersion,
                MapCapability = MapCapability.State,
                MapAdapterVersion = MapAdapterVersion,
                WeaponCapabilities = weaponCapabilitiesProvider(),
                CombatCapabilities = combatCapabilitiesProvider(),
                EquipmentCapabilities = equipmentCapabilitiesProvider(),
                ContainerCapabilities = containerCapabilitiesProvider(),
                EconomyCapabilities = economyCapabilitiesProvider(),
                RouteCapabilities = RouteCapability.State == AdapterCapabilityState.Supported
                    ? RouteStatisticsReducer.Supported(RouteCapability.Detail ?? RouteAdapterVersion)
                    : RouteStatisticsReducer.Unavailable(RouteCapability.Detail ?? "Route adapter is unavailable.")
            }
        });
        if (!transition.Started)
        {
            return;
        }

        sampleCadence.Reset();
        checkpointScheduler.Reset();
        movementMapId = tracker.ActiveMapId;
        pendingBoundary = null;
        routeTransitionPending = false;
        destinationPlacementObserved = false;
        deathObservationGate.Reset();
        SampleMainDuck(utcNow, now);
        sampleCadence.MarkCompleted(now);
        SaveCheckpoint(utcNow, now);
        diagnosticHandler(
            $"Run started id={tracker.ActiveRunId} nativeRaid={ReadNativeRaidId() ?? "unknown"} map={ReadMapIdentity().MapId}.");
    }

    private void ApplyTerminal(RunLifecycleEventKind kind)
    {
        if (!tracker.IsActive)
        {
            return;
        }

        var now = NowMonotonic();
        var utcNow = DateTime.UtcNow;
        var transition = terminalBoundary.Apply(
            tracker,
            new RunLifecycleEvent
            {
                Kind = kind,
                TimestampUtc = utcNow,
                MonotonicSeconds = now
            },
            diagnosticHandler,
            () =>
            {
                if (!tracker.IsSuspended && MovementCapability.State == AdapterCapabilityState.Supported)
                {
                    SampleMainDuck(utcNow, now);
                }

                tracker.ObserveIntegrity(NativeIntegrityProbe.Read());
                SaveCheckpoint(utcNow, now);
            });
        if (transition.Completed == null)
        {
            return;
        }

        sampleCadence.Reset();
        checkpointScheduler.Reset();
        movementMapId = null;
        routeTransitionPending = false;
        destinationPlacementObserved = false;
        pendingDeathTerminal = false;
        deathObservationGate.Reset();

        if (completionHandler(transition.Completed))
        {
            diagnosticHandler(
                $"Run finalized id={transition.Completed.RunId} outcome={transition.Completed.Outcome} "
                + $"active={transition.Completed.ActiveDurationSeconds:0.###}s physical={transition.Completed.PhysicalDistance:0.###}m "
                + $"teleport={transition.Completed.TeleportDistance:0.###}m.");
        }
    }

    private void ApplySuspension(RunLifecycleEventKind kind, MovementObservationKind boundaryOnResume)
    {
        var transition = tracker.Apply(Event(kind));
        if (transition.CheckpointRequired && tracker.IsActive)
        {
            var now = NowMonotonic();
            SaveCheckpoint(DateTime.UtcNow, now);
        }

        if (kind is RunLifecycleEventKind.PauseEnded or RunLifecycleEventKind.LoadingEnded)
        {
            if (pendingBoundary != MovementObservationKind.LoadingBoundary)
            {
                pendingBoundary = boundaryOnResume;
            }
        }
    }

    private void SampleMainDuck(DateTime utcNow, double monotonicSeconds)
    {
        if (mainCharacter == null || !mainCharacter.IsMainCharacter)
        {
            return;
        }

        try
        {
            var currentMapId = ReadMapIdentity().MapId;
            if (movementMapId == null)
            {
                movementMapId = currentMapId;
            }
            else if (!string.Equals(movementMapId, currentMapId, StringComparison.Ordinal))
            {
                movementMapId = currentMapId;
                if (pendingBoundary != MovementObservationKind.LoadingBoundary)
                {
                    pendingBoundary = MovementObservationKind.MapBoundary;
                }
            }

            var position = mainCharacter.transform.position;
            var speed = ReadMaximumPlausibleSpeed(mainCharacter);
            var kind = pendingBoundary ?? MovementObservationKind.Regular;
            var result = tracker.ObserveMovement(
                new Position3D(position.x, position.y, position.z),
                monotonicSeconds,
                speed,
                kind);
            if (result.Disposition != MovementDisposition.InvalidIgnored)
            {
                pendingBoundary = null;
            }

            if (result.Disposition == MovementDisposition.Teleport)
            {
                SaveCheckpoint(utcNow, monotonicSeconds);
            }
        }
        catch (Exception exception)
        {
            DisableMovement($"Main-duck position/speed sampling failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private bool SaveCheckpoint(DateTime utcNow, double monotonicSeconds, bool awaitPersistence = true)
    {
        if (checkpointWritePending)
        {
            if (!awaitPersistence) return false;
            DrainPendingCheckpoint();
        }
        var checkpoint = tracker.CreateCheckpoint(utcNow, monotonicSeconds);
        if (checkpoint == null)
        {
            return false;
        }
        NativeHotPathDiagnostics.CountCheckpointClone();
        var mutationRevision = tracker.CheckpointMutationRevision;

        if (!checkpointHandler(checkpoint))
        {
            checkpointScheduler.RecordResult(succeeded: false, monotonicSeconds: monotonicSeconds);
            return false;
        }

        if (checkpointCompletionPoller == null)
        {
            tracker.MarkCheckpointSaved(monotonicSeconds, mutationRevision);
            checkpointScheduler.RecordResult(succeeded: true, monotonicSeconds: monotonicSeconds);
            return true;
        }

        checkpointWritePending = true;
        pendingCheckpointMonotonicSeconds = monotonicSeconds;
        pendingCheckpointMutationRevision = mutationRevision;
        return !awaitPersistence || DrainPendingCheckpoint();
    }

    private void PollPendingCheckpoint()
    {
        if (!checkpointWritePending || checkpointCompletionPoller == null) return;
        ApplyCheckpointCompletion(checkpointCompletionPoller());
    }

    private bool DrainPendingCheckpoint()
    {
        if (!checkpointWritePending) return true;
        if (checkpointCompletionFlusher == null) return false;
        return ApplyCheckpointCompletion(checkpointCompletionFlusher());
    }

    private bool ApplyCheckpointCompletion(DeferredWriteState state)
    {
        if (!checkpointWritePending) return state is DeferredWriteState.None or DeferredWriteState.Succeeded;
        if (state is DeferredWriteState.None or DeferredWriteState.Pending) return false;
        var monotonicSeconds = pendingCheckpointMonotonicSeconds;
        var mutationRevision = pendingCheckpointMutationRevision;
        checkpointWritePending = false;
        pendingCheckpointMonotonicSeconds = 0;
        pendingCheckpointMutationRevision = 0;
        var succeeded = state == DeferredWriteState.Succeeded;
        if (succeeded) tracker.MarkCheckpointSaved(monotonicSeconds, mutationRevision);
        checkpointScheduler.RecordResult(succeeded, monotonicSeconds);
        return succeeded;
    }

    private void SynchronizeMainCharacter()
    {
        if (callbackLifetime.DisposalStarted)
        {
            return;
        }

        CharacterMainControl? observed = null;
        try
        {
            var level = LevelManager.Instance;
            var candidate = level?.MainCharacter;
            if (candidate != null && candidate.IsMainCharacter)
            {
                observed = candidate;
            }
        }
        catch
        {
            observed = null;
        }

        if (ReferenceEquals(observed, mainCharacter))
        {
            return;
        }

        DetachMainCharacter();
        mainCharacter = observed;
        mainCharacterGate.Replace(observed);
        if (mainCharacter == null)
        {
            return;
        }

        mainCharacter.OnSetPositionEvent += OnMainCharacterSetPosition;
        if (tracker.IsActive && MovementCapability.State == AdapterCapabilityState.Supported)
        {
            var position = mainCharacter.transform.position;
            tracker.ObserveMovement(
                new Position3D(position.x, position.y, position.z),
                NowMonotonic(),
                ReadMaximumPlausibleSpeed(mainCharacter),
                MovementObservationKind.ObjectReplacement);
        }
    }

    private void DetachMainCharacter()
    {
        if (mainCharacter != null)
        {
            mainCharacter.OnSetPositionEvent -= OnMainCharacterSetPosition;
            mainCharacter = null;
        }

        mainCharacterGate.Clear();
    }

    private bool TryDetachMainCharacter()
    {
        try
        {
            DetachMainCharacter();
            return true;
        }
        catch (Exception exception)
        {
            diagnosticHandler(
                $"Main-duck instance subscription cleanup failed; cleanup remains retryable: "
                + $"{exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private void SynchronizeNativeStates()
    {
        var observedPause = GameManager.Paused;
        if (observedPause != paused)
        {
            paused = observedPause;
            ApplySuspension(
                paused ? RunLifecycleEventKind.PauseStarted : RunLifecycleEventKind.PauseEnded,
                MovementObservationKind.ResumeBoundary);
        }

        var multiSceneLoading = MultiSceneCore.Instance != null && MultiSceneCore.Instance.IsLoading;
        var observedLoading = SceneLoader.IsSceneLoading || LevelManager.LevelInitializing || multiSceneLoading;
        if (observedLoading != loading)
        {
            loading = observedLoading;
            if (loading && tracker.IsActive)
            {
                BeginRouteTransition();
            }
            else
            {
                ApplySuspension(
                    loading ? RunLifecycleEventKind.LoadingStarted : RunLifecycleEventKind.LoadingEnded,
                    MovementObservationKind.LoadingBoundary);
            }
        }
    }

    private void BeginRouteTransition()
    {
        if (routeTransitionPending || !tracker.IsActive) return;
        routeTransitionPending = true;
        destinationPlacementObserved = false;
        pendingBoundary = MovementObservationKind.LoadingBoundary;
        var transition = tracker.Apply(Event(RunLifecycleEventKind.MapTransitionStarted));
        if (transition.CheckpointRequired)
        {
            SaveCheckpoint(DateTime.UtcNow, NowMonotonic());
        }
    }

    private void TryResumeDestination()
    {
        if (!routeTransitionPending
            || loading
            || paused
            || !destinationPlacementObserved
            || !LevelManager.LevelInited
            || !InputManager.InputActived
            || mainCharacter == null
            || !mainCharacter.IsMainCharacter
            || mainCharacter.Health == null
            || mainCharacter.Health.IsDead)
            return;

        var map = ReadMapIdentity();
        var transition = tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.DestinationControlReady,
            TimestampUtc = DateTime.UtcNow,
            MonotonicSeconds = NowMonotonic(),
            Map = map
        });
        if (!transition.StateChanged) return;
        routeTransitionPending = false;
        destinationPlacementObserved = false;
        movementMapId = map.MapId;
        pendingBoundary = null;
        SaveCheckpoint(DateTime.UtcNow, NowMonotonic());
        diagnosticHandler($"Route destination ready run={tracker.ActiveRunId} segment={tracker.ActiveSegmentId ?? "unavailable"} map={map.MapId}.");
        try
        {
            destinationReadyObserver?.Invoke();
        }
        catch (Exception exception)
        {
            diagnosticHandler($"Destination equipment re-observation failed safely: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void SynchronizeRaidInitialization()
    {
        if (tracker.IsActive || !NativeRaidContext.IsRaidMap())
        {
            return;
        }

        var raid = RaidUtilities.CurrentRaid;
        if (raid.valid && !raid.ended)
        {
            tracker.Apply(new RunLifecycleEvent
            {
                Kind = RunLifecycleEventKind.RaidInitialized,
                TimestampUtc = DateTime.UtcNow,
                MonotonicSeconds = NowMonotonic(),
                NativeRaidId = raid.ID.ToString(CultureInfo.InvariantCulture)
            });
        }
    }

    private MapIdentity ReadMapIdentity()
    {
        try
        {
            var level = LevelManager.Instance;
            if (level == null)
            {
                return new MapIdentity();
            }

            var scene = level.gameObject.scene;
            var stableId = MultiSceneCore.Instance != null
                ? (!string.IsNullOrWhiteSpace(MultiSceneCore.ActiveSubSceneID)
                    ? MultiSceneCore.ActiveSubSceneID
                    : MultiSceneCore.MainSceneID)
                : SceneInfoCollection.GetSceneID(scene.buildIndex);
            var entry = string.IsNullOrWhiteSpace(stableId)
                ? null
                : SceneInfoCollection.GetSceneInfo(stableId);
            if (MapIdentity.TryFromNativeStableId(
                    stableId,
                    entry?.DisplayName,
                    entry != null,
                    out var identity))
            {
                return identity;
            }

            DisableMapIdentity(
                "No verified ActiveSubSceneID, MainSceneID, or SceneInfoCollection scene ID was available; "
                + "route identity is unavailable rather than inferred from a scene name or object identity.");
            return identity;
        }
        catch (Exception exception)
        {
            DisableMapIdentity(
                $"Map identity lookup failed; route identity is unavailable and the overall run uses the explicit unknown fallback: "
                + $"{exception.GetType().Name}: {exception.Message}");
            return new MapIdentity();
        }
    }

    private static float ReadMaximumPlausibleSpeed(CharacterMainControl character)
    {
        var maximum = Math.Max(character.CharacterWalkSpeed, character.CharacterRunSpeed);
        maximum = Math.Max(maximum, character.DashSpeed);
        if (float.IsNaN(maximum) || float.IsInfinity(maximum) || maximum <= 0)
        {
            throw new InvalidOperationException("Verified native movement speed was not finite and positive.");
        }

        return maximum;
    }

    private static string? ReadNativeRaidId()
    {
        try
        {
            var raid = RaidUtilities.CurrentRaid;
            return raid.valid ? raid.ID.ToString(CultureInfo.InvariantCulture) : null;
        }
        catch
        {
            return null;
        }
    }

    private void DisableMovement(string detail)
    {
        if (MovementCapability.State == AdapterCapabilityState.DisabledIncompatible)
        {
            return;
        }

        capabilities[1] = Capability(
            MovementAdapterId,
            MovementAdapterVersion,
            AdapterCapabilityState.DisabledIncompatible,
            detail);
        tracker.DisableMovement();
        sampleCadence.Reset();
        capabilityHandler(capabilities);
        diagnosticHandler(detail);
    }

    private void DisableMapIdentity(string detail)
    {
        if (MapCapability.State == AdapterCapabilityState.DisabledIncompatible)
        {
            return;
        }

        capabilities[2] = Capability(
            MapAdapterId,
            MapAdapterVersion,
            AdapterCapabilityState.DisabledIncompatible,
            detail);
        capabilities[3] = Capability(
            RouteAdapterId,
            RouteAdapterVersion,
            AdapterCapabilityState.DisabledIncompatible,
            detail);
        tracker.DisableRoute(detail);
        capabilityHandler(capabilities);
        diagnosticHandler(detail);
    }

    private void SetAllCapabilities(AdapterCapabilityState state, string detail)
    {
        capabilities.Clear();
        capabilities.Add(Capability(LifecycleAdapterId, LifecycleAdapterVersion, state, detail));
        capabilities.Add(Capability(MovementAdapterId, MovementAdapterVersion, state, detail));
        capabilities.Add(Capability(MapAdapterId, MapAdapterVersion, state, detail));
        capabilities.Add(Capability(RouteAdapterId, RouteAdapterVersion, state, detail));
        capabilityHandler(capabilities);
    }

    private static CapabilityRecord Capability(
        string adapterId,
        string version,
        AdapterCapabilityState state,
        string detail) => new()
        {
            AdapterId = adapterId,
            State = state,
            Version = version,
            Detail = detail
        };

    private SubscriptionBinding[] CreateSubscriptions()
    {
        var onNewRaid = callbackLifetime.Guard<RaidUtilities.RaidInfo>(OnNewRaid);
        var onRaidEnd = callbackLifetime.Guard<RaidUtilities.RaidInfo>(OnRaidEnd);
        var onRaidDead = callbackLifetime.Guard<RaidUtilities.RaidInfo>(OnRaidDead);
        var onLevelInitialized = callbackLifetime.Guard(OnLevelInitialized);
        var onAfterLevelInitialized = callbackLifetime.Guard(OnAfterLevelInitialized);
        var onEvacuated = callbackLifetime.Guard<EvacuationInfo>(OnEvacuated);
        var onMainCharacterDead = callbackLifetime.Guard<DamageInfo>(OnMainCharacterDead);
        var onPauseStarted = callbackLifetime.Guard(OnPauseStarted);
        var onPauseEnded = callbackLifetime.Guard(OnPauseEnded);
        var onSceneLoadingStarted = callbackLifetime.Guard<SceneLoadingContext>(OnSceneLoadingStarted);
        var onSceneLoadingFinished = callbackLifetime.Guard<SceneLoadingContext>(OnSceneLoadingFinished);
        var onSceneAfterInitialize = callbackLifetime.Guard<SceneLoadingContext>(OnSceneAfterInitialize);
        var onSubSceneWillBeUnloaded = callbackLifetime.Guard<MultiSceneCore, Scene>(OnSubSceneWillBeUnloaded);
        var onSubSceneLoaded = callbackLifetime.Guard<MultiSceneCore, Scene>(OnSubSceneLoaded);
        var onCheatModeStatusChanged = callbackLifetime.Guard<bool>(OnCheatModeStatusChanged);
        var onRuleChanged = callbackLifetime.Guard(OnRuleChanged);
        return new SubscriptionBinding[]
        {
            new(() => RaidUtilities.OnNewRaid += onNewRaid, () => RaidUtilities.OnNewRaid -= onNewRaid),
            new(() => RaidUtilities.OnRaidEnd += onRaidEnd, () => RaidUtilities.OnRaidEnd -= onRaidEnd),
            new(() => RaidUtilities.OnRaidDead += onRaidDead, () => RaidUtilities.OnRaidDead -= onRaidDead),
            new(() => LevelManager.OnLevelInitialized += onLevelInitialized, () => LevelManager.OnLevelInitialized -= onLevelInitialized),
            new(() => LevelManager.OnAfterLevelInitialized += onAfterLevelInitialized, () => LevelManager.OnAfterLevelInitialized -= onAfterLevelInitialized),
            new(() => LevelManager.OnEvacuated += onEvacuated, () => LevelManager.OnEvacuated -= onEvacuated),
            new(() => LevelManager.OnMainCharacterDead += onMainCharacterDead, () => LevelManager.OnMainCharacterDead -= onMainCharacterDead),
            new(() => PauseMenu.onPauseMenuOn += onPauseStarted, () => PauseMenu.onPauseMenuOn -= onPauseStarted),
            new(() => PauseMenu.onPauseMenuOff += onPauseEnded, () => PauseMenu.onPauseMenuOff -= onPauseEnded),
            new(() => SceneLoader.onStartedLoadingScene += onSceneLoadingStarted, () => SceneLoader.onStartedLoadingScene -= onSceneLoadingStarted),
            new(() => SceneLoader.onFinishedLoadingScene += onSceneLoadingFinished, () => SceneLoader.onFinishedLoadingScene -= onSceneLoadingFinished),
            new(() => SceneLoader.onAfterSceneInitialize += onSceneAfterInitialize, () => SceneLoader.onAfterSceneInitialize -= onSceneAfterInitialize),
            new(() => MultiSceneCore.OnSubSceneWillBeUnloaded += onSubSceneWillBeUnloaded, () => MultiSceneCore.OnSubSceneWillBeUnloaded -= onSubSceneWillBeUnloaded),
            new(() => MultiSceneCore.OnSubSceneLoaded += onSubSceneLoaded, () => MultiSceneCore.OnSubSceneLoaded -= onSubSceneLoaded),
            new(() => CheatMode.OnCheatModeStatusChanged += onCheatModeStatusChanged, () => CheatMode.OnCheatModeStatusChanged -= onCheatModeStatusChanged),
            new(() => GameRulesManager.OnRuleChanged += onRuleChanged, () => GameRulesManager.OnRuleChanged -= onRuleChanged)
        };
    }

    private RunLifecycleEvent Event(RunLifecycleEventKind kind) => new()
    {
        Kind = kind,
        TimestampUtc = DateTime.UtcNow,
        MonotonicSeconds = NowMonotonic()
    };

    private double NowMonotonic() => monotonicClock.Elapsed.TotalSeconds;

    private void OnNewRaid(RaidUtilities.RaidInfo raid)
    {
        var utcNow = DateTime.UtcNow;
        var now = NowMonotonic();
        var transition = terminalBoundary.Apply(tracker, new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.RaidInitialized,
            TimestampUtc = utcNow,
            MonotonicSeconds = now,
            NativeRaidId = raid.ID.ToString(CultureInfo.InvariantCulture)
        }, diagnosticHandler, () => SaveCheckpoint(utcNow, now));
        if (transition.Completed != null)
        {
            HandleCompleted(transition.Completed, "new native raid");
        }
    }

    private void OnRaidEnd(RaidUtilities.RaidInfo raid)
    {
        if (raid.dead)
        {
            pendingDeathTerminal = tracker.IsActive;
            return;
        }

        ApplyTerminal(RunLifecycleEventKind.Interrupted);
    }

    private void OnRaidDead(RaidUtilities.RaidInfo raid) => pendingDeathTerminal = tracker.IsActive;

    private void OnLevelInitialized() => SynchronizeMainCharacter();

    private void OnAfterLevelInitialized() => SynchronizeMainCharacter();

    private void OnEvacuated(EvacuationInfo info) => ApplyTerminal(RunLifecycleEventKind.Extracted);

    private void OnMainCharacterDead(DamageInfo info)
    {
        if (!deathObservationGate.TryObserve(tracker.IsActive))
        {
            return;
        }

        try
        {
            playerDeathObserver?.Invoke(info);
        }
        catch (Exception exception)
        {
            diagnosticHandler($"Main-character death observer failed safely: {exception.GetType().Name}: {exception.Message}");
        }

        // Health.Hurt has not returned yet. Keep the run active until Update so
        // the post-call HP delta can be recorded before terminal aggregation.
        pendingDeathTerminal = true;
    }

    private void OnPauseStarted()
    {
        paused = true;
        ApplySuspension(RunLifecycleEventKind.PauseStarted, MovementObservationKind.ResumeBoundary);
    }

    private void OnPauseEnded()
    {
        paused = false;
        ApplySuspension(RunLifecycleEventKind.PauseEnded, MovementObservationKind.ResumeBoundary);
    }

    private void OnSceneLoadingStarted(SceneLoadingContext context)
    {
        loading = true;
        if (tracker.IsActive) BeginRouteTransition();
        else ApplySuspension(RunLifecycleEventKind.LoadingStarted, MovementObservationKind.LoadingBoundary);
    }

    private void OnSceneLoadingFinished(SceneLoadingContext context) => SynchronizeNativeStates();

    private void OnSceneAfterInitialize(SceneLoadingContext context) => SynchronizeNativeStates();

    private void OnSubSceneWillBeUnloaded(MultiSceneCore core, Scene scene)
    {
        loading = true;
        if (tracker.IsActive) BeginRouteTransition();
        else ApplySuspension(RunLifecycleEventKind.LoadingStarted, MovementObservationKind.LoadingBoundary);
    }

    private void OnSubSceneLoaded(MultiSceneCore core, Scene scene) => SynchronizeNativeStates();

    private void OnCheatModeStatusChanged(bool _) => RefreshActiveIntegrity();

    private void OnRuleChanged() => RefreshActiveIntegrity();

    private void RefreshActiveIntegrity()
    {
        if (!tracker.IsActive)
        {
            return;
        }

        try
        {
            if (!tracker.ObserveIntegrity(NativeIntegrityProbe.Read()))
            {
                return;
            }

            var now = NowMonotonic();
            SaveCheckpoint(DateTime.UtcNow, now);
            diagnosticHandler($"Active run integrity changed id={tracker.ActiveRunId}; the run is excluded from default duration records.");
        }
        catch (Exception exception)
        {
            diagnosticHandler($"Active run integrity refresh failed safely: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void OnMainCharacterSetPosition(CharacterMainControl character, Vector3 position)
    {
        if (!callbackLifetime.CanHandleCallbacks
            || !mainCharacterGate.Accepts(character)
            || !tracker.IsActive)
        {
            return;
        }

        var transitionPlacement = routeTransitionPending;
        if (transitionPlacement)
        {
            destinationPlacementObserved = true;
        }
        if (MovementCapability.State != AdapterCapabilityState.Supported)
        {
            return;
        }

        try
        {
            var now = NowMonotonic();
            var result = tracker.ObserveMovement(
                new Position3D(position.x, position.y, position.z),
                now,
                ReadMaximumPlausibleSpeed(character),
                transitionPlacement ? MovementObservationKind.LoadingBoundary : MovementObservationKind.ExplicitTeleport);
            if (result.Disposition is MovementDisposition.Teleport or MovementDisposition.TransitionExcluded)
            {
                pendingBoundary = null;
                SaveCheckpoint(DateTime.UtcNow, now);
            }
        }
        catch (Exception exception)
        {
            DisableMovement($"Explicit main-duck position observation failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void HandleCompleted(RunSummary summary, string reason)
    {
        DrainPendingCheckpoint();
        sampleCadence.Reset();
        checkpointScheduler.Reset();
        movementMapId = null;
        routeTransitionPending = false;
        destinationPlacementObserved = false;
        pendingDeathTerminal = false;
        deathObservationGate.Reset();
        if (completionHandler(summary))
        {
            diagnosticHandler($"Run finalized id={summary.RunId} outcome={summary.Outcome} reason={reason}.");
        }
    }
}
