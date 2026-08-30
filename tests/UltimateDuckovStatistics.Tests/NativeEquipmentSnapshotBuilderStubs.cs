#pragma warning disable CS0067, CS0414, CA1051, CA1711 // Stubs mirror installed native names, public fields, and callback surfaces.

namespace ItemStatsSystem
{
    public sealed class Inventory
    {
        private static int nextInstanceId;
        private readonly int instanceId = Interlocked.Increment(ref nextInstanceId);
        public List<Item> Content { get; } = new();
        public bool Loading { get; set; }
        public event Action<Inventory, int>? onContentChanged;

        public int GetInstanceID() => instanceId;
        public void RaiseContentChanged(int index = 0) => onContentChanged?.Invoke(this, index);
    }

    public sealed class Item
    {
        private static int nextInstanceId;
        private readonly int instanceId = Interlocked.Increment(ref nextInstanceId);
        public int TypeID { get; set; }
        public bool Stackable { get; set; } = true;
        public int StackCount { get; set; } = 1;
        public string DisplayName { get; set; } = string.Empty;
        public string DisplayNameRaw { get; set; } = string.Empty;
        public List<Items.Slot> Slots { get; } = new();
        public Inventory? Inventory { get; set; }
        public HashSet<string> Tags { get; } = new(StringComparer.Ordinal);
        public bool UseDurability { get; set; }
        public float Durability { get; set; }
        public bool IsBeingDestroyed { get; private set; }
        public event Action<Item>? onItemTreeChanged;

        public int GetInstanceID() => instanceId;
        public void MarkDestroyed() => IsBeingDestroyed = true;
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
    public Health Health { get; set; } = new();
    public float CharacterWalkSpeed { get; set; } = 4;
    public float CharacterRunSpeed { get; set; } = 8;
    public float DashSpeed { get; set; } = 12;
    public UnityEngine.Transform transform { get; } = new();
    public event Action<CharacterMainControl, UnityEngine.Vector3>? OnSetPositionEvent;

    public static void RaiseSlotChanged(CharacterMainControl main, ItemStatsSystem.Items.Slot slot) =>
        OnMainCharacterSlotContentChangedEvent?.Invoke(main, slot);

    public static void RaiseHoldChanged(CharacterMainControl main, DuckovItemAgent itemAgent) =>
        OnMainCharacterChangeHoldItemAgentEvent?.Invoke(main, itemAgent);

    public static void RaiseInventoryChanged(CharacterMainControl main, ItemStatsSystem.Inventory inventory, int index) =>
        OnMainCharacterInventoryChangedEvent?.Invoke(main, inventory, index);

    public void SetPosition(UnityEngine.Vector3 position)
    {
        transform.position = position;
        OnSetPositionEvent?.Invoke(this, position);
    }

    public static void ResetNativeState()
    {
        Main = null;
        OnMainCharacterSlotContentChangedEvent = null;
        OnMainCharacterChangeHoldItemAgentEvent = null;
        OnMainCharacterInventoryChangedEvent = null;
    }
}

public sealed class Health
{
    public bool IsDead { get; set; }
}

public sealed class DamageInfo
{
}

public sealed class EvacuationInfo
{
}
#pragma warning restore CA1050

namespace UnityEngine
{
    public static class Application
    {
        public static string version { get; set; } = "2.3.30";
        public static string persistentDataPath { get; set; } = string.Empty;
    }

    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
        public static void LogException(Exception exception) { }
    }

    public readonly struct Vector3
    {
        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public readonly float x;
        public readonly float y;
        public readonly float z;
    }

    public sealed class Transform
    {
        public Vector3 position { get; set; }
    }

    public sealed class GameObject
    {
        public SceneManagement.Scene scene { get; set; } = new(1);
    }
}

namespace UnityEngine.SceneManagement
{
    public readonly struct Scene
    {
        public Scene(int buildIndex) => this.buildIndex = buildIndex;
        public readonly int buildIndex;
    }
}

namespace Duckov.Economy
{
#pragma warning disable CA1051 // Stub mirrors the installed public-field native contract.
    public struct Cost
    {
        public long money;
        public ItemEntry[] items;

        public struct ItemEntry
        {
            public int id;
            public long amount;
        }

        public readonly bool IsFree => money <= 0 && (items == null || items.Length == 0);
        public readonly bool Enough => EconomyManager.IsEnough(this);
        public readonly bool Pay(bool accountAvaliable = true, bool cashAvaliable = true) =>
            EconomyManager.Pay(this, accountAvaliable, cashAvaliable);

#pragma warning disable CA1822 // Stub signature must mirror the installed instance method.
        internal readonly Cysharp.Threading.Tasks.UniTask Return(
            bool directToBuffer = false,
            bool toPlayerInventory = false,
            int amountFactor = 1,
            List<ItemStatsSystem.Item>? generatedItemsBuffer = null) =>
            Cysharp.Threading.Tasks.UniTask.CompletedTask;
#pragma warning restore CA1822
    }
#pragma warning restore CA1051

