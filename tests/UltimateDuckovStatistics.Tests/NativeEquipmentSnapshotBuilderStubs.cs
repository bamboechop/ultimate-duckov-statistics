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
    public DuckovItemAgent? CurrentHoldItemAgent { get; set; }
}
#pragma warning restore CA1050
