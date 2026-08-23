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
        "native-crafting/2.3.30+correlated-cost-return-v2+declared-output-v1+profile-handoff-v1+patch-stamp-v1+deferred-profile-v1";
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
    private HarmonyPatchSetStamp? craftPatchStamp;
    private HarmonyPatchSetStamp? returnPatchStamp;
    private Func<bool>? profileTransitionCleanupBarrier;
    private bool accepting;
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
            ResolveContracts(out var resolvedCraftMethod, out var resolvedReturnMethod);
            if (!ReflectiveHarmonyPatcher.TryCreate(HarmonyId, out var patcher, out var harmonyDetail) || patcher == null)
                return Disable($"Crafting completion is unavailable: {harmonyDetail}");
            patcherLease.Attach(patcher);
            if (!patcher.IsPatchSetTrusted(resolvedCraftMethod, Array.Empty<HarmonyPatchExpectation>(), out var prePatchDetail))
                throw new InvalidOperationException($"Unsafe pre-existing patch set on CraftingManager.Craft(CraftingFormula): {prePatchDetail}");
            if (!patcher.IsPatchSetTrusted(resolvedReturnMethod, Array.Empty<HarmonyPatchExpectation>(), out prePatchDetail))
                throw new InvalidOperationException($"Unsafe pre-existing patch set on Cost.Return: {prePatchDetail}");
            CraftingHarmonyBridge.Attach(this);
            patcher.Patch(
                resolvedCraftMethod,
                prefix: CraftingHarmonyCallbacks.CraftPrefixMethod,
                postfix: CraftingHarmonyCallbacks.CraftPostfixMethod,
                finalizer: CraftingHarmonyCallbacks.CraftFinalizerMethod);
            patcher.Patch(resolvedReturnMethod, postfix: CraftingHarmonyCallbacks.ReturnPostfixMethod);
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
            craftMethod = resolvedCraftMethod;
            returnMethod = resolvedReturnMethod;
            craftPatchStamp = resolvedCraftStamp;
            returnPatchStamp = resolvedReturnStamp;
            lock (lifecycleSync)
            {
                capabilities = CraftingNativeContractPolicy.Supported(
                    "The correlated Cost.Return task completed after native output delivery, before downstream crafting callbacks.",
                    "CraftingFormula.id and singular result.id/result.amount captured at the native request boundary.");
                accepting = true;
            }
            patchInspectionScheduler.Reset(DateTime.UtcNow, 1);
            pendingRetryScheduler.Reset(DateTime.UtcNow, 1);
            StageAndPublishCapabilities();
            DiagnosticOnce(
                "initialized",
                $"Crafting completion patch active with HarmonyLib {patcher.Version}; completion actions and declared produced quantity are generation-lifetime totals. Workstation, run/map attribution, and multiple-output recipes are unavailable on the installed contract.");
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
        lock (lifecycleSync) active = accepting;
        if ((!pendingPublication.IsEmpty || profileHandoff.HasCompletedData || capabilityPublication.IsPending)
            && pendingRetryScheduler.TryTake(nowUtc, 1, out _))
            FlushPending();
        if (!active) return;
        if (craftMethod == null || returnMethod == null || !patchInspectionScheduler.TryTake(nowUtc, 1, out _)) return;
        var patcher = patcherLease.Value;
        var detail = "The crafting patch-state stamp is unavailable.";
        if (patcher == null
            || !patcher.IsPatchSetStampCurrent(craftPatchStamp, out detail)
            || !patcher.IsPatchSetStampCurrent(returnPatchStamp, out detail))
        {
            DisableRuntime(
                patcher == null
                    ? "Crafting tracking disabled after patch drift: the Harmony owner is unavailable."
                    : $"Crafting tracking disabled after patch drift: {detail}");
        }
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
        var capabilitiesPublished = FlushPendingCapabilities();
        var hadPending = !pendingPublication.IsEmpty;
        try
        {
            var aggregatePublished = pendingPublication.TryFlush(recordHandler);
            var aggregatePersisted = !hadPending || (aggregatePublished && persistenceHandler());
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
            craftPatchStamp = null;
            returnPatchStamp = null;
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
        lock (lifecycleSync)
        {
            if (!accepting
                || capabilities.CompletionActions.State != AdapterCapabilityState.Supported
                || formula.result.amount <= 0
                || string.IsNullOrWhiteSpace(formula.id))
                return null;
            var itemId = formula.result.id.ToString(CultureInfo.InvariantCulture);
            var token = boundary.Begin(new CraftingCompletionEvidence(
                itemId,
                ReadDisplayName(formula.result.id, itemId),
                formula.id,
                formula.result.amount));
            return new CraftingNativeScope(this, new CraftingDeliveryCorrelation(token));
        }
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
        out MethodInfo resolvedReturnMethod)
    {
        var itemEntryType = typeof(CraftingFormula.ItemEntry);
        var formulaResult = typeof(CraftingFormula).GetField("result", BindingFlags.Instance | BindingFlags.Public);
        var formulaId = typeof(CraftingFormula).GetField("id", BindingFlags.Instance | BindingFlags.Public);
        var resultId = itemEntryType.GetField("id", BindingFlags.Instance | BindingFlags.Public);
        var resultAmount = itemEntryType.GetField("amount", BindingFlags.Instance | BindingFlags.Public);
        if (formulaResult?.FieldType != itemEntryType || formulaId?.FieldType != typeof(string)
            || resultId?.FieldType != typeof(int) || resultAmount?.FieldType != typeof(int))
            throw new MissingFieldException("CraftingFormula id/result.id/result.amount contract");
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
        var metadata = typeof(ItemAssetsCollection).GetMethod(
            "GetMetaData",
            BindingFlags.Static | BindingFlags.Public,
            binder: null,
            new[] { typeof(int) },
            modifiers: null);
        if (metadata?.ReturnType != typeof(ItemMetaData))
            throw new MissingMethodException("ItemAssetsCollection.GetMetaData(int)");
    }

    private string ReadDisplayName(int itemTypeId, string stableId)
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
                $"Crafted output {stableId} metadata was unavailable; stable identity was retained: {Unwrap(exception).Message}");
        }
        DiagnosticOnce(
            "metadata:" + stableId,
            $"Crafted output {stableId} metadata did not expose a name; stable identity was retained.");
        return string.Empty;
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
            ? invocation.InnerException
            : exception;
}
