// Installed signatures used by source-linked container/loot observers. Tests
// invoke callbacks explicitly; these objects do not simulate the Unity loop.
#pragma warning disable CA1050, CA1051, CA1707, CA1720, CA1822, CS0067
using Cysharp.Threading.Tasks;
using ItemStatsSystem;
using UnityEngine;

public sealed partial class CharacterMainControl
{
    private void OnDead(DamageInfo damageInfo) { }
}

public class InteractableBase : UnityEngine.Object
{
    private CharacterMainControl? interactCharacter;
    internal void SetInteractingCharacter(CharacterMainControl actor) => interactCharacter = actor;
}

public sealed class InteractableLootbox : InteractableBase
{
    private Inventory? inventoryReference;
    public static event Action<InteractableLootbox>? OnStartLoot;
    public static event Action<InteractableLootbox>? OnStopLoot;
    public int Key { get; set; } = 1;
    private int GetKey() => Key;
    internal void SetInventory(Inventory inventory) => inventoryReference = inventory;
    internal void StartLoot() => OnStartLoot?.Invoke(this);
    internal void StopLoot() => OnStopLoot?.Invoke(this);
    public static InteractableLootbox CreateFromItem(Item item, Vector3 position, Quaternion rotation,
        bool moveToMainScene, InteractableLootbox? prefab, bool useTombPrefab) => new() { inventoryReference = item.Inventory };
}

namespace Duckov.Utilities
{
    public static partial class GameplayDataSettings
    {
        public static LootPrefabSettings Prefabs { get; } = new();
    }
    public sealed class LootPrefabSettings
    {
        public InteractableLootbox LootBoxPrefab_Tomb { get; } = new();
    }
}

namespace Duckov.UI
{
    public sealed class LootView : UnityEngine.Object
    {
        private InteractableLootbox? targetLootBox;
        public bool open { get; set; }
        public bool isActiveAndEnabled { get; set; } = true;
        internal void SetTarget(InteractableLootbox box) { targetLootBox = box; open = true; }
        private void OnOpen() { }
        private void OnClose() { }
        private void OnDisable() { }
    }
}

namespace ItemStatsSystem
{
    public sealed partial class Inventory : UnityEngine.Object
    {
        public Item? AttachedToItem { get; set; }
        public int Capacity => Content.Count;
        public Item? GetItemAt(int index) => index >= 0 && index < Content.Count ? Content[index] : null;
        public int GetIndex(Item item) => Content.IndexOf(item);
        public bool AddAt(Item item, int atPosition)
        {
            item.Detach();
            Content.Insert(atPosition, item); item.InInventory = this;
            RaiseContentChanged(atPosition);
            return true;
        }
    }

    public sealed partial class Item : UnityEngine.Object
    {
        public Inventory? InInventory { get; set; }
        public Items.Slot? PluggedIntoSlot { get; set; }
        public Item? ParentItem => InInventory?.AttachedToItem;
        public UnityEngine.Object? ParentObject => InInventory;
        public bool Inspected { get; set; } = true;
        public bool NeedInspection => !Inspected;
        public void Detach()
        {
            var old = InInventory;
            var index = old?.GetIndex(this) ?? -1;
            old?.Content.Remove(this); InInventory = null;
            old?.RaiseContentChanged(index);
        }
        public void Combine(Item incomingItem) { }
        public UniTask<Item> Split(int count) => UniTask<Item>.FromResult(this);
    }

    public static class ItemExtensions
    {
        public static DuckovItemAgent Drop(Item item, Vector3 position, bool createRigidbody, Vector3 velocity, float pickupDelay)
            => new() { Item = item };
    }
}

namespace ItemStatsSystem.Items
{
    public sealed partial class Slot
    {
        public Item? Master { get; set; }
        public bool Plug(Item otherItem, out Item? unpluggedItem)
        { unpluggedItem = Content; Content = otherItem; return true; }
    }
}
