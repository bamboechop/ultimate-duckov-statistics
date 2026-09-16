using System.Runtime.CompilerServices;
using ItemStatsSystem;
using ItemStatsSystem.Items;

namespace UltimateDuckovStatistics.Encounters;

internal sealed partial class NativeEncounterLootObserver
{
    private int detachedRegistrations;
    private int worldDropRegistrations;
    private ConditionalWeakTable<Item, WorldMarker> worldDropped = new();

    private Endpoint Resolve(Item? item)
    {
        for (var depth = 0; depth < 12 && item != null; depth++)
        {
            if (item.InInventory != null && inventories.TryGetValue(item.InInventory, out var corpse)) return Endpoint.ForCorpse(corpse);
            if (ReferenceEquals(item, CharacterMainControl.Main?.CharacterItem)) return Endpoint.Player;
            if (item.InInventory != null && ReferenceEquals(item.InInventory, PetProxy.PetInventory)) return Endpoint.Pet;
            var parent = item.ParentItem;
            if (parent == null) return Endpoint.Unknown;
            item = parent;
        }
        return Endpoint.Unknown;
    }

    private Endpoint Resolve(Inventory inventory)
    {
        if (inventories.TryGetValue(inventory, out var corpse)) return Endpoint.ForCorpse(corpse);
        if (ReferenceEquals(inventory, CharacterMainControl.Main?.CharacterItem?.Inventory)) return Endpoint.Player;
        if (ReferenceEquals(inventory, PetProxy.PetInventory)) return Endpoint.Pet;
        return Resolve(inventory.AttachedToItem);
    }

    private bool Revealed(Item item, Endpoint source, Endpoint destination, Detached? prior)
    {
        if (source.CorpseId != 0)
            return items.TryGetValue(item, out var corpse) && corpse.BoxId == source.CorpseId && corpse.Observed && !item.NeedInspection;
        if (source.Kind is "Player" or "Pet") return !item.NeedInspection;
        return prior?.Revealed ?? (destination.Observed && item.Inspected);
    }

    private Detached? Prior(Item item, Endpoint source)
    {
        if (source.Kind != "Unknown" || item.ParentObject != null || worldDropped.TryGetValue(item, out _)) return null;
        return detached.TryGetValue(item, out var prior) && prior.Epoch == epoch && prior.Valid ? prior : null;
    }

    private Operation? Begin(string kind, Item item, Endpoint destination, Item? receiving = null,
        Inventory? targetInventory = null, int targetIndex = -1, Slot? targetSlot = null)
    {
        if (!Active || item == null || subscriptions.Count == 0) return null;
        var source = Resolve(item);
        var prior = Prior(item, source);
        var playerDetachInLoot = kind == "Detach" && (source.Kind is "Player" or "Pet")
            && (activeViewCorpse != null || operations.Count != 0);
        if (source.CorpseId == 0 && destination.CorpseId == 0 && prior == null && !playerDetachInLoot) return null;
        if (operations.Count >= ScopeLimit) throw new InvalidOperationException("Loot operation depth capacity reached.");
        var visible = Revealed(item, source, destination, prior);
        var quantity = item.StackCount;
        var priorConfirmed = prior != null && prior.Remaining == quantity && prior.NativeTypeId == item.TypeID;
        var operation = new Operation
        {
            Id = ++sequence, ParentId = operations.Count == 0 ? 0 : operations[operations.Count - 1].Id,
            Kind = kind, Item = item, ItemId = Id(item), Source = source, Destination = destination,
            Prior = prior, EffectiveSource = source.Kind != "Unknown" ? source : priorConfirmed ? prior!.Source : Endpoint.Unknown,
            QuantityBefore = quantity, NativeTypeId = item.TypeID,
            TypeId = visible ? (int?)item.TypeID : null, Revealed = visible,
            Receiving = receiving, ReceivingBefore = receiving == null ? 0 : receiving.StackCount,
            ReceivingType = receiving == null ? -1 : receiving.TypeID,
            TargetInventory = targetInventory, TargetIndex = targetIndex, TargetSlot = targetSlot, Epoch = epoch
        };
        operations.Add(operation);
        return operation;
    }

