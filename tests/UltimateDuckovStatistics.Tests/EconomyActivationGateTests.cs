using Duckov.Economy;
using ItemStatsSystem;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Tests;

[Collection(NativeEconomyAdapterTestGroup.CollectionName)]
public sealed class EconomyActivationGateTests : IDisposable
{
    private static readonly DateTime TestTime = new(2026, 8, 17, 20, 0, 0, DateTimeKind.Utc);

    public EconomyActivationGateTests()
    {
        ResetNativeState();
        UnityEngine.Application.version = "2.3.30";
    }

    [Fact]
    [Trait("Category", "M9")]
    [Trait("Category", "Persistence")]
    [Trait("Category", "NativeAdapter")]
    public void ContinuedActivationSaveFailureRetainsSynchronousCashAndMoneyUntilSequenceZeroIsDurable()
    {
        using var directory = new TemporaryDirectory();
        var repository = new ProfileRepository(
            directory.Path,
            () => TestTime,
            new Queue<string>(["generation-one", "session-one"]).Dequeue);
        repository.Open(Identity());
        repository.EnableDeferredItemPersistence();
        var blockedTemporaryPath = AtomicJsonPaths.GetTemporaryPath(repository.CurrentProfilePath!);
        Directory.CreateDirectory(blockedTemporaryPath);
        var failures = new List<Exception>();
        var published = new List<CurrencyFlowRecorded>();
        var mapId = "map-one";
        var segmentId = "segment-one";
        var gate = new EconomyActivationGate(
            activationId => repository.BeginEconomyActivation(activationId),
            failures.Add);
        using var adapter = new NativeEconomyAdapter(
            () => repository.CurrentGenerationId,
            () => "run-one",
            () => mapId,
            () => segmentId,
            () => true,
            flow =>
            {
                if (!repository.RecordDeferred(flow)) return false;
                published.Add(flow);
                return true;
            },
            _ => { },
            _ => { },
            gate.EnsureReady);
        gate.Begin(adapter.ActivationId);
        Assert.False(gate.IsReady);
        Assert.True(Assert.Single(failures) is IOException or UnauthorizedAccessException);

        adapter.Initialize();
        adapter.Tick();
        EconomyManager.RaiseMoneyChanged(0, 5);
        var cash = new Item
        {
            TypeID = EconomyManager.CashItemID,
            StackCount = 7,
            DisplayName = "Cash"
        };
        ItemUtilities.OwnedItems.Add(cash);
        var character = new CharacterMainControl { IsMainCharacter = true };
        CharacterMainControl.Main = character;
        InteractablePickup.RaisePickup(
            new InteractablePickup { ItemAgent = new ItemAgent { Item = cash } },
            character);
        adapter.Tick();

        mapId = "map-two";
        segmentId = "segment-two";
        ItemUtilities.OwnedItems.Remove(cash);
        ItemUtilities.RaisePlayerItemOperation();
        adapter.Tick();

        Assert.False(gate.IsReady);
        Assert.Empty(published);
        Assert.Empty(repository.Current.Statistics.Economy.Currencies);

        mapId = "map-after-transition";
        segmentId = "segment-after-transition";
        LevelManager.RaiseLevelBeginInitializing();
        EconomyManager.RaiseLoaded();
        LevelManager.RaiseAfterLevelInitialized();

        Assert.False(gate.IsReady);
        Assert.Empty(published);
        Assert.Empty(repository.Current.Statistics.Economy.Currencies);

        Directory.Delete(blockedTemporaryPath);
        adapter.Tick();

        Assert.True(gate.IsReady);
        Assert.Collection(
            published,
            money =>
            {
                Assert.Equal(CurrencyKind.Money, money.Currency);
                Assert.Equal(5, money.Amount);
                Assert.Equal(1, money.ProducerSequence);
            },
            observedCash =>
            {
                Assert.Equal(CurrencyKind.Cash, observedCash.Currency);
                Assert.Equal(7, observedCash.Amount);
                Assert.Equal(2, observedCash.ProducerSequence);
                Assert.True(observedCash.ProvenExternalRaidAcquisition);
                Assert.Equal("map-one", observedCash.MapId);
                Assert.Equal("segment-one", observedCash.SegmentId);
            },
            spentCash =>
            {
                Assert.Equal(CurrencyKind.Cash, spentCash.Currency);
                Assert.Equal(CurrencyFlowDirection.Outflow, spentCash.Direction);
                Assert.Equal(7, spentCash.Amount);
                Assert.Equal(3, spentCash.ProducerSequence);
                Assert.False(spentCash.ProvenExternalRaidAcquisition);
                Assert.Equal("map-two", spentCash.MapId);
                Assert.Equal("segment-two", spentCash.SegmentId);
            });
        Assert.All(published, flow => Assert.Equal(adapter.ActivationId, flow.ProducerActivationId));
        Assert.Equal(adapter.ActivationId, repository.Current.Statistics.Economy.ReplayCursor!.ActivationId);
        Assert.Equal(3, repository.Current.Statistics.Economy.ReplayCursor.ClosedThroughSequence);
        Assert.Equal(5, repository.Current.Statistics.Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.Equal(7, repository.Current.Statistics.Economy.Currencies["Cash"].Totals.GrossInflow);
        Assert.Equal(7, repository.Current.Statistics.Economy.Currencies["Cash"].Totals.GrossOutflow);
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "M9")]
    [Trait("Category", "Persistence")]
    [Trait("Category", "NativeAdapter")]
    public void ActiveRunRewardAndSaleCallbacksRetainValidConservativeRaidMoneyFlows()
    {
        using var directory = new TemporaryDirectory();
        var repository = new ProfileRepository(
            directory.Path,
            () => TestTime,
            new Queue<string>(["generation-one", "session-one"]).Dequeue);
        repository.Open(Identity());
        repository.EnableDeferredItemPersistence();
        var published = new List<CurrencyFlowRecorded>();
        var gate = new EconomyActivationGate(
            activationId => repository.BeginEconomyActivation(activationId),
            exception => throw exception);
        using var adapter = new NativeEconomyAdapter(
            () => repository.CurrentGenerationId,
            () => "run-one",
            () => "map-one",
            () => "segment-one",
            () => true,
            flow =>
            {
                if (!repository.RecordDeferred(flow)) return false;
                published.Add(flow);
                return true;
            },
            _ => { },
            _ => { },
            gate.EnsureReady);
        gate.Begin(adapter.ActivationId);
        adapter.Initialize();
        adapter.Tick();

        EconomyManager.RaiseMoneyChanged(0, 5);
        Duckov.Quests.Reward.RaiseClaimed(new Duckov.Quests.Rewards.QuestReward_Money
        {
            ID = "reward-active-run",
            Description = "Auto reward",
            Amount = 5
        });
        adapter.Tick();

        EconomyManager.RaiseMoneyChanged(5, 11);
        StockShop.RaiseSold(
            new StockShop { MerchantID = "merchant-active-run", DisplayName = "Raid merchant" },
            new Item { TypeID = 99 },
            6);
        adapter.Tick();

        Assert.Collection(
            published,
            reward => AssertConservativeRaidMoney(reward, 5),
            sale => AssertConservativeRaidMoney(sale, 6));
        Assert.Equal(11, repository.Current.Statistics.Economy.Currencies["Money"].Totals.GrossInflow);
        repository.CloseClean();
    }

