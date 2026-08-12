using System.Globalization;
using ItemStatsSystem;
using ItemStatsSystem.Items;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;
using UnityEngine;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class NativeEquipmentAdapter : IDisposable, IRetryableCleanup
{
    internal const string AdapterVersion = "native-equipment/2.3.30+public-item-tree-v1";
    private const string SupportedGameVersion = "2.3.30";
    private const double ReconciliationIntervalSeconds = 0.2;
    private readonly Func<bool> runActiveProvider;
    private readonly Func<EquipmentSnapshot, bool> snapshotHandler;
    private readonly Func<bool> invalidationHandler;
    private readonly Action<IReadOnlyList<CapabilityRecord>> capabilityHandler;
    private readonly Action<string> diagnosticHandler;
    private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly MonotonicCadenceGate cadence = new(ReconciliationIntervalSeconds);
    private readonly NativeCallbackLifetime callbackLifetime = new();
    private CharacterMainControl? observedMain;
    private Item? observedCharacterItem;
    private EquipmentSnapshot? latestSnapshot;
    private EquipmentMetricCapabilities metricCapabilities =
        EquipmentNativeContractPolicy.CreateUnavailableCapabilities("Equipment tracking has not been initialized.");

    public NativeEquipmentAdapter(
        Func<bool> runActiveProvider,
        Func<EquipmentSnapshot, bool> snapshotHandler,
        Func<bool> invalidationHandler,
        Action<IReadOnlyList<CapabilityRecord>> capabilityHandler,
        Action<string> diagnosticHandler)
    {
        this.runActiveProvider = runActiveProvider ?? throw new ArgumentNullException(nameof(runActiveProvider));
        this.snapshotHandler = snapshotHandler ?? throw new ArgumentNullException(nameof(snapshotHandler));
        this.invalidationHandler = invalidationHandler ?? throw new ArgumentNullException(nameof(invalidationHandler));
        this.capabilityHandler = capabilityHandler ?? throw new ArgumentNullException(nameof(capabilityHandler));
        this.diagnosticHandler = diagnosticHandler ?? throw new ArgumentNullException(nameof(diagnosticHandler));
    }

    public EquipmentMetricCapabilities MetricCapabilities => EquipmentStatisticsReducer.CloneCapabilities(metricCapabilities);

    public IReadOnlyList<CapabilityRecord> Initialize()
    {
        if (callbackLifetime.DisposalStarted) throw new ObjectDisposedException(nameof(NativeEquipmentAdapter));
        if (callbackLifetime.IsActive) return EquipmentNativeContractPolicy.ToRecords(metricCapabilities, AdapterVersion);
        if (!string.Equals(Application.version ?? string.Empty, SupportedGameVersion, StringComparison.Ordinal))
        {
            SetDisabled($"Installed Duckov version '{Application.version}' does not match verified equipment contract version '{SupportedGameVersion}'.");
            return EquipmentNativeContractPolicy.ToRecords(metricCapabilities, AdapterVersion);
        }

        try
        {
            var slotChanged = callbackLifetime.Guard<CharacterMainControl, Slot>((_, _) => ObserveNow());
            var holdChanged = callbackLifetime.Guard<CharacterMainControl, DuckovItemAgent>((_, _) => ObserveNow());
            callbackLifetime.Activate(new[]
            {
                new SubscriptionBinding(
                    () => CharacterMainControl.OnMainCharacterSlotContentChangedEvent += slotChanged,
                    () => CharacterMainControl.OnMainCharacterSlotContentChangedEvent -= slotChanged),
                new SubscriptionBinding(
                    () => CharacterMainControl.OnMainCharacterChangeHoldItemAgentEvent += holdChanged,
                    () => CharacterMainControl.OnMainCharacterChangeHoldItemAgentEvent -= holdChanged)
            });
            metricCapabilities = EquipmentNativeContractPolicy.CreateSupportedCapabilities();
            capabilityHandler(EquipmentNativeContractPolicy.ToRecords(metricCapabilities, AdapterVersion));
            SynchronizeMain();
            ObserveNow();
            diagnosticHandler("Native equipment hooks subscribed; tote activation remains deliberately unavailable.");
        }
        catch (Exception exception)
        {
            TryCleanup();
            SetDisabled($"Equipment hook activation failed: {exception.GetType().Name}: {exception.Message}");
        }
        return EquipmentNativeContractPolicy.ToRecords(metricCapabilities, AdapterVersion);
    }

    public void Tick()
    {
        var now = clock.Elapsed.TotalSeconds;
        if (!callbackLifetime.CanHandleCallbacks || !cadence.IsDue(now)) return;
        cadence.MarkCompleted(now);
        SynchronizeMain();
        ObserveNow();
    }

    public EquipmentEventAssociation CaptureAssociation()
    {
        if (!callbackLifetime.CanHandleCallbacks || !runActiveProvider()) return new EquipmentEventAssociation();
        ObserveNow();
        var snapshot = latestSnapshot;
        return snapshot == null ? new EquipmentEventAssociation() : new EquipmentEventAssociation
        {
            LoadoutId = snapshot.LoadoutId,
            SelectedWeaponId = snapshot.SelectedWeaponId,
            SelectedWeaponSlotId = snapshot.SelectedWeaponSlotId,
            TotemSetId = snapshot.TotemSetId
        };
    }

    public bool TryCleanup()
    {
        UnsubscribeCharacterTree();
        var cleaned = callbackLifetime.TryCleanup(() => true, out var failure);
        if (failure != null) diagnosticHandler($"Equipment cleanup remains retryable: {failure.GetType().Name}: {failure.Message}");
        latestSnapshot = null;
        observedMain = null;
        return cleaned;
    }

    public void Dispose() => TryCleanup();

    private void SynchronizeMain()
    {
        var current = CharacterMainControl.Main;
        if (current != null && !current.IsMainCharacter) current = null;
        var characterItem = current?.CharacterItem;
        if (ReferenceEquals(current, observedMain) && ReferenceEquals(characterItem, observedCharacterItem)) return;
        UnsubscribeCharacterTree();
        observedMain = current;
        observedCharacterItem = characterItem;
        if (observedCharacterItem != null) observedCharacterItem.onItemTreeChanged += OnItemTreeChanged;
        latestSnapshot = null;
    }

    private void UnsubscribeCharacterTree()
    {
        if (observedCharacterItem != null) observedCharacterItem.onItemTreeChanged -= OnItemTreeChanged;
        observedCharacterItem = null;
    }

    private void OnItemTreeChanged(Item _) => ObserveNow();

    private void ObserveNow()
    {
        if (!callbackLifetime.CanHandleCallbacks) return;
        SynchronizeMain();
        if (!runActiveProvider())
        { latestSnapshot = null; return; }
        if (observedMain == null || observedCharacterItem == null || !observedMain.IsMainCharacter)
        { InvalidateObservation(); return; }
        try
        {
            var snapshot = BuildSnapshot(observedMain, observedCharacterItem);
            latestSnapshot = snapshot;
            snapshotHandler(snapshot);
        }
        catch (Exception exception)
        {
            InvalidateObservation();
            diagnosticHandler($"Equipment observation failed safely: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static EquipmentSnapshot BuildSnapshot(CharacterMainControl main, Item characterItem)
    {
        var equipped = new List<EquippedItemSnapshot>();
        var totems = new List<TotemSnapshot>();
        foreach (var slot in characterItem.Slots.OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            var item = slot.Content;
            if (item == null) continue;
            var kind = Classify(slot.Key, item);
            var itemId = ItemId(item, kind switch
            {
                EquipmentItemKind.Weapon => "weapon",
                EquipmentItemKind.Totem => "totem",
                _ => "item"
            });
            equipped.Add(new EquippedItemSnapshot
            {
                SlotId = "duckov:slot:" + (slot.Key ?? string.Empty),
                SlotDisplayName = string.IsNullOrWhiteSpace(slot.DisplayName) ? slot.Key ?? string.Empty : slot.DisplayName,
                ItemId = itemId,
                ItemDisplayName = DisplayName(item),
                Kind = kind,
                AttachmentSignature = AttachmentSignature(item)
            });
            if (item.Tags != null && item.Tags.Contains("Totem"))
                totems.Add(new TotemSnapshot
                {
                    ItemId = ItemId(item, "totem"),
                    DisplayName = DisplayName(item),
                    CarryKind = TotemCarryKind.DirectSlot,
                    ContainerId = "duckov:character",
                    ActivationState = item.UseDurability && item.Durability <= 0
                        ? TotemActivationState.ProvenInactive : TotemActivationState.ProvenActive
                });
            if (string.Equals(item.DisplayNameRaw, "Item_ToteBag", StringComparison.Ordinal) && item.Inventory != null)
            {
                foreach (var toteItem in item.Inventory.Content.Where(value => value != null && value.Tags != null && value.Tags.Contains("Totem")))
                    totems.Add(new TotemSnapshot
                    {
                        ItemId = ItemId(toteItem, "totem"),
                        DisplayName = DisplayName(toteItem),
                        CarryKind = TotemCarryKind.ToteInventory,
                        ContainerId = ItemId(item, "tote"),
                        ActivationState = TotemActivationState.Unknown
                    });
            }
        }

        equipped = equipped.OrderBy(value => value.SlotId, StringComparer.Ordinal).ToList();
        totems = totems.OrderBy(value => value.CarryKind).ThenBy(value => value.ContainerId, StringComparer.Ordinal).ThenBy(value => value.ItemId, StringComparer.Ordinal).ToList();
        var selected = main.CurrentHoldItemAgent?.Item;
        var selectedSlotId = selected == null ? string.Empty : characterItem.Slots
            .Where(value => ReferenceEquals(value.Content, selected))
            .Select(value => "duckov:slot:" + (value.Key ?? string.Empty))
            .FirstOrDefault() ?? string.Empty;
        var selectedEntry = equipped.FirstOrDefault(value =>
            string.Equals(value.SlotId, selectedSlotId, StringComparison.Ordinal)
            && value.Kind == EquipmentItemKind.Weapon);
        var selectedId = selectedEntry?.ItemId ?? string.Empty;
        if (selectedEntry == null) selectedSlotId = string.Empty;
        var loadoutId = EquipmentIdentity.LoadoutId(equipped);
        var totemSetId = EquipmentIdentity.ActiveTotemSetId(totems);
        return new EquipmentSnapshot
        {
            Items = equipped,
            Totems = totems,
            SelectedWeaponId = selectedId,
            SelectedWeaponSlotId = selectedSlotId,
            LoadoutId = loadoutId,
            TotemSetId = totemSetId,
            SnapshotId = EquipmentIdentity.SnapshotId(
                loadoutId,
                selectedSlotId,
                selectedId,
                totemSetId,
                EquipmentIdentity.TotemPresenceSignature(totems))
        };
    }

    private void InvalidateObservation()
    {
        latestSnapshot = null;
        invalidationHandler();
    }

    private static string AttachmentSignature(Item item)
    {
        var parts = new List<string>();
        AddAttachments(item, parts, 0);
        return EquipmentIdentity.StableHash(string.Join(";", parts.OrderBy(value => value, StringComparer.Ordinal)));
    }

    private static void AddAttachments(Item parent, List<string> parts, int depth)
    {
        if (depth >= 8 || parts.Count >= 64 || parent.Slots == null) return;
        foreach (var slot in parent.Slots.OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            if (slot.Content == null) continue;
            parts.Add(depth.ToString(CultureInfo.InvariantCulture) + ":" + (slot.Key ?? string.Empty) + "=" + slot.Content.TypeID.ToString(CultureInfo.InvariantCulture));
            AddAttachments(slot.Content, parts, depth + 1);
        }
    }

    private static EquipmentItemKind Classify(string? slotKey, Item item)
    {
        if (item.Tags != null && item.Tags.Contains("Totem")) return EquipmentItemKind.Totem;
        return slotKey switch
        {
            "PrimaryWeapon" or "SecondaryWeapon" or "MeleeWeapon" => EquipmentItemKind.Weapon,
            "Armor" => EquipmentItemKind.Armor,
            "Helmat" => EquipmentItemKind.Helmet,
            "Backpack" => EquipmentItemKind.Backpack,
            "FaceMask" => EquipmentItemKind.Face,
            "Headset" => EquipmentItemKind.Headset,
            _ => EquipmentItemKind.Other
        };
    }

    private static string ItemId(Item item, string kind) => "duckov:" + kind + ":" + item.TypeID.ToString(CultureInfo.InvariantCulture);
    private static string DisplayName(Item item) => string.IsNullOrWhiteSpace(item.DisplayName) ? "Unknown item " + item.TypeID.ToString(CultureInfo.InvariantCulture) : item.DisplayName;
    private void SetDisabled(string detail)
    {
        metricCapabilities = EquipmentNativeContractPolicy.CreateUnavailableCapabilities(detail);
        capabilityHandler(EquipmentNativeContractPolicy.ToRecords(metricCapabilities, AdapterVersion));
        diagnosticHandler(detail);
    }
}