    private void Finish(Operation? operation, bool? nativeResult, Exception? nativeException)
    {
        if (operation == null) return;
        operations.Remove(operation);
        if (!Active || operation.Epoch != epoch) return;
        var item = operation.Item;
        var exists = item != null;
        var remaining = exists ? item!.StackCount : 0;
        var destinationAfter = exists ? Resolve(item) : Endpoint.Unknown;
        var evidence = "RawOperationOnly";
        int? measured = null;
        var flowDestination = operation.Destination;

        if (operation.Kind == "Combine")
        {
            var receiver = operation.Receiving;
            if (receiver != null && operation.NativeTypeId == operation.ReceivingType && receiver.TypeID == operation.ReceivingType
                && (!exists || item!.TypeID == operation.NativeTypeId)
                && LootProbeQuantityEvidence.TryCombineDelta(operation.QuantityBefore, remaining, operation.ReceivingBefore,
                    receiver.StackCount, nativeException != null, out var confirmed))
            {
                measured = confirmed;
                evidence = "ExactCombineDelta";
                if (!Endpoint.Same(Resolve(receiver), flowDestination)) evidence = "ReceivingOwnerChangedDuringCombine";
                else Reconcile(operation, confirmed, flowDestination);
            }
            else evidence = "CombineDeltaUnresolved";
        }
        else if (operation.Kind is "AddAt" or "Plug")
        {
            var exactTarget = nativeException == null && nativeResult == true && exists && (operation.Kind == "AddAt"
                ? operation.TargetInventory != null && ReferenceEquals(item!.InInventory, operation.TargetInventory)
                    && ReferenceEquals(operation.TargetInventory.GetItemAt(operation.TargetIndex), item)
                : operation.TargetSlot != null && ReferenceEquals(item!.PluggedIntoSlot, operation.TargetSlot)
                    && ReferenceEquals(operation.TargetSlot.Content, item));
            if (nativeException == null && nativeResult == true && exactTarget && Endpoint.Same(destinationAfter, flowDestination)
                && remaining == operation.QuantityBefore && item!.TypeID == operation.NativeTypeId)
            {
                measured = remaining;
                evidence = "WholeItemDestinationConfirmed";
                Reconcile(operation, remaining, flowDestination);
            }
            else evidence = "NoWholeItemTransferProvenNestedPrimitivesMayExist";
            // Once attached anywhere, an old corpse detach must never bridge a later
            // NPC/player/world handoff as if it were a direct original take.
            if (exists && item!.ParentObject != null) { detached.Remove(item); worldDropped.Remove(item); }
        }
        else if (operation.Kind == "Detach" && nativeException == null && exists && item!.ParentObject == null)
        {
            if (!item.IsBeingDestroyed && remaining > 0 && !worldDropped.TryGetValue(item, out _))
            {
                if (operation.Source.Kind != "Unknown")
                    Remember(item, operation.Source, operation.Id, operation.Revealed, operation.NativeTypeId, remaining, "NativeDetach", operation.ItemId);
                // Split result -> Detach is a native no-op. Keep its exact split proof.
            }
            else detached.Remove(item);
            evidence = "RemovalOnlyDestinationPending";
        }
        Emit("operation", new
        {
            OperationId = operation.Id, ParentOperationId = operation.ParentId, operation.Kind, operation.ItemId,
            operation.TypeId, operation.Revealed, Source = operation.Source, EffectiveSource = operation.EffectiveSource,
            RequestedDestination = operation.Destination, DestinationAfter = destinationAfter,
            PriorDetachOrSplitId = operation.Prior?.OperationId, PriorBasis = operation.Prior?.Basis,
            Before = operation.Revealed ? (int?)operation.QuantityBefore : null,
            After = operation.Revealed ? (int?)remaining : null,
            QuantityMoved = operation.Revealed ? measured : null, NativeResult = nativeResult,
            NativeException = nativeException?.GetType().Name, Evidence = evidence,
            AlreadyCommittedChildQuantity = operation.Revealed ? (long?)operation.CommittedChildren : null,
            operation.ConflictingChildren, Additive = false, TransferCoverage = "Partial"
        });
    }