    public void Dispose() => ResetNativeState();

    private static void AssertConservativeRaidMoney(CurrencyFlowRecorded flow, long amount)
    {
        Assert.Equal(CurrencyKind.Money, flow.Currency);
        Assert.Equal(amount, flow.Amount);
        Assert.Equal(CurrencySourceCategory.UnknownAdjustment, flow.Source);
        Assert.Equal(GameplayContext.Raid, flow.GameplayContext);
        Assert.Equal("run-one", flow.RunId);
        Assert.Equal("segment-one", flow.SegmentId);
        Assert.Equal("map-one", flow.MapId);
        Assert.Null(flow.NativeSourceId);
    }

    private static SaveIdentitySnapshot Identity() => new()
    {
        Slot = 1,
        SaveFilePresent = true,
        SaveFileCreationUtcTicks = 100,
        ObservedWriteUtcTicks = 110,
        ObservedLength = 4096,
        GameVersion = "2.3.30",
        ContentSha256 = new string('a', 64),
        SaveTimeBinary = TestTime.ToBinary()
    };

    private static void ResetNativeState()
    {
        EconomyManager.ResetNativeState();
        StockShop.ResetNativeState();
        Duckov.Quests.Reward.ResetNativeState();
        InteractablePickup.ResetNativeState();
        ItemUtilities.ResetNativeState();
        PlayerStorage.ResetNativeState();
        LevelManager.ResetNativeState();
        CharacterMainControl.ResetNativeState();
        PetProxy.PetInventory = null;
    }
}
