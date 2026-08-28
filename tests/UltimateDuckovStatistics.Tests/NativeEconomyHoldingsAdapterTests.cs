using Duckov.Economy;
using Duckov.Scenes;
using ItemStatsSystem;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UnityEngine;

namespace UltimateDuckovStatistics.Tests;

[Collection(NativeEconomyAdapterTestGroup.CollectionName)]
public sealed class NativeEconomyHoldingsAdapterTests : IDisposable
{
    public NativeEconomyHoldingsAdapterTests() => ResetNative();
    public void Dispose() => ResetNative();

    [Fact]
    [Trait("Category", "M15")]
    public void ActivationCoalescesExactRootsAndInternalMovementDoesNotPersistAFalseChange()
    {
        var roots = ConfigureReadyRoots();
        roots.Main.Content.Add(Cash(3));
        roots.Storage.Content.Add(Cash(4));
        roots.Pet.Content.Add(Cash(5));
        EconomyManager.Instance = new EconomyManager();
        EconomyManager.Money = 400;
        Saves.SavesSystem.EconomyDataExists = true;
        using var harness = new Harness("generation-1");

        harness.Adapter.Initialize();
        Assert.Equal(0, harness.PublicationAttempts);
        harness.Adapter.Tick();

        Assert.Equal(1, harness.PublicationAttempts);
        Assert.Equal(1, harness.PersistedChanges);
        Assert.Equal(400, harness.Snapshot.Money.Value);
        Assert.Equal(12, harness.Snapshot.Cash.Value);
        Assert.Equal(412, EconomyHoldingsReducer.Project(harness.Snapshot).LiquidWealth.Value);
        Assert.Equal(AdapterCapabilityState.Supported, harness.Snapshot.Capabilities.Money.State);
        Assert.Equal(AdapterCapabilityState.Supported, harness.Snapshot.Capabilities.Cash.State);

        PlayerStorage.RaiseChanged(roots.Storage);
        ItemUtilities.RaisePlayerItemOperation();
        harness.Adapter.Tick();

        Assert.Equal(2, harness.PublicationAttempts);
        Assert.Equal(1, harness.PersistedChanges);

        roots.Storage.Content[0].StackCount = 10;
        roots.Storage.RaiseContentChanged();
        PlayerStorage.RaiseChanged(roots.Storage);
        harness.Adapter.Tick();

        Assert.Equal(3, harness.PublicationAttempts);
        Assert.Equal(2, harness.PersistedChanges);
        Assert.Equal(18, harness.Snapshot.Cash.Value);
    }

    [Fact]
    [Trait("Category", "M15")]
    public void CashWaitsForEveryHydratedRootWhileMoneyPublishesIndependently()
    {
        var roots = ConfigureReadyRoots();
        roots.Pet.Loading = true;
        roots.Pet.Content.Add(Cash(7));
        EconomyManager.Instance = new EconomyManager();
        EconomyManager.Money = 25;
        Saves.SavesSystem.EconomyDataExists = true;
        using var harness = new Harness("generation-1");
        harness.Adapter.Initialize();

        harness.Adapter.Tick();

        Assert.Equal(EconomyHoldingObservationState.Current, harness.Snapshot.Money.State);
        Assert.Equal(25, harness.Snapshot.Money.Value);
        Assert.Equal(EconomyHoldingObservationState.Unavailable, harness.Snapshot.Cash.State);
        Assert.True(harness.Adapter.HasPendingObservation);

        roots.Pet.Loading = false;
        roots.Pet.RaiseContentChanged();
        roots.Pet.RaiseContentChanged();
        harness.Adapter.Tick();

        Assert.Equal(EconomyHoldingObservationState.Current, harness.Snapshot.Cash.State);
        Assert.Equal(7, harness.Snapshot.Cash.Value);
        Assert.False(harness.Adapter.HasPendingObservation);
    }

