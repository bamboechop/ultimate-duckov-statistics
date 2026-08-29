using Duckov.Economy;
using ItemStatsSystem;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UnityEngine;

namespace UltimateDuckovStatistics.Tests;

[Collection(NativeEconomyAdapterTestGroup.CollectionName)]
public sealed class NativeEconomyHoldingsCoordinatorTests : IDisposable
{
    private readonly string originalPersistentDataPath = Application.persistentDataPath;

    public NativeEconomyHoldingsCoordinatorTests() => ResetNative();

    public void Dispose()
    {
        ResetNative();
        Application.persistentDataPath = originalPersistentDataPath;
    }

    [Fact]
    [Trait("Category", "M15")]
    [Trait("Category", "Lifecycle")]
    public void NativeMoneyLoadBeforeUdsSaveSwitchCannotCrossContaminatePriorGeneration()
    {
        using var directory = new TemporaryDirectory();
        Application.persistentDataPath = directory.Path;
        WriteNativeSave(directory.Path, slot: 1, saveTime: 1);
        WriteNativeSave(directory.Path, slot: 2, saveTime: 2);
        ConfigureReadyRoots();

        Saves.SavesSystem.CurrentSlot = 1;
        Saves.SavesSystem.EconomyDataExists = true;
        EconomyManager.Instance = new EconomyManager();
        EconomyManager.Money = 111;
        var moneyBySlot = new Dictionary<int, long>
        {
            [1] = 111,
            [2] = 222
        };

        // Installed ordering: the native subscriber is registered before UDS,
        // CurrentSlot changes first, and native Money is loaded synchronously.
        Saves.SavesSystem.OnSetFile += () =>
        {
            EconomyManager.Money = moneyBySlot[Saves.SavesSystem.CurrentSlot];
            EconomyManager.RaiseLoaded();
        };

        using var coordinator = new NativeProfileCoordinator();
        coordinator.Initialize();
        using var adapter = new NativeEconomyHoldingsAdapter(
            () => coordinator.CurrentGenerationId,
            coordinator.HandleEconomyHoldings,
            coordinator.MarkEconomyHoldingsNotCurrent,
            coordinator.MarkEconomyHoldingsUnavailable,
            coordinator.SetEconomyHoldingsCapabilities,
            _ => { });
        adapter.Initialize();
        coordinator.SetEconomyHoldingsBoundaryBarrier(adapter.FlushPending);
        coordinator.EconomyHoldingsSaveSlotTransitionStarted += adapter.BeginSaveSlotProfileChange;
        coordinator.EconomyHoldingsSaveSlotTransitionCompleted += adapter.CompleteSaveSlotProfileChange;
        coordinator.EconomyHoldingsProfileResetStarted += adapter.BeginProfileReset;
        coordinator.EconomyHoldingsProfileResetCompleted += adapter.CompleteProfileReset;
        coordinator.ProfileChanging += adapter.BeginProfileChange;
        coordinator.ProfileChanged += adapter.CompleteProfileChange;

        adapter.Tick();
        coordinator.Flush();
        var generationA = coordinator.CurrentGenerationId;
        Assert.Equal(111, coordinator.Current!.Statistics.Holdings.Money.Value);

        var generationBAtHandoff = string.Empty;
        var bWasUnavailableBeforeProfileChanged = false;
        coordinator.WorldTimeProfileChangeCompleted += _ =>
        {
            generationBAtHandoff = coordinator.CurrentGenerationId;
            bWasUnavailableBeforeProfileChanged =
                coordinator.Current!.Statistics.Holdings.Money.State
                == EconomyHoldingObservationState.Unavailable;
        };

        Saves.SavesSystem.SetFile(2);

        Assert.NotEqual(generationA, generationBAtHandoff);
        Assert.True(bWasUnavailableBeforeProfileChanged);
        Assert.Equal(EconomyHoldingObservationState.Unavailable,
            coordinator.Current!.Statistics.Holdings.Money.State);

        adapter.Tick();
        Assert.Equal(EconomyHoldingObservationState.Current,
            coordinator.Current.Statistics.Holdings.Money.State);
        Assert.Equal(222, coordinator.Current.Statistics.Holdings.Money.Value);
        coordinator.Flush();

        var slotAPath = ProfilePath(coordinator.DataRoot, slot: 1);
        var slotAPrimary = LoadExact(slotAPath);
        var slotABackup = LoadExact(AtomicJsonPaths.GetBackupPath(slotAPath));
        Assert.Equal(generationA, slotAPrimary.GenerationId);
        Assert.Equal(EconomyHoldingObservationState.LastObserved,
            slotAPrimary.Statistics.Holdings.Money.State);
        Assert.Equal(111, slotAPrimary.Statistics.Holdings.Money.Value);
        Assert.Equal(111, slotABackup.Statistics.Holdings.Money.Value);

        var slotBPath = ProfilePath(coordinator.DataRoot, slot: 2);
        var slotBPrimary = LoadExact(slotBPath);
        var slotBBackup = LoadExact(AtomicJsonPaths.GetBackupPath(slotBPath));
        Assert.Equal(generationBAtHandoff, slotBPrimary.GenerationId);
        Assert.Equal(EconomyHoldingObservationState.Current,
            slotBPrimary.Statistics.Holdings.Money.State);
        Assert.Equal(222, slotBPrimary.Statistics.Holdings.Money.Value);
        Assert.Equal(EconomyHoldingObservationState.Current,
            slotBBackup.Statistics.Holdings.Money.State);
        Assert.Equal(222, slotBBackup.Statistics.Holdings.Money.Value);
    }

