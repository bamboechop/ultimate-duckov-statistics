using System.Diagnostics;
using System.Globalization;
using Duckov;
using Duckov.Rules;
using Duckov.Scenes;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
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
    private readonly Stopwatch monotonicClock = Stopwatch.StartNew();
    private readonly RunLifecycleTracker tracker;
    private readonly MonotonicCadenceGate sampleCadence = new(SampleIntervalSeconds);
    private readonly CheckpointRetryGate checkpointRetry = new(CheckpointRetryIntervalSeconds);
    private readonly MonotonicCadenceGate combatCheckpointCadence = new(CombatCheckpointIntervalSeconds);
    private readonly ReferenceSubjectGate<CharacterMainControl> mainCharacterGate = new();
    private readonly NativeCallbackLifetime callbackLifetime = new();
    private readonly DeathObservationGate deathObservationGate = new();
    private readonly List<CapabilityRecord> capabilities = new();
    private CharacterMainControl? mainCharacter;
    private bool paused;
    private bool loading;
    private MovementObservationKind? pendingBoundary;
    private string? movementMapId;
    private Action<DamageInfo>? playerDeathObserver;
    private bool pendingDeathTerminal;

    public NativeRunLifecycleAdapter(
        Func<string> saveGenerationIdProvider,
        Func<ActiveRunCheckpoint, bool> checkpointHandler,
        Func<RunSummary, bool> completionHandler,
        Action<IReadOnlyList<CapabilityRecord>> capabilityHandler,
        Action<string> diagnosticHandler,
        Func<WeaponMetricCapabilities>? weaponCapabilitiesProvider = null,
        Func<CombatMetricCapabilities>? combatCapabilitiesProvider = null,
        Func<EquipmentMetricCapabilities>? equipmentCapabilitiesProvider = null)
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
        tracker = new RunLifecycleTracker(() => Guid.NewGuid().ToString("N"));
        SetAllCapabilities(
            AdapterCapabilityState.DisabledIncompatible,
            "Run lifecycle and movement have not been initialized.");
    }

    public bool IsActive => tracker.IsActive;

    public string? CurrentRunId => tracker.ActiveRunId;

    public string? CurrentMapId => tracker.ActiveMapId;

    public bool RecordShot(ShotRecorded shot)
    {
        if (!callbackLifetime.CanHandleCallbacks || !tracker.RecordShot(shot))
        {
            return false;
        }

        var now = NowMonotonic();
        if (!SaveCheckpoint(DateTime.UtcNow, now))
        {
            diagnosticHandler(
                $"Accepted firing action {shot.EventId}; crash-safe active-run checkpoint remains pending "
                + "and will be retried no more than once per second.");
        }

        return true;
    }

    public bool RecordCombat(CombatRecorded value) =>
        callbackLifetime.CanHandleCallbacks && tracker.RecordCombat(value);

    public bool UpdateCombatCapabilities(CombatMetricCapabilities capabilities) =>
        callbackLifetime.CanHandleCallbacks && tracker.UpdateCombatCapabilities(capabilities);

    public bool ObserveEquipment(EquipmentSnapshot snapshot) =>
        callbackLifetime.CanHandleCallbacks
        && tracker.ObserveEquipment(snapshot, DateTime.UtcNow, NowMonotonic());

    public bool InvalidateEquipmentObservation() =>
        callbackLifetime.CanHandleCallbacks
        && tracker.SuspendEquipment(DateTime.UtcNow, NowMonotonic());

    public bool FlushCheckpoint() => !tracker.IsActive
        || SaveCheckpoint(DateTime.UtcNow, NowMonotonic());

    public void SetPlayerDeathObserver(Action<DamageInfo>? observer) => playerDeathObserver = observer;

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
        if (!callbackLifetime.CanHandleCallbacks || LifecycleCapability.State != AdapterCapabilityState.Supported)
        {
            return;
        }

        try
        {
            SynchronizeMainCharacter();
            SynchronizeNativeStates();
            SynchronizeRaidInitialization();
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

            var coalescedCombatCheckpointDue = tracker.CombatCheckpointRequired
                                                && combatCheckpointCadence.IsDue(now);
            if (checkpointRetry.ShouldAttempt(
                    coalescedCombatCheckpointDue,
                    periodicCheckpointDue,
                    now))
            {
                SaveCheckpoint(utcNow, now);
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
        checkpointRetry.Reset();
        combatCheckpointCadence.Reset();
        movementMapId = null;
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
        checkpointRetry.Reset();
        combatCheckpointCadence.Reset();
        movementMapId = null;
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
                EquipmentCapabilities = equipmentCapabilitiesProvider()
            }
        });
        if (!transition.Started)
        {
            return;
        }

        sampleCadence.Reset();
        checkpointRetry.Reset();
        combatCheckpointCadence.Reset();
        movementMapId = tracker.ActiveMapId;
        pendingBoundary = null;
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
        if (!tracker.IsSuspended && MovementCapability.State == AdapterCapabilityState.Supported)
        {
            SampleMainDuck(utcNow, now);
        }

        tracker.ObserveIntegrity(NativeIntegrityProbe.Read());

        var transition = tracker.Apply(new RunLifecycleEvent
        {
            Kind = kind,
            TimestampUtc = utcNow,
            MonotonicSeconds = now
        });
        if (transition.Completed == null)
        {
            return;
        }

        sampleCadence.Reset();
        checkpointRetry.Reset();
        combatCheckpointCadence.Reset();
        movementMapId = null;
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

    private bool SaveCheckpoint(DateTime utcNow, double monotonicSeconds)
    {
        var checkpoint = tracker.CreateCheckpoint(utcNow, monotonicSeconds);
        if (checkpoint == null)
        {
            return false;
        }

        if (!checkpointHandler(checkpoint))
        {
            checkpointRetry.RecordResult(succeeded: false, monotonicSeconds: monotonicSeconds);
            return false;
        }

        tracker.MarkCheckpointSaved(monotonicSeconds);
        combatCheckpointCadence.MarkCompleted(monotonicSeconds);
        checkpointRetry.RecordResult(succeeded: true, monotonicSeconds: monotonicSeconds);
        return true;
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
            ApplySuspension(
                loading ? RunLifecycleEventKind.LoadingStarted : RunLifecycleEventKind.LoadingEnded,
                MovementObservationKind.LoadingBoundary);
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
                ? MultiSceneCore.MainSceneID
                : SceneInfoCollection.GetSceneID(scene.buildIndex);
            if (!string.IsNullOrWhiteSpace(stableId))
            {
                var entry = SceneInfoCollection.GetSceneInfo(stableId);
                var displayName = entry?.DisplayName;
                return new MapIdentity
                {
                    MapId = $"duckov:map:{stableId}",
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? stableId : displayName,
                    IsKnown = entry != null
                };
            }

            var fallback = !string.IsNullOrWhiteSpace(scene.name)
                ? scene.name
                : $"build-{scene.buildIndex.ToString(CultureInfo.InvariantCulture)}";
            return new MapIdentity
            {
                MapId = $"duckov:map:scene:{fallback}",
                DisplayName = $"{MapIdentity.UnknownDisplayName} ({fallback})",
                IsKnown = false
            };
        }
        catch (Exception exception)
        {
            DisableMapIdentity(
                $"Map identity lookup failed; using the explicit unknown fallback: {exception.GetType().Name}: {exception.Message}");
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
        capabilityHandler(capabilities);
        diagnosticHandler(detail);
    }

    private void SetAllCapabilities(AdapterCapabilityState state, string detail)
    {
        capabilities.Clear();
        capabilities.Add(Capability(LifecycleAdapterId, LifecycleAdapterVersion, state, detail));
        capabilities.Add(Capability(MovementAdapterId, MovementAdapterVersion, state, detail));
        capabilities.Add(Capability(MapAdapterId, MapAdapterVersion, state, detail));
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
        if (tracker.IsActive)
        {
            ApplyTerminal(RunLifecycleEventKind.Interrupted);
        }

        tracker.Apply(new RunLifecycleEvent
        {
            Kind = RunLifecycleEventKind.RaidInitialized,
            TimestampUtc = DateTime.UtcNow,
            MonotonicSeconds = NowMonotonic(),
            NativeRaidId = raid.ID.ToString(CultureInfo.InvariantCulture)
        });
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
        ApplySuspension(RunLifecycleEventKind.LoadingStarted, MovementObservationKind.LoadingBoundary);
    }

    private void OnSceneLoadingFinished(SceneLoadingContext context) => SynchronizeNativeStates();

    private void OnSceneAfterInitialize(SceneLoadingContext context) => SynchronizeNativeStates();

    private void OnSubSceneWillBeUnloaded(MultiSceneCore core, Scene scene)
    {
        loading = true;
        ApplySuspension(RunLifecycleEventKind.LoadingStarted, MovementObservationKind.LoadingBoundary);
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
            || !tracker.IsActive
            || MovementCapability.State != AdapterCapabilityState.Supported)
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
                MovementObservationKind.ExplicitTeleport);
            if (result.Disposition == MovementDisposition.Teleport)
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
}
