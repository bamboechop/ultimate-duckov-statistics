using System.Reflection;
using Duckov.Buffs;
using ItemStatsSystem;
using UltimateDuckovStatistics.Core.Classification;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Tracking;
using UnityEngine;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class NativeHealingAttributionAdapter : IHealingAttributionObserver, IDisposable
{
    internal const string AdapterId = "native-healing-attribution";
    internal const string AdapterVersion = "native-healing-attribution/2.3.30+harmony-2.4.1";
    private readonly Action<HealingApplied> healingHandler;
    private readonly Action<string> diagnosticHandler;
    private readonly HealingAttributionTracker tracker;
    private readonly Dictionary<int, string?> itemApplicationScopes = new();
    private ReflectiveHarmonyPatcher? patcher;
    private PatchRegistration[] patchRegistrations = Array.Empty<PatchRegistration>();
    private CharacterBuffManager? subscribedBuffManager;
    private bool lifecycleSubscribed;
    private bool retryWhenHarmonyLoads;
    private DateTime nextInitializationAttemptUtc;
    private DateTime nextConflictCheckUtc;
    private bool conflictCleanupPending;
    private bool disposed;

    public NativeHealingAttributionAdapter(
        Action<HealingApplied> healingHandler,
        Action<string> diagnosticHandler)
    {
        this.healingHandler = healingHandler ?? throw new ArgumentNullException(nameof(healingHandler));
        this.diagnosticHandler = diagnosticHandler ?? throw new ArgumentNullException(nameof(diagnosticHandler));
        tracker = new HealingAttributionTracker(() => Guid.NewGuid().ToString("N"));
        Capability = Disabled("Healing attribution has not been initialized.");
    }

    public CapabilityRecord Capability { get; private set; }

    public event Action<CapabilityRecord>? CapabilityChanged;

    public CapabilityRecord Initialize()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(NativeHealingAttributionAdapter));
        }

        if (Capability.State == AdapterCapabilityState.Supported && patcher != null)
        {
            return Capability;
        }

        if (!TryResolveContracts(out var healthMethod, out var effectMethod, out var buffMethod, out var contractFailure))
        {
            retryWhenHarmonyLoads = false;
            SetCapability(Disabled(contractFailure));
            diagnosticHandler(contractFailure);
            return Capability;
        }

        if (!ReflectiveHarmonyPatcher.TryCreate(out patcher, out var harmonyDetail) || patcher == null)
        {
            retryWhenHarmonyLoads = !ReflectiveHarmonyPatcher.IsHarmonyLoaded;
            nextInitializationAttemptUtc = DateTime.UtcNow.AddSeconds(1);
            SetCapability(Disabled(harmonyDetail));
            diagnosticHandler(harmonyDetail);
            return Capability;
        }

        try
        {
            foreach (var method in new[] { healthMethod, effectMethod, buffMethod })
            {
                if (!patcher.IsPatchSetTrusted(
                        method,
                        Array.Empty<HarmonyPatchExpectation>(),
                        out var patchSetDetail))
                {
                    patcher.Dispose();
                    patcher = null;
                    retryWhenHarmonyLoads = false;
                    SetCapability(Disabled(
                        $"Healing attribution disabled because {method.DeclaringType?.Name}.{method.Name} has an unsafe pre-existing Harmony patch set: {patchSetDetail}"));
                    diagnosticHandler(Capability.Detail!);
                    return Capability;
                }
            }

            var registrations = CreatePatchRegistrations(healthMethod, effectMethod, buffMethod);
            HealingHarmonyBridge.Attach(this);
            patcher.Patch(
                healthMethod,
                HealingHarmonyCallbacks.HealthPrefixMethod,
                HealingHarmonyCallbacks.HealthPostfixMethod);
            patcher.Patch(
                effectMethod,
                HealingHarmonyCallbacks.EffectPrefixMethod,
                finalizer: HealingHarmonyCallbacks.EffectFinalizerMethod);
            patcher.Patch(buffMethod, postfix: HealingHarmonyCallbacks.BuffPostfixMethod);
            patchRegistrations = registrations;
            foreach (var registration in patchRegistrations)
            {
                if (!patcher.IsPatchSetTrusted(
                        registration.Original,
                        registration.ExpectedOwnedPatches,
                        out var patchSetDetail))
                {
                    throw new InvalidOperationException(
                        $"Installed Harmony patch set validation failed for "
                        + $"{registration.Original.DeclaringType?.Name}.{registration.Original.Name}: "
                        + patchSetDetail);
                }
            }

            RaidUtilities.OnNewRaid += OnRaidTransition;
            RaidUtilities.OnRaidEnd += OnRaidTransition;
            lifecycleSubscribed = true;
            retryWhenHarmonyLoads = false;
            nextConflictCheckUtc = DateTime.UtcNow.AddSeconds(1);
            SetCapability(new CapabilityRecord
            {
                AdapterId = AdapterId,
                State = HealingCapabilityPolicy.GetState(HealingCapabilityCondition.Available),
                Version = AdapterVersion,
                Detail = $"Exact main-duck Health.AddHealth attribution via HarmonyLib {patcher.Version}; no Harmony assembly is bundled."
            });
            diagnosticHandler($"Healing attribution patches active with HarmonyLib {patcher.Version}.");
        }
        catch (Exception exception)
        {
            try
            {
                patcher?.Dispose();
            }
            catch
            {
                // Preserve the original capability failure.
            }

            patcher = null;
            HealingHarmonyBridge.Detach(this);
            retryWhenHarmonyLoads = false;
            patchRegistrations = Array.Empty<PatchRegistration>();
            conflictCleanupPending = false;
            SetCapability(Disabled($"Healing patch activation failed: {Unwrap(exception).GetType().Name}: {Unwrap(exception).Message}"));
            diagnosticHandler(Capability.Detail!);
        }

        return Capability;
    }

    public void Tick()
    {
        var nowUtc = DateTime.UtcNow;
        if (conflictCleanupPending)
        {
            CompleteConflictCleanup();
            return;
        }

        if (retryWhenHarmonyLoads && nowUtc >= nextInitializationAttemptUtc)
        {
            Initialize();
        }

        if (Capability.State != AdapterCapabilityState.Supported)
        {
            return;
        }

        if (nowUtc >= nextConflictCheckUtc)
        {
            nextConflictCheckUtc = nowUtc.AddSeconds(2);
            foreach (var registration in patchRegistrations)
            {
                if (!IsRegistrationTrusted(registration, out var patchSetDetail))
                {
                    SchedulePatchSetConflict(registration.Original, patchSetDetail);
                    CompleteConflictCleanup();
                    return;
                }
            }
        }

        CharacterBuffManager? manager = null;
        try
        {
            var character = LevelManager.Instance?.MainCharacter;
            manager = character?.GetBuffManager();
        }
        catch
        {
            manager = null;
        }

        if (ReferenceEquals(manager, subscribedBuffManager))
        {
            return;
        }

        if (subscribedBuffManager != null)
        {
            subscribedBuffManager.onRemoveBuff -= OnBuffRemoved;
        }

        subscribedBuffManager = manager;
        if (subscribedBuffManager != null)
        {
            subscribedBuffManager.onRemoveBuff += OnBuffRemoved;
        }
    }

    public int ExpirePendingBefore(DateTime cutoffUtc)
    {
        var expired = tracker.ExpirePendingBefore(cutoffUtc);
        foreach (var runtimeItemId in itemApplicationScopes.Keys
                     .Where(runtimeItemId => tracker.TryGetUseCorrelation(runtimeItemId) == null)
                     .ToArray())
        {
            EndApplication(runtimeItemId);
        }

        return expired;
    }

    public void BeginUse(ItemUseSnapshot snapshot)
    {
        if (Capability.State != AdapterCapabilityState.Supported)
        {
            return;
        }

        var classification = ItemClassifier.Classify(snapshot.Classification);
        tracker.BeginUse(new HealingUseContext
        {
            CorrelationId = Guid.NewGuid().ToString("N"),
            RuntimeItemId = snapshot.RuntimeItemId,
            StartedUtc = snapshot.TimestampUtc,
            SaveGenerationId = snapshot.SaveGenerationId,
            RunId = snapshot.RunId,
            MapId = snapshot.MapId,
            GameVersion = snapshot.GameVersion,
            GameBuild = snapshot.GameBuild,
            GameplayContext = snapshot.GameplayContext,
            IntegrityTags = snapshot.IntegrityTags,
            AdapterCapability = AdapterCapabilityState.Supported,
            AdapterVersion = AdapterVersion,
            ItemId = snapshot.ItemId,
            DisplayName = snapshot.DisplayName,
            Group = classification.Group
        });
    }

    public void BeginApplication(int runtimeItemId)
    {
        if (Capability.State != AdapterCapabilityState.Supported)
        {
            return;
        }

        EndApplication(runtimeItemId);
        itemApplicationScopes[runtimeItemId] = HealingHarmonyBridge.PushItemApplication(runtimeItemId);
    }

    public void EndApplication(int runtimeItemId)
    {
        if (itemApplicationScopes.TryGetValue(runtimeItemId, out var scopeId))
        {
            HealingHarmonyBridge.Pop(scopeId);
            itemApplicationScopes.Remove(runtimeItemId);
        }
    }

    public void MarkSuccessful(int runtimeItemId)
    {
        // Proof is committed only after CA_UseItem's main-player completion and
        // successful persistence of the corresponding ItemUseRecorded event.
    }

    public void CompleteUse(int runtimeItemId, ItemUseRecorded? successfulUse)
    {
        EndApplication(runtimeItemId);
        foreach (var healing in tracker.CompleteUse(runtimeItemId, successfulUse))
        {
            healingHandler(healing);
        }
    }

    public void Reset()
    {
        foreach (var runtimeItemId in itemApplicationScopes.Keys.ToArray())
        {
            EndApplication(runtimeItemId);
        }

        tracker.Clear();
        HealingHarmonyBridge.ClearScopes();
    }

    public string? TryGetUseCorrelation(int runtimeItemId) => tracker.TryGetUseCorrelation(runtimeItemId);

    public bool IsPatchPointTrusted(HealingPatchPoint patchPoint)
    {
        if (Capability.State != AdapterCapabilityState.Supported)
        {
            return false;
        }

        var registration = patchRegistrations.FirstOrDefault(candidate => candidate.Point == patchPoint);
        if (registration == null)
        {
            SchedulePatchSetConflict(
                method: null,
                $"Required internal patch registration is missing for {patchPoint}.");
            return false;
        }

        if (IsRegistrationTrusted(registration, out var patchSetDetail))
        {
            return true;
        }

        SchedulePatchSetConflict(registration.Original, patchSetDetail);
        return false;
    }

    public string? ResolveEffectCorrelation(EffectAction effectAction)
    {
        if (effectAction == null)
        {
            return null;
        }

        try
        {
            var buff = effectAction.GetComponentInParent<Buff>();
            if (buff != null)
            {
                return tracker.TryGetBuffCorrelation(buff.GetInstanceID());
            }

            var item = effectAction.Master?.Item;
            return item == null ? null : tracker.TryGetUseCorrelation(item.GetInstanceID());
        }
        catch
        {
            return null;
        }
    }

    public void ReconcileAppliedBuff(CharacterBuffManager manager, Buff buffPrefab, string? correlationId)
    {
        try
        {
            if (manager == null
                || buffPrefab == null
                || manager.Master == null
                || !manager.Master.IsMainCharacter)
            {
                return;
            }

            var applied = manager.Buffs.LastOrDefault(buff => buff != null && buff.ID == buffPrefab.ID);
            if (applied == null)
            {
                return;
            }

            var runtimeBuffId = applied.GetInstanceID();
            var changed = tracker.ReconcileBuff(runtimeBuffId, correlationId);
            if (!changed)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(correlationId))
            {
                diagnosticHandler($"Cleared healing provenance for unowned refresh of buff {applied.ID}.");
            }
            else
            {
                diagnosticHandler($"Bound healing buff {applied.ID} to a proven item-use context candidate.");
            }
        }
        catch (Exception exception)
        {
            diagnosticHandler($"Failed to bind healing buff provenance: {exception.GetType().Name}.");
        }
    }

    public void RecordHealthApplication(HealingHealthPatchState state, double actualHealthRestored)
    {
        foreach (var healing in tracker.Observe(
                     state.CorrelationId,
                     new HealingObservation
                     {
                         ApplicationId = state.ApplicationId ?? string.Empty,
                         TimestampUtc = DateTime.UtcNow,
                         ActualHealthRestored = actualHealthRestored,
                         IsMainPlayerTarget = state.IsMainPlayerTarget
                     }))
        {
            healingHandler(healing);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (lifecycleSubscribed)
        {
            RaidUtilities.OnNewRaid -= OnRaidTransition;
            RaidUtilities.OnRaidEnd -= OnRaidTransition;
            lifecycleSubscribed = false;
        }

        if (subscribedBuffManager != null)
        {
            subscribedBuffManager.onRemoveBuff -= OnBuffRemoved;
            subscribedBuffManager = null;
        }

        Reset();
        HealingHarmonyBridge.Detach(this);
        try
        {
            patcher?.Dispose();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }

        patcher = null;
        patchRegistrations = Array.Empty<PatchRegistration>();
        conflictCleanupPending = false;
    }

    private static bool TryResolveContracts(
        out MethodInfo healthMethod,
        out MethodInfo effectMethod,
        out MethodInfo buffMethod,
        out string failure)
    {
        if (!HealingNativeContractResolver.TryResolve(
                typeof(Health),
                typeof(EffectAction),
                typeof(EffectTriggerEventContext),
                typeof(CharacterBuffManager),
                typeof(Buff),
                typeof(CharacterMainControl),
                out var resolvedHealth,
                out var resolvedEffect,
                out var resolvedBuff,
                out failure))
        {
            healthMethod = null!;
            effectMethod = null!;
            buffMethod = null!;
            return false;
        }

        healthMethod = resolvedHealth!;
        effectMethod = resolvedEffect!;
        buffMethod = resolvedBuff!;
        return true;
    }

    private static CapabilityRecord Disabled(string detail) => new()
    {
        AdapterId = AdapterId,
        State = HealingCapabilityPolicy.GetState(HealingCapabilityCondition.ActivationFailure),
        Version = AdapterVersion,
        Detail = detail
    };

    private static PatchRegistration[] CreatePatchRegistrations(
        MethodInfo healthMethod,
        MethodInfo effectMethod,
        MethodInfo buffMethod) =>
    [
        new PatchRegistration(
            HealingPatchPoint.Health,
            healthMethod,
            [
                new HarmonyPatchExpectation("Prefixes", HealingHarmonyCallbacks.HealthPrefixMethod),
                new HarmonyPatchExpectation("Postfixes", HealingHarmonyCallbacks.HealthPostfixMethod)
            ]),
        new PatchRegistration(
            HealingPatchPoint.Effect,
            effectMethod,
            [
                new HarmonyPatchExpectation("Prefixes", HealingHarmonyCallbacks.EffectPrefixMethod),
                new HarmonyPatchExpectation("Finalizers", HealingHarmonyCallbacks.EffectFinalizerMethod)
            ]),
        new PatchRegistration(
            HealingPatchPoint.Buff,
            buffMethod,
            [
                new HarmonyPatchExpectation("Postfixes", HealingHarmonyCallbacks.BuffPostfixMethod)
            ])
    ];

    private bool IsRegistrationTrusted(PatchRegistration registration, out string detail)
    {
        if (patcher == null)
        {
            detail = "The UDS Harmony patcher is unavailable.";
            return false;
        }

        return patcher.IsPatchSetTrusted(
            registration.Original,
            registration.ExpectedOwnedPatches,
            out detail);
    }

    private void SchedulePatchSetConflict(MethodInfo? method, string detail)
    {
        if (conflictCleanupPending)
        {
            return;
        }

        conflictCleanupPending = true;
        retryWhenHarmonyLoads = false;
        if (lifecycleSubscribed)
        {
            RaidUtilities.OnNewRaid -= OnRaidTransition;
            RaidUtilities.OnRaidEnd -= OnRaidTransition;
            lifecycleSubscribed = false;
        }

        if (subscribedBuffManager != null)
        {
            subscribedBuffManager.onRemoveBuff -= OnBuffRemoved;
            subscribedBuffManager = null;
        }

        Reset();
        HealingHarmonyBridge.Detach(this);
        var methodName = method == null
            ? "an internal required hook"
            : $"{method.DeclaringType?.Name}.{method.Name}";
        SetCapability(Disabled(
            $"Healing attribution disabled because {methodName} has an unsafe Harmony patch set: {detail}"));
        diagnosticHandler(Capability.Detail!);
    }

    private void CompleteConflictCleanup()
    {
        if (!conflictCleanupPending)
        {
            return;
        }

        try
        {
            patcher?.Dispose();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }

        patcher = null;
        patchRegistrations = Array.Empty<PatchRegistration>();
        conflictCleanupPending = false;
    }

    private void SetCapability(CapabilityRecord capability)
    {
        var changed = Capability.State != capability.State
                      || !string.Equals(Capability.Version, capability.Version, StringComparison.Ordinal)
                      || !string.Equals(Capability.Detail, capability.Detail, StringComparison.Ordinal);
        Capability = capability;
        if (changed)
        {
            CapabilityChanged?.Invoke(capability);
        }
    }

    private void OnBuffRemoved(CharacterBuffManager manager, Buff buff)
    {
        if (buff != null)
        {
            tracker.RemoveBuff(buff.GetInstanceID());
        }
    }

    private void OnRaidTransition(RaidUtilities.RaidInfo raid) => Reset();

    private static Exception Unwrap(Exception exception) =>
        exception is TargetInvocationException { InnerException: not null } invocation
            ? invocation.InnerException
            : exception;

    private sealed class PatchRegistration
    {
        public PatchRegistration(
            HealingPatchPoint point,
            MethodInfo original,
            HarmonyPatchExpectation[] expectedOwnedPatches)
        {
            Point = point;
            Original = original;
            ExpectedOwnedPatches = expectedOwnedPatches;
        }

        public HealingPatchPoint Point { get; }

        public MethodInfo Original { get; }

        public HarmonyPatchExpectation[] ExpectedOwnedPatches { get; }
    }
}
