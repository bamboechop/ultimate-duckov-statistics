using System.Reflection;
using Stopwatch = System.Diagnostics.Stopwatch;
using Duckov.UI;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;
using UnityEngine;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class NativeWorldTimeAdapter : IDisposable, IRetryableCleanup
{
    internal const string AdapterVersion = "native-world-time-sleep/2.3.30+clock86300+patch-stamp-v1+durable30s-v1+profile-handoff-v7";
    internal const string HarmonyId = "at.bamboechop.ultimate-duckov-statistics.world-time-sleep";
    private const string SupportedGameVersion = "2.3.30";
    private readonly Func<string> generationIdProvider;
    private readonly Func<WorldTimeMutation, bool> recordHandler;
    private readonly Func<bool> persistenceHandler;
    private readonly Action<IReadOnlyList<CapabilityRecord>, WorldTimeMetricCapabilities> capabilityHandler;
    private readonly Action<string> diagnosticHandler;
    private readonly NativeCallbackLifetime callbackLifetime = new();
    private readonly RetryableHarmonyPatcherLease patcherLease = new();
    private readonly NativeWorldTimeObservationBoundary boundary = new();
    private readonly NativeWorldTimeProfileHandoffBoundary profileHandoff = new();
    private readonly NativeWorldTimePersistenceCadence persistenceCadence = new();
    private readonly Stopwatch monotonicClock = Stopwatch.StartNew();
    private readonly IncrementalPatchInspectionScheduler patchInspectionScheduler = new(TimeSpan.FromSeconds(2));
    private readonly HashSet<string> diagnosticKeys = new(StringComparer.Ordinal);
    private WorldTimeMetricCapabilities capabilities = WorldTimeNativeContractPolicy.Unavailable(
        WorldTimeNativeContractPolicy.BootstrapProvenance);
    private MethodInfo? sleepAdvanceMethod;
    private HarmonyPatchSetStamp? sleepPatchStamp;
    public NativeWorldTimeAdapter(
        Func<string> generationIdProvider,
        Func<WorldTimeMutation, bool> recordHandler,
        Func<bool> persistenceHandler,
        Action<IReadOnlyList<CapabilityRecord>, WorldTimeMetricCapabilities> capabilityHandler,
        Action<string> diagnosticHandler)
    {
        this.generationIdProvider = generationIdProvider ?? throw new ArgumentNullException(nameof(generationIdProvider));
        this.recordHandler = recordHandler ?? throw new ArgumentNullException(nameof(recordHandler));
        this.persistenceHandler = persistenceHandler ?? throw new ArgumentNullException(nameof(persistenceHandler));
        this.capabilityHandler = capabilityHandler ?? throw new ArgumentNullException(nameof(capabilityHandler));
        this.diagnosticHandler = diagnosticHandler ?? throw new ArgumentNullException(nameof(diagnosticHandler));
    }

    public WorldTimeMetricCapabilities MetricCapabilities =>
        WorldTimeStatisticsReducer.CloneCapabilities(capabilities);

    public IReadOnlyList<CapabilityRecord> Initialize()
    {
        if (callbackLifetime.DisposalStarted) throw new ObjectDisposedException(nameof(NativeWorldTimeAdapter));
        if (callbackLifetime.IsActive) return Records();
        var installedVersion = Application.version ?? string.Empty;
        if (!string.Equals(installedVersion, SupportedGameVersion, StringComparison.Ordinal))
            return DisableAll($"Installed Duckov version '{installedVersion}' does not match verified version '{SupportedGameVersion}'.");

        const string clockProvenance =
            "GameClock.OnGameClockStep with GameClock.Day and TimeOfDay ticks; installed native day length is 86,300 seconds.";
        var guardedClock = callbackLifetime.Guard(OnGameClockStep);
        var guardedSleep = callbackLifetime.Guard(OnAfterSleep);
        try
        {
            callbackLifetime.Activate(
            [
                new SubscriptionBinding(
                    () => GameClock.OnGameClockStep += guardedClock,
                    () => GameClock.OnGameClockStep -= guardedClock),
                new SubscriptionBinding(
                    () => SleepView.OnAfterSleep += guardedSleep,
                    () => SleepView.OnAfterSleep -= guardedSleep)
            ]);
            capabilities = WorldTimeNativeContractPolicy.ClockSupportedSleepUnavailable(
                clockProvenance,
                "Exact sleep advancement patch has not been established.");
            EstablishBaseline();
            TryInitializeSleepPatch(clockProvenance);
            persistenceCadence.Start(NowMonotonic());
            Publish();
            DiagnosticOnce(
                "initialized",
                "World clock observation subscribed with one-second monotonic publication and 30-second monotonic durability; changed-profile load hydration requires a replacement GameClock instance and is baseline-only, while a proven same-profile reopen retains the captured clock and sleep completion requires the exact GameClock.Step(float) patch plus SleepView.OnAfterSleep.");
        }
        catch (Exception exception)
        {
            DisableAll($"World-time activation failed: {Unwrap(exception).GetType().Name}: {Unwrap(exception).Message}");
            TryCleanup();
        }
        return Records();
    }

    public void Tick(DateTime nowUtc)
    {
        if (!callbackLifetime.CanHandleCallbacks) return;
        var monotonicSeconds = NowMonotonic();
        if (persistenceCadence.ShouldPublish(monotonicSeconds))
        {
            PublishPending(monotonicSeconds, requestDurability: false);
        }
        if (persistenceCadence.ShouldSchedulePersistence(monotonicSeconds))
        {
            RequestPersistence(monotonicSeconds);
        }
        if (capabilities.SleepAdvancedTime.State != AdapterCapabilityState.Supported || sleepAdvanceMethod == null) return;
        if (!patchInspectionScheduler.TryTake(nowUtc, 1, out _)) return;
        var patcher = patcherLease.Value;
        if (patcher == null)
        {
            DisableSleep("Sleep tracking disabled after GameClock.Step patch drift: the Harmony owner is unavailable.");
            return;
        }
        if (!patcher.IsPatchSetStampCurrent(sleepPatchStamp, out var detail))
            DisableSleep($"Sleep tracking disabled after GameClock.Step patch drift: {detail}");
    }

    public bool FlushPending() => PublishPending(NowMonotonic(), requestDurability: true);

    private bool PublishPending(double monotonicSeconds, bool requestDurability)
    {
        var changed = false;
        try
        {
            var succeeded = boundary.FlushPending(mutation =>
            {
                if (!recordHandler(mutation)) return false;
                changed = true;
                return true;
            });
            persistenceCadence.RecordPublicationAttempt(succeeded, changed, monotonicSeconds);
            if (!succeeded) return false;
            return !requestDurability
                || !persistenceCadence.ShouldSchedulePersistence(monotonicSeconds, force: true)
                || RequestPersistence(monotonicSeconds);
        }
        catch (Exception exception)
        {
            persistenceCadence.RecordPublicationAttempt(succeeded: false, changed: false, monotonicSeconds);
            DiagnosticOnce("flush-exception", $"World-time aggregate flush failed and remains pending: {Unwrap(exception).Message}");
            return false;
        }
    }

    private bool RequestPersistence(double monotonicSeconds)
    {
        try
        {
            var succeeded = persistenceHandler();
            persistenceCadence.RecordPersistenceAttempt(succeeded, monotonicSeconds);
            if (!succeeded)
                DiagnosticOnce("persistence-request", "World-time profile persistence request failed and will be retried.");
            return succeeded;
        }
        catch (Exception exception)
        {
            persistenceCadence.RecordPersistenceAttempt(succeeded: false, monotonicSeconds);
            DiagnosticOnce("persistence-exception", $"World-time profile persistence request failed and will be retried: {Unwrap(exception).Message}");
            return false;
        }
    }

    public void BeginProfileChangeAwaitingNativeLoad(long transitionId)
    {
        var currentClockInstance = GameClock.Instance;
        var currentReading = ObserveCurrentClockBeforeProfileHandoff(currentClockInstance);
        profileHandoff.BeginAwaitingNativeLoad(transitionId, currentClockInstance, currentReading);
        boundary.ClearPendingSleep();
        DiagnosticOnce(
            "profile-awaiting-load-start:" + transitionId,
            "World-time capture is awaiting the profile-open result: callbacks from the captured clock are buffered for a proven same-profile reopen, while only a replacement instance can hydrate a changed generation.");
    }

    public void BeginNewGameProfileChange(long transitionId)
    {
        var currentClockInstance = GameClock.Instance;
        ObserveCurrentClockBeforeProfileHandoff(currentClockInstance);
        boundary.ClearPendingSleep();
        if (currentClockInstance == null)
        {
            profileHandoff.BeginAwaitingNativeLoad(transitionId, currentClockInstance: null);
            DiagnosticOnce(
                "new-game-awaiting-clock-start:" + transitionId,
                "New-game world-time capture is awaiting the native clock because no loaded instance was available at the report boundary.");
            return;
        }

        HandleObservationResult(profileHandoff.BeginNewGame(transitionId, ReadClock()));
        DiagnosticOnce(
            "new-game-buffer-start:" + transitionId,
            "New-game world-time capture buffered the already-loaded native clock before returning to Duckov boot processing.");
    }

    public void CompleteProfileChange(long transitionId)
    {
        CompleteProfileChange(transitionId, preserveCurrentClock: false);
    }

    public void CompleteProfileChangeWithCurrentClock(long transitionId)
    {
        CompleteProfileChange(transitionId, preserveCurrentClock: true);
    }

    private void CompleteProfileChange(long transitionId, bool preserveCurrentClock)
    {
        var generationId = generationIdProvider();
        persistenceCadence.Start(NowMonotonic());
        var currentClockInstance = GameClock.Instance;
        var currentReading = currentClockInstance == null ? (WorldClockReading?)null : ReadClock();
        var completed = preserveCurrentClock
            ? profileHandoff.CompleteProfileChangeWithCurrentClock(
                transitionId,
                generationId,
                boundary,
                currentClockInstance,
                currentReading,
                out var completionObservation,
                out var completedSleepTransferred)
            : profileHandoff.CompleteProfileChange(
                transitionId,
                generationId,
                boundary,
                currentReading,
                out completionObservation,
                out completedSleepTransferred);
        if (!completed)
        {
            DiagnosticOnce(
                "profile-handoff-superseded:" + transitionId,
                "A superseded world-time profile handoff completion was ignored.");
            return;
        }

        if (completionObservation.HasValue) HandleObservationResult(completionObservation.Value);
        if (completedSleepTransferred
            && !PublishPending(NowMonotonic(), requestDurability: true))
            DiagnosticOnce(
                "profile-handoff-sleep-persistence:" + transitionId,
                "Completed sleep transferred from the profile handoff remains queued until its exact world-time mutation can be persisted.");
        DiagnosticOnce(
            "profile-handoff-complete:" + transitionId,
            "World-time profile handoff completed without attributing stale prior-slot callbacks or losing buffered boot advancement.");
    }

    public void ResetForProfileChangeWithCurrentClock()
    {
        var generationId = generationIdProvider();
        persistenceCadence.Start(NowMonotonic());
        var currentReading = GameClock.Instance == null ? (WorldClockReading?)null : ReadClock();
        var observation = profileHandoff.ResetCurrentProfile(
            generationId,
            currentReading,
            boundary,
            out var awaitingNativeLoad);
        if (awaitingNativeLoad)
        {
            DiagnosticOnce(
                "profile-reset-preserved-awaiting-load:" + generationId,
                "World-time statistics reset preserved the selected slot's native-load handoff; the prior slot clock remains ineligible as a baseline.");
            return;
        }

        if (!observation.HasValue)
        {
            DiagnosticOnce(
                "profile-reset-awaiting-clock:" + generationId,
                "World-time baseline reset for the active save generation; the next native clock observation will establish its baseline.");
            return;
        }

        HandleObservationResult(observation.Value);
        DiagnosticOnce(
            "profile-reset-current-clock:" + generationId,
            "World-time baseline reset from the already-loaded native clock so subsequent new-game boot advancement remains observable.");
    }

    public bool TryCleanup()
    {
        if (!FlushPending())
        {
            DiagnosticOnce("cleanup-flush", "World-time aggregate cleanup remains pending until the retained mutation is accepted.");
            return false;
        }
        WorldTimeHarmonyBridge.Detach(this);
        var subscriptionsCleaned = callbackLifetime.TryCleanup(() => true, out var subscriptionFailure);
        if (subscriptionFailure != null)
            DiagnosticOnce("cleanup-subscription", $"World-time event cleanup remains pending: {subscriptionFailure.Message}");
        var patchesCleaned = patcherLease.TryCleanup(out var patchDetail);
        if (!patchesCleaned)
            DiagnosticOnce("cleanup-patches", $"World-time patch cleanup remains pending and will be retried: {patchDetail}");
        if (subscriptionsCleaned && patchesCleaned)
        {
            sleepAdvanceMethod = null;
            sleepPatchStamp = null;
            profileHandoff.Reset();
            boundary.ClearPendingSleep();
        }
        return subscriptionsCleaned && patchesCleaned;
    }

    public void Dispose() => TryCleanup();

    internal NativeSleepPatchState BeginNativeSleepAdvance(float seconds)
    {
        if (!callbackLifetime.CanHandleCallbacks
            || capabilities.SleepAdvancedTime.State != AdapterCapabilityState.Supported
            || float.IsNaN(seconds) || float.IsInfinity(seconds) || seconds < 0f
            || GameClock.Instance == null)
            return default;
        var reading = ReadClock();
        if (!WorldTimeObservationTracker.TryCoordinate(reading, out var coordinate, out _)) return default;
        var ticks = TimeSpan.FromSeconds(seconds).Ticks;
        var profileTransitionId = profileHandoff.TryGetActiveTransitionId(out var activeTransitionId)
            ? activeTransitionId
            : 0;
        return new NativeSleepPatchState(
            true,
            generationIdProvider(),
            coordinate,
            ticks,
            profileTransitionId);
    }

    internal void CompleteNativeSleepAdvance(NativeSleepPatchState state)
    {
        if (!state.Valid || GameClock.Instance == null) return;
        var reading = ReadClock();
        if (!WorldTimeObservationTracker.TryCoordinate(reading, out var coordinate, out var detail)
            || coordinate < state.BeforeCoordinate)
        {
            DisableSleep($"Sleep advancement became contradictory: {detail}");
            return;
        }
        if (!SleepAdvanceContract.TryValidate(
                state.RequestedTicks,
                state.BeforeCoordinate,
                coordinate,
                out var actual,
                out var validationDetail))
        {
            DisableSleep(validationDetail);
            return;
        }
        var accepted = state.IsProfileHandoff
            ? profileHandoff.BeginSleepCompletion(state.ProfileTransitionId, actual)
            : boundary.BeginSleepCompletion(state.GenerationId, actual);
        if (!accepted)
            DisableSleep("Overlapping or duplicate native sleep advancement was rejected.");
    }

    private void OnGameClockStep()
    {
        if (capabilities.ObservedElapsed.State != AdapterCapabilityState.Supported || GameClock.Instance == null) return;
        try
        {
            var instance = GameClock.Instance;
            var result = profileHandoff.Observe(
                generationIdProvider(),
                instance,
                ReadClock(),
                boundary);
            if (result.HasValue) HandleObservationResult(result.Value);
        }
        catch (Exception exception)
        {
            DisableClock($"World clock observation failed safely: {Unwrap(exception).Message}");
        }
    }

    private WorldClockReading? ObserveCurrentClockBeforeProfileHandoff(object? currentClockInstance)
    {
        if (capabilities.ObservedElapsed.State != AdapterCapabilityState.Supported
            || currentClockInstance == null)
            return null;
        try
        {
            var reading = ReadClock();
            var result = profileHandoff.Observe(
                generationIdProvider(),
                currentClockInstance,
                reading,
                boundary);
            if (result.HasValue) HandleObservationResult(result.Value);
            return reading;
        }
        catch (Exception exception)
        {
            DisableClock($"World clock handoff snapshot failed safely: {Unwrap(exception).Message}");
            return null;
        }
    }

    private void OnAfterSleep()
    {
        try
        {
            if (capabilities.CompletedSleepSessions.State != AdapterCapabilityState.Supported) return;
            if (profileHandoff.CompleteSleep()) return;
            if (!boundary.CompleteSleep(generationIdProvider()))
            {
                DiagnosticOnce("sleep-completion-without-candidate", "Sleep completion callback had no matching exact advancement candidate and was not counted.");
                return;
            }
            if (!PublishPending(NowMonotonic(), requestDurability: true))
                DiagnosticOnce("sleep-completion-persistence", "Completed sleep remains queued until its exact world-time mutation can be persisted.");
        }
        catch (Exception exception)
        {
            DisableSleep($"Sleep completion failed safely: {Unwrap(exception).Message}");
        }
    }

    private void EstablishBaseline()
    {
        if (GameClock.Instance == null) return;
        boundary.ObserveClock(generationIdProvider(), ReadClock());
    }

    private void TryInitializeSleepPatch(string clockProvenance)
    {
        const string sleepProvenance =
            "Exact GameClock.Step(float) advancement paired once with Duckov.UI.SleepView.OnAfterSleep.";
        try
        {
            sleepAdvanceMethod = typeof(GameClock).GetMethod(
                "Step",
                BindingFlags.Static | BindingFlags.NonPublic,
                binder: null,
                types: [typeof(float)],
                modifiers: null)
                ?? throw new MissingMethodException("GameClock.Step(float)");
            if (sleepAdvanceMethod.ReturnType != typeof(void))
                throw new MissingMethodException("GameClock.Step(float) return type is incompatible.");
            if (!ReflectiveHarmonyPatcher.TryCreate(HarmonyId, out var patcher, out var harmonyDetail) || patcher == null)
                throw new InvalidOperationException(harmonyDetail);
            patcherLease.Attach(patcher);
            if (!patcher.IsPatchSetTrusted(sleepAdvanceMethod, Array.Empty<HarmonyPatchExpectation>(), out var trustDetail))
                throw new InvalidOperationException($"Unsafe pre-existing GameClock.Step patch set: {trustDetail}");
            WorldTimeHarmonyBridge.Attach(this);
            patcher.Patch(
                sleepAdvanceMethod,
                WorldTimeHarmonyCallbacks.SleepAdvancePrefixMethod,
                WorldTimeHarmonyCallbacks.SleepAdvancePostfixMethod);
            HarmonyPatchExpectation[] expected =
            [
                new("Prefixes", WorldTimeHarmonyCallbacks.SleepAdvancePrefixMethod),
                new("Postfixes", WorldTimeHarmonyCallbacks.SleepAdvancePostfixMethod)
            ];
            if (!patcher.TryCaptureValidatedPatchSetStamp(
                    sleepAdvanceMethod,
                    expected,
                    out sleepPatchStamp,
                    out var stampDetail)
                || sleepPatchStamp == null)
                throw new InvalidOperationException(stampDetail);
            capabilities = WorldTimeNativeContractPolicy.Supported(clockProvenance, sleepProvenance);
            patchInspectionScheduler.Reset(DateTime.UtcNow, 1);
        }
        catch (Exception exception)
        {
            WorldTimeHarmonyBridge.Detach(this);
            patcherLease.TryCleanup(out _);
            sleepAdvanceMethod = null;
            sleepPatchStamp = null;
            capabilities = WorldTimeNativeContractPolicy.ClockSupportedSleepUnavailable(
                clockProvenance,
                $"Exact sleep contract unavailable: {Unwrap(exception).GetType().Name}: {Unwrap(exception).Message}");
            DiagnosticOnce("sleep-unavailable", capabilities.SleepAdvancedTime.Provenance);
        }
    }

    private void DisableClock(string detail)
    {
        capabilities.CalendarDays = WorldTimeNativeContractPolicy.Availability(AdapterCapabilityState.DisabledIncompatible, detail);
        capabilities.ObservedElapsed = WorldTimeNativeContractPolicy.Availability(AdapterCapabilityState.DisabledIncompatible, detail);
        Publish();
        DiagnosticOnce("clock-disabled:" + detail, detail);
    }

    private void DisableSleep(string detail)
    {
        capabilities.CompletedSleepSessions = WorldTimeNativeContractPolicy.Availability(AdapterCapabilityState.DisabledIncompatible, detail);
        capabilities.SleepAdvancedTime = WorldTimeNativeContractPolicy.Availability(AdapterCapabilityState.DisabledIncompatible, detail);
        boundary.ClearPendingSleep();
        WorldTimeHarmonyBridge.Detach(this);
        Publish();
        DiagnosticOnce("sleep-disabled:" + detail, detail);
    }

    private IReadOnlyList<CapabilityRecord> DisableAll(string detail)
    {
        capabilities = WorldTimeNativeContractPolicy.Unavailable(detail);
        Publish();
        DiagnosticOnce("all-disabled:" + detail, detail);
        return Records();
    }

    private IReadOnlyList<CapabilityRecord> Records() =>
        WorldTimeNativeContractPolicy.ToRecords(capabilities, AdapterVersion);

    private void Publish() => capabilityHandler(Records(), MetricCapabilities);

    private void DiagnosticOnce(string key, string detail)
    {
        if (diagnosticKeys.Count >= 48 || !diagnosticKeys.Add(key)) return;
        try { diagnosticHandler(detail); }
        catch { }
    }

    private static WorldClockReading ReadClock() => new(GameClock.Day, GameClock.TimeOfDay.Ticks);

    private void HandleObservationResult(WorldTimeObservationResult result)
    {
        if (result.State is WorldTimeObservationState.Invalid
            or WorldTimeObservationState.Backward
            or WorldTimeObservationState.Overflow)
            DisableClock(result.Detail);
    }

    private double NowMonotonic() => monotonicClock.Elapsed.TotalSeconds;

    private static Exception Unwrap(Exception exception) =>
        exception is TargetInvocationException { InnerException: not null } invocation
            ? invocation.InnerException
            : exception;
}
