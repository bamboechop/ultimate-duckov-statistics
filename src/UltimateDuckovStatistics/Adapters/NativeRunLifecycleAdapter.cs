using System.Diagnostics;
using System.Globalization;
using Duckov.Scenes;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Tracking;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class NativeRunLifecycleAdapter : IDisposable
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
    private readonly Func<string> saveGenerationIdProvider;
    private readonly Action<ActiveRunCheckpoint> checkpointHandler;
    private readonly Func<RunSummary, bool> completionHandler;
    private readonly Action<IReadOnlyList<CapabilityRecord>> capabilityHandler;
    private readonly Action<string> diagnosticHandler;
    private readonly Stopwatch monotonicClock = Stopwatch.StartNew();
    private readonly RunLifecycleTracker tracker;
    private readonly MonotonicCadenceGate sampleCadence = new(SampleIntervalSeconds);
    private readonly ReferenceSubjectGate<CharacterMainControl> mainCharacterGate = new();
    private readonly IdempotentSubscriptionSet subscriptions = new();
    private readonly List<CapabilityRecord> capabilities = new();
    private CharacterMainControl? mainCharacter;
    private bool disposed;
    private bool paused;
    private bool loading;
    private MovementObservationKind? pendingBoundary;
    private string? movementMapId;

    public NativeRunLifecycleAdapter(
        Func<string> saveGenerationIdProvider,
        Action<ActiveRunCheckpoint> checkpointHandler,
        Func<RunSummary, bool> completionHandler,
        Action<IReadOnlyList<CapabilityRecord>> capabilityHandler,
        Action<string> diagnosticHandler)
    {
        this.saveGenerationIdProvider = saveGenerationIdProvider
            ?? throw new ArgumentNullException(nameof(saveGenerationIdProvider));
        this.checkpointHandler = checkpointHandler ?? throw new ArgumentNullException(nameof(checkpointHandler));
        this.completionHandler = completionHandler ?? throw new ArgumentNullException(nameof(completionHandler));
        this.capabilityHandler = capabilityHandler ?? throw new ArgumentNullException(nameof(capabilityHandler));
        this.diagnosticHandler = diagnosticHandler ?? throw new ArgumentNullException(nameof(diagnosticHandler));
        tracker = new RunLifecycleTracker(() => Guid.NewGuid().ToString("N"));
        SetAllCapabilities(
            AdapterCapabilityState.DisabledIncompatible,
            "Run lifecycle and movement have not been initialized.");
    }

    public bool IsActive => tracker.IsActive;

    public string? CurrentRunId => tracker.ActiveRunId;

    public string? CurrentMapId => tracker.ActiveMapId;

    public IReadOnlyList<CapabilityRecord> Initialize()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(NativeRunLifecycleAdapter));
        }

        if (subscriptions.IsActive)
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
            subscriptions.Activate(CreateSubscriptions());
            SetAllCapabilities(
                AdapterCapabilityState.Supported,
                "Verified Duckov 2.3.30 public lifecycle, map, main-duck position, and movement-speed contracts.");
            SynchronizeRaidInitialization();
            SynchronizeNativeStates();
            diagnosticHandler("Native run-lifecycle and main-duck movement hooks subscribed; sampling interval is 0.2 seconds.");
        }
        catch (Exception exception)
        {
            Unsubscribe();
            SetAllCapabilities(
                AdapterCapabilityState.DisabledIncompatible,
                $"Run-lifecycle activation failed: {exception.GetType().Name}: {exception.Message}");
            diagnosticHandler(capabilities[0].Detail!);
        }

        return capabilities;
    }

    public void Tick()
    {
        if (!subscriptions.IsActive || LifecycleCapability.State != AdapterCapabilityState.Supported)
        {
            return;
        }

        try
        {
            SynchronizeMainCharacter();
            SynchronizeNativeStates();
            SynchronizeRaidInitialization();
            TryStartRun();
            var now = NowMonotonic();
            var utcNow = DateTime.UtcNow;
            if (tracker.IsActive && tracker.Tick(utcNow, now))
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
        if (tracker.IsActive)
        {
            ApplyTerminal(RunLifecycleEventKind.Interrupted);
        }

        DetachMainCharacter();
        sampleCadence.Reset();
        movementMapId = null;
        tracker.Apply(Event(RunLifecycleEventKind.RaidCleared));
    }

    public void Dispose()
    {
        if (disposed)
        {
            Unsubscribe();
            return;
        }

        disposed = true;
        if (tracker.IsActive)
        {
            ApplyTerminal(RunLifecycleEventKind.Interrupted);
        }

        DetachMainCharacter();
        sampleCadence.Reset();
        movementMapId = null;
        Unsubscribe();
        diagnosticHandler("Native run-lifecycle and movement hooks unsubscribed; sampler stopped and main-duck reference released.");
    }

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
                MapAdapterVersion = MapAdapterVersion
            }
        });
        if (!transition.Started)
        {
            return;
        }

        sampleCadence.Reset();
        movementMapId = tracker.ActiveMapId;
        pendingBoundary = null;
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
        movementMapId = null;

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

    private void SaveCheckpoint(DateTime utcNow, double monotonicSeconds)
    {
        var checkpoint = tracker.CreateCheckpoint(utcNow, monotonicSeconds);
        if (checkpoint == null)
        {
            return;
        }

        checkpointHandler(checkpoint);
        tracker.MarkCheckpointSaved(monotonicSeconds);
    }

    private void SynchronizeMainCharacter()
    {
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

    private void Unsubscribe()
    {
        try
        {
            subscriptions.Deactivate();
        }
        catch (Exception exception)
        {
            diagnosticHandler(
                $"Run-lifecycle subscription cleanup failed; cleanup remains retryable: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private SubscriptionBinding[] CreateSubscriptions() => new SubscriptionBinding[]
    {
        new(() => RaidUtilities.OnNewRaid += OnNewRaid, () => RaidUtilities.OnNewRaid -= OnNewRaid),
        new(() => RaidUtilities.OnRaidEnd += OnRaidEnd, () => RaidUtilities.OnRaidEnd -= OnRaidEnd),
        new(() => RaidUtilities.OnRaidDead += OnRaidDead, () => RaidUtilities.OnRaidDead -= OnRaidDead),
        new(() => LevelManager.OnLevelInitialized += OnLevelInitialized, () => LevelManager.OnLevelInitialized -= OnLevelInitialized),
        new(() => LevelManager.OnAfterLevelInitialized += OnAfterLevelInitialized, () => LevelManager.OnAfterLevelInitialized -= OnAfterLevelInitialized),
        new(() => LevelManager.OnEvacuated += OnEvacuated, () => LevelManager.OnEvacuated -= OnEvacuated),
        new(() => LevelManager.OnMainCharacterDead += OnMainCharacterDead, () => LevelManager.OnMainCharacterDead -= OnMainCharacterDead),
        new(() => PauseMenu.onPauseMenuOn += OnPauseStarted, () => PauseMenu.onPauseMenuOn -= OnPauseStarted),
        new(() => PauseMenu.onPauseMenuOff += OnPauseEnded, () => PauseMenu.onPauseMenuOff -= OnPauseEnded),
        new(() => SceneLoader.onStartedLoadingScene += OnSceneLoadingStarted, () => SceneLoader.onStartedLoadingScene -= OnSceneLoadingStarted),
        new(() => SceneLoader.onFinishedLoadingScene += OnSceneLoadingFinished, () => SceneLoader.onFinishedLoadingScene -= OnSceneLoadingFinished),
        new(() => SceneLoader.onAfterSceneInitialize += OnSceneAfterInitialize, () => SceneLoader.onAfterSceneInitialize -= OnSceneAfterInitialize),
        new(() => MultiSceneCore.OnSubSceneWillBeUnloaded += OnSubSceneWillBeUnloaded, () => MultiSceneCore.OnSubSceneWillBeUnloaded -= OnSubSceneWillBeUnloaded),
        new(() => MultiSceneCore.OnSubSceneLoaded += OnSubSceneLoaded, () => MultiSceneCore.OnSubSceneLoaded -= OnSubSceneLoaded)
    };

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

    private void OnRaidEnd(RaidUtilities.RaidInfo raid) =>
        ApplyTerminal(raid.dead ? RunLifecycleEventKind.Died : RunLifecycleEventKind.Interrupted);

    private void OnRaidDead(RaidUtilities.RaidInfo raid) => ApplyTerminal(RunLifecycleEventKind.Died);

    private void OnLevelInitialized() => SynchronizeMainCharacter();

    private void OnAfterLevelInitialized() => SynchronizeMainCharacter();

    private void OnEvacuated(EvacuationInfo info) => ApplyTerminal(RunLifecycleEventKind.Extracted);

    private void OnMainCharacterDead(DamageInfo info) => ApplyTerminal(RunLifecycleEventKind.Died);

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

    private void OnMainCharacterSetPosition(CharacterMainControl character, Vector3 position)
    {
        if (!mainCharacterGate.Accepts(character)
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
