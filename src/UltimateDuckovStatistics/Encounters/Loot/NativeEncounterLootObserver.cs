using System.Reflection;
using System.Runtime.CompilerServices;
using Cysharp.Threading.Tasks;
using Duckov.UI;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Compatibility;
using UnityEngine;

namespace UltimateDuckovStatistics.Encounters;

// Qualification evidence only. Raw operation records are non-additive; separate
// reconciled transfer records represent proven directed quantity flow.
internal sealed partial class NativeEncounterLootObserver : IEncounterObserver
{
    public Core.Encounters.EncounterCaptureIssue? FailureIssue => healthy ? null : Core.Encounters.EncounterCaptureIssue.LootIncomplete;
    private const string OwnerId = "at.bamboechop.ultimate-duckov-statistics.encounters.loot";
    private const int RegistryLimit = 4096;
    private const int ItemLimit = 32768;
    private const int ScopeLimit = 32;
    private static NativeEncounterLootObserver? current;
    private readonly IEncounterObservationSink sink;
    private readonly Action<string> log;
    private readonly int owningThread = Environment.CurrentManagedThreadId;
    private readonly RetryableHarmonyPatcherLease lease = new();
    private readonly List<Registration> registrations = new();
    private readonly List<WeakReference<Inventory>> subscriptions = new();
    private readonly Dictionary<int, Corpse> corpseRecords = new();
    private ConditionalWeakTable<InteractableLootbox, Corpse> boxes = new();
    private ConditionalWeakTable<Inventory, Corpse> inventories = new();
    private ConditionalWeakTable<Item, Corpse> items = new();
    private ConditionalWeakTable<Item, Detached> detached = new();
    private readonly List<DeathScope> deaths = new();
    private readonly List<Operation> operations = new();
    private readonly FieldInfo localInventory = null!;
    private readonly FieldInfo interactingCharacter = null!;
    private readonly FieldInfo viewTargetBox = null!;
    private bool disposed;
    private bool healthy;
    private bool observationLimited;
    private int registeredItems;
    private long sequence;
    private double nextVerification;
    private string generation = string.Empty;
    private string run = string.Empty;
    private long epoch;

