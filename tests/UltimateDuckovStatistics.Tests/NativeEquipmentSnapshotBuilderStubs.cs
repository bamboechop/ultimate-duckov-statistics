namespace ItemStatsSystem
{
    public sealed class Inventory
    {
        public List<Item> Content { get; } = new();
        public event Action<Inventory, int>? onContentChanged;

        public void RaiseContentChanged(int index = 0) => onContentChanged?.Invoke(this, index);
    }

    public sealed class Item
    {
        private static int nextInstanceId;
        private readonly int instanceId = Interlocked.Increment(ref nextInstanceId);
        public int TypeID { get; set; }
        public int StackCount { get; set; } = 1;
        public string DisplayName { get; set; } = string.Empty;
        public string DisplayNameRaw { get; set; } = string.Empty;
        public List<Items.Slot> Slots { get; } = new();
        public Inventory? Inventory { get; set; }
        public HashSet<string> Tags { get; } = new(StringComparer.Ordinal);
        public bool UseDurability { get; set; }
        public float Durability { get; set; }
        public event Action<Item>? onItemTreeChanged;

        public int GetInstanceID() => instanceId;
        public void RaiseItemTreeChanged() => onItemTreeChanged?.Invoke(this);
    }

    public sealed class ItemAgent
    {
        public Item? Item { get; set; }
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

namespace Duckov.Economy
{
#pragma warning disable CA1051 // Stub mirrors the installed public-field native contract.
    public sealed class Cost
    {
        public long money;
        public ItemEntry[] items = Array.Empty<ItemEntry>();

        public sealed class ItemEntry
        {
            public int id;
            public long amount;
        }
    }
#pragma warning restore CA1051

    public static class EconomyManager
    {
        public const int CashItemID = 451;
        public static event Action<long, long>? OnMoneyChanged;
        public static event Action<long>? OnMoneyPaid;
        public static event Action? OnEconomyManagerLoaded;
        public static event Action<Cost>? OnCostPaid;

        public static void RaiseMoneyChanged(long oldValue, long newValue) => OnMoneyChanged?.Invoke(oldValue, newValue);
        public static void RaiseMoneyPaid(long amount) => OnMoneyPaid?.Invoke(amount);
        public static void RaiseLoaded() => OnEconomyManagerLoaded?.Invoke();
        public static void RaiseCostPaid(long cashAmount = 0, long moneyAmount = 0) => OnCostPaid?.Invoke(new Cost
        {
            money = moneyAmount,
            items = cashAmount <= 0
                ? Array.Empty<Cost.ItemEntry>()
                : [new Cost.ItemEntry { id = CashItemID, amount = cashAmount }]
        });
        public static Action<long>? CaptureMoneyPaidSubscribers() => OnMoneyPaid;
        public static Action<Cost>? CaptureCostPaidSubscribers() => OnCostPaid;
        public static void ResetNativeState()
        {
            OnMoneyChanged = null;
            OnMoneyPaid = null;
            OnEconomyManagerLoaded = null;
            OnCostPaid = null;
        }
    }

    public sealed class StockShop
    {
        public static event Action<StockShop, ItemStatsSystem.Item, int>? OnItemSoldByPlayer;
        public string MerchantID { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;

        public static void RaiseSold(StockShop shop, ItemStatsSystem.Item item, int amount) =>
            OnItemSoldByPlayer?.Invoke(shop, item, amount);
        public static Action<StockShop, ItemStatsSystem.Item, int>? CaptureSoldSubscribers() => OnItemSoldByPlayer;
        public static void ResetNativeState() => OnItemSoldByPlayer = null;
    }
}

namespace Duckov.Quests
{
    public class Reward
    {
        public static event Action<Reward>? OnRewardClaimed;
        public string ID { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        public static void RaiseClaimed(Reward reward) => OnRewardClaimed?.Invoke(reward);
        public static void ResetNativeState() => OnRewardClaimed = null;
    }
}

namespace Duckov.Quests.Rewards
{
#pragma warning disable CA1707 // Match the installed Duckov native type exactly.
    public sealed class QuestReward_Money : Duckov.Quests.Reward
    {
        public long Amount { get; set; }
    }
#pragma warning restore CA1707
}

#pragma warning disable CA1050 // Duckov exposes these types in the global namespace.
public sealed class InteractablePickup
{
    public static event Action<InteractablePickup, CharacterMainControl>? OnPickupSuccess;
    public ItemStatsSystem.ItemAgent? ItemAgent { get; set; }

    public static void RaisePickup(InteractablePickup pickup, CharacterMainControl character) =>
        OnPickupSuccess?.Invoke(pickup, character);
    public static void ResetNativeState() => OnPickupSuccess = null;
}

public static class ItemUtilities
{
    public static event Action? OnPlayerItemOperation;
    public static List<ItemStatsSystem.Item> OwnedItems { get; } = new();
    public static int ScanCount { get; private set; }
    public static Exception? ScanException { get; set; }

    public static IEnumerable<ItemStatsSystem.Item> FindAllBelongsToPlayer(Func<ItemStatsSystem.Item, bool> predicate)
    {
        ScanCount++;
        if (ScanException != null) throw ScanException;
        return OwnedItems.Where(predicate).ToArray();
    }
    public static void RaisePlayerItemOperation() => OnPlayerItemOperation?.Invoke();
    public static void ResetNativeState()
    {
        OnPlayerItemOperation = null;
        OwnedItems.Clear();
        ScanCount = 0;
        ScanException = null;
    }
}

public sealed class PlayerStorage
{
    public static event Action<PlayerStorage, ItemStatsSystem.Inventory, int>? OnPlayerStorageChange;
    public static void RaiseChanged(ItemStatsSystem.Inventory inventory, int index = 0) =>
        OnPlayerStorageChange?.Invoke(new PlayerStorage(), inventory, index);
    public static void ResetNativeState() => OnPlayerStorageChange = null;
}

public static class LevelManager
{
    public static event Action? OnLevelBeginInitializing;
    public static event Action? OnAfterLevelInitialized;
    public static event Action<CharacterMainControl>? OnControllingCharacterChanged;
    public static void RaiseLevelBeginInitializing() => OnLevelBeginInitializing?.Invoke();
    public static void RaiseAfterLevelInitialized() => OnAfterLevelInitialized?.Invoke();
    public static void RaiseControllingCharacterChanged(CharacterMainControl value) => OnControllingCharacterChanged?.Invoke(value);
    public static void ResetNativeState()
    {
        OnLevelBeginInitializing = null;
        OnAfterLevelInitialized = null;
        OnControllingCharacterChanged = null;
    }
}

public static class PetProxy
{
    public static ItemStatsSystem.Inventory? PetInventory { get; set; }
}
#pragma warning restore CA1050

namespace UltimateDuckovStatistics.Adapters
{
    internal static class NativeIntegrityProbe
    {
        public static UltimateDuckovStatistics.Core.Domain.IntegrityTags Read() =>
            UltimateDuckovStatistics.Core.Domain.IntegrityTags.Normal;
    }
}
