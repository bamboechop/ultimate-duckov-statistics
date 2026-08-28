using System.Globalization;
using ItemStatsSystem;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Adapters;

internal static class NativeEquipmentSnapshotBuilder
{
    private const int MaxOrdinaryInventoryItems = 256;
    private const int MaxToteSlots = 8;
    private const int MaxNestedSlotsPerRoot = 256;
    private const int MaxNestedDepth = 8;
    private const int NativeToteBagTypeId = 1255;
    private const string NativeToteSlotKey = "AnyThing";

    public static EquipmentSnapshot Build(CharacterMainControl main, Item characterItem)
    {
        if (main == null) throw new ArgumentNullException(nameof(main));
        if (characterItem == null) throw new ArgumentNullException(nameof(characterItem));

        var equipped = new List<EquippedItemSnapshot>();
        var characterSlots = new List<CharacterEquipmentSlotSnapshot>();
        var totems = new List<TotemSnapshot>();
        var characterSlotStateComplete = true;
        var nestedSlotStateComplete = true;
        var selected = main.CurrentHoldItemAgent?.Item;
        var selectedSlotId = string.Empty;
        var orderedCharacterSlots = characterItem.Slots
            .OrderBy(value => value?.Key, StringComparer.Ordinal)
            .ToList();
        var duplicateCharacterSlotKeys = FindDuplicateSlotKeys(orderedCharacterSlots);
        if (duplicateCharacterSlotKeys.Count > 0) characterSlotStateComplete = false;
        foreach (var slot in orderedCharacterSlots)
        {
            if (slot == null || string.IsNullOrWhiteSpace(slot.Key))
            {
                characterSlotStateComplete = false;
                continue;
            }
            if (duplicateCharacterSlotKeys.Contains(slot.Key)) continue;
            var slotId = "duckov:slot:" + slot.Key;
            var slotDisplayName = string.IsNullOrWhiteSpace(slot.DisplayName) ? slot.Key : slot.DisplayName;
            var item = slot.Content;
            if (item == null)
            {
                characterSlots.Add(new CharacterEquipmentSlotSnapshot
                {
                    SlotId = slotId,
                    SlotDisplayName = slotDisplayName,
                    State = EquipmentSlotState.Empty
                });
                continue;
            }
            var kind = Classify(slot.Key, item);
            var itemId = ItemId(item, kind switch
            {
                EquipmentItemKind.Weapon => "weapon",
                EquipmentItemKind.Totem => "totem",
                _ => "item"
            });
            var nestedSlots = BuildNestedSlots(item, out var nestedComplete);
            nestedSlotStateComplete &= nestedComplete;
            if (selectedSlotId.Length == 0
                && kind == EquipmentItemKind.Weapon
                && ReferenceEquals(item, selected))
            {
                selectedSlotId = slotId;
            }
            equipped.Add(new EquippedItemSnapshot
            {
                SlotId = slotId,
                SlotDisplayName = slotDisplayName,
                ItemId = itemId,
                ItemDisplayName = DisplayName(item),
                Kind = kind,
                AttachmentSignature = AttachmentSignature(item),
                NestedSlots = nestedSlots,
                NestedSlotStateComplete = nestedComplete
            });
            characterSlots.Add(new CharacterEquipmentSlotSnapshot
            {
                SlotId = slotId,
                SlotDisplayName = slotDisplayName,
                State = EquipmentSlotState.Occupied,
                ItemId = itemId,
                ItemDisplayName = DisplayName(item),
                ItemKind = kind
            });
            if (IsTotem(item))
            {
                totems.Add(new TotemSnapshot
                {
                    ItemId = ItemId(item, "totem"),
                    DisplayName = DisplayName(item),
                    CarryKind = TotemCarryKind.DirectSlot,
                    ContainerId = "duckov:character",
                    ActivationState = item.UseDurability && item.Durability <= 0
                        ? TotemActivationState.ProvenInactive : TotemActivationState.ProvenActive
                });
            }
        }

        AddOrdinaryInventoryToteContents(characterItem.Inventory, totems);

        equipped = equipped.OrderBy(value => value.SlotId, StringComparer.Ordinal).ToList();
        characterSlots = characterSlots.OrderBy(value => value.SlotId, StringComparer.Ordinal).ToList();
        totems = totems.OrderBy(value => value.CarryKind).ThenBy(value => value.ContainerId, StringComparer.Ordinal).ThenBy(value => value.ItemId, StringComparer.Ordinal).ToList();
        var selectedEntry = equipped.FirstOrDefault(value =>
            string.Equals(value.SlotId, selectedSlotId, StringComparison.Ordinal)
            && value.Kind == EquipmentItemKind.Weapon);
        var selectedId = selectedEntry?.ItemId ?? string.Empty;
        if (selectedEntry == null) selectedSlotId = string.Empty;
        // Whole-set M6 identities are exact only when every character root was
        // readable and uniquely keyed. Retained siblings remain valid M14
        // evidence, but they must not be hashed into a plausible subset identity.
        var loadoutId = characterSlotStateComplete
            ? EquipmentIdentity.LoadoutId(equipped)
            : EquipmentEventAssociation.UnavailableId;
        var totemSetId = characterSlotStateComplete
            ? EquipmentIdentity.ActiveTotemSetId(totems)
            : EquipmentEventAssociation.UnavailableId;
        return new EquipmentSnapshot
        {
            Items = equipped,
            CharacterSlots = characterSlots,
            CharacterSlotStateComplete = characterSlotStateComplete,
            NestedSlotStateComplete = nestedSlotStateComplete,
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
                EquipmentIdentity.TotemPresenceSignature(totems),
                SlotStateSignature(
                    characterSlots,
                    equipped,
                    characterSlotStateComplete,
                    nestedSlotStateComplete))
        };
    }

    private static void AddOrdinaryInventoryToteContents(Inventory? ordinaryInventory, List<TotemSnapshot> totems)
    {
        if (ordinaryInventory?.Content == null) return;
        foreach (var tote in ordinaryInventory.Content.Take(MaxOrdinaryInventoryItems))
        {
            if (tote == null
                || tote.TypeID != NativeToteBagTypeId
                || tote.Slots == null)
            {
                continue;
            }

            foreach (var toteSlot in tote.Slots.Take(MaxToteSlots).Where(value =>
                         string.Equals(value.Key, NativeToteSlotKey, StringComparison.Ordinal)))
            {
                var toteItem = toteSlot.Content;
                if (toteItem == null || !IsTotem(toteItem)) continue;
                totems.Add(new TotemSnapshot
                {
                    ItemId = ItemId(toteItem, "totem"),
                    DisplayName = DisplayName(toteItem),
                    CarryKind = TotemCarryKind.ToteInventory,
                    ContainerId = ItemId(tote, "tote"),
                    ActivationState = TotemActivationState.Unknown
                });
            }
        }
    }

    private static string AttachmentSignature(Item item)
    {
        var parts = new List<string>();
        AddAttachments(item, parts, 0, string.Empty);
        return EquipmentIdentity.StableHash(string.Join(";", parts.OrderBy(value => value, StringComparer.Ordinal)));
    }

    private static List<NestedEquipmentSlotSnapshot> BuildNestedSlots(Item root, out bool complete)
    {
        var result = new List<NestedEquipmentSlotSnapshot>();
        complete = true;
        AddNestedSlots(root, result, 0, string.Empty, ref complete);
        return result.OrderBy(value => value.Path, StringComparer.Ordinal).ToList();
    }

    private static void AddNestedSlots(
        Item parent,
        List<NestedEquipmentSlotSnapshot> result,
        int depth,
        string ancestorPath,
        ref bool complete)
    {
        if (parent.Slots == null) return;
        if (depth >= MaxNestedDepth)
        {
            if (parent.Slots.Count > 0) complete = false;
            return;
        }
        var orderedSlots = parent.Slots
            .OrderBy(value => value?.Key, StringComparer.Ordinal)
            .ToList();
        var duplicateSlotKeys = FindDuplicateSlotKeys(orderedSlots);
        if (duplicateSlotKeys.Count > 0) complete = false;
        foreach (var slot in orderedSlots)
        {
            if (result.Count >= MaxNestedSlotsPerRoot)
            {
                complete = false;
                return;
            }
            if (slot == null || string.IsNullOrWhiteSpace(slot.Key))
            {
                complete = false;
                continue;
            }
            if (duplicateSlotKeys.Contains(slot.Key)) continue;
            var path = ancestorPath
                + slot.Key.Length.ToString(CultureInfo.InvariantCulture) + ":" + slot.Key + "/";
            var child = slot.Content;
            result.Add(new NestedEquipmentSlotSnapshot
            {
                Path = path,
                SlotKey = slot.Key,
                SlotDisplayName = string.IsNullOrWhiteSpace(slot.DisplayName) ? slot.Key : slot.DisplayName,
                State = child == null ? EquipmentSlotState.Empty : EquipmentSlotState.Occupied,
                ItemId = child == null ? string.Empty : ItemId(child, "item"),
                ItemDisplayName = child == null ? string.Empty : DisplayName(child)
            });
            if (child != null) AddNestedSlots(child, result, depth + 1, path, ref complete);
        }
    }

    private static HashSet<string> FindDuplicateSlotKeys(IEnumerable<ItemStatsSystem.Items.Slot> slots)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = new HashSet<string>(StringComparer.Ordinal);
        foreach (var slot in slots)
        {
            if (slot == null || string.IsNullOrWhiteSpace(slot.Key)) continue;
            if (!seen.Add(slot.Key)) duplicates.Add(slot.Key);
        }
        return duplicates;
    }

    private static string SlotStateSignature(
        IEnumerable<CharacterEquipmentSlotSnapshot> characterSlots,
        IEnumerable<EquippedItemSnapshot> equipped,
        bool characterSlotStateComplete,
        bool nestedSlotStateComplete)
    {
        var roots = characterSlots.Select(value =>
            Component("root") + Component(value.SlotId)
            + Component(((int)value.State).ToString(CultureInfo.InvariantCulture))
            + Component(value.ItemId));
        var nested = equipped.SelectMany(item => item.NestedSlots.Select(slot =>
            Component("nested") + Component(item.SlotId) + Component(item.ItemId) + Component(slot.Path)
            + Component(((int)slot.State).ToString(CultureInfo.InvariantCulture)) + Component(slot.ItemId)));
        var completeness = new[]
            {
                Component("character-complete") + Component(characterSlotStateComplete ? "1" : "0"),
                Component("nested-complete") + Component(nestedSlotStateComplete ? "1" : "0")
            }
            .Concat(equipped.Select(item =>
                Component("nested-parent-complete") + Component(item.SlotId) + Component(item.ItemId)
                + Component(item.NestedSlotStateComplete ? "1" : "0")));
        return EquipmentIdentity.StableHash(string.Concat(
            roots.Concat(nested).Concat(completeness).OrderBy(value => value, StringComparer.Ordinal)));
    }

    internal static string DisplayMetadataSignature(EquipmentSnapshot snapshot)
    {
        if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
        var roots = snapshot.CharacterSlots.Select(value =>
            Component("root-display") + Component(value.SlotId) + Component(value.SlotDisplayName)
            + Component(value.ItemId) + Component(value.ItemDisplayName));
        var equipped = snapshot.Items.Select(value =>
            Component("item-display") + Component(value.SlotId) + Component(value.SlotDisplayName)
            + Component(value.ItemId) + Component(value.ItemDisplayName));
        var nested = snapshot.Items.SelectMany(parent => parent.NestedSlots.Select(value =>
            Component("nested-display") + Component(parent.SlotId) + Component(parent.ItemId)
            + Component(value.Path) + Component(value.SlotDisplayName) + Component(value.ItemId)
            + Component(value.ItemDisplayName)));
        var totems = snapshot.Totems.Select(value =>
            Component("totem-display") + Component(((int)value.CarryKind).ToString(CultureInfo.InvariantCulture))
            + Component(value.ContainerId) + Component(value.ItemId) + Component(value.DisplayName));
        return EquipmentIdentity.StableHash(string.Concat(
            roots.Concat(equipped).Concat(nested).Concat(totems).OrderBy(value => value, StringComparer.Ordinal)));
    }

    private static string Component(string? value)
    {
        value ??= string.Empty;
        return value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
    }

    private static void AddAttachments(Item parent, List<string> parts, int depth, string ancestorPath)
    {
        if (depth >= 8 || parts.Count >= 64 || parent.Slots == null) return;
        foreach (var slot in parent.Slots.OrderBy(value => value?.Key, StringComparer.Ordinal))
        {
            if (slot == null || slot.Content == null) continue;
            var slotKey = slot.Key ?? string.Empty;
            var path = ancestorPath
                + slotKey.Length.ToString(CultureInfo.InvariantCulture) + ":" + slotKey + "/";
            parts.Add(path + "=" + slot.Content.TypeID.ToString(CultureInfo.InvariantCulture));
            AddAttachments(slot.Content, parts, depth + 1, path);
        }
    }

    private static EquipmentItemKind Classify(string? slotKey, Item item)
    {
        if (IsTotem(item)) return EquipmentItemKind.Totem;
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

    private static bool IsTotem(Item item) => item.Tags != null && item.Tags.Contains("Totem");
    private static string ItemId(Item item, string kind) => "duckov:" + kind + ":" + item.TypeID.ToString(CultureInfo.InvariantCulture);
    private static string DisplayName(Item item) => string.IsNullOrWhiteSpace(item.DisplayName) ? "Unknown item " + item.TypeID.ToString(CultureInfo.InvariantCulture) : item.DisplayName;
}