    [Fact]
    [Trait("Category", "M15")]
    public void SameInstanceMissingEconomyDataKeepsNewGenerationMoneyUnavailableUntilMutation()
    {
        ConfigureReadyRoots();
        EconomyManager.Instance = new EconomyManager();
        EconomyManager.Money = 50;
        Saves.SavesSystem.EconomyDataExists = true;
        using var harness = new Harness("generation-1");
        harness.Adapter.Initialize();
        harness.Adapter.Tick();
        Assert.Equal(50, harness.Snapshot.Money.Value);

        harness.Adapter.BeginProfileChange();
        harness.Generation = "generation-2";
        harness.Snapshot = new EconomyHoldingsSnapshot
        {
            SaveGenerationId = harness.Generation,
            Capabilities = harness.Adapter.MetricCapabilities
        };
        Saves.SavesSystem.EconomyDataExists = false;
        harness.Adapter.CompleteProfileChange();
        harness.Adapter.Tick();

        Assert.Equal(EconomyHoldingObservationState.Unavailable, harness.Snapshot.Money.State);
        Assert.Equal(EconomyHoldingObservationState.Current, harness.Snapshot.Cash.State);
        EconomyManager.Money = 0;
        EconomyManager.RaiseMoneyChanged(50, 0);
        harness.Adapter.Tick();
        Assert.Equal(EconomyHoldingObservationState.Current, harness.Snapshot.Money.State);
        Assert.Equal(0, harness.Snapshot.Money.Value);
    }

    [Fact]
    [Trait("Category", "M15")]
    public void SceneBoundaryMakesValuesLastObservedUntilRootsAreAuthoritativeAgain()
    {
        ConfigureReadyRoots();
        EconomyManager.Instance = new EconomyManager();
        EconomyManager.Money = 9;
        Saves.SavesSystem.EconomyDataExists = true;
        using var harness = new Harness("generation-1");
        harness.Adapter.Initialize();
        harness.Adapter.Tick();

        SceneLoader.IsSceneLoading = true;
        SceneLoader.RaiseStarted();
        Assert.Equal(EconomyHoldingObservationState.LastObserved, harness.Snapshot.Money.State);
        Assert.Equal(EconomyHoldingObservationState.LastObserved, harness.Snapshot.Cash.State);
        harness.Adapter.Tick();
        Assert.Equal(EconomyHoldingObservationState.LastObserved, harness.Snapshot.Cash.State);

        SceneLoader.IsSceneLoading = false;
        SceneLoader.RaiseAfterInitialize();
        harness.Adapter.Tick();
        Assert.Equal(EconomyHoldingObservationState.Current, harness.Snapshot.Money.State);
        Assert.Equal(EconomyHoldingObservationState.Current, harness.Snapshot.Cash.State);

        harness.Adapter.Dispose();
        ItemUtilities.RaisePlayerItemOperation();
        Assert.False(harness.Adapter.HasPendingObservation);
    }