    public sealed class EconomyManager
    {
        public const int CashItemID = 451;
        public static EconomyManager? Instance { get; set; }
        public static long Money { get; set; }
        public static event Action<long, long>? OnMoneyChanged;
        public static event Action<long>? OnMoneyPaid;
        public static event Action? OnEconomyManagerLoaded;
        public static event Action<Cost>? OnCostPaid;

        public static bool Pay(Cost cost, bool accountAvaliable = true, bool cashAvaliable = true)
        {
            if (!IsEnough(cost, accountAvaliable, cashAvaliable)) return false;
            OnCostPaid?.Invoke(cost);
            return true;
        }

        public static bool IsEnough(Cost cost, bool accountAvaliable = true, bool cashAvaliable = true)
        {
            foreach (var entry in cost.items ?? Array.Empty<Cost.ItemEntry>())
                if (ItemUtilities.GetItemCount(entry.id) < entry.amount) return false;
            return true;
        }

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
            Instance = null;
            Money = 0;
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
    public static int GetItemCount(int typeID) => OwnedItems
        .Where(item => item.TypeID == typeID)
        .Sum(item => item.StackCount);
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
    public static ItemStatsSystem.Inventory? Inventory { get; set; }
    public static bool Loading { get; set; }
    public static event Action<PlayerStorage, ItemStatsSystem.Inventory, int>? OnPlayerStorageChange;
    public static event Action? OnLoadingFinished;
    public static void RaiseChanged(ItemStatsSystem.Inventory inventory, int index = 0) =>
        OnPlayerStorageChange?.Invoke(new PlayerStorage(), inventory, index);
    public static void RaiseLoadingFinished() => OnLoadingFinished?.Invoke();
    public static void ResetNativeState()
    {
        Inventory = null;
        Loading = false;
        OnPlayerStorageChange = null;
        OnLoadingFinished = null;
    }
}

public static class LevelManager
{
    public static LevelManagerInstance? Instance { get; set; }
    public static event Action? OnLevelBeginInitializing;
    public static event Action? OnLevelInitialized;
    public static event Action? OnAfterLevelInitialized;
    public static event Action<CharacterMainControl>? OnControllingCharacterChanged;
    public static event Action<EvacuationInfo>? OnEvacuated;
    public static event Action<DamageInfo>? OnMainCharacterDead;
    public static event Action? OnNewGameReport;
    public static bool LevelInitializing { get; set; }
    public static bool LevelInited { get; set; } = true;
    public static void RaiseLevelBeginInitializing() => OnLevelBeginInitializing?.Invoke();
    public static void RaiseLevelInitialized() => OnLevelInitialized?.Invoke();
    public static void RaiseAfterLevelInitialized() => OnAfterLevelInitialized?.Invoke();
    public static void RaiseControllingCharacterChanged(CharacterMainControl value) => OnControllingCharacterChanged?.Invoke(value);
    public static void RaiseEvacuated() => OnEvacuated?.Invoke(new EvacuationInfo());
    public static void RaiseMainCharacterDead() => OnMainCharacterDead?.Invoke(new DamageInfo());
    public static void RaiseNewGameReport() => OnNewGameReport?.Invoke();
    public static void ResetNativeState()
    {
        Instance = null;
        OnLevelBeginInitializing = null;
        OnLevelInitialized = null;
        OnAfterLevelInitialized = null;
        OnControllingCharacterChanged = null;
        OnEvacuated = null;
        OnMainCharacterDead = null;
        OnNewGameReport = null;
        LevelInitializing = false;
        LevelInited = true;
    }
}

public sealed class LevelManagerInstance
{
    public CharacterMainControl? MainCharacter { get; set; }
    public PetProxy? PetProxy { get; set; }
    public UnityEngine.GameObject gameObject { get; } = new();
}

public static class InputManager
{
    public static bool InputActived { get; set; } = true;
}

public static class RaidUtilities
{
#pragma warning disable CA1051 // Stub mirrors the installed public-field native contract.
    public struct RaidInfo
    {
        public int ID;
        public bool valid;
        public bool ended;
        public bool dead;
    }
#pragma warning restore CA1051

    public static RaidInfo CurrentRaid { get; set; }
    public static event Action<RaidInfo>? OnNewRaid;
    public static event Action<RaidInfo>? OnRaidEnd;
    public static event Action<RaidInfo>? OnRaidDead;

    public static void RaiseNewRaid(RaidInfo raid)
    {
        CurrentRaid = raid;
        OnNewRaid?.Invoke(raid);
    }

    public static void RaiseRaidEnd(bool dead = false)
    {
        var raid = CurrentRaid;
        raid.ended = true;
        raid.dead = dead;
        CurrentRaid = raid;
        OnRaidEnd?.Invoke(raid);
    }

