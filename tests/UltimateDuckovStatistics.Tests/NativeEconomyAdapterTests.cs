using Duckov.Economy;
using Duckov.Quests;
using Duckov.Quests.Rewards;
using ItemStatsSystem;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Tests;

public sealed class NativeEconomyAdapterTests : IDisposable
{
    private readonly List<CurrencyFlowRecorded> published = new();
    private readonly List<IReadOnlyList<CapabilityRecord>> capabilities = new();
    private readonly List<string> diagnostics = new();
    private bool runActive;
    private string? runId;
    private string? segmentId;
    private string? mapId;

    public NativeEconomyAdapterTests()
    {
        ResetNativeState();
        UnityEngine.Application.version = "2.3.30";
    }

    [Fact]
    [Trait("Category", "M9")]
    [Trait("Category", "NativeAdapter")]
    public void MoneyBalanceChangesPublishExactUnknownAndSemanticFlowsOnce()
    {
        using var adapter = CreateAdapter();
        adapter.Initialize();
        adapter.Tick();

        EconomyManager.RaiseMoneyChanged(1_000, 875);
        adapter.Tick();
        var unknown = Assert.Single(published);
        Assert.Equal(CurrencyKind.Money, unknown.Currency);
        Assert.Equal(CurrencyFlowDirection.Outflow, unknown.Direction);
        Assert.Equal(125, unknown.Amount);
        Assert.Equal(CurrencySourceCategory.UnknownAdjustment, unknown.Source);
        Assert.Equal(GameplayContext.Base, unknown.GameplayContext);

        EconomyManager.RaiseMoneyChanged(875, 925);
        StockShop.RaiseSold(
            new StockShop { MerchantID = "merchant:one", DisplayName = "Merchant" },
            new Item { TypeID = 7 },
            50);
        adapter.Tick();

        var sale = Assert.Single(published, flow => flow.Source == CurrencySourceCategory.Sale);
        Assert.Equal(50, sale.Amount);
        Assert.Equal(CurrencyFlowDirection.Inflow, sale.Direction);
        Assert.Equal(GameplayContext.Shop, sale.GameplayContext);
        Assert.Equal("duckov:merchant:merchant:one", sale.NativeSourceId);
    }

    [Fact]
    [Trait("Category", "M9")]
    [Trait("Category", "NativeAdapter")]
    public void RewardAttributionUsesTheExactCompletedMoneyDelta()
    {
        using var adapter = CreateAdapter();
        adapter.Initialize();
        adapter.Tick();

        EconomyManager.RaiseMoneyChanged(20, 55);
        Reward.RaiseClaimed(new QuestReward_Money
        {
            ID = "reward:42",
            Description = "Quest reward",
            Amount = 35
        });
        adapter.Tick();

        var flow = Assert.Single(published);
        Assert.Equal(35, flow.Amount);
        Assert.Equal(CurrencySourceCategory.Reward, flow.Source);
        Assert.Equal(GameplayContext.Reward, flow.GameplayContext);
        Assert.Equal("duckov:quest-reward-money:reward:42", flow.NativeSourceId);
    }

    [Fact]
    [Trait("Category", "M9")]
    [Trait("Category", "NativeAdapter")]
    public void LoadedAndInternallyRearrangedCashNeverBecomesAFlow()
    {
        var carried = Cash(10);
        ItemUtilities.OwnedItems.Add(carried);
        using var adapter = CreateAdapter();
        adapter.Initialize();
        adapter.Tick();
        Assert.Empty(published);
        Assert.Equal(1, ItemUtilities.ScanCount);

        ItemUtilities.OwnedItems.Clear();
        ItemUtilities.OwnedItems.Add(Cash(4));
        ItemUtilities.OwnedItems.Add(Cash(6));
        ItemUtilities.RaisePlayerItemOperation();
        CharacterMainControl.RaiseInventoryChanged(MainCharacter(), new Inventory(), 0);
        PlayerStorage.RaiseChanged(new Inventory());
        adapter.Tick();

        Assert.Empty(published);
    }