    [Fact]
    [Trait("Category", "M15")]
    public void UnsupportedInstalledVersionDisablesAllCapabilitiesAndExposesNoCurrentValue()
    {
        ConfigureReadyRoots();
        EconomyManager.Instance = new EconomyManager();
        EconomyManager.Money = 99;
        Application.version = "2.3.31";
        using var harness = new Harness("generation-1");
        EconomyHoldingsReducer.Apply(
            harness.Snapshot,
            new EconomyHoldingsMutation("generation-1", DateTime.UtcNow, 99, 1, "old contract"));

        harness.Adapter.Initialize();

        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, harness.Snapshot.Capabilities.Money.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, harness.Snapshot.Capabilities.Cash.State);
        Assert.Equal(EconomyHoldingObservationState.Unavailable, harness.Snapshot.Money.State);
        Assert.Equal(EconomyHoldingObservationState.Unavailable, harness.Snapshot.Cash.State);
    }

    [Fact]
    [Trait("Category", "M15")]
    public void InvalidMoneyDisablesOnlyMoneyAndConditionalLiquidWealth()
    {
        ConfigureReadyRoots();
        EconomyManager.Instance = new EconomyManager();
        EconomyManager.Money = -1;
        Saves.SavesSystem.EconomyDataExists = true;
        using var harness = new Harness("generation-1");

        harness.Adapter.Initialize();
        harness.Adapter.Tick();

        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, harness.Snapshot.Capabilities.Money.State);
        Assert.Equal(AdapterCapabilityState.Supported, harness.Snapshot.Capabilities.Cash.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, harness.Snapshot.Capabilities.LiquidWealth.State);
        Assert.Equal(EconomyHoldingObservationState.Unavailable, harness.Snapshot.Money.State);
        Assert.Equal(EconomyHoldingObservationState.Current, harness.Snapshot.Cash.State);
    }

    [Fact]
    [Trait("Category", "M15")]
    public void VerifiedCashBoundDisablesOnlyCashAndConditionalLiquidWealth()
    {
        var roots = ConfigureReadyRoots();
        for (var index = 0; index <= NativeEconomyHoldingsSnapshotBuilder.MaximumItemsPerOwnedInventory; index++)
            roots.Storage.Content.Add(new Item { TypeID = 999, StackCount = 1 });
        EconomyManager.Instance = new EconomyManager();
        EconomyManager.Money = 10;
        Saves.SavesSystem.EconomyDataExists = true;
        using var harness = new Harness("generation-1");

        harness.Adapter.Initialize();
        harness.Adapter.Tick();

        Assert.Equal(AdapterCapabilityState.Supported, harness.Snapshot.Capabilities.Money.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, harness.Snapshot.Capabilities.Cash.State);
        Assert.Equal(AdapterCapabilityState.DisabledIncompatible, harness.Snapshot.Capabilities.LiquidWealth.State);
        Assert.Equal(EconomyHoldingObservationState.Current, harness.Snapshot.Money.State);
        Assert.Equal(EconomyHoldingObservationState.Unavailable, harness.Snapshot.Cash.State);
    }

    private static (Inventory Main, Inventory Storage, Inventory Pet) ConfigureReadyRoots()
    {
        var mainInventory = new Inventory();
        var storageInventory = new Inventory();
        var petInventory = new Inventory();
        var main = new CharacterMainControl
        {
            IsMainCharacter = true,
            CharacterItem = new Item { Inventory = mainInventory }
        };
        CharacterMainControl.Main = main;
        PlayerStorage.Inventory = storageInventory;
        PetProxy.PetInventory = petInventory;
        LevelManager.Instance = new LevelManagerInstance
        {
            MainCharacter = main,
            PetProxy = new PetProxy { Inventory = petInventory }
        };
        LevelManager.LevelInited = true;
        LevelManager.LevelInitializing = false;
        SceneLoader.IsSceneLoading = false;
        return (mainInventory, storageInventory, petInventory);
    }

    private static Item Cash(int value) => new()
    {
        TypeID = EconomyManager.CashItemID,
        StackCount = value
    };

    private static void ResetNative()
    {
        Application.version = "2.3.30";
        CharacterMainControl.ResetNativeState();
        ItemUtilities.ResetNativeState();
        PlayerStorage.ResetNativeState();
        LevelManager.ResetNativeState();
        PetProxy.PetInventory = null;
        EconomyManager.ResetNativeState();
        Saves.SavesSystem.ResetNativeState();
        SceneLoader.ResetNativeState();
    }

    private sealed class Harness : IDisposable
    {
        public Harness(string generation)
        {
            Generation = generation;
            Snapshot = new EconomyHoldingsSnapshot { SaveGenerationId = generation };
            Adapter = new NativeEconomyHoldingsAdapter(
                () => Generation,
                mutation =>
                {
                    PublicationAttempts++;
                    if (EconomyHoldingsReducer.Apply(Snapshot, mutation)) PersistedChanges++;
                    return true;
                },
                (money, cash, provenance) =>
                    EconomyHoldingsReducer.MarkNotCurrent(Snapshot, Generation, money, cash, provenance),
                (money, cash, provenance) =>
                    EconomyHoldingsReducer.MarkUnavailable(Snapshot, Generation, money, cash, provenance),
                (records, capabilities) =>
                {
                    CapabilityRecords = records.Select(Clone).ToList();
                    EconomyHoldingsReducer.ApplyCapabilities(Snapshot, capabilities);
                },
                _ => { });
        }

        public string Generation { get; set; }
        public EconomyHoldingsSnapshot Snapshot { get; set; }
        public NativeEconomyHoldingsAdapter Adapter { get; }
        public int PublicationAttempts { get; private set; }
        public int PersistedChanges { get; private set; }
        public IReadOnlyList<CapabilityRecord> CapabilityRecords { get; private set; } = Array.Empty<CapabilityRecord>();
        public void Dispose() => Adapter.Dispose();

        private static CapabilityRecord Clone(CapabilityRecord source) => new()
        {
            AdapterId = source.AdapterId,
            State = source.State,
            Version = source.Version,
            Detail = source.Detail
        };
    }
}