    [Fact]
    [Trait("Category", "M15")]
    [Trait("Category", "Lifecycle")]
    public void UserResetRetainsAuthoritativeMoneyObservedBeforeFirstEconomySave()
    {
        using var directory = new TemporaryDirectory();
        Application.persistentDataPath = directory.Path;
        WriteNativeSave(directory.Path, slot: 1, saveTime: 1);
        ConfigureReadyRoots();

        Saves.SavesSystem.CurrentSlot = 1;
        Saves.SavesSystem.EconomyDataExists = false;
        var economyManager = new EconomyManager();
        EconomyManager.Instance = economyManager;
        EconomyManager.Money = 333;

        using var coordinator = new NativeProfileCoordinator();
        coordinator.Initialize();
        using var adapter = new NativeEconomyHoldingsAdapter(
            () => coordinator.CurrentGenerationId,
            coordinator.HandleEconomyHoldings,
            coordinator.MarkEconomyHoldingsNotCurrent,
            coordinator.MarkEconomyHoldingsUnavailable,
            coordinator.SetEconomyHoldingsCapabilities,
            _ => { });
        adapter.Initialize();
        coordinator.SetEconomyHoldingsBoundaryBarrier(adapter.FlushPending);
        coordinator.EconomyHoldingsSaveSlotTransitionStarted += adapter.BeginSaveSlotProfileChange;
        coordinator.EconomyHoldingsSaveSlotTransitionCompleted += adapter.CompleteSaveSlotProfileChange;
        coordinator.EconomyHoldingsProfileResetStarted += adapter.BeginProfileReset;
        coordinator.EconomyHoldingsProfileResetCompleted += adapter.CompleteProfileReset;
        coordinator.ProfileChanging += adapter.BeginProfileChange;
        coordinator.ProfileChanged += adapter.CompleteProfileChange;

        EconomyManager.RaiseMoneyChanged(0, 333);
        adapter.Tick();
        coordinator.Flush();
        var priorGeneration = coordinator.CurrentGenerationId;
        Assert.Equal(EconomyHoldingObservationState.Current,
            coordinator.Current!.Statistics.Holdings.Money.State);
        Assert.Equal(333, coordinator.Current.Statistics.Holdings.Money.Value);

        coordinator.ResetCurrent();
        var resetGeneration = coordinator.CurrentGenerationId;
        Assert.NotEqual(priorGeneration, resetGeneration);
        Assert.False(Saves.SavesSystem.EconomyDataExists);
        Assert.Same(economyManager, EconomyManager.Instance);
        adapter.Tick();
        coordinator.Flush();

        Assert.Equal(EconomyHoldingObservationState.Current,
            coordinator.Current.Statistics.Holdings.Money.State);
        Assert.Equal(333, coordinator.Current.Statistics.Holdings.Money.Value);

        var path = ProfilePath(coordinator.DataRoot, slot: 1);
        var primary = LoadExact(path);
        var backup = LoadExact(AtomicJsonPaths.GetBackupPath(path));
        Assert.Equal(resetGeneration, primary.GenerationId);
        Assert.Equal(EconomyHoldingObservationState.Current,
            primary.Statistics.Holdings.Money.State);
        Assert.Equal(333, primary.Statistics.Holdings.Money.Value);
        Assert.Equal(resetGeneration, backup.GenerationId);
        Assert.Equal(EconomyHoldingObservationState.Current,
            backup.Statistics.Holdings.Money.State);
        Assert.Equal(333, backup.Statistics.Holdings.Money.Value);
    }

    private static ProfileDocument LoadExact(string path)
    {
        var loaded = new AtomicJsonStore<ProfileDocument>().Load(
            path,
            ProfileMigrator.ValidateRecoveryCandidate);
        Assert.NotNull(loaded.Value);
        return loaded.Value;
    }

    private static string ProfilePath(string dataRoot, int slot) => Path.Combine(
        dataRoot,
        "profiles",
        $"slot-{slot:D2}",
        "current",
        "profile.json");

    private static void WriteNativeSave(string root, int slot, long saveTime)
    {
        var path = Path.Combine(root, Saves.SavesSystem.GetFilePath(slot));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, $"{{\"SaveTime\":{{\"value\":{saveTime}}}}}");
    }

    private static void ConfigureReadyRoots()
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
        Duckov.Scenes.SceneLoader.IsSceneLoading = false;
    }

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
        Duckov.Scenes.SceneLoader.ResetNativeState();
    }
}