    [Fact]
    [Trait("Category", "M9")]
    [Trait("Category", "NativeAdapter")]
    public void RaidPickupIsAcquiredOnceAndDropRepickupIsNotAcquiredAgain()
    {
        runActive = true;
        runId = "run:one";
        segmentId = "segment:B";
        mapId = "duckov:map:B";
        using var adapter = CreateAdapter();
        adapter.Initialize();
        adapter.Tick();

        var cash = Cash(8);
        ItemUtilities.OwnedItems.Add(cash);
        var pickup = new InteractablePickup { ItemAgent = new ItemAgent { Item = cash } };
        InteractablePickup.RaisePickup(pickup, MainCharacter());
        InteractablePickup.RaisePickup(pickup, MainCharacter());
        adapter.Tick();

        var acquisition = Assert.Single(published);
        Assert.Equal(8, acquisition.Amount);
        Assert.True(acquisition.ProvenExternalRaidAcquisition);
        Assert.Equal(CurrencySourceCategory.LootOrPickup, acquisition.Source);
        Assert.Equal("run:one", acquisition.RunId);
        Assert.Equal("segment:B", acquisition.SegmentId);
        Assert.Equal("duckov:map:B", acquisition.MapId);

        ItemUtilities.OwnedItems.Remove(cash);
        ItemUtilities.RaisePlayerItemOperation();
        adapter.Tick();
        ItemUtilities.OwnedItems.Add(cash);
        InteractablePickup.RaisePickup(pickup, MainCharacter());
        adapter.Tick();

        Assert.Equal(3, published.Count);
        Assert.Equal(CurrencyFlowDirection.Outflow, published[1].Direction);
        Assert.Equal(CurrencyFlowDirection.Inflow, published[2].Direction);
        Assert.Equal(CurrencySourceCategory.UnknownAdjustment, published[2].Source);
        Assert.False(published[2].ProvenExternalRaidAcquisition);
    }

    [Fact]
    [Trait("Category", "M9")]
    [Trait("Category", "NativeAdapter")]
    public void MainFlagWithoutExactMainCharacterIdentityCannotProveCashAcquisition()
    {
        runActive = true;
        runId = "run-1";
        segmentId = "segment-1";
        mapId = "map-1";
        _ = MainCharacter();
        var impostor = new CharacterMainControl { IsMainCharacter = true };
        using var adapter = CreateAdapter();
        adapter.Initialize();
        adapter.Tick();

        var cash = Cash(5);
        ItemUtilities.OwnedItems.Add(cash);
        InteractablePickup.RaisePickup(
            new InteractablePickup { ItemAgent = new ItemAgent { Item = cash } },
            impostor);
        ItemUtilities.RaisePlayerItemOperation();
        adapter.Tick();

        var flow = Assert.Single(published);
        Assert.Equal(CurrencySourceCategory.UnknownAdjustment, flow.Source);
        Assert.False(flow.ProvenExternalRaidAcquisition);
    }

