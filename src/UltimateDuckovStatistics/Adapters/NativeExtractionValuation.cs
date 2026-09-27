using Duckov.Economy;
using Duckov.Scenes;
using ItemStatsSystem;

namespace UltimateDuckovStatistics.Adapters;

internal static class NativeExtractionValuation
{
    internal const int MaximumItems = 16384;

    public static decimal? Read(Action<string> diagnostic)
    {
        try
        {
            var main = LevelManager.Instance?.MainCharacter;
            var character = main?.CharacterItem;
            var pet = LevelManager.Instance?.PetProxy?.Inventory;
            if (!LevelManager.LevelInited || LevelManager.LevelInitializing || SceneLoader.IsSceneLoading
                || main == null || !main.IsMainCharacter || !ReferenceEquals(main, CharacterMainControl.Main)
                || character?.Inventory == null || pet == null || pet.Loading
                || EconomyManager.Instance == null || EconomyManager.Money < 0)
                return null;
            return ReadOwnedTree(character, pet, EconomyManager.Money);
        }
        catch (Exception exception)
        {
            diagnostic($"Extraction value unavailable: {exception.GetType().Name}: {exception.Message}");
            return null;
        }
    }

    internal static decimal ReadOwnedTree(Item character, Inventory pet, long wallet)
    {
        if (wallet < 0) throw new InvalidOperationException("Negative wallet balance.");
        var seen = new HashSet<int>();
        var pending = new Stack<Item>();
        pending.Push(character);
        AddInventory(pet, pending);
        long raw = 0;
        while (pending.Count > 0)
        {
            var item = pending.Pop();
            if (item == null) continue;
            if (item.IsBeingDestroyed) throw new InvalidOperationException("Owned item is being destroyed.");
            if (item.GetComponent<ItemSetting_Gun>()?.LoadingBullets == true)
                throw new InvalidOperationException("Ammunition is in transit during reload.");
            // Alias roots / corrupt cycles cannot increase the estimate or loop forever.
            if (!seen.Add(item.GetInstanceID())) throw new InvalidOperationException("Repeated owned item identity.");
            if (seen.Count > MaximumItems) throw new InvalidOperationException("Owned item observation bound exceeded.");
            if (!ReferenceEquals(item, character))
            {
                if (item.Value < 0 || item.StackCount < 0) throw new InvalidOperationException("Invalid native item value/count.");
                // Same per-unit durability rounding as Item.GetTotalRawValue, with checked Int64 totals.
                var unit = (float)item.Value;
                if (item.UseDurability)
                {
                    if (!float.IsFinite(item.MaxDurability) || !float.IsFinite(item.Durability)
                        || item.MaxDurability <= 0 || item.Durability < 0 || item.Durability > item.MaxDurability)
                        throw new InvalidOperationException("Invalid item durability.");
                    unit *= item.Durability / item.MaxDurability;
                }
                raw = checked(raw + checked((long)Math.Floor(unit) * (item.Stackable ? item.StackCount : 1)));
            }
            if (item.Slots != null)
                foreach (var slot in item.Slots)
                    if (slot?.Content is { } content) pending.Push(content);
            if (item.Inventory != null) AddInventory(item.Inventory, pending);
            if (pending.Count > MaximumItems) throw new InvalidOperationException("Owned item observation bound exceeded.");
        }
        // Cash in the physical tree is an item; Money is the separate native wallet.
        // Do not add the all-owned Cash aggregate (which also includes stash).
        return wallet + raw / 2m;
    }

    private static void AddInventory(Inventory inventory, Stack<Item> pending)
    {
        if (inventory.Loading || inventory.Content == null || inventory.Content.Count > MaximumItems)
            throw new InvalidOperationException("Owned inventory is unavailable or too large.");
        foreach (var item in inventory.Content)
            if (item != null) pending.Push(item);
    }
}
