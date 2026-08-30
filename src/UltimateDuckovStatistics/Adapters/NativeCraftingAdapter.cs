using System.Globalization;
using System.Reflection;
using Cysharp.Threading.Tasks;
using Duckov.Economy;
using ItemStatsSystem;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;
using UnityEngine;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class NativeCraftingAdapter : IDisposable, IRetryableCleanup
{
    internal const string AdapterVersion =
        "native-crafting/2.3.30+correlated-cost-return-v2+event-cost-v2+duplicate-pay-proof-v3+delivery-gated-capability-v1+resource-hook-isolation-v1+profile-handoff-v1+patch-stamp-v1+deferred-profile-v1";
    internal const string HarmonyId = "at.bamboechop.ultimate-duckov-statistics.crafting";
    private const string SupportedGameVersion = "2.3.30";
    private const int DiagnosticKeyCapacity = 32;
    private readonly object lifecycleSync = new();
    private readonly Func<string> generationIdProvider;
    private readonly Func<CraftingMutation, bool> recordHandler;
    private readonly Func<bool> persistenceHandler;
    private readonly Action<IReadOnlyList<CapabilityRecord>, CraftingMetricCapabilities> capabilityHandler;
    private readonly Action<string> diagnosticHandler;
    private readonly Func<bool> cleanupRetryHandler;
    private readonly CraftingCompletionBoundary boundary = new();
    private readonly CraftingPendingAccumulator pendingPublication = new();
    private readonly CraftingProfileHandoffBoundary profileHandoff = new();
    private readonly CraftingCapabilityPublicationBoundary capabilityPublication = new();
    private readonly RetryableHarmonyPatcherLease patcherLease = new();
    private readonly IncrementalPatchInspectionScheduler patchInspectionScheduler = new(TimeSpan.FromSeconds(2));
    private readonly IncrementalPatchInspectionScheduler pendingRetryScheduler = new(TimeSpan.FromSeconds(1));
    private readonly HashSet<string> diagnosticKeys = new(StringComparer.Ordinal);
    private CraftingMetricCapabilities capabilities = CraftingNativeContractPolicy.Unavailable(
        CraftingNativeContractPolicy.BootstrapProvenance);
    private MethodInfo? craftMethod;
    private MethodInfo? returnMethod;
    private MethodInfo? payMethod;
    private MethodInfo? itemCountMethod;
    private MethodInfo? stackCountSetter;
    private MethodInfo? markDestroyedMethod;
    private HarmonyPatchSetStamp? craftPatchStamp;
    private HarmonyPatchSetStamp? returnPatchStamp;
    private HarmonyPatchSetStamp? payPatchStamp;
    private HarmonyPatchSetStamp? itemCountPatchStamp;
    private HarmonyPatchSetStamp? stackCountPatchStamp;
    private HarmonyPatchSetStamp? markDestroyedPatchStamp;
    private Func<bool>? profileTransitionCleanupBarrier;
    private bool accepting;
    private bool resourceProofHooksActive;
    private bool cleanupRequested;
    private bool terminalShutdownRequested;
    private bool cleaned;

    public NativeCraftingAdapter(
        Func<string> generationIdProvider,
        Func<CraftingMutation, bool> recordHandler,
        Func<bool> persistenceHandler,
        Action<IReadOnlyList<CapabilityRecord>, CraftingMetricCapabilities> capabilityHandler,
        Action<string> diagnosticHandler,
        Func<bool> cleanupRetryHandler)
    {
        this.generationIdProvider = generationIdProvider ?? throw new ArgumentNullException(nameof(generationIdProvider));
        this.recordHandler = recordHandler ?? throw new ArgumentNullException(nameof(recordHandler));
        this.persistenceHandler = persistenceHandler ?? throw new ArgumentNullException(nameof(persistenceHandler));
        this.capabilityHandler = capabilityHandler ?? throw new ArgumentNullException(nameof(capabilityHandler));
        this.diagnosticHandler = diagnosticHandler ?? throw new ArgumentNullException(nameof(diagnosticHandler));
        this.cleanupRetryHandler = cleanupRetryHandler ?? throw new ArgumentNullException(nameof(cleanupRetryHandler));
    }

    public CraftingMetricCapabilities MetricCapabilities
    {
        get
        {
            lock (lifecycleSync)
                return CraftingStatisticsReducer.CloneCapabilities(capabilities);
        }
    }

    public IReadOnlyList<CapabilityRecord> Initialize()
    {
        lock (lifecycleSync)
        {
            if (cleanupRequested || cleaned) throw new ObjectDisposedException(nameof(NativeCraftingAdapter));
            if (accepting) return Records();
        }
        var installedVersion = Application.version ?? string.Empty;
        if (!string.Equals(installedVersion, SupportedGameVersion, StringComparison.Ordinal))
            return Disable($"Installed Duckov version '{installedVersion}' does not match verified version '{SupportedGameVersion}'.");

        try
        {
            ResolveContracts(
                out var resolvedCraftMethod,
                out var resolvedReturnMethod,
                out var resolvedPayMethod,
                out var resolvedItemCountMethod,
                out var resolvedStackCountSetter,
                out var resolvedMarkDestroyedMethod);
            if (!ReflectiveHarmonyPatcher.TryCreate(HarmonyId, out var patcher, out var harmonyDetail) || patcher == null)
                return Disable($"Crafting completion is unavailable: {harmonyDetail}");
            patcherLease.Attach(patcher);
            if (!patcher.IsPatchSetTrusted(resolvedCraftMethod, Array.Empty<HarmonyPatchExpectation>(), out var prePatchDetail))
                throw new InvalidOperationException($"Unsafe pre-existing patch set on CraftingManager.Craft(CraftingFormula): {prePatchDetail}");
            if (!patcher.IsPatchSetTrusted(resolvedReturnMethod, Array.Empty<HarmonyPatchExpectation>(), out prePatchDetail))
                throw new InvalidOperationException($"Unsafe pre-existing patch set on Cost.Return: {prePatchDetail}");
            if (!patcher.IsPatchSetTrusted(resolvedPayMethod, Array.Empty<HarmonyPatchExpectation>(), out prePatchDetail))
                throw new InvalidOperationException($"Unsafe pre-existing patch set on EconomyManager.Pay(Cost): {prePatchDetail}");
            string? resourceHookDetail = null;
            if (!patcher.IsPatchSetTrusted(resolvedItemCountMethod, Array.Empty<HarmonyPatchExpectation>(), out prePatchDetail))
                resourceHookDetail = ResourceHookUnavailableDetail(
                    "ItemUtilities.GetItemCount(int)",
                    prePatchDetail);
            else if (!patcher.IsPatchSetTrusted(resolvedStackCountSetter, Array.Empty<HarmonyPatchExpectation>(), out prePatchDetail))
                resourceHookDetail = ResourceHookUnavailableDetail(
                    "Item.StackCount setter",
                    prePatchDetail);
            else if (!patcher.IsPatchSetTrusted(resolvedMarkDestroyedMethod, Array.Empty<HarmonyPatchExpectation>(), out prePatchDetail))
                resourceHookDetail = ResourceHookUnavailableDetail(
                    "Item.MarkDestroyed()",
                    prePatchDetail);
            var resourceHooksValidated = string.IsNullOrWhiteSpace(resourceHookDetail);
            CraftingHarmonyBridge.Attach(this);
            patcher.Patch(
                resolvedCraftMethod,
                prefix: CraftingHarmonyCallbacks.CraftPrefixMethod,
                postfix: CraftingHarmonyCallbacks.CraftPostfixMethod,
                finalizer: CraftingHarmonyCallbacks.CraftFinalizerMethod);
            patcher.Patch(resolvedReturnMethod, postfix: CraftingHarmonyCallbacks.ReturnPostfixMethod);
            patcher.Patch(
                resolvedPayMethod,
                prefix: CraftingHarmonyCallbacks.PayPrefixMethod,
                postfix: CraftingHarmonyCallbacks.PayPostfixMethod,
                finalizer: CraftingHarmonyCallbacks.PayFinalizerMethod);
            if (resourceHooksValidated)
            {
                try
                {
                    patcher.Patch(resolvedItemCountMethod, postfix: CraftingHarmonyCallbacks.GetItemCountPostfixMethod);
                    patcher.Patch(
                        resolvedStackCountSetter,
                        prefix: CraftingHarmonyCallbacks.StackCountPrefixMethod,
                        postfix: CraftingHarmonyCallbacks.StackCountPostfixMethod);
                    patcher.Patch(resolvedMarkDestroyedMethod, prefix: CraftingHarmonyCallbacks.MarkDestroyedPrefixMethod);
                }
                catch (Exception exception)
                {
                    resourceHooksValidated = false;
                    resourceHookDetail = ResourceHookUnavailableDetail(
                        "resource-proof patch installation",
                        $"{Unwrap(exception).GetType().Name}: {Unwrap(exception).Message}");
                }
            }
            var expectedCraft = new[]
            {
                new HarmonyPatchExpectation("Prefixes", CraftingHarmonyCallbacks.CraftPrefixMethod),
                new HarmonyPatchExpectation("Postfixes", CraftingHarmonyCallbacks.CraftPostfixMethod),
                new HarmonyPatchExpectation("Finalizers", CraftingHarmonyCallbacks.CraftFinalizerMethod)
            };
            if (!patcher.TryCaptureValidatedPatchSetStamp(
                    resolvedCraftMethod,
                    expectedCraft,
                    out var resolvedCraftStamp,
                    out var stampDetail)
                || resolvedCraftStamp == null)
                throw new InvalidOperationException($"Installed crafting patch set/stamp validation failed: {stampDetail}");
            var expectedReturn = new[]
            {
                new HarmonyPatchExpectation("Postfixes", CraftingHarmonyCallbacks.ReturnPostfixMethod)
            };
            if (!patcher.TryCaptureValidatedPatchSetStamp(
                    resolvedReturnMethod,
                    expectedReturn,
                    out var resolvedReturnStamp,
                    out stampDetail)
                || resolvedReturnStamp == null)
                throw new InvalidOperationException($"Installed crafting delivery patch set/stamp validation failed: {stampDetail}");
            var expectedPay = new[]
            {
                new HarmonyPatchExpectation("Prefixes", CraftingHarmonyCallbacks.PayPrefixMethod),
                new HarmonyPatchExpectation("Postfixes", CraftingHarmonyCallbacks.PayPostfixMethod),
                new HarmonyPatchExpectation("Finalizers", CraftingHarmonyCallbacks.PayFinalizerMethod)
            };
            if (!patcher.TryCaptureValidatedPatchSetStamp(
                    resolvedPayMethod,
                    expectedPay,
                    out var resolvedPayStamp,
                    out stampDetail)
                || resolvedPayStamp == null)
                throw new InvalidOperationException($"Installed crafting payment patch set/stamp validation failed: {stampDetail}");
            HarmonyPatchSetStamp? resolvedItemCountStamp = null;
            HarmonyPatchSetStamp? resolvedStackCountStamp = null;
            HarmonyPatchSetStamp? resolvedMarkDestroyedStamp = null;
            if (resourceHooksValidated)
            {
                try
                {
                    var expectedItemCount = new[]
                    {
                        new HarmonyPatchExpectation("Postfixes", CraftingHarmonyCallbacks.GetItemCountPostfixMethod)
                    };
                    if (!patcher.TryCaptureValidatedPatchSetStamp(
                            resolvedItemCountMethod,
                            expectedItemCount,
                            out resolvedItemCountStamp,
                            out stampDetail)
                        || resolvedItemCountStamp == null)
                        throw new InvalidOperationException($"affordability patch set/stamp validation failed: {stampDetail}");
                    var expectedStackCount = new[]
                    {
                        new HarmonyPatchExpectation("Prefixes", CraftingHarmonyCallbacks.StackCountPrefixMethod),
                        new HarmonyPatchExpectation("Postfixes", CraftingHarmonyCallbacks.StackCountPostfixMethod)
                    };
                    if (!patcher.TryCaptureValidatedPatchSetStamp(
                            resolvedStackCountSetter,
                            expectedStackCount,
                            out resolvedStackCountStamp,
                            out stampDetail)
                        || resolvedStackCountStamp == null)
                        throw new InvalidOperationException($"stack-mutation patch set/stamp validation failed: {stampDetail}");
                    var expectedMarkDestroyed = new[]
                    {
                        new HarmonyPatchExpectation("Prefixes", CraftingHarmonyCallbacks.MarkDestroyedPrefixMethod)
                    };
                    if (!patcher.TryCaptureValidatedPatchSetStamp(
                            resolvedMarkDestroyedMethod,
                            expectedMarkDestroyed,
                            out resolvedMarkDestroyedStamp,
                            out stampDetail)
                        || resolvedMarkDestroyedStamp == null)
                        throw new InvalidOperationException($"stack-destruction patch set/stamp validation failed: {stampDetail}");
                }
                catch (Exception exception)
                {
                    resourceHooksValidated = false;
                    resourceHookDetail = ResourceHookUnavailableDetail(
                        "resource-proof patch validation",
                        $"{Unwrap(exception).GetType().Name}: {Unwrap(exception).Message}");
                }
            }
            craftMethod = resolvedCraftMethod;
            returnMethod = resolvedReturnMethod;
            payMethod = resolvedPayMethod;
            itemCountMethod = resourceHooksValidated ? resolvedItemCountMethod : null;
            stackCountSetter = resourceHooksValidated ? resolvedStackCountSetter : null;
            markDestroyedMethod = resourceHooksValidated ? resolvedMarkDestroyedMethod : null;
            craftPatchStamp = resolvedCraftStamp;
            returnPatchStamp = resolvedReturnStamp;
            payPatchStamp = resolvedPayStamp;
            itemCountPatchStamp = resolvedItemCountStamp;
            stackCountPatchStamp = resolvedStackCountStamp;
            markDestroyedPatchStamp = resolvedMarkDestroyedStamp;
            lock (lifecycleSync)
            {
                var initializedCapabilities = CraftingNativeContractPolicy.Supported(
                    "The correlated Cost.Return task completed after native output delivery, before downstream crafting callbacks.",
                    "CraftingFormula.id and singular result.id/result.amount captured at the native request boundary.",
                    "CraftingFormula.cost.items stable identities and declared quantities captured at invocation; repeated resource ids require Duckov's own matched Pay-time affordability and net ownership-ending stack-mutation observations to prove their canonical combined quantity before successful delivery publication.",
                    "CraftingFormula.cost.money captured at invocation; a successful correlated delivery proves the preceding native Pay returned true for that declared total charge.");
                if (!resourceHooksValidated)
                    RestrictResourceCapabilities(initializedCapabilities, resourceHookDetail!);
                capabilities = initializedCapabilities;
                resourceProofHooksActive = resourceHooksValidated;
                accepting = true;
            }
            patchInspectionScheduler.Reset(DateTime.UtcNow, 1);
            pendingRetryScheduler.Reset(DateTime.UtcNow, 1);
            StageAndPublishCapabilities();
            if (!resourceHooksValidated)
                DiagnosticOnce("resource-hooks-unavailable", resourceHookDetail!);
            DiagnosticOnce(
                "initialized",
                resourceHooksValidated
                    ? $"Crafting completion patch active with HarmonyLib {patcher.Version}; completion actions, declared produced quantity, event-time item resource costs, and declared total currency charge are generation-lifetime totals. Money/Cash split, workstation, run/map attribution, and multiple-output recipes are unavailable on the installed contract."
                    : $"Crafting completion and payment patches active with HarmonyLib {patcher.Version}; completion actions, declared produced quantity, and declared total currency charge remain available, while item-resource tracking is unavailable because its isolated proof hooks were not trusted. Money/Cash split, workstation, run/map attribution, and multiple-output recipes are unavailable on the installed contract.");
        }
        catch (Exception exception)
        {
            Disable($"Crafting activation failed: {Unwrap(exception).GetType().Name}: {Unwrap(exception).Message}");
            TryCleanup();
        }
        return Records();
    }

    public void Tick(DateTime nowUtc)
    {
        bool active;
        bool inspectResourceHooks;
        lock (lifecycleSync)
        {
            active = accepting;
            inspectResourceHooks = resourceProofHooksActive;
        }
        if ((!pendingPublication.IsEmpty || profileHandoff.HasCompletedData || capabilityPublication.IsPending)
            && pendingRetryScheduler.TryTake(nowUtc, 1, out _))
            FlushPending();
        if (!active) return;
        if (craftMethod == null || returnMethod == null || payMethod == null
            || !patchInspectionScheduler.TryTake(nowUtc, 1, out _)) return;
        var patcher = patcherLease.Value;
        var detail = "The crafting patch-state stamp is unavailable.";
        if (patcher == null
            || !patcher.IsPatchSetStampCurrent(craftPatchStamp, out detail)
            || !patcher.IsPatchSetStampCurrent(returnPatchStamp, out detail)
            || !patcher.IsPatchSetStampCurrent(payPatchStamp, out detail))
        {
            DisableRuntime(
                patcher == null
                    ? "Crafting tracking disabled after patch drift: the Harmony owner is unavailable."
                    : $"Crafting tracking disabled after patch drift: {detail}");
            return;
        }
        if (!inspectResourceHooks) return;
        if (itemCountMethod == null || stackCountSetter == null || markDestroyedMethod == null
            || itemCountPatchStamp == null || stackCountPatchStamp == null || markDestroyedPatchStamp == null)
        {
            DisableResourceRuntime(ResourceHookUnavailableDetail(
                "resource-proof patch state",
                "runtime inspection found incomplete UDS hook metadata"));
            return;
        }
        if (!patcher.IsPatchSetStampCurrent(itemCountPatchStamp, out detail))
            DisableResourceRuntime(ResourceHookUnavailableDetail(
                "ItemUtilities.GetItemCount(int)",
                $"runtime patch drift: {detail}"));
        else if (!patcher.IsPatchSetStampCurrent(stackCountPatchStamp, out detail))
            DisableResourceRuntime(ResourceHookUnavailableDetail(
                "Item.StackCount setter",
                $"runtime patch drift: {detail}"));
        else if (!patcher.IsPatchSetStampCurrent(markDestroyedPatchStamp, out detail))
            DisableResourceRuntime(ResourceHookUnavailableDetail(
                "Item.MarkDestroyed()",
                $"runtime patch drift: {detail}"));
    }

    public bool FlushPending()
    {
        var handoffPublished = FlushCompletedProfileHandoffs();
        var currentPublished = FlushCurrentPending();
        return handoffPublished && currentPublished && !profileHandoff.HasUncommittedData;
    }

    public bool FlushPendingForProfileTransition()
    {
        var handoffPublished = FlushCompletedProfileHandoffs();
        var currentPublished = FlushCurrentPending();
        return handoffPublished && currentPublished;
    }

    public void SetProfileTransitionCleanupBarrier(Func<bool> barrier) =>
        profileTransitionCleanupBarrier = barrier ?? throw new ArgumentNullException(nameof(barrier));

    private bool FlushCompletedProfileHandoffs()
    {
        try
        {
            return profileHandoff.TryFlushCompleted(mutation =>
            {
                pendingPublication.Add(mutation);
                return true;
            });
        }
        catch (Exception exception)
        {
            DiagnosticOnce(
                "handoff-flush:" + Unwrap(exception).GetType().Name,
                $"Crafting profile-handoff publication failed and remains retryable: {Unwrap(exception).Message}");
            return false;
        }
    }

    private bool FlushCurrentPending()
    {
        var hadPending = !pendingPublication.IsEmpty;
        try
        {
            CraftingMutation? acceptedMutation = null;
            var aggregatePublished = pendingPublication.TryFlush(mutation =>
            {
                if (!recordHandler(mutation)) return false;
                acceptedMutation = mutation;
                return true;
            });
            var aggregatePersisted = !hadPending || (aggregatePublished && persistenceHandler());
            if (acceptedMutation != null) StageDeliveredCostCapabilities(acceptedMutation);
            var capabilitiesPublished = FlushPendingCapabilities();
            return capabilitiesPublished && aggregatePublished && aggregatePersisted;
        }
        catch (Exception exception)
        {
            DiagnosticOnce(
                "flush:" + exception.GetType().Name,
                $"Crafting aggregate publication failed and remains retryable when not yet accepted: {Unwrap(exception).Message}");
            return false;
        }
    }

    public bool TryCleanup()
    {
        lock (lifecycleSync)
        {
            if (cleaned) return true;
            accepting = false;
            cleanupRequested = true;
        }
        if (profileTransitionCleanupBarrier?.Invoke() == false)
        {
            DiagnosticOnce(
                "cleanup-profile-transition",
                "Crafting cleanup remains pending until queued profile transitions commit their staged completions.");
            return false;
        }
        if (boundary.OutstandingCount != 0)
        {
            DiagnosticOnce(
                "cleanup-inflight",
                "Crafting cleanup is retained until all already-started native craft tasks finish or fail and every proven completion finishes aggregate publication; no new craft tasks are accepted.");
            return false;
        }
        if (profileHandoff.HasUncommittedData)
        {
            DiagnosticOnce(
                "cleanup-active-handoff",
                "Crafting cleanup refused to discard completed output that is awaiting its target profile generation.");
            return false;
        }
        if (!FlushPending())
        {
            DiagnosticOnce("cleanup-flush", "Crafting cleanup is retained until its pending aggregate and capability publications are accepted.");
            return false;
        }
        CraftingHarmonyBridge.Detach(this);
        var patchesCleaned = patcherLease.TryCleanup(out var patchDetail);
        if (!patchesCleaned)
        {
            DiagnosticOnce("cleanup-patches", $"Crafting patch cleanup remains pending and will be retried: {patchDetail}");
            return false;
        }
        lock (lifecycleSync)
        {
            craftMethod = null;
            returnMethod = null;
            payMethod = null;
            itemCountMethod = null;
            stackCountSetter = null;
            markDestroyedMethod = null;
            craftPatchStamp = null;
            returnPatchStamp = null;
            payPatchStamp = null;
            itemCountPatchStamp = null;
            stackCountPatchStamp = null;
            markDestroyedPatchStamp = null;
            resourceProofHooksActive = false;
            profileTransitionCleanupBarrier = null;
            profileHandoff.Reset();
            cleaned = true;
        }
        return true;
    }

    public bool TryCleanupForTerminalShutdown()
    {
        lock (lifecycleSync)
        {
            if (cleaned) return true;
            accepting = false;
            cleanupRequested = true;
            terminalShutdownRequested = true;
        }
        var abandoned = boundary.AbandonUnprovenForTerminalShutdown();
        if (abandoned != 0)
        {
            DiagnosticOnce(
                "terminal-inflight",
                $"Terminal shutdown abandoned {abandoned} incomplete native craft task(s); unproven output was not counted.");
        }
        return TryCleanup();
    }

    public void Dispose() => TryCleanup();

    public void BeginProfileChange(long transitionId)
    {
        lock (lifecycleSync)
        {
            if (cleaned) return;
            profileHandoff.Begin(transitionId);
        }
        DiagnosticOnce(
            "profile-handoff-start:" + transitionId,
            "Crafting completions are staged for the queued profile transition until its target generation commits.");
    }

    public void CompleteProfileChange(long transitionId)
    {
        try
        {
            string generationId;
            bool completed;
            lock (lifecycleSync)
            {
                generationId = generationIdProvider();
                completed = profileHandoff.Complete(transitionId, generationId);
            }
            if (!completed)
            {
                DiagnosticOnce(
                    "profile-handoff-superseded:" + transitionId,
                    "A superseded crafting profile-handoff completion was ignored.");
                return;
            }
            if (!FlushPendingForProfileTransition())
            {
                pendingRetryScheduler.Reset(DateTime.UtcNow, 1);
                DiagnosticOnce(
                    "profile-handoff-pending:" + transitionId,
                    "Crafting completions transferred to the committed profile remain queued for publication retry.");
            }
            DiagnosticOnce(
                "profile-handoff-complete:" + transitionId,
                "Crafting profile handoff committed completed output to the selected save generation.");
        }
        catch (Exception exception)
        {
            DisableRuntime(
                $"Crafting tracking disabled after profile-handoff completion failed: {Unwrap(exception).GetType().Name}: {Unwrap(exception).Message}");
        }
    }

    internal CraftingNativeScope? BeginNativeCraft(CraftingFormula formula)
    {
        bool resourceCaptureEnabled;
        lock (lifecycleSync)
        {
            if (!accepting
                || capabilities.CompletionActions.State != AdapterCapabilityState.Supported
                || formula.result.amount <= 0
                || string.IsNullOrWhiteSpace(formula.id))
                return null;
            resourceCaptureEnabled = resourceProofHooksActive
                                     && capabilities.ItemResourceIdentity.State == AdapterCapabilityState.Supported
                                     && capabilities.OutputResourceAssociation.State == AdapterCapabilityState.Supported;
        }

        var resources = Array.Empty<CraftingResourceCostEvidence>();
        var resourceDetail = string.Empty;
        var resourcesProven = resourceCaptureEnabled
                              && TrySnapshotResourceCosts(formula.cost, out resources, out resourceDetail);
        var currencyProven = TrySnapshotCurrencyCost(formula.cost, out var currencyCharged, out var currencyDetail);
        CraftingNativeScope? scope;
        lock (lifecycleSync)
        {
            if (!accepting
                || capabilities.CompletionActions.State != AdapterCapabilityState.Supported
                || formula.result.amount <= 0
                || string.IsNullOrWhiteSpace(formula.id))
                return null;
            if (!resourceProofHooksActive
                || capabilities.ItemResourceIdentity.State != AdapterCapabilityState.Supported
                || capabilities.OutputResourceAssociation.State != AdapterCapabilityState.Supported)
            {
                resourcesProven = false;
                resources = Array.Empty<CraftingResourceCostEvidence>();
            }
            var itemId = formula.result.id.ToString(CultureInfo.InvariantCulture);
            var token = boundary.Begin(new CraftingCompletionEvidence(
                itemId,
                ReadDisplayName(formula.result.id, itemId, "Crafted output"),
                formula.id,
                formula.result.amount,
                resources,
                currencyCharged,
                resourcesProven,
                currencyProven));
            scope = new CraftingNativeScope(
                this,
                new CraftingDeliveryCorrelation(token),
                new CraftingResourcePaymentProof(formula.cost, resourcesProven),
                resourcesProven ? string.Empty : resourceDetail,
                currencyProven ? string.Empty : currencyDetail);
        }
        return scope;
    }

    internal static bool BeginNativePayment(CraftingNativeScope scope, Cost cost) =>
        scope.ResourcePaymentProof.TryBegin(cost);

    internal static void ObserveNativePaymentItemCount(CraftingNativeScope scope, int itemTypeId, int count) =>
        scope.ResourcePaymentProof.ObserveItemCount(itemTypeId, count);

    internal static void ObserveNativePaymentStackCountMutation(
        CraftingNativeScope scope,
        Item item,
        int beforeCount,
        int afterCount,
        bool wasBeingDestroyed) =>
        scope.ResourcePaymentProof.ObserveStackCountMutation(
            item,
            beforeCount,
            afterCount,
            wasBeingDestroyed);

    internal static void ObserveNativePaymentStackDestroyed(CraftingNativeScope scope, Item item) =>
        scope.ResourcePaymentProof.ObserveStackDestroyed(item);

    internal void CompleteNativePayment(CraftingNativeScope scope, bool result)
    {
        var detail = scope.ResourcePaymentProof.Complete(result);
        if (!result || string.IsNullOrWhiteSpace(detail)) return;
        InvalidateResourceEvidence(scope, detail);
    }

    internal static void AbandonNativePayment(CraftingNativeScope scope) =>
        scope.ResourcePaymentProof.AbandonPayment();

    private void InvalidateResourceEvidence(CraftingNativeScope scope, string detail)
    {
        bool invalidated;
        lock (lifecycleSync)
        {
            invalidated = boundary.TryInvalidateResourceEvidence(scope.Correlation.Token);
            if (invalidated) scope.RecordResourceEvidenceFailure(detail);
        }
        if (!invalidated)
        {
            DisableRuntime("Crafting tracking disabled because repeated-resource evidence could not be invalidated before delivery publication.");
            return;
        }
    }

    private void StageDeliveredCostCapabilities(CraftingMutation mutation)
    {
        var resourceUnavailable = mutation.Rows.Any(row => !row.ResourceEvidenceProven);
        var currencyUnavailable = mutation.Rows.Any(row => !row.CurrencyEvidenceProven);
        if (!resourceUnavailable && !currencyUnavailable) return;

        CraftingMetricCapabilities? snapshot = null;
        lock (lifecycleSync)
        {
            var changed = RestrictCostCapabilitiesLocked(
                resourceUnavailable
                    ? CraftingStatisticsReducer.DeliveredResourceEvidenceUnavailableProvenance
                    : null,
                currencyUnavailable
                    ? CraftingStatisticsReducer.DeliveredCurrencyEvidenceUnavailableProvenance
                    : null);
            if (changed) snapshot = CraftingStatisticsReducer.CloneCapabilities(capabilities);
        }
        if (snapshot == null) return;
        capabilityPublication.Stage(
            CraftingNativeContractPolicy.ToRecords(snapshot, AdapterVersion),
            snapshot);
    }

    private bool TrySnapshotResourceCosts(
        Cost cost,
        out CraftingResourceCostEvidence[] resources,
        out string detail)
    {
        resources = Array.Empty<CraftingResourceCostEvidence>();
        if (cost.items == null)
        {
            detail = string.Empty;
            return true;
        }
        try
        {
            var canonical = new Dictionary<string, CraftingResourceCostEvidence>(StringComparer.Ordinal);
            foreach (var entry in cost.items)
            {
                if (entry.amount <= 0)
                {
                    detail = $"Crafting item-resource tracking is unavailable because resource {entry.id.ToString(CultureInfo.InvariantCulture)} exposed non-positive declared quantity {entry.amount.ToString(CultureInfo.InvariantCulture)}.";
                    return false;
                }
                var stableId = entry.id.ToString(CultureInfo.InvariantCulture);
                var displayName = ReadDisplayName(entry.id, stableId, "Crafting resource");
                if (canonical.TryGetValue(stableId, out var current))
                {
                    canonical[stableId] = new CraftingResourceCostEvidence(
                        stableId,
                        string.IsNullOrWhiteSpace(displayName) ? current.DisplayName : displayName,
                        checked(current.ConsumedQuantity + entry.amount));
                }
                else
                {
                    canonical.Add(stableId, new CraftingResourceCostEvidence(stableId, displayName, entry.amount));
                }
            }
            resources = canonical.Values.OrderBy(value => value.ResourceItemId, StringComparer.Ordinal).ToArray();
            detail = string.Empty;
            return true;
        }
        catch (OverflowException)
        {
            detail = "Crafting item-resource tracking is unavailable because repeated declared resource quantities exceeded Int64 canonicalization.";
            return false;
        }
        catch (Exception exception)
        {
            detail = $"Crafting item-resource tracking is unavailable because invocation evidence could not be read: {Unwrap(exception).GetType().Name}: {Unwrap(exception).Message}";
            return false;
        }
    }

    private static bool TrySnapshotCurrencyCost(Cost cost, out long currencyCharged, out string detail)
    {
        currencyCharged = 0;
        if (cost.money < 0)
        {
            detail = $"Crafting currency tracking is unavailable because the invocation exposed negative declared charge {cost.money.ToString(CultureInfo.InvariantCulture)}.";
            return false;
        }
        currencyCharged = cost.money;
        detail = string.Empty;
        return true;
    }

    private bool RestrictCostCapabilitiesLocked(string? resourceDetail, string? currencyDetail)
    {
        var changed = false;
        if (!string.IsNullOrWhiteSpace(resourceDetail)
            && RestrictResourceCapabilities(capabilities, resourceDetail))
        {
            changed = true;
            DiagnosticOnce("resource-evidence-unavailable", resourceDetail);
        }
        if (!string.IsNullOrWhiteSpace(currencyDetail)
            && capabilities.CurrencyCharge.State != AdapterCapabilityState.DisabledIncompatible)
        {
            capabilities.CurrencyCharge = CraftingNativeContractPolicy.Availability(
                AdapterCapabilityState.DisabledIncompatible,
                currencyDetail);
            changed = true;
            DiagnosticOnce("currency-evidence-unavailable", currencyDetail);
        }
        return changed;
    }

    internal UniTask<List<Item>> WrapNativeCraft(
        CraftingNativeScope scope,
        UniTask<List<Item>> source) => AwaitNativeCraft(source, scope);

    internal UniTask WrapNativeDelivery(
        CraftingNativeScope scope,
        bool directToBuffer,
        bool toPlayerInventory,
        int amountFactor,
        List<Item>? generatedItemsBuffer,
        UniTask source)
    {
        if (directToBuffer
            || !toPlayerInventory
            || amountFactor != 1
            || generatedItemsBuffer == null)
        {
            DisableRuntime("Crafting tracking disabled because the correlated Cost.Return delivery arguments changed from the verified contract.");
            return source;
        }
        if (!scope.Correlation.TryClaimDeliveryTask())
        {
            DisableRuntime("Crafting tracking disabled because one craft request invoked the correlated Cost.Return contract more than once.");
            return source;
        }
        return AwaitNativeDelivery(source, scope);
    }

    internal void AbandonSynchronousNativeCraft(CraftingNativeScope scope)
    {
        if (!scope.Correlation.DeliveryProven) boundary.Abandon(scope.Correlation.Token);
        RetryCleanupIfRequested();
    }

    internal void FailNativeCraftBegin(Exception exception) => DisableRuntime(
        $"Crafting tracking disabled after native request correlation failed: {Unwrap(exception).GetType().Name}: {Unwrap(exception).Message}");

    internal void FailNativeCraftWrapping(CraftingNativeScope scope, Exception exception)
    {
        if (!scope.Correlation.DeliveryProven) boundary.Abandon(scope.Correlation.Token);
        DisableRuntime(
            $"Crafting tracking disabled after native task correlation failed: {Unwrap(exception).GetType().Name}: {Unwrap(exception).Message}");
        RetryCleanupIfRequested();
    }

    private async UniTask AwaitNativeDelivery(UniTask source, CraftingNativeScope scope)
    {
        await source;
        CompleteDeliveredCraft(scope);
    }

    private async UniTask<List<Item>> AwaitNativeCraft(
        UniTask<List<Item>> source,
        CraftingNativeScope scope)
    {
        try
        {
            List<Item> result;
            try
            {
                result = await source;
            }
            catch
            {
                if (!scope.Correlation.DeliveryProven)
                    boundary.Abandon(scope.Correlation.Token);
                throw;
            }
            if (result == null)
            {
                if (!scope.Correlation.DeliveryProven)
                    boundary.Abandon(scope.Correlation.Token);
                else
                    DisableRuntime("Crafting tracking disabled because the native craft task returned null after proven output delivery.");
                return result!;
            }
            if (!scope.Correlation.DeliveryProven)
            {
                boundary.Abandon(scope.Correlation.Token);
                bool terminal;
                lock (lifecycleSync) terminal = terminalShutdownRequested;
                if (!terminal)
                    DisableRuntime("Crafting tracking disabled because the native craft task completed without its correlated Cost.Return delivery proof.");
            }
            return result;
        }
        finally { RetryCleanupIfRequested(); }
    }

    private void CompleteDeliveredCraft(CraftingNativeScope scope)
    {
        var publicationClaimed = false;
        try
        {
            if (scope.ResourcePaymentProof.RequiresPaymentProof && !scope.ResourcePaymentProof.IsExact)
                InvalidateResourceEvidence(scope, scope.ResourcePaymentProof.DeliveryDetail());
            string generationId;
            CraftingMutation mutation;
            long profileTransitionId;
            lock (lifecycleSync)
            {
                if (terminalShutdownRequested) return;
                if (!scope.Correlation.TryMarkDeliveryProven()) return;
                generationId = profileHandoff.TryGetActiveTransitionId(out profileTransitionId)
                    ? CraftingProfileHandoffBoundary.StagedGenerationId
                    : generationIdProvider();
                if (!string.IsNullOrWhiteSpace(generationId)
                    && boundary.TryComplete(
                        scope.Correlation.Token,
                        generationId,
                        DateTime.UtcNow,
                        out var completedMutation))
                {
                    mutation = completedMutation;
                    publicationClaimed = true;
                }
                else
                {
                    mutation = CraftingMutation.Empty;
                }
            }
            if (string.IsNullOrWhiteSpace(generationId))
            {
                boundary.Abandon(scope.Correlation.Token);
                DisableRuntime("Crafting tracking disabled after delivery because the active save generation was unavailable.");
                return;
            }
            if (!publicationClaimed)
            {
                DisableRuntime("Crafting tracking disabled because correlated delivery evidence could not be claimed exactly once.");
                return;
            }
            if (!string.IsNullOrWhiteSpace(scope.ResourceEvidenceFailureDetail))
                DiagnosticOnce("resource-evidence-unavailable", scope.ResourceEvidenceFailureDetail);
            if (!string.IsNullOrWhiteSpace(scope.CurrencyEvidenceFailureDetail))
                DiagnosticOnce("currency-evidence-unavailable", scope.CurrencyEvidenceFailureDetail);
            if (profileTransitionId != 0)
            {
                if (!profileHandoff.Stage(profileTransitionId, mutation))
                {
                    DisableRuntime("Crafting tracking disabled because completed delivery could not be staged for its queued profile transition.");
                    return;
                }
                DiagnosticOnce(
                    "completion-handoff:" + profileTransitionId,
                    "A proven crafting completion is staged until its queued profile transition commits the target save generation.");
                return;
            }
            var wasEmpty = pendingPublication.Add(mutation);
            if (wasEmpty && !FlushPending())
            {
                pendingRetryScheduler.Reset(DateTime.UtcNow, 1);
                DiagnosticOnce("completion-pending", "A proven crafting completion is retained for aggregate publication retry.");
            }
        }
        catch (Exception exception)
        {
            if (!publicationClaimed) boundary.Abandon(scope.Correlation.Token);
            DisableRuntime(
                $"Crafting tracking disabled after a post-delivery instrumentation failure: {Unwrap(exception).GetType().Name}: {Unwrap(exception).Message}");
        }
        finally
        {
            if (publicationClaimed) boundary.FinishPublication(scope.Correlation.Token);
            RetryCleanupIfRequested();
        }
    }

    private void RetryCleanupIfRequested()
    {
        bool retryCleanup;
        lock (lifecycleSync) retryCleanup = cleanupRequested;
        if (!retryCleanup) return;
        try { cleanupRetryHandler(); }
        catch (Exception exception)
        {
            DiagnosticOnce(
                "cleanup-retry:" + exception.GetType().Name,
                $"Crafting cleanup retry failed and remains pending: {Unwrap(exception).Message}");
        }
    }

    private static void ResolveContracts(
        out MethodInfo resolvedCraftMethod,
        out MethodInfo resolvedReturnMethod,
        out MethodInfo resolvedPayMethod,
        out MethodInfo resolvedItemCountMethod,
        out MethodInfo resolvedStackCountSetter,
        out MethodInfo resolvedMarkDestroyedMethod)
    {
        var itemEntryType = typeof(CraftingFormula.ItemEntry);
        var formulaResult = typeof(CraftingFormula).GetField("result", BindingFlags.Instance | BindingFlags.Public);
        var formulaId = typeof(CraftingFormula).GetField("id", BindingFlags.Instance | BindingFlags.Public);
        var formulaCost = typeof(CraftingFormula).GetField("cost", BindingFlags.Instance | BindingFlags.Public);
        var resultId = itemEntryType.GetField("id", BindingFlags.Instance | BindingFlags.Public);
        var resultAmount = itemEntryType.GetField("amount", BindingFlags.Instance | BindingFlags.Public);
        if (formulaResult?.FieldType != itemEntryType || formulaId?.FieldType != typeof(string)
            || formulaCost?.FieldType != typeof(Cost)
            || resultId?.FieldType != typeof(int) || resultAmount?.FieldType != typeof(int))
            throw new MissingFieldException("CraftingFormula id/result.id/result.amount/cost contract");
        var costItemEntryType = typeof(Cost.ItemEntry);
        var costMoney = typeof(Cost).GetField("money", BindingFlags.Instance | BindingFlags.Public);
        var costItems = typeof(Cost).GetField("items", BindingFlags.Instance | BindingFlags.Public);
        var costItemId = costItemEntryType.GetField("id", BindingFlags.Instance | BindingFlags.Public);
        var costItemAmount = costItemEntryType.GetField("amount", BindingFlags.Instance | BindingFlags.Public);
        var costEnough = typeof(Cost).GetProperty("Enough", BindingFlags.Instance | BindingFlags.Public);
        var costPay = typeof(Cost).GetMethod(
            "Pay",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            new[] { typeof(bool), typeof(bool) },
            modifiers: null);
        resolvedPayMethod = typeof(EconomyManager).GetMethod(
            "Pay",
            BindingFlags.Static | BindingFlags.Public,
            binder: null,
            new[] { typeof(Cost), typeof(bool), typeof(bool) },
            modifiers: null)
            ?? throw new MissingMethodException("EconomyManager.Pay(Cost, bool, bool)");
        if (costMoney?.FieldType != typeof(long)
            || costItems?.FieldType != typeof(Cost.ItemEntry[])
            || costItemId?.FieldType != typeof(int)
            || costItemAmount?.FieldType != typeof(long)
            || costEnough?.PropertyType != typeof(bool)
            || costPay?.ReturnType != typeof(bool)
            || resolvedPayMethod.ReturnType != typeof(bool))
            throw new MissingMemberException("Cost money/items/Enough/Pay and EconomyManager.Pay contracts");
        resolvedCraftMethod = typeof(CraftingManager).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .SingleOrDefault(method => method.Name == "Craft"
                && method.ReturnType == typeof(UniTask<List<Item>>)
                && method.GetParameters().Select(parameter => parameter.ParameterType)
                    .SequenceEqual(new[] { typeof(CraftingFormula) }))
            ?? throw new MissingMethodException("CraftingManager.Craft(CraftingFormula)");
        var publicCraft = typeof(CraftingManager).GetMethod(
            "Craft",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            new[] { typeof(string) },
            modifiers: null);
        if (publicCraft?.ReturnType != typeof(UniTask<List<Item>>))
            throw new MissingMethodException("CraftingManager.Craft(string)");
        var callback = typeof(CraftingManager).GetField("OnItemCrafted", BindingFlags.Static | BindingFlags.Public);
        if (callback?.FieldType != typeof(Action<CraftingFormula, Item>))
            throw new MissingFieldException("CraftingManager.OnItemCrafted");
        resolvedReturnMethod = typeof(Cost).GetMethod(
            "Return",
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            new[] { typeof(bool), typeof(bool), typeof(int), typeof(List<Item>) },
            modifiers: null)
            ?? throw new MissingMethodException("Duckov.Economy.Cost.Return(bool, bool, int, List<Item>)");
        if (resolvedReturnMethod.ReturnType != typeof(UniTask))
            throw new MissingMethodException("Duckov.Economy.Cost.Return UniTask result");
        resolvedItemCountMethod = typeof(ItemUtilities).GetMethod(
            "GetItemCount",
            BindingFlags.Static | BindingFlags.Public,
            binder: null,
            new[] { typeof(int) },
            modifiers: null)
            ?? throw new MissingMethodException("ItemUtilities.GetItemCount(int)");
        if (resolvedItemCountMethod.ReturnType != typeof(int))
            throw new MissingMethodException("ItemUtilities.GetItemCount(int) result");
        var stackCountProperty = typeof(Item).GetProperty(
            "StackCount",
            BindingFlags.Instance | BindingFlags.Public);
        resolvedStackCountSetter = stackCountProperty?.SetMethod
            ?? throw new MissingMethodException("Item.StackCount setter");
        if (stackCountProperty.PropertyType != typeof(int)
            || stackCountProperty.GetMethod == null
            || !stackCountProperty.GetMethod.IsPublic
            || !resolvedStackCountSetter.IsPublic)
            throw new MissingMemberException("Item.StackCount public Int32 getter/setter contract");
        resolvedMarkDestroyedMethod = typeof(Item).GetMethod(
            "MarkDestroyed",
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            Type.EmptyTypes,
            modifiers: null)
            ?? throw new MissingMethodException("Item.MarkDestroyed()");
        if (resolvedMarkDestroyedMethod.ReturnType != typeof(void))
            throw new MissingMethodException("Item.MarkDestroyed() void result");
        var metadata = typeof(ItemAssetsCollection).GetMethod(
            "GetMetaData",
            BindingFlags.Static | BindingFlags.Public,
            binder: null,
            new[] { typeof(int) },
            modifiers: null);
        if (metadata?.ReturnType != typeof(ItemMetaData))
            throw new MissingMethodException("ItemAssetsCollection.GetMetaData(int)");
    }

    private string ReadDisplayName(int itemTypeId, string stableId, string role)
    {
        try
        {
            var metadata = ItemAssetsCollection.GetMetaData(itemTypeId);
            var displayName = metadata.DisplayName;
            if (!string.IsNullOrWhiteSpace(displayName)) return displayName;
            if (!string.IsNullOrWhiteSpace(metadata.Name)) return metadata.Name;
        }
        catch (Exception exception)
        {
            DiagnosticOnce(
                "metadata:" + stableId,
                $"{role} {stableId} metadata was unavailable; stable identity was retained: {Unwrap(exception).Message}");
        }
        DiagnosticOnce(
            "metadata:" + stableId,
            $"{role} {stableId} metadata did not expose a name; stable identity was retained.");
        return string.Empty;
    }

    private static string ResourceHookUnavailableDetail(string target, string detail) =>
        $"Crafting item-resource tracking is unavailable because the resource-proof hook for {target} is not trusted: {detail}. Completion, output, recipe, batch, and independently proven currency tracking remain active.";

    private static bool RestrictResourceCapabilities(CraftingMetricCapabilities value, string detail)
    {
        if (value.ItemResourceIdentity.State == AdapterCapabilityState.DisabledIncompatible
            && value.OutputResourceAssociation.State == AdapterCapabilityState.DisabledIncompatible)
            return false;
        value.ItemResourceIdentity = CraftingNativeContractPolicy.Availability(
            AdapterCapabilityState.DisabledIncompatible,
            detail);
        value.OutputResourceAssociation = CraftingNativeContractPolicy.Availability(
            AdapterCapabilityState.DisabledIncompatible,
            detail);
        return true;
    }

    private void DisableResourceRuntime(string detail)
    {
        bool changed;
        lock (lifecycleSync)
        {
            if (!accepting || !resourceProofHooksActive) return;
            resourceProofHooksActive = false;
            itemCountMethod = null;
            stackCountSetter = null;
            markDestroyedMethod = null;
            itemCountPatchStamp = null;
            stackCountPatchStamp = null;
            markDestroyedPatchStamp = null;
            changed = RestrictResourceCapabilities(capabilities, detail);
            boundary.InvalidateAllResourceEvidence();
        }
        if (changed) StageAndPublishCapabilities();
        DiagnosticOnce("resource-runtime-disabled", detail);
    }

    private IReadOnlyList<CapabilityRecord> Disable(string detail)
    {
        lock (lifecycleSync)
            capabilities = CraftingNativeContractPolicy.Unavailable(detail);
        StageAndPublishCapabilities();
        DiagnosticOnce("disabled:" + detail, detail);
        return Records();
    }

    private void DisableRuntime(string detail)
    {
        lock (lifecycleSync)
        {
            accepting = false;
            capabilities = CraftingNativeContractPolicy.Unavailable(detail);
        }
        CraftingHarmonyBridge.Detach(this);
        StageAndPublishCapabilities();
        DiagnosticOnce("runtime-disabled:" + detail, detail);
    }

    private void StageAndPublishCapabilities()
    {
        CraftingMetricCapabilities snapshot;
        lock (lifecycleSync) snapshot = CraftingStatisticsReducer.CloneCapabilities(capabilities);
        capabilityPublication.Stage(
            CraftingNativeContractPolicy.ToRecords(snapshot, AdapterVersion),
            snapshot);
        if (!FlushPendingCapabilities()) pendingRetryScheduler.Reset(DateTime.UtcNow, 1);
    }

    private bool FlushPendingCapabilities()
    {
        try { return capabilityPublication.TryPublish(capabilityHandler); }
        catch (Exception exception)
        {
            DiagnosticOnce(
                "capability-publish:" + Unwrap(exception).GetType().Name,
                $"Crafting capability publication failed and remains retryable: {Unwrap(exception).Message}");
            return false;
        }
    }

    private IReadOnlyList<CapabilityRecord> Records() =>
        CraftingNativeContractPolicy.ToRecords(MetricCapabilities, AdapterVersion);

    private void DiagnosticOnce(string key, string detail)
    {
        lock (lifecycleSync)
        {
            if (diagnosticKeys.Count >= DiagnosticKeyCapacity || !diagnosticKeys.Add(key)) return;
        }
        try { diagnosticHandler(detail); }
        catch { }
    }

    private static Exception Unwrap(Exception exception) =>
        exception is TargetInvocationException { InnerException: not null } invocation
            ? invocation.InnerException!
            : exception;
}