    private void Reconcile(Operation operation, int measured, Endpoint destination)
    {
        if (!LootTransferAccounting.TryResidual(measured, operation.CommittedChildren, operation.ConflictingChildren, out var quantity))
        {
            Emit("transfer-unresolved", new { OperationId = operation.Id, Reason = "Nested quantity evidence conflicts", Partial = true });
            return;
        }
        if (quantity == 0) return;
        var source = operation.EffectiveSource;
        if (source.Kind == "Unknown" || destination.Kind == "Unknown")
        {
            Emit("transfer-unresolved", new { OperationId = operation.Id, Reason = "Unproved source or destination ownership", Partial = true });
            if (operation.Prior != null) operation.Prior.Valid = false;
            return;
        }
        if (operation.Source.Kind == "Unknown")
        {
            var prior = operation.Prior;
            if (prior == null || !prior.Valid || prior.Epoch != epoch || prior.Remaining < quantity)
            {
                Emit("transfer-unresolved", new { OperationId = operation.Id, Reason = "Detached quantity provenance no longer matches", Partial = true });
                return;
            }
            prior.Remaining -= quantity;
        }
        // Parent operation boundaries can overlap these primitives. Reserve the
        // already-recorded quantity before a parent reaches its finalizer.
        foreach (var parent in operations)
        {
            if (!ReferenceEquals(parent.Item, operation.Item) || parent.Kind == "Detach") continue;
            if (Endpoint.Same(parent.EffectiveSource, source) && Endpoint.Same(parent.Destination, destination))
                parent.CommittedChildren = checked(parent.CommittedChildren + quantity);
            else parent.ConflictingChildren = true;
        }
        if (Endpoint.Same(source, destination)) return;
        var direction = source.CorpseId != 0 && destination.Kind == "Player" ? "TakenToPlayer"
            : source.CorpseId != 0 && destination.Kind == "Pet" ? "TakenToPet"
            : destination.CorpseId != 0 && source.Kind == "Player" ? "ReturnedFromPlayer"
            : destination.CorpseId != 0 && source.Kind == "Pet" ? "ReturnedFromPet" : null;
        if (direction == null)
        {
            Emit("transfer-other-owner", new { OperationId = operation.Id, Source = source, Destination = destination, Additive = false });
            return;
        }
        var corpseId = source.CorpseId != 0 ? source.CorpseId : destination.CorpseId;
        if (!corpseRecords.TryGetValue(corpseId, out var corpse)) return;
        LootTransferAccounting? totals = null;
        if (operation.Revealed && operation.TypeId is int type)
        {
            if (!corpse.Totals.TryGetValue(type, out totals))
            {
                if (corpse.Totals.Count >= 512) throw new InvalidOperationException("Loot per-corpse item type limit reached.");
                corpse.Totals.Add(type, totals = new LootTransferAccounting());
            }
            totals.Add(direction, quantity);
        }
        Emit("transfer", new
        {
            TransferId = ++sequence, OperationId = operation.Id, ParentOperationId = operation.ParentId,
            corpse.ActorId, CorpseId = corpse.BoxId, corpse.InventoryId, operation.ItemId, operation.TypeId,
            Direction = direction, Quantity = operation.Revealed ? (int?)quantity : null,
            Source = source, Destination = destination,
            SourceLink = operation.Source.Kind == "Unknown" ? operation.Prior?.Basis : "AttachedAtOperationEntry",
            SourceOperationId = operation.Prior?.OperationId, SourceItemId = operation.Prior?.SourceItemId,
            Additive = true, Evidence = "MatchedNativeQuantityAndOwnership",
            // Copy scalar counters: never queue the mutable accounting object.
            TakenToPlayer = totals?.TakenToPlayer, TakenToPet = totals?.TakenToPet,
            ReturnedFromPlayer = totals?.ReturnedFromPlayer, ReturnedFromPet = totals?.ReturnedFromPet,
            NetOutward = totals?.NetOutward,
            Semantics = "Gross directed corpse flow; returns may include player-added identical items; no unique-unit/current-possession claim",
            TransferCoverage = "PartialUntilNativePathQualification"
        });
    }

