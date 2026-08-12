using System.Reflection;
using ItemStatsSystem;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;
using UnityEngine;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class NativeContainerAdapter : IDisposable, IRetryableCleanup
{
    internal const string AdapterVersion = "native-container-loot-access/2.3.30";
    internal const string HarmonyId = "at.bamboechop.ultimate-duckov-statistics.containers";
    private const string SupportedGameVersion = "2.3.30";
    private const string SupportedGameBuild = "24013657";
    private const int DiagnosticKeyCapacity = 32;
    private readonly Func<string> saveGenerationIdProvider;
    private readonly Func<string?> runIdProvider;
    private readonly Func<string?> mapIdProvider;
    private readonly Func<bool> runActiveProvider;
    private readonly Func<ContainerLooted, bool> recordHandler;
    private readonly Action<IReadOnlyList<CapabilityRecord>> capabilityHandler;
    private readonly Action<ContainerMetricCapabilities> runCapabilityHandler;
    private readonly Action<string> diagnosticHandler;
    private readonly NativeCallbackLifetime callbackLifetime = new();
    private readonly RetryableHarmonyPatcherLease patcherLease = new();
    private readonly HashSet<string> diagnosticKeys = new(StringComparer.Ordinal);
    private ContainerMetricCapabilities capabilities = ContainerNativeContractPolicy.Unavailable(
        "Container capability has not been initialized.");
    private MethodInfo? getKeyMethod;
    private FieldInfo? interactCharacterField;
    private PatchRegistration[] registrations = Array.Empty<PatchRegistration>();
    private DateTime nextConflictCheckUtc;

    public NativeContainerAdapter(
        Func<string> saveGenerationIdProvider,
        Func<string?> runIdProvider,
        Func<string?> mapIdProvider,
        Func<bool> runActiveProvider,
        Func<ContainerLooted, bool> recordHandler,
        Action<IReadOnlyList<CapabilityRecord>> capabilityHandler,
        Action<ContainerMetricCapabilities> runCapabilityHandler,
        Action<string> diagnosticHandler)
    {
        this.saveGenerationIdProvider = saveGenerationIdProvider ?? throw new ArgumentNullException(nameof(saveGenerationIdProvider));
        this.runIdProvider = runIdProvider ?? throw new ArgumentNullException(nameof(runIdProvider));
        this.mapIdProvider = mapIdProvider ?? throw new ArgumentNullException(nameof(mapIdProvider));
        this.runActiveProvider = runActiveProvider ?? throw new ArgumentNullException(nameof(runActiveProvider));
        this.recordHandler = recordHandler ?? throw new ArgumentNullException(nameof(recordHandler));
        this.capabilityHandler = capabilityHandler ?? throw new ArgumentNullException(nameof(capabilityHandler));
        this.runCapabilityHandler = runCapabilityHandler ?? throw new ArgumentNullException(nameof(runCapabilityHandler));
        this.diagnosticHandler = diagnosticHandler ?? throw new ArgumentNullException(nameof(diagnosticHandler));
    }

    public ContainerMetricCapabilities MetricCapabilities => ContainerStatisticsReducer.CloneCapabilities(capabilities);

    public IReadOnlyList<CapabilityRecord> Initialize()
    {
        if (callbackLifetime.DisposalStarted) throw new ObjectDisposedException(nameof(NativeContainerAdapter));
        if (callbackLifetime.IsActive) return Records();
        var installedVersion = Application.version ?? string.Empty;
        if (!string.Equals(installedVersion, SupportedGameVersion, StringComparison.Ordinal))
            return Disable($"Installed Duckov version '{installedVersion}' does not match verified version '{SupportedGameVersion}'.");

        try
        {
            ResolveContracts(out var characterDeath, out var createFromItem);
            if (!ReflectiveHarmonyPatcher.TryCreate(HarmonyId, out var patcher, out var harmonyDetail) || patcher == null)
                return Disable($"Container corpse provenance is unavailable: {harmonyDetail}");
            patcherLease.Attach(patcher);
            registrations =
            [
                new PatchRegistration(characterDeath,
                [
                    new HarmonyPatchExpectation("Prefixes", ContainerHarmonyCallbacks.CharacterDeathPrefixMethod),
                    new HarmonyPatchExpectation("Finalizers", ContainerHarmonyCallbacks.CharacterDeathFinalizerMethod)
                ]),
                new PatchRegistration(createFromItem,
                [new HarmonyPatchExpectation("Postfixes", ContainerHarmonyCallbacks.CreateFromItemPostfixMethod)])
            ];
            foreach (var registration in registrations)
            {
                if (!patcher.IsPatchSetTrusted(registration.Original, Array.Empty<HarmonyPatchExpectation>(), out var detail))
                    throw new InvalidOperationException($"Unsafe pre-existing patch set on {registration.Original.DeclaringType?.Name}.{registration.Original.Name}: {detail}");
            }
            ContainerHarmonyBridge.Attach(this);
            patcher.Patch(characterDeath, ContainerHarmonyCallbacks.CharacterDeathPrefixMethod,
                finalizer: ContainerHarmonyCallbacks.CharacterDeathFinalizerMethod);
            patcher.Patch(createFromItem, postfix: ContainerHarmonyCallbacks.CreateFromItemPostfixMethod);
            foreach (var registration in registrations)
            {
                if (!patcher.IsPatchSetTrusted(registration.Original, registration.Expected, out var detail))
                    throw new InvalidOperationException($"Installed container patch validation failed: {detail}");
            }

            var guarded = callbackLifetime.Guard<InteractableLootbox>(OnStartLoot);
            callbackLifetime.Activate(
            [
                new SubscriptionBinding(
                    () => InteractableLootbox.OnStartLoot += guarded,
                    () => InteractableLootbox.OnStartLoot -= guarded)
            ]);
            capabilities = ContainerNativeContractPolicy.Supported();
            Publish();
            nextConflictCheckUtc = DateTime.UtcNow.AddSeconds(2);
            diagnosticHandler(
                $"Container access hook subscribed; GetKey and exact-main-duck ownership verified; corpse provenance patches active with HarmonyLib {patcher.Version}.");
        }
        catch (Exception exception)
        {
            Disable($"Container activation failed: {Unwrap(exception).GetType().Name}: {Unwrap(exception).Message}");
            TryCleanup();
        }
        return Records();
    }

    public void Tick()
    {
        if (!callbackLifetime.CanHandleCallbacks
            || capabilities.UniqueContainersLooted.State != AdapterCapabilityState.Supported
            || DateTime.UtcNow < nextConflictCheckUtc) return;
        try
        {
            nextConflictCheckUtc = DateTime.UtcNow.AddSeconds(2);
            var patcher = patcherLease.Value;
            foreach (var registration in registrations)
            {
                var detail = patcher == null ? "The container Harmony owner is unavailable." : string.Empty;
                if (patcher == null || !patcher.IsPatchSetTrusted(registration.Original, registration.Expected, out detail))
                {
                    DisableRuntime($"Container tracking disabled after corpse-provenance patch drift: {detail}");
                    return;
                }
            }
        }
        catch (Exception exception)
        {
            DisableRuntime(
                $"Container tracking disabled because corpse-provenance patch verification failed: "
                + $"{Unwrap(exception).GetType().Name}: {Unwrap(exception).Message}");
        }
    }

    public bool TryCleanup()
    {
        ContainerHarmonyBridge.Detach(this);
        var subscriptionsCleaned = callbackLifetime.TryCleanup(() => true, out var subscriptionFailure);
        if (subscriptionFailure != null)
            DiagnosticOnce("cleanup-subscription",
                $"Container event cleanup remains pending: {subscriptionFailure.GetType().Name}: {subscriptionFailure.Message}");
        var patchesCleaned = patcherLease.TryCleanup(out var patchDetail);
        if (!patchesCleaned)
            DiagnosticOnce("cleanup-patches", $"Container patch cleanup remains pending and will be retried: {patchDetail}");
        if (subscriptionsCleaned && patchesCleaned) registrations = Array.Empty<PatchRegistration>();
        return subscriptionsCleaned && patchesCleaned;
    }

    public void Dispose() => TryCleanup();

    private void OnStartLoot(InteractableLootbox lootbox)
    {
        try
        {
            ObserveStartLoot(lootbox);
        }
        catch (Exception exception)
        {
            DisableRuntime(
                $"Container tracking disabled after an unexpected successful-access observation failure: "
                + $"{Unwrap(exception).GetType().Name}: {Unwrap(exception).Message}");
        }
    }

    private void ObserveStartLoot(InteractableLootbox lootbox)
    {
        if (lootbox == null || capabilities.UniqueContainersLooted.State != AdapterCapabilityState.Supported) return;
        var generationId = saveGenerationIdProvider();
        var runId = runIdProvider() ?? string.Empty;
        var mapId = mapIdProvider() ?? MapIdentity.UnknownId;
        var runActive = runActiveProvider();
        var raid = NativeRaidContext.GetGameplayContext() == GameplayContext.Raid;
        CharacterMainControl? actor = null;
        try { actor = interactCharacterField?.GetValue(lootbox) as CharacterMainControl; }
        catch (Exception exception) { DiagnosticOnce("actor-read", $"Container actor evidence failed: {Unwrap(exception).Message}"); }
        var exactMain = actor != null && actor.IsMainCharacter && ReferenceEquals(actor, CharacterMainControl.Main);
        var corpse = ContainerHarmonyBridge.TryGetCorpseProvenance(lootbox, out var corpseProvenance);
        if (runActive && raid && exactMain && corpse)
        {
            DiagnosticOnce(
                "excluded-corpse:" + corpseProvenance,
                $"Container access excluded as corpse loot by {corpseProvenance} provenance.");
        }
        if (!ContainerLootAcceptancePolicy.RequiresStableIdentity(runActive, raid, exactMain, corpse)
            || string.IsNullOrWhiteSpace(generationId)
            || string.IsNullOrWhiteSpace(runId)) return;

        var stableKey = ContainerLootAcceptancePolicy.TryReadStableKey(
            () => getKeyMethod?.Invoke(lootbox, null),
            out var key,
            out var keyDetail);
        if (!stableKey)
        {
            DisableRuntime(
                $"Container tracking disabled because a qualifying successful access had no usable stable GetKey identity: {keyDetail}");
            return;
        }

        var recorded = recordHandler(new ContainerLooted
        {
            EventId = Guid.NewGuid().ToString("N"),
            TimestampUtc = DateTime.UtcNow,
            SaveGenerationId = generationId,
            RunId = runId,
            MapId = mapId,
            GameVersion = Application.version ?? string.Empty,
            GameBuild = SupportedGameBuild,
            GameplayContext = GameplayContext.Raid,
            IntegrityTags = NativeIntegrityProbe.Read(),
            ContainerKey = key,
            AdapterVersion = AdapterVersion
        });
        DiagnosticOnce(
            $"container-key:{runId}:{key}:{recorded}",
            recorded
                ? $"Container access accepted run={runId} stableKey={key}."
                : $"Container access not added by the active-run reducer run={runId} stableKey={key}; duplicate or context changed.");
    }

    private void DisableRuntime(string detail)
    {
        capabilities = ContainerNativeContractPolicy.Unavailable(detail);
        ContainerHarmonyBridge.Detach(this);
        try
        {
            Publish();
        }
        catch (Exception exception)
        {
            DiagnosticOnce(
                "runtime-disable-publish",
                $"Container runtime-disable capability publication failed safely: {Unwrap(exception).Message}");
        }
        DiagnosticOnce("runtime-disabled:" + detail, detail);
    }

    private void ResolveContracts(out MethodInfo characterDeath, out MethodInfo createFromItem)
    {
        getKeyMethod = Exact(typeof(InteractableLootbox), "GetKey", BindingFlags.Instance | BindingFlags.NonPublic, typeof(int));
        interactCharacterField = typeof(InteractableBase).GetField("interactCharacter", BindingFlags.Instance | BindingFlags.NonPublic);
        characterDeath = Exact(typeof(CharacterMainControl), "OnDead", BindingFlags.Instance | BindingFlags.NonPublic,
            typeof(void), typeof(DamageInfo)) ?? throw new MissingMethodException("CharacterMainControl.OnDead(DamageInfo)");
        createFromItem = Exact(typeof(InteractableLootbox), "CreateFromItem", BindingFlags.Static | BindingFlags.Public,
            typeof(InteractableLootbox), typeof(Item), typeof(Vector3), typeof(Quaternion), typeof(bool),
            typeof(InteractableLootbox), typeof(bool)) ?? throw new MissingMethodException("InteractableLootbox.CreateFromItem");
        if (getKeyMethod == null) throw new MissingMethodException("InteractableLootbox.GetKey()");
        if (interactCharacterField?.FieldType != typeof(CharacterMainControl))
            throw new MissingFieldException("InteractableBase.interactCharacter");
    }

    private IReadOnlyList<CapabilityRecord> Disable(string detail)
    {
        capabilities = ContainerNativeContractPolicy.Unavailable(detail);
        Publish();
        DiagnosticOnce("disabled:" + detail, detail);
        return Records();
    }

    private IReadOnlyList<CapabilityRecord> Records() =>
        [ContainerNativeContractPolicy.ToRecord(capabilities, AdapterVersion)];

    private void Publish()
    {
        capabilityHandler(Records());
        runCapabilityHandler(ContainerStatisticsReducer.CloneCapabilities(capabilities));
    }

    private void DiagnosticOnce(string key, string detail)
    {
        if (diagnosticKeys.Count >= DiagnosticKeyCapacity || !diagnosticKeys.Add(key)) return;
        try { diagnosticHandler(detail); }
        catch { }
    }

    private static MethodInfo? Exact(
        Type type,
        string name,
        BindingFlags flags,
        Type returnType,
        params Type[] parameters) => type.GetMethods(flags).SingleOrDefault(method =>
            method.Name == name
            && method.ReturnType == returnType
            && method.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(parameters));

    private static Exception Unwrap(Exception exception) =>
        exception is TargetInvocationException { InnerException: not null } invocation
            ? invocation.InnerException
            : exception;

    private sealed class PatchRegistration
    {
        public PatchRegistration(MethodInfo original, HarmonyPatchExpectation[] expected)
        { Original = original; Expected = expected; }
        public MethodInfo Original { get; }
        public HarmonyPatchExpectation[] Expected { get; }
    }
}
