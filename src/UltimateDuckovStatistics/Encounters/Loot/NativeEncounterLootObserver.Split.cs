using System.Collections.Concurrent;
using Cysharp.Threading.Tasks;
using ItemStatsSystem;

namespace UltimateDuckovStatistics.Encounters;

internal sealed partial class NativeEncounterLootObserver
{
    private const int MaximumPendingSplits = 128;
    private readonly Dictionary<long, SplitRequest> pendingSplits = new();
    private readonly ConcurrentQueue<SplitCompletion> splitCompletions = new();
    private int queuedSplitCompletions;
    private int splitQueueOverflow;
    private bool splitLimitReported;

    private SplitRequest? BeginSplit(Item item, int count)
    {
        if (!Active || subscriptions.Count == 0 || item == null) return null;
        var source = Resolve(item);
        var prior = Prior(item, source);
        if (source.CorpseId == 0 && prior == null
            && !(activeViewCorpse != null && (source.Kind is "Player" or "Pet"))) return null;
        if (pendingSplits.Count >= MaximumPendingSplits)
        {
            if (!splitLimitReported)
            {
                splitLimitReported = true;
                Emit("split-coverage", new { Reason = "Pending split observation limit; native task left untouched", Partial = true });
            }
            return null;
        }
        var quantity = item.StackCount;
        var effectiveSource = source.Kind != "Unknown" ? source
            : prior != null && prior.Remaining == quantity && prior.NativeTypeId == item.TypeID ? prior.Source : Endpoint.Unknown;
        var request = new SplitRequest
        {
            Id = ++sequence, Epoch = epoch, Generation = generation, Run = run,
            SourceItem = new WeakReference<Item>(item), SourceItemId = Id(item), Source = effectiveSource,
            Before = quantity, Requested = count, NativeTypeId = item.TypeID,
            Revealed = Revealed(item, source, Endpoint.Unknown, prior), Prior = prior
        };
        pendingSplits.Add(request.Id, request);
        Emit("split-request", new
        {
            OperationId = request.Id, ItemId = request.SourceItemId, Source = request.Source,
            TypeId = request.Revealed ? (int?)request.NativeTypeId : null,
            RequestedQuantity = request.Revealed ? (int?)count : null,
            Evidence = "RequestOnlyAwaitingNativeCompletion", Additive = false
        });
        return request;
    }

    private UniTask<Item> WrapSplit(UniTask<Item> original, SplitRequest request)
    {
        if (!Active || request.Epoch != epoch || !pendingSplits.ContainsKey(request.Id)) return original;
        if (request.SourceItem.TryGetTarget(out var source) && source != null)
        {
            request.AfterInitialCall = source.StackCount;
            request.InitialDeltaVerified = request.Requested > 0 && request.Requested < request.Before
                && (long)request.Before - request.AfterInitialCall == request.Requested;
            if (request.Prior != null && request.InitialDeltaVerified)
            {
                if (request.Prior.Remaining == request.Before) request.Prior.Remaining = request.AfterInitialCall;
                else request.InitialDeltaVerified = false;
            }
        }
        var weak = new WeakReference<NativeEncounterLootObserver>(this);
        return AwaitSplit(original, weak, request);
    }

    private static async UniTask<Item> AwaitSplit(UniTask<Item> original,
        WeakReference<NativeEncounterLootObserver> weak, SplitRequest request)
    {
        // The returned wrapper is the sole consumer of the original task. This
        // eagerly registers one await, even if the native caller abandons its
        // result; it is deliberately not claimed to preserve passive scheduling.
        Item result;
        try { result = await original; }
        catch (Exception exception)
        {
            NotifySplit(weak, request, null, exception);
            throw;
        }
        NotifySplit(weak, request, result, null);
        return result;
    }

    private static void NotifySplit(WeakReference<NativeEncounterLootObserver> weak,
        SplitRequest request, Item? result, Exception? exception)
    {
        try
        {
            if (weak.TryGetTarget(out var owner)) owner.SplitCompleted(request, result, exception);
        }
        catch { /* Diagnostics must preserve the original result or exception. */ }
    }