    public static void ResetNativeState()
    {
        CurrentRaid = default;
        OnNewRaid = null;
        OnRaidEnd = null;
        OnRaidDead = null;
    }
}

public static class PauseMenu
{
    public static event Action? onPauseMenuOn;
    public static event Action? onPauseMenuOff;
}

public static class CheatMode
{
    public static event Action<bool>? OnCheatModeStatusChanged;
}

public static class SceneInfoCollection
{
    public sealed class SceneInfo
    {
        public string DisplayName { get; set; } = "Test map";
    }

    public static string GetSceneID(int buildIndex) => "test-map";
    public static SceneInfo? GetSceneInfo(string stableId) => new();
}

#pragma warning disable CA1707 // Stubs mirror the installed Duckov native type names exactly.
public sealed class ItemSetting_Gun
{
    public int TargetBulletID { get; set; }
    public string CurrentBulletName { get; set; } = string.Empty;
}

public sealed class ItemAgent_Gun
{
    public static event Action<ItemAgent_Gun>? OnMainCharacterShootEvent;
    public CharacterMainControl? Holder { get; set; }
    public ItemStatsSystem.Item? Item { get; set; }
    public ItemSetting_Gun? GunItemSetting { get; set; }

    public static void RaiseMainCharacterShoot(ItemAgent_Gun agent) =>
        OnMainCharacterShootEvent?.Invoke(agent);

    public static void ResetNativeState() => OnMainCharacterShootEvent = null;
}
#pragma warning restore CA1707

public static class GameManager
{
    public static bool Paused { get; set; }
}

public sealed class MultiSceneCore
{
    public static MultiSceneCore? Instance { get; set; }
    public static string ActiveSubSceneID { get; set; } = string.Empty;
    public static string MainSceneID { get; set; } = "test-map";
    public static event Action<MultiSceneCore, UnityEngine.SceneManagement.Scene>? OnSubSceneWillBeUnloaded;
    public static event Action<MultiSceneCore, UnityEngine.SceneManagement.Scene>? OnSubSceneLoaded;
    public bool IsLoading { get; set; }
}

public sealed class PetProxy
{
    public static ItemStatsSystem.Inventory? PetInventory { get; set; }
    public ItemStatsSystem.Inventory? Inventory { get; set; }
}

namespace Saves
{
    public static class SavesSystem
    {
        public static bool EconomyDataExists { get; set; }
        public static int CurrentSlot { get; set; } = 1;
        public static event Action? OnSetFile;
        public static event Action? OnSaveDeleted;
        public static event Action? OnCollectSaveData;
        public static bool KeyExisits(string key) => key == "EconomyData" && EconomyDataExists;
        public static string GetFilePath(int slot) => Path.Combine("Saves", $"slot-{slot:D2}.json");
        public static void SetFile(int slot)
        {
            CurrentSlot = slot;
            OnSetFile?.Invoke();
        }
        public static void RaiseSaveDeleted() => OnSaveDeleted?.Invoke();
        public static void RaiseCollectSaveData() => OnCollectSaveData?.Invoke();
        public static void ResetNativeState()
        {
            EconomyDataExists = false;
            CurrentSlot = 1;
            OnSetFile = null;
            OnSaveDeleted = null;
            OnCollectSaveData = null;
        }
    }
}
#pragma warning restore CA1050

namespace UltimateDuckovStatistics.Adapters
{
    internal static class NativeIntegrityProbe
    {
        public static UltimateDuckovStatistics.Core.Domain.IntegrityTags Read() =>
            UltimateDuckovStatistics.Core.Domain.IntegrityTags.Normal;
    }

    internal static class NativeRaidContext
    {
        public static UltimateDuckovStatistics.Core.Domain.GameplayContext GameplayContext { get; set; } =
            UltimateDuckovStatistics.Core.Domain.GameplayContext.Raid;

        public static UltimateDuckovStatistics.Core.Domain.GameplayContext GetGameplayContext() => GameplayContext;

        public static bool IsRaidMap() => GameplayContext ==
            UltimateDuckovStatistics.Core.Domain.GameplayContext.Raid;
    }
}

namespace Duckov.Scenes
{
    public sealed class SceneLoadingContext
    {
    }

    public static class SceneLoader
    {
        public static bool IsSceneLoading { get; set; }
        public static event Action<SceneLoadingContext>? onStartedLoadingScene;
        public static event Action<SceneLoadingContext>? onFinishedLoadingScene;
        public static event Action<SceneLoadingContext>? onAfterSceneInitialize;
        public static void RaiseStarted() => onStartedLoadingScene?.Invoke(new SceneLoadingContext());
        public static void RaiseFinished() => onFinishedLoadingScene?.Invoke(new SceneLoadingContext());
        public static void RaiseAfterInitialize() => onAfterSceneInitialize?.Invoke(new SceneLoadingContext());
        public static void ResetNativeState()
        {
            IsSceneLoading = false;
            onStartedLoadingScene = null;
            onFinishedLoadingScene = null;
            onAfterSceneInitialize = null;
        }
    }
}

namespace Duckov.Rules
{
    public static class GameRulesManager
    {
        public static event Action? OnRuleChanged;
    }
}

#pragma warning restore CS0067, CS0414, CA1051, CA1711