    public NativeEncounterLootObserver(IEncounterObservationSink sink, Action<string> log)
    {
        this.sink = sink;
        this.log = log;
        try
        {
            localInventory = typeof(InteractableLootbox).GetField("inventoryReference", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException("InteractableLootbox.inventoryReference");
            interactingCharacter = typeof(InteractableBase).GetField("interactCharacter", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException("InteractableBase.interactCharacter");
            viewTargetBox = typeof(LootView).GetField("targetLootBox", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new MissingFieldException("LootView.targetLootBox");
            if (current != null) throw new InvalidOperationException("A loot probe already owns callbacks.");
            if (localInventory.FieldType != typeof(Inventory) || interactingCharacter.FieldType != typeof(CharacterMainControl)
                || viewTargetBox.FieldType != typeof(InteractableLootbox))
                throw new MissingFieldException("Loot inventory or interacting-character field type changed.");
            Add(typeof(Item), "Detach", typeof(void), Type.EmptyTypes, nameof(DetachPrefix), null, nameof(DetachFinalizer));
            Add(typeof(Item), "Combine", typeof(void), new[] { typeof(Item) }, nameof(CombinePrefix), null, nameof(CombineFinalizer));
            Add(typeof(Inventory), "AddAt", typeof(bool), new[] { typeof(Item), typeof(int) }, nameof(AddAtPrefix), null, nameof(AddAtFinalizer));
            Add(typeof(Slot), "Plug", typeof(bool), new[] { typeof(Item), typeof(Item).MakeByRefType() }, nameof(PlugPrefix), null, nameof(PlugFinalizer));
            Add(typeof(Item), "Split", typeof(UniTask<Item>), new[] { typeof(int) }, nameof(SplitPrefix), nameof(SplitPostfix), nameof(SplitFinalizer));
            Add(typeof(Item), "set_Inspected", typeof(void), new[] { typeof(bool) }, null, nameof(InspectedPostfix), null);
            Add(typeof(LootView), "OnOpen", typeof(void), Type.EmptyTypes, null, nameof(ViewOpenPostfix), null);
            Add(typeof(LootView), "OnClose", typeof(void), Type.EmptyTypes, nameof(ViewClosePrefix), null, null);
            Add(typeof(LootView), "OnDisable", typeof(void), Type.EmptyTypes, nameof(ViewClosePrefix), null, null);
            Add(typeof(ItemExtensions), "Drop", typeof(DuckovItemAgent), new[] { typeof(Item), typeof(Vector3), typeof(bool), typeof(Vector3), typeof(float) }, nameof(WorldDropPrefix), null, null);
            if (!ReflectiveHarmonyPatcher.TryCreate(OwnerId, out var patcher, out var detail) || patcher == null)
                throw new InvalidOperationException(detail);
            lease.Attach(patcher);
            // Shared death/corpse boundaries come from the existing container bridge.
            // Low-level item targets retain strict isolated patch ownership.
            foreach (var registration in registrations)
                if (!patcher.IsPatchSetTrusted(registration.Target, Array.Empty<HarmonyPatchExpectation>(), out detail))
                    throw new InvalidOperationException(registration.Target.Name + ": " + detail);
            current = this;
            foreach (var registration in registrations)
                patcher.Patch(registration.Target, registration.Prefix, registration.Postfix, finalizer: registration.Finalizer);
            Verify();
            InteractableLootbox.OnStartLoot += Open;
            InteractableLootbox.OnStopLoot += InspectionStopped;
            generation = sink.Context.GenerationId;
            run = sink.Context.RunId;
            healthy = true;
            Status = "Active: split completion and reconciled directed loot quantities; aggregate coverage remains partial pending native qualification.";
        }
        catch (Exception exception)
        {
            Fault(exception);
            if (ReferenceEquals(current, this)) current = null;
            lease.TryCleanup(out _);
        }
    }

    public string Status { get; private set; } = "Unavailable";
    private bool Active
    {
        get
        {
            try
            {
                if (Environment.CurrentManagedThreadId != owningThread) return false;
                var context = sink.Context;
                return healthy && !disposed && context.Active
                    && string.Equals(generation, context.GenerationId, StringComparison.Ordinal)
                    && string.Equals(run, context.RunId, StringComparison.Ordinal);
            }
            catch (Exception exception) { Fault(exception); return false; }
        }
    }

    public void Tick(EncounterObservationContext context)
    {
        if (!healthy || disposed) return;
        Guard(() =>
        {
            if (!string.Equals(generation, context.GenerationId, StringComparison.Ordinal)
                || !string.Equals(run, context.RunId, StringComparison.Ordinal))
            {
                ResetRegistry();
                generation = context.GenerationId;
                run = context.RunId;
            }
            DrainSplitCompletions();
            RefreshViewLifetime();
            if (context.MonotonicSeconds >= nextVerification)
            {
                nextVerification = context.MonotonicSeconds + 1;
                Verify();
            }
        });
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        healthy = false;
        if (ReferenceEquals(current, this)) current = null;
        InteractableLootbox.OnStartLoot -= Open;
        InteractableLootbox.OnStopLoot -= InspectionStopped;
        try { ResetRegistry(); }
        catch (Exception exception) { SafeLog("Encounter loot registry cleanup: " + exception.GetType().Name); }
        try
        {
            if (!lease.TryCleanup(out var detail)) SafeLog("Encounter loot cleanup pending: " + detail);
        }
        catch (Exception exception) { SafeLog("Encounter loot patch cleanup: " + exception.GetType().Name); }
        Status = "Disposed";
    }

    private void ResetRegistry()
    {
        foreach (var weak in subscriptions)
            if (weak.TryGetTarget(out var inventory) && inventory != null)
                inventory.onContentChanged -= ContentChanged;
        subscriptions.Clear();
        corpseRecords.Clear();
        boxes = new(); inventories = new(); items = new(); detached = new();
        worldDropped = new();
        deaths.Clear(); operations.Clear();
        epoch++;
        pendingSplits.Clear();
        splitLimitReported = false;
        Interlocked.Exchange(ref splitQueueOverflow, 0);
        while (splitCompletions.TryDequeue(out _)) Interlocked.Decrement(ref queuedSplitCompletions);
        activeView = null;
        activeViewCorpse = null;
        registeredItems = 0;
        detachedRegistrations = 0;
        worldDropRegistrations = 0;
        observationLimited = false;
    }

    private void Add(Type type, string name, Type result, Type[] parameters, string? prefix, string? postfix, string? finalizer)
    {
        var method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, parameters, null);
        if (method == null || method.ReturnType != result) throw new MissingMethodException(type.FullName, name);
        registrations.Add(new Registration(method, Callback(prefix), Callback(postfix), Callback(finalizer)));
    }

    private static MethodInfo? Callback(string? name) => name == null ? null :
        typeof(NativeEncounterLootObserver).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(name);

    private void Verify()
    {
        var patcher = lease.Value ?? throw new InvalidOperationException("Loot patch owner missing.");
        foreach (var registration in registrations)
            if (!patcher.IsPatchSetTrusted(registration.Target, registration.Expected, out var detail))
                throw new InvalidOperationException(registration.Target.Name + ": " + detail);
    }

    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception exception) { Fault(exception); }
    }