    private void SplitCompleted(SplitRequest request, Item? result, Exception? exception)
    {
        // The wrapper invokes this before completing its own returned task. On
        // the game thread this binds the clone before Send/Detach/AddAt can run.
        // Off-thread completions use weak payloads only and never inspect Unity.
        if (Environment.CurrentManagedThreadId == owningThread)
        {
            Guard(() => CompleteSplit(request, result, exception?.GetType().Name, delayedObservation: false));
            return;
        }
        if (Interlocked.Increment(ref queuedSplitCompletions) > MaximumPendingSplits)
        {
            Interlocked.Decrement(ref queuedSplitCompletions);
            Interlocked.Exchange(ref splitQueueOverflow, 1);
            return;
        }
        splitCompletions.Enqueue(new SplitCompletion(request,
            ReferenceEquals(result, null) ? null : new WeakReference<Item>(result), exception?.GetType().Name));
    }

    private void CompleteSplit(SplitRequest request, Item? result, string? exceptionType, bool delayedObservation)
    {
        if (!pendingSplits.TryGetValue(request.Id, out var pending) || !ReferenceEquals(pending, request)) return;
        pendingSplits.Remove(request.Id);
        if (!Active || request.Epoch != epoch || request.Generation != generation || request.Run != run) return;
        var valid = exceptionType == null && !delayedObservation && request.InitialDeltaVerified
            && result != null && result.ParentObject == null && !result.IsBeingDestroyed
            && request.Source.Kind != "Unknown"
            && LootTransferAccounting.IsValidSplit(request.Before, request.AfterInitialCall,
                request.Requested, result.StackCount, request.NativeTypeId == result.TypeID);
        if (valid)
            Remember(result!, request.Source, request.Id, request.Revealed, request.NativeTypeId,
                request.Requested, "NativeSplitResultCompleted", request.SourceItemId);
        Emit("split-completed", new
        {
            OperationId = request.Id, SourceItemId = request.SourceItemId,
            ResultItemId = result == null ? (int?)null : Id(result), Source = request.Source,
            TypeId = request.Revealed ? (int?)request.NativeTypeId : null,
            Quantity = request.Revealed && result != null ? (int?)result.StackCount : null,
            SourceBefore = request.Revealed ? (int?)request.Before : null,
            SourceAfterInitialCall = request.Revealed ? (int?)request.AfterInitialCall : null,
            SourceQuantityDecrementVerified = request.InitialDeltaVerified,
            ProvenanceBound = valid, ExceptionType = exceptionType, DelayedMainThreadObservation = delayedObservation,
            Evidence = valid ? "ExactNativeSplitResultBeforeConsumerContinues" : "SplitProvenanceUnavailable",
            Additive = false, TransferProven = false
        });
    }

    private void DrainSplitCompletions()
    {
        if (Interlocked.Exchange(ref splitQueueOverflow, 0) != 0 && Active)
            Emit("split-coverage", new { Reason = "Off-thread split completion queue overflow", Partial = true });
        var drained = 0;
        while (drained++ < MaximumPendingSplits && splitCompletions.TryDequeue(out var completion))
        {
            Interlocked.Decrement(ref queuedSplitCompletions);
            Item? item = null;
            completion.Result?.TryGetTarget(out item);
            CompleteSplit(completion.Request, item, completion.ExceptionType, delayedObservation: true);
        }
    }

    private sealed class SplitRequest
    {
        public long Id { get; set; }
        public long Epoch { get; set; }
        public string Generation { get; set; } = string.Empty;
        public string Run { get; set; } = string.Empty;
        public WeakReference<Item> SourceItem { get; set; } = null!;
        public int SourceItemId { get; set; }
        public Endpoint Source { get; set; } = Endpoint.Unknown;
        public int Before { get; set; }
        public int AfterInitialCall { get; set; }
        public int Requested { get; set; }
        public int NativeTypeId { get; set; }
        public bool Revealed { get; set; }
        public bool InitialDeltaVerified { get; set; }
        public Detached? Prior { get; set; }
    }

    private sealed class SplitCompletion
    {
        public SplitCompletion(SplitRequest request, WeakReference<Item>? result, string? exceptionType)
        { Request = request; Result = result; ExceptionType = exceptionType; }
        public SplitRequest Request { get; }
        public WeakReference<Item>? Result { get; }
        public string? ExceptionType { get; }
    }
}
