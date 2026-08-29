using Duckov.Economy;
using Duckov.Scenes;
using ItemStatsSystem;

namespace UltimateDuckovStatistics.Adapters;

internal static class NativeEconomyHoldingsSnapshotBuilder
{
    internal const int MaximumItemsPerOwnedInventory = 16384;
    private const int CashItemTypeId = EconomyManager.CashItemID;

    public static bool TryReadMoney(out long value, out string unavailableReason) =>
        TryReadMoney(out value, out unavailableReason, out _);

    public static bool TryReadMoney(
        out long value,
        out string unavailableReason,
        out bool incompatible)
    {
        incompatible = false;
        try
        {
            if (EconomyManager.Instance == null)
            {
                value = 0;
                unavailableReason = "Duckov EconomyManager.Instance is not hydrated.";
                return false;
            }
            value = EconomyManager.Money;
            if (value < 0)
            {
                incompatible = true;
                unavailableReason = "Duckov reported a negative Money balance.";
                return false;
            }
            unavailableReason = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            value = 0;
            incompatible = true;
            unavailableReason = $"Authoritative Money read failed: {exception.GetType().Name}.";
            return false;
        }
    }

    public static bool TryReadCash(out long value, out string unavailableReason) =>
        TryReadCash(out value, out unavailableReason, out _);

    public static bool TryReadCash(
        out long value,
        out string unavailableReason,
        out bool incompatible)
    {
        value = 0;
        incompatible = false;
        NativeHotPathDiagnostics.CountEconomyHoldingsReadinessCheck();
        if (!TryGetReadyRoots(out var roots, out unavailableReason, out incompatible)) return false;
        NativeHotPathDiagnostics.CountEconomyHoldingsCashScan();

        var inventoryIds = new HashSet<int>();
        var itemAmounts = new Dictionary<int, long>();
        try
        {
            foreach (var inventory in roots)
            {
                // One native inventory accidentally exposed through two roots is
                // still one owned inventory and must not be counted twice.
                if (!inventoryIds.Add(inventory.GetInstanceID())) continue;
                foreach (var item in inventory.Content)
                {
                    if (item == null || item.TypeID != CashItemTypeId) continue;
                    if (item.StackCount < 0)
                    {
                        incompatible = true;
                        unavailableReason = "Duckov reported a negative Cash stack.";
                        return false;
                    }
                    var itemId = item.GetInstanceID();
                    if (itemAmounts.TryGetValue(itemId, out var existing))
                    {
                        if (existing != item.StackCount)
                        {
                            incompatible = true;
                            unavailableReason = "One Cash identity had conflicting stack counts across owned roots.";
                            return false;
                        }
                        continue;
                    }
                    itemAmounts.Add(itemId, item.StackCount);
                    value = checked(value + item.StackCount);
                }
            }
        }
        catch (OverflowException)
        {
            value = 0;
            incompatible = true;
            unavailableReason = "Checked total owned Cash exceeded Int64.";
            return false;
        }
        catch (Exception exception)
        {
            value = 0;
            incompatible = true;
            unavailableReason = $"Authoritative Cash read failed: {exception.GetType().Name}.";
            return false;
        }

        unavailableReason = string.Empty;
        return true;
    }

    public static bool AreCashRootsReady(out string unavailableReason) =>
        TryGetReadyRoots(out _, out unavailableReason, out _);

    private static bool TryGetReadyRoots(
        out Inventory[] roots,
        out string unavailableReason,
        out bool incompatible)
    {
        roots = Array.Empty<Inventory>();
        incompatible = false;
        if (!LevelManager.LevelInited || LevelManager.LevelInitializing || SceneLoader.IsSceneLoading)
        {
            unavailableReason = "Duckov level and owned inventories are not fully initialized.";
            return false;
        }
        var manager = LevelManager.Instance;
        var main = manager?.MainCharacter;
        var mainInventory = main?.CharacterItem?.Inventory;
        var storage = PlayerStorage.Inventory;
        var pet = manager?.PetProxy?.Inventory;
        if (main == null || !main.IsMainCharacter || !ReferenceEquals(main, CharacterMainControl.Main))
        {
            unavailableReason = "The authoritative main character is unavailable.";
            return false;
        }
        if (mainInventory == null || storage == null || pet == null)
        {
            unavailableReason = "One or more authoritative Cash inventory roots are unavailable.";
            return false;
        }
        if (PlayerStorage.Loading || mainInventory.Loading || storage.Loading || pet.Loading)
        {
            unavailableReason = "One or more authoritative Cash inventory roots are still hydrating.";
            return false;
        }
        if (mainInventory.Content == null || storage.Content == null || pet.Content == null)
        {
            unavailableReason = "One or more authoritative Cash inventory contents are unreadable.";
            return false;
        }
        if (mainInventory.Content.Count > MaximumItemsPerOwnedInventory
            || storage.Content.Count > MaximumItemsPerOwnedInventory
            || pet.Content.Count > MaximumItemsPerOwnedInventory)
        {
            incompatible = true;
            unavailableReason =
                $"The defensive {MaximumItemsPerOwnedInventory}-item per-inventory Cash observation bound was exceeded.";
            return false;
        }
        roots = [mainInventory, storage, pet];
        unavailableReason = string.Empty;
        return true;
    }
}