    private void Fault(Exception exception)
    {
        healthy = false;
        deaths.Clear();
        operations.Clear();
        Status = "Unavailable: " + exception.GetType().Name + ": " + exception.Message;
        SafeLog("Encounter loot " + Status);
    }

    private void SafeLog(string text) { try { log(text); } catch { } }
    private void Emit(string kind, object payload) => sink.Record("loot." + kind, payload);
    private int Id(UnityEngine.Object value) => sink.ActorId(value);

    private DeathScope? BeginDeath(CharacterMainControl actor)
    {
        if (!Active) return null;
        if (deaths.Count >= ScopeLimit) throw new InvalidOperationException("Loot death scope capacity reached.");
        var scope = new DeathScope(actor.CharacterItem, Id(actor), ++sequence);
        deaths.Add(scope);
        Emit("death-source", new { ActorId = scope.ActorId, SourceItemId = scope.Item == null ? 0 : Id(scope.Item), OperationId = scope.Sequence });
        return scope;
    }

    private void EndDeath(DeathScope? scope)
    {
        if (scope == null) return;
        deaths.Remove(scope);
    }

    private void Created(Item source, InteractableLootbox? box)
    {
        if (!Active || box == null || deaths.Count == 0) return;
        var scope = deaths[deaths.Count - 1];
        if (!ReferenceEquals(scope.Item, source)) return;
        if (boxes.TryGetValue(box, out _)) return;
        if (subscriptions.Count >= RegistryLimit) throw new InvalidOperationException("Loot corpse registry capacity reached.");
        // Read the verified local field: the Inventory getter can create other inventories.
        if (localInventory.GetValue(box) is not Inventory inventory || inventory == null)
        {
            Emit("join-unavailable", new { ActorId = scope.ActorId, Reason = "No local corpse inventory" });
            return;
        }
        if (inventories.TryGetValue(inventory, out _)) throw new InvalidOperationException("Corpse inventory was already joined.");
        var corpse = new Corpse(scope.ActorId, Id(box), Id(inventory));
        corpseRecords.Add(corpse.BoxId, corpse);
        boxes.Add(box, corpse);
        inventories.Add(inventory, corpse);
        subscriptions.Add(new WeakReference<Inventory>(inventory));
        inventory.onContentChanged += ContentChanged;
        Emit("corpse-join", new { ActorId = scope.ActorId, SourceItemId = Id(source), CorpseId = corpse.BoxId, InventoryId = corpse.InventoryId, DeathOperationId = scope.Sequence, Observed = false, TransferCoverage = "Partial" });
    }

    private void Open(InteractableLootbox box) => Guard(() =>
    {
        if (!Active || !boxes.TryGetValue(box, out var corpse)) return;
        if (!ReferenceEquals(interactingCharacter.GetValue(box), CharacterMainControl.Main)) return;
        Emit("inventory-access-start", new { corpse.ActorId, CorpseId = corpse.BoxId, corpse.InventoryId });
    });

