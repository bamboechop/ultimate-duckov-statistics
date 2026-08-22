using System.Globalization;
using System.Reflection;
using Cysharp.Threading.Tasks;
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
        "native-crafting/2.3.30+private-task-completion-v1+declared-output-v1+patch-stamp-v1+deferred-profile-v1";
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
    private readonly RetryableHarmonyPatcherLease patcherLease = new();
    private readonly IncrementalPatchInspectionScheduler patchInspectionScheduler = new(TimeSpan.FromSeconds(2));
    private readonly IncrementalPatchInspectionScheduler pendingRetryScheduler = new(TimeSpan.FromSeconds(1));
    private readonly HashSet<string> diagnosticKeys = new(StringComparer.Ordinal);
    private CraftingMetricCapabilities capabilities = CraftingNativeContractPolicy.Unavailable(
        CraftingNativeContractPolicy.BootstrapProvenance);
    private MethodInfo? craftMethod;
    private HarmonyPatchSetStamp? patchStamp;
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
            ResolveContracts(out var resolvedCraftMethod);
            if (!ReflectiveHarmonyPatcher.TryCreate(HarmonyId, out var patcher, out var harmonyDetail) || patcher == null)
                return Disable($"Crafting completion is unavailable: {harmonyDetail}");
            patcherLease.Attach(patcher);
            if (!patcher.IsPatchSetTrusted(resolvedCraftMethod, Array.Empty<HarmonyPatchExpectation>(), out var prePatchDetail))
                throw new InvalidOperationException($"Unsafe pre-existing patch set on CraftingManager.Craft(CraftingFormula): {prePatchDetail}");
            CraftingHarmonyBridge.Attach(this);
            patcher.Patch(resolvedCraftMethod, postfix: CraftingHarmonyCallbacks.CraftPostfixMethod);
            var expected = new[]
            {
                new HarmonyPatchExpectation("Postfixes", CraftingHarmonyCallbacks.CraftPostfixMethod)
            };
            if (!patcher.TryCaptureValidatedPatchSetStamp(
                    resolvedCraftMethod,
                    expected,
                    out var stamp,
                    out var stampDetail)
                || stamp == null)
                throw new InvalidOperationException($"Installed crafting patch set/stamp validation failed: {stampDetail}");
            craftMethod = resolvedCraftMethod;
            patchStamp = stamp;
            lock (lifecycleSync)
            {
                capabilities = CraftingNativeContractPolicy.Supported(
                    "CraftingManager.Craft(CraftingFormula) returned a non-null task result after native output delivery.",
                    "CraftingFormula.id and singular result.id/result.amount captured at the native request boundary.");
                accepting = true;
            }
            patchInspectionScheduler.Reset(DateTime.UtcNow, 1);
            pendingRetryScheduler.Reset(DateTime.UtcNow, 1);
            PublishCapabilities();
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
        if (!pendingPublication.IsEmpty && pendingRetryScheduler.TryTake(nowUtc, 1, out _))
            FlushPending();
        if (!active) return;
        if (craftMethod == null || !patchInspectionScheduler.TryTake(nowUtc, 1, out _)) return;
        var patcher = patcherLease.Value;
        var detail = "The crafting patch-state stamp is unavailable.";
        if (patcher == null || !patcher.IsPatchSetStampCurrent(patchStamp, out detail))
        {
            DisableRuntime(
                patcher == null
                    ? "Crafting tracking disabled after patch drift: the Harmony owner is unavailable."
                    : $"Crafting tracking disabled after patch drift: {detail}");
        }
    }

    public bool FlushPending()
    {
        var hadPending = !pendingPublication.IsEmpty;
        try
        {
            if (!pendingPublication.TryFlush(recordHandler)) return false;
            if (!hadPending) return true;
            return persistenceHandler();
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
        if (boundary.OutstandingCount != 0)
        {
            DiagnosticOnce(
                "cleanup-inflight",
                "Crafting cleanup is retained until all already-started native craft tasks finish or fail and every proven completion finishes aggregate publication; no new craft tasks are accepted.");
            return false;
        }
        if (!FlushPending())
        {
            DiagnosticOnce("cleanup-flush", "Crafting cleanup is retained until its pending aggregate is accepted.");
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
            patchStamp = null;
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

    internal UniTask<List<Item>> WrapNativeCraft(CraftingFormula formula, UniTask<List<Item>> source)
    {
        CraftingCompletionToken token;
        lock (lifecycleSync)
        {
            if (!accepting
                || capabilities.CompletionActions.State != AdapterCapabilityState.Supported
                || formula.result.amount <= 0
                || string.IsNullOrWhiteSpace(formula.id))
                return source;
            var itemId = formula.result.id.ToString(CultureInfo.InvariantCulture);
            token = boundary.Begin(new CraftingCompletionEvidence(
                itemId,
                ReadDisplayName(formula.result.id, itemId),
                formula.id,
                formula.result.amount));
        }
        return AwaitCompletion(source, token);
    }

    private async UniTask<List<Item>> AwaitCompletion(
        UniTask<List<Item>> source,
        CraftingCompletionToken token)
    {
        var publicationClaimed = false;
        try
        {
            List<Item> result;
            try
            {
                result = await source;
            }
            catch
            {
                boundary.Abandon(token);
                throw;
            }
            if (result == null)
            {
                boundary.Abandon(token);
                return result!;
            }
            try
            {
                string generationId;
                CraftingMutation mutation;
                lock (lifecycleSync)
                {
                    if (terminalShutdownRequested) return result;
                    generationId = generationIdProvider();
                    if (!string.IsNullOrWhiteSpace(generationId)
                        && boundary.TryComplete(token, generationId, DateTime.UtcNow, out var completedMutation))
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
                    boundary.Abandon(token);
                    DisableRuntime("Crafting tracking disabled after completion because the active save generation was unavailable.");
                    return result;
                }
                if (!publicationClaimed) return result;
                var wasEmpty = pendingPublication.Add(mutation);
                if (wasEmpty && !FlushPending())
                {
                    pendingRetryScheduler.Reset(DateTime.UtcNow, 1);
                    DiagnosticOnce("completion-pending", "A proven crafting completion is retained for aggregate publication retry.");
                }
            }
            catch (Exception exception)
            {
                if (!publicationClaimed) boundary.Abandon(token);
                DisableRuntime(
                    $"Crafting tracking disabled after a post-delivery instrumentation failure: {Unwrap(exception).GetType().Name}: {Unwrap(exception).Message}");
            }
            return result;
        }
        finally
        {
            if (publicationClaimed) boundary.FinishPublication(token);
            bool retryCleanup;
            lock (lifecycleSync) retryCleanup = cleanupRequested;
            if (retryCleanup)
            {
                try { cleanupRetryHandler(); }
                catch (Exception exception)
                {
                    DiagnosticOnce(
                        "cleanup-retry:" + exception.GetType().Name,
                        $"Crafting cleanup retry failed and remains pending: {Unwrap(exception).Message}");
                }
            }
        }
    }

    private static void ResolveContracts(out MethodInfo resolvedCraftMethod)
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
        PublishCapabilities();
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
        try { PublishCapabilities(); }
        catch (Exception exception)
        {
            DiagnosticOnce("runtime-disable-publish", $"Crafting capability publication failed safely: {Unwrap(exception).Message}");
        }
        DiagnosticOnce("runtime-disabled:" + detail, detail);
    }

    private void PublishCapabilities()
    {
        try { capabilityHandler(Records(), MetricCapabilities); }
        catch (Exception exception)
        {
            DiagnosticOnce("capability-publish", $"Crafting capability publication failed safely: {Unwrap(exception).Message}");
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
