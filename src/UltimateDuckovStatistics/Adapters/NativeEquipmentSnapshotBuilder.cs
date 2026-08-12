using System.Globalization;
using ItemStatsSystem;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Adapters;

internal static class NativeEquipmentSnapshotBuilder
{
    private const int MaxOrdinaryInventoryItems = 256;
    private const int MaxToteSlots = 8;
    private const int NativeToteBagTypeId = 1255;
    private const string NativeToteSlotKey = "AnyThing";

    public static EquipmentSnapshot Build(CharacterMainControl main, Item characterItem)
    {
        if (main == null) throw new ArgumentNullException(nameof(main));
        if (characterItem == null) throw new ArgumentNullException(nameof(characterItem));

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

    private static void AddAttachments(Item parent, List<string> parts, int depth, string ancestorPath)
    {
        if (depth >= 8 || parts.Count >= 64 || parent.Slots == null) return;
        foreach (var slot in parent.Slots.OrderBy(value => value.Key, StringComparer.Ordinal))
        {
            if (slot.Content == null) continue;
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