    private void InspectionStopped(InteractableLootbox box) => Guard(() =>
    {
        if (!Active || !boxes.TryGetValue(box, out var corpse)) return;
        Emit("inspection-stop", new { corpse.ActorId, CorpseId = corpse.BoxId, corpse.InventoryId,
            ViewStillObserved = corpse.Open, Meaning = "Native interaction/inspection stopped; not a LootView close" });
    });

    private void ContentChanged(Inventory inventory, int index) => Guard(() =>
    {
        if (!Active || !inventories.TryGetValue(inventory, out var corpse) || !corpse.Open) return;
        var item = index < 0 ? null : inventory.GetItemAt(index);
        if (item != null) Track(item, corpse);
        Emit("inventory-row", new { corpse.ActorId, CorpseId = corpse.BoxId, corpse.InventoryId, Index = index, Loading = inventory.Loading, Row = item == null ? null : Row(item, corpse, index), Evidence = "ObservationOnlyNotTransfer" });
    });

    private void Track(Item item, Corpse corpse)
    {
        if (items.TryGetValue(item, out var previous))
        {
            if (!ReferenceEquals(previous, corpse))
            {
                items.Remove(item);
                items.Add(item, corpse);
            }
            return;
        }
        if (registeredItems >= ItemLimit)
        {
            if (!observationLimited) Emit("coverage", new { Reason = "Observed item registry limit", TransferCoverage = "Partial" });
            observationLimited = true;
            return;
        }
        items.Add(item, corpse);
        registeredItems++;
    }

    private object Row(Item item, Corpse corpse, int index)
    {
        var revealed = corpse.Observed && !item.NeedInspection;
        return new { Index = index, ItemId = Id(item), Revealed = revealed, TypeId = revealed ? (int?)item.TypeID : null, Quantity = revealed ? (int?)item.StackCount : null };
    }

    private void Inspection(Item item)
    {
        if (!Active || !items.TryGetValue(item, out var corpse) || !corpse.Open) return;
        if (item.InInventory == null || !inventories.TryGetValue(item.InInventory, out var currentCorpse) || !ReferenceEquals(corpse, currentCorpse)) return;
        Emit("inspection", new { corpse.ActorId, CorpseId = corpse.BoxId, Row = Row(item, corpse, item.InInventory.GetIndex(item)) });
    }

    private sealed class Corpse
    {
        public Corpse(int actor, int box, int inventory) { ActorId = actor; BoxId = box; InventoryId = inventory; }
        public int ActorId { get; }
        public int BoxId { get; }
        public int InventoryId { get; }
        public bool Open { get; set; }
        public bool Observed { get; set; }
        public Dictionary<int, LootTransferAccounting> Totals { get; } = new();
    }

    private sealed class DeathScope
    {
        public DeathScope(Item? item, int actorId, long sequence) { Item = item; ActorId = actorId; Sequence = sequence; }
        public Item? Item { get; }
        public int ActorId { get; }
        public long Sequence { get; }
    }

    private sealed class Registration
    {
        public Registration(MethodInfo target, MethodInfo? prefix, MethodInfo? postfix, MethodInfo? finalizer)
        {
            Target = target; Prefix = prefix; Postfix = postfix; Finalizer = finalizer;
            var expected = new List<HarmonyPatchExpectation>();
            if (prefix != null) expected.Add(new HarmonyPatchExpectation("Prefixes", prefix));
            if (postfix != null) expected.Add(new HarmonyPatchExpectation("Postfixes", postfix));
            if (finalizer != null) expected.Add(new HarmonyPatchExpectation("Finalizers", finalizer));
            Expected = expected.ToArray();
        }
        public MethodInfo Target { get; }
        public MethodInfo? Prefix { get; }
        public MethodInfo? Postfix { get; }
        public MethodInfo? Finalizer { get; }
        public HarmonyPatchExpectation[] Expected { get; }
    }
}