    [Fact]
    [Trait("Category", "M9")]
    [Trait("Category", "Capability")]
    public void DropIdentityBoundDisablesOnlyAcquisitionBeforeAnEvictionCouldDoubleCount()
    {
        runActive = true;
        runId = "run:bounded";
        segmentId = "segment:bounded";
        mapId = "duckov:map:bounded";
        using var adapter = CreateAdapter();
        adapter.Initialize();
        adapter.Tick();

        for (var index = 0; index <= 512; index++)
        {
            var cash = Cash(1);
            ItemUtilities.OwnedItems.Add(cash);
            InteractablePickup.RaisePickup(
                new InteractablePickup { ItemAgent = new ItemAgent { Item = cash } },
                MainCharacter());
            ItemUtilities.OwnedItems.Remove(cash);
            ItemUtilities.RaisePlayerItemOperation();
            adapter.Tick();
        }

        var afterSaturation = Cash(3);
        ItemUtilities.OwnedItems.Add(afterSaturation);
        InteractablePickup.RaisePickup(
            new InteractablePickup { ItemAgent = new ItemAgent { Item = afterSaturation } },
            MainCharacter());

        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, adapter.MetricCapabilities.CashExternalAcquisition.State);
        var final = published[^1];
        Assert.Equal(3, final.Amount);
        Assert.Equal(CurrencySourceCategory.UnknownAdjustment, final.Source);
        Assert.False(final.ProvenExternalRaidAcquisition);
        Assert.Equal(AdapterCapabilityState.Supported, adapter.MetricCapabilities.CashAmountDirection.State);
    }

    [Fact]
    [Trait("Category", "M9")]
    [Trait("Category", "NativeAdapter")]
    public void CompletedCashSaleIsSemanticButCompletedCostRemainsUnknown()
    {
        ItemUtilities.OwnedItems.Add(Cash(20));
        using var adapter = CreateAdapter();
        adapter.Initialize();
        adapter.Tick();

        ItemUtilities.OwnedItems.Add(Cash(6));
        StockShop.RaiseSold(new StockShop(), new Item { TypeID = 99 }, 6);
        adapter.Tick();
        var sale = Assert.Single(published);
        Assert.Equal(CurrencySourceCategory.Sale, sale.Source);
        Assert.Equal(GameplayContext.Shop, sale.GameplayContext);

        ItemUtilities.OwnedItems.RemoveAt(0);
        ItemUtilities.OwnedItems.Add(Cash(15));
        EconomyManager.RaiseCostPaid();
        ItemUtilities.RaisePlayerItemOperation();
        adapter.Tick();
        var cost = published[^1];
        Assert.Equal(CurrencyFlowDirection.Outflow, cost.Direction);
        Assert.Equal(5, cost.Amount);
        Assert.Equal(CurrencySourceCategory.UnknownAdjustment, cost.Source);
    }

    [Fact]
    [Trait("Category", "M9")]
    [Trait("Category", "NativeAdapter")]
    public void DuplicateSetupAndStaleCallbacksDoNotDuplicatePublication()
    {
        var adapter = CreateAdapter();
        adapter.Initialize();
        adapter.Initialize();
        adapter.Tick();
        EconomyManager.RaiseMoneyChanged(1, 2);
        adapter.Tick();
        Assert.Single(published);
        Assert.Contains(diagnostics, value => value.Contains("Duplicate economy adapter setup ignored", StringComparison.Ordinal));

        adapter.Dispose();
        EconomyManager.RaiseMoneyChanged(2, 3);
        adapter.Tick();
        Assert.Single(published);
    }

    [Fact]
    [Trait("Category", "M9")]
    [Trait("Category", "Lifecycle")]
    public void DisposePublishesPendingMoneyAndCashBeforeUnsubscribing()
    {
        ItemUtilities.OwnedItems.Add(Cash(10));
        var adapter = CreateAdapter();
        adapter.Initialize();
        adapter.Tick();

        EconomyManager.RaiseMoneyChanged(100, 93);
        ItemUtilities.OwnedItems.Add(Cash(4));
        ItemUtilities.RaisePlayerItemOperation();

        adapter.Dispose();

        Assert.Collection(
            published.OrderBy(flow => flow.Currency),
            money =>
            {
                Assert.Equal(CurrencyKind.Money, money.Currency);
                Assert.Equal(7, money.Amount);
            },
            cash =>
            {
                Assert.Equal(CurrencyKind.Cash, cash.Currency);
                Assert.Equal(4, cash.Amount);
            });
    }

    [Fact]
    [Trait("Category", "M9")]
    [Trait("Category", "Lifecycle")]
    public void RetainedSemanticCallbacksAreInertAfterDispose()
    {
        ItemUtilities.OwnedItems.Add(Cash(10));
        var adapter = CreateAdapter();
        adapter.Initialize();
        adapter.Tick();
        var staleSale = StockShop.CaptureSoldSubscribers();
        var staleCost = EconomyManager.CaptureCostPaidSubscribers();

        adapter.Dispose();
        ItemUtilities.OwnedItems.Add(Cash(5));
        staleSale!(new StockShop(), new Item { TypeID = 99 }, 5);
        staleCost!(new Cost());

        Assert.Empty(published);
        Assert.Equal(1, ItemUtilities.ScanCount);
    }

    [Fact]
    [Trait("Category", "M9")]
    [Trait("Category", "Capability")]
    public void CapabilityPublicationKeepsTerminalDispositionAtTheNarrowUnsupportedBoundary()
    {
        using var adapter = CreateAdapter();
        adapter.Initialize();

        Assert.Equal(AdapterCapabilityState.Supported, adapter.MetricCapabilities.MoneyAmountDirection.State);
        Assert.Equal(AdapterCapabilityState.Experimental, adapter.MetricCapabilities.MoneySourceAttribution.State);
        Assert.Equal(AdapterCapabilityState.Experimental, adapter.MetricCapabilities.CashExternalAcquisition.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, adapter.MetricCapabilities.CashTerminalOutcomes.State);
        Assert.Contains("fungible", adapter.MetricCapabilities.CashTerminalOutcomes.Provenance, StringComparison.OrdinalIgnoreCase);
        Assert.Single(capabilities);
    }

    [Fact]
    [Trait("Category", "M9")]
    [Trait("Category", "Capability")]
    [Trait("Category", "Performance")]
    public void FailedCashContractDisablesOnceWithoutRecurringScans()
    {
        ItemUtilities.ScanException = new InvalidOperationException("broken inventory contract");
        using var adapter = CreateAdapter();
        adapter.Initialize();

        adapter.Tick();
        for (var index = 0; index < 60; index++)
        {
            ItemUtilities.RaisePlayerItemOperation();
            adapter.Tick();
        }

        Assert.Equal(1, ItemUtilities.ScanCount);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, adapter.MetricCapabilities.CashAmountDirection.State);
        Assert.Empty(published);
    }

    [Fact]
    [Trait("Category", "M9")]
    [Trait("Category", "Capability")]
    public void InvalidMoneyContractDisablesSubsequentBalanceCallbacks()
    {
        using var adapter = CreateAdapter();
        adapter.Initialize();
        adapter.Tick();

        EconomyManager.RaiseMoneyChanged(10, -1);
        EconomyManager.RaiseMoneyChanged(10, 11);
        adapter.Tick();

        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, adapter.MetricCapabilities.MoneyAmountDirection.State);
        Assert.Empty(published);
    }

    [Fact]
    [Trait("Category", "M9")]
    [Trait("Category", "NativeAdapter")]
    public void DuplicateMoneyCallbacksAreIgnoredAndDiscontinuityPreservesPriorExactFlowBeforeDisabling()
    {
        using var adapter = CreateAdapter();
        adapter.Initialize();
        adapter.Tick();

        EconomyManager.RaiseMoneyChanged(100, 90);
        EconomyManager.RaiseMoneyChanged(100, 90);
        adapter.Tick();
        var first = Assert.Single(published);
        Assert.Equal(10, first.Amount);

        EconomyManager.RaiseMoneyChanged(90, 80);
        EconomyManager.RaiseMoneyChanged(70, 60);
        EconomyManager.RaiseMoneyChanged(60, 50);
        adapter.Tick();

        Assert.Equal(2, published.Count);
        Assert.Equal(10, published[1].Amount);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, adapter.MetricCapabilities.MoneyAmountDirection.State);
        Assert.Contains(diagnostics, message => message.Contains("discontinuous", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "M9")]
    [Trait("Category", "Lifecycle")]
    public void SceneInitializationFlushesPendingMoneyBeforeResettingCashBaselines()
    {
        runActive = true;
        runId = "run-scene";
        segmentId = "segment-source";
        mapId = "map-source";
        using var adapter = CreateAdapter();
        adapter.Initialize();
        adapter.Tick();

        EconomyManager.RaiseMoneyChanged(50, 45);
        ItemUtilities.OwnedItems.Add(Cash(4));
        ItemUtilities.RaisePlayerItemOperation();
        LevelManager.RaiseAfterLevelInitialized();

        Assert.Equal(2, published.Count);
        var money = Assert.Single(published, flow => flow.Currency == CurrencyKind.Money);
        Assert.Equal(5, money.Amount);
        Assert.Equal(CurrencyFlowDirection.Outflow, money.Direction);
        var cash = Assert.Single(published, flow => flow.Currency == CurrencyKind.Cash);
        Assert.Equal(4, cash.Amount);
        Assert.Equal("segment-source", cash.SegmentId);
        adapter.Tick();
        Assert.Equal(2, published.Count);
    }

    [Fact]
    [Trait("Category", "M9")]
    [Trait("Category", "Lifecycle")]
    public void FullSceneInventoryHydrationDoesNotBecomeBaseInflowOnRaidEntryOrExtraction()
    {
        ItemUtilities.OwnedItems.Add(Cash(37));
        using var adapter = CreateAdapter();
        adapter.Initialize();
        adapter.Tick();
        Assert.Empty(published);

        LevelManager.RaiseLevelBeginInitializing();
        EconomyManager.RaiseLoaded();
        ItemUtilities.OwnedItems.Clear();
        adapter.Tick();
        ItemUtilities.OwnedItems.Add(Cash(37));
        ItemUtilities.RaisePlayerItemOperation();
        adapter.Tick();
        Assert.Empty(published);
        Assert.Equal(1, ItemUtilities.ScanCount);

        runActive = true;
        runId = "run-scene-hydration";
        segmentId = "segment-source";
        mapId = "map-source";
        LevelManager.RaiseAfterLevelInitialized();
        Assert.Empty(published);
        Assert.Equal(2, ItemUtilities.ScanCount);

        ItemUtilities.OwnedItems.Add(Cash(7));
        ItemUtilities.RaisePlayerItemOperation();
        adapter.Tick();
        var raidFlow = Assert.Single(published);
        Assert.Equal(7, raidFlow.Amount);
        Assert.Equal(GameplayContext.Raid, raidFlow.GameplayContext);
        Assert.Equal(3, ItemUtilities.ScanCount);

        runActive = false;
        runId = null;
        segmentId = null;
        mapId = null;
        LevelManager.RaiseLevelBeginInitializing();
        EconomyManager.RaiseLoaded();
        ItemUtilities.OwnedItems.Clear();
        adapter.Tick();
        ItemUtilities.OwnedItems.Add(Cash(37));
        ItemUtilities.RaisePlayerItemOperation();
        adapter.Tick();
        ItemUtilities.OwnedItems.Add(Cash(7));
        ItemUtilities.RaisePlayerItemOperation();
        adapter.Tick();
        Assert.Single(published);
        Assert.Equal(3, ItemUtilities.ScanCount);

        LevelManager.RaiseAfterLevelInitialized();
        adapter.Tick();

        Assert.Single(published);
        Assert.Equal(7, published[0].Amount);
        Assert.Equal(CurrencyFlowDirection.Inflow, published[0].Direction);
        Assert.Equal(GameplayContext.Raid, published[0].GameplayContext);
        Assert.Equal(4, ItemUtilities.ScanCount);
    }

    [Fact]
    [Trait("Category", "M9")]
    [Trait("Category", "Performance")]
    public void RapidMoneyFlowsRemainExactAndCashScansAreEventDrivenAndCoalesced()
    {
        using var adapter = CreateAdapter();
        adapter.Initialize();
        adapter.Tick();
        Assert.Equal(1, ItemUtilities.ScanCount);

        for (var index = 0; index < 60; index++) adapter.Tick();
        Assert.Equal(1, ItemUtilities.ScanCount);
        for (var index = 0; index < 100; index++) ItemUtilities.RaisePlayerItemOperation();
        adapter.Tick();
        Assert.Equal(2, ItemUtilities.ScanCount);

        for (var index = 0; index < 1_000; index++) EconomyManager.RaiseMoneyChanged(index, index + 1L);
        adapter.Tick();
        Assert.Equal(1_000, published.Count);
        Assert.All(published, flow => Assert.Equal(1, flow.Amount));
        Assert.Equal(1_000, published.Select(flow => flow.EventId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(2, ItemUtilities.ScanCount);
        Assert.Equal(AdapterCapabilityState.Supported, adapter.MetricCapabilities.MoneyAmountDirection.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, adapter.MetricCapabilities.MoneySourceAttribution.State);
    }

    public void Dispose() => ResetNativeState();

    private NativeEconomyAdapter CreateAdapter() => new(
        () => "generation:one",
        () => runId,
        () => mapId,
        () => segmentId,
        () => runActive,
        flow =>
        {
            published.Add(flow);
            return true;
        },
        records => capabilities.Add(records),
        diagnostics.Add);

    private static CharacterMainControl MainCharacter()
    {
        var character = new CharacterMainControl { IsMainCharacter = true };
        CharacterMainControl.Main = character;
        return character;
    }
    private static Item Cash(int amount) => new() { TypeID = EconomyManager.CashItemID, StackCount = amount, DisplayName = "Cash" };

    private static void ResetNativeState()
    {
        EconomyManager.ResetNativeState();
        StockShop.ResetNativeState();
        Reward.ResetNativeState();
        InteractablePickup.ResetNativeState();
        ItemUtilities.ResetNativeState();
        PlayerStorage.ResetNativeState();
        LevelManager.ResetNativeState();
        CharacterMainControl.ResetNativeState();
        PetProxy.PetInventory = null;
    }
}
