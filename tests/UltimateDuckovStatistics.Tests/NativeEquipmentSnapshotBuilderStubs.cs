namespace ItemStatsSystem
{
    public sealed class Inventory
    {
        public List<Item> Content { get; } = new();
    }

    public sealed class Item
    {
        public int TypeID { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string DisplayNameRaw { get; set; } = string.Empty;
        public List<Items.Slot> Slots { get; } = new();
        public Inventory? Inventory { get; set; }
        public HashSet<string> Tags { get; } = new(StringComparer.Ordinal);
        public bool UseDurability { get; set; }
        public float Durability { get; set; }
        public event Action<Item>? onItemTreeChanged;

        public void RaiseItemTreeChanged() => onItemTreeChanged?.Invoke(this);
    }
}

namespace ItemStatsSystem.Items
{
    public sealed class Slot
    {
        public string Key { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public ItemStatsSystem.Item? Content { get; set; }
    }
}

#pragma warning disable CA1050 // Duckov exposes both types in the global namespace.
public sealed class DuckovItemAgent
{
    public ItemStatsSystem.Item? Item { get; set; }
}

public sealed class CharacterMainControl
{
    public static CharacterMainControl? Main { get; set; }
    public static event Action<CharacterMainControl, ItemStatsSystem.Items.Slot>? OnMainCharacterSlotContentChangedEvent;
    public static event Action<CharacterMainControl, DuckovItemAgent>? OnMainCharacterChangeHoldItemAgentEvent;
    public static event Action<CharacterMainControl, ItemStatsSystem.Inventory, int>? OnMainCharacterInventoryChangedEvent;

    public bool IsMainCharacter { get; set; }
    public ItemStatsSystem.Item? CharacterItem { get; set; }
    public DuckovItemAgent? CurrentHoldItemAgent { get; set; }

    public static void RaiseSlotChanged(CharacterMainControl main, ItemStatsSystem.Items.Slot slot) =>
        OnMainCharacterSlotContentChangedEvent?.Invoke(main, slot);

    public static void RaiseHoldChanged(CharacterMainControl main, DuckovItemAgent itemAgent) =>
        OnMainCharacterChangeHoldItemAgentEvent?.Invoke(main, itemAgent);

    public static void RaiseInventoryChanged(CharacterMainControl main, ItemStatsSystem.Inventory inventory, int index) =>
        OnMainCharacterInventoryChangedEvent?.Invoke(main, inventory, index);

    public static void ResetNativeState()
    {
        Main = null;
        OnMainCharacterSlotContentChangedEvent = null;
        OnMainCharacterChangeHoldItemAgentEvent = null;
        OnMainCharacterInventoryChangedEvent = null;
    }
}
#pragma warning restore CA1050

namespace UnityEngine
{
    public static class Application
    {
        public static string version { get; set; } = "2.3.30";
    }
}