    private void Remember(Item item, Endpoint source, long operationId, bool revealed, int type, int quantity, string basis, int sourceItemId)
    {
        if (detachedRegistrations >= ItemLimit) throw new InvalidOperationException("Loot detached provenance capacity reached.");
        detached.Remove(item);
        detached.Add(item, new Detached(source, operationId, revealed, type, quantity, epoch, basis, sourceItemId));
        detachedRegistrations++;
    }

    private void ObserveWorldDrop(Item item)
    {
        if (!Active || item == null || subscriptions.Count == 0) return;
        var source = Resolve(item);
        var prior = Prior(item, source);
        if (source.CorpseId == 0 && prior == null && activeViewCorpse == null) return;
        if (prior != null) prior.Valid = false;
        detached.Remove(item);
        if (!worldDropped.TryGetValue(item, out _))
        {
            if (worldDropRegistrations >= ItemLimit) throw new InvalidOperationException("Loot world-drop marker limit reached.");
            worldDropped.Add(item, new WorldMarker());
            worldDropRegistrations++;
        }
        Emit("world-drop-boundary", new { ItemId = Id(item), Source = source,
            Reason = "World drop breaks direct detached-to-recipient proof", Additive = false });
    }

    private sealed class WorldMarker { }
    private sealed class Endpoint
    {
        public static readonly Endpoint Unknown = new("Unknown", 0, 0);
        public static readonly Endpoint Player = new("Player", 0, 0);
        public static readonly Endpoint Pet = new("Pet", 0, 0);
        public Endpoint(string kind, int actorId, int corpseId, bool observed = false) { Kind = kind; ActorId = actorId; CorpseId = corpseId; Observed = observed; }
        public string Kind { get; }
        public int ActorId { get; }
        public int CorpseId { get; }
        public bool Observed { get; }
        public static Endpoint ForCorpse(Corpse corpse) => new("Corpse", corpse.ActorId, corpse.BoxId, corpse.Observed);
        public static bool Same(Endpoint left, Endpoint right) => left.Kind == right.Kind && left.CorpseId == right.CorpseId && left.ActorId == right.ActorId;
    }

    private sealed class Detached
    {
        public Detached(Endpoint source, long operationId, bool revealed, int type, int remaining, long epoch, string basis, int sourceItemId)
        { Source = source; OperationId = operationId; Revealed = revealed; NativeTypeId = type; Remaining = remaining; Epoch = epoch; Basis = basis; SourceItemId = sourceItemId; }
        public Endpoint Source { get; }
        public long OperationId { get; }
        public bool Revealed { get; }
        public int NativeTypeId { get; }
        public int Remaining { get; set; }
        public long Epoch { get; }
        public bool Valid { get; set; } = true;
        public string Basis { get; }
        public int SourceItemId { get; }
    }

    private sealed class Operation
    {
        public long Id { get; set; }
        public long ParentId { get; set; }
        public long Epoch { get; set; }
        public string Kind { get; set; } = string.Empty;
        public Item? Item { get; set; }
        public Item? Receiving { get; set; }
        public Inventory? TargetInventory { get; set; }
        public int TargetIndex { get; set; }
        public Slot? TargetSlot { get; set; }
        public int ItemId { get; set; }
        public int QuantityBefore { get; set; }
        public int ReceivingBefore { get; set; }
        public int ReceivingType { get; set; }
        public int NativeTypeId { get; set; }
        public int? TypeId { get; set; }
        public bool Revealed { get; set; }
        public Endpoint Source { get; set; } = Endpoint.Unknown;
        public Endpoint EffectiveSource { get; set; } = Endpoint.Unknown;
        public Endpoint Destination { get; set; } = Endpoint.Unknown;
        public Detached? Prior { get; set; }
        public long CommittedChildren { get; set; }
        public bool ConflictingChildren { get; set; }
    }
}
