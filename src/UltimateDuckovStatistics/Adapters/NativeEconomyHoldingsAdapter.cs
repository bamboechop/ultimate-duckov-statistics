using Duckov.Economy;
using Duckov.Scenes;
using ItemStatsSystem;
using Saves;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UnityEngine;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class NativeEconomyHoldingsAdapter : IDisposable
{
    internal const string AdapterVersion = "native-economy-holdings/2.3.30+authoritative-roots-v1";
    private const string SupportedGameVersion = "2.3.30";
    private const string SupportedGameBuild = "24013657";
    private const string MoneyProvenance =
        "Duckov EconomyManager.Instance and Int64 Money after EconomyData hydration.";
    private const string CashProvenance =
        "Checked identity-safe Cash item 451 sum across hydrated main, PlayerStorage, and PetProxy top-level inventories.";
    private const string LiquidProvenance =
        "Duckov ATM Save/Draw and EconomyManager Pay/IsEnough prove Money and Cash use directly comparable 1:1 units.";

    private readonly Func<string> generationProvider;
    private readonly Func<EconomyHoldingsMutation, bool> observationPublisher;
    private readonly Func<bool, bool, string, bool> stalePublisher;
    private readonly Func<bool, bool, string, bool> unavailablePublisher;
    private readonly Action<IReadOnlyList<CapabilityRecord>, EconomyHoldingsMetricCapabilities> capabilityPublisher;
    private readonly Action<string> diagnostic;
    private readonly EconomyHoldingsObservationGate observationGate = new();
    private Inventory? subscribedPetInventory;
    private EconomyManager? preTransitionEconomyInstance;
    private bool profileChanging;
    private bool moneyHydrationTrusted;
    private bool subscribed;
    private bool disposed;
    private EconomyHoldingsMetricCapabilities metricCapabilities =
        EconomyHoldingsNativeContractPolicy.Unavailable(EconomyHoldingsNativeContractPolicy.BootstrapProvenance);

    public NativeEconomyHoldingsAdapter(
        Func<string> generationProvider,
        Func<EconomyHoldingsMutation, bool> observationPublisher,
        Func<bool, bool, string, bool> stalePublisher,
        Func<bool, bool, string, bool> unavailablePublisher,
        Action<IReadOnlyList<CapabilityRecord>, EconomyHoldingsMetricCapabilities> capabilityPublisher,
        Action<string> diagnostic)
    {
        this.generationProvider = generationProvider ?? throw new ArgumentNullException(nameof(generationProvider));
        this.observationPublisher = observationPublisher ?? throw new ArgumentNullException(nameof(observationPublisher));
        this.stalePublisher = stalePublisher ?? throw new ArgumentNullException(nameof(stalePublisher));
        this.unavailablePublisher = unavailablePublisher ?? throw new ArgumentNullException(nameof(unavailablePublisher));
        this.capabilityPublisher = capabilityPublisher ?? throw new ArgumentNullException(nameof(capabilityPublisher));
        this.diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
    }

    public EconomyHoldingsMetricCapabilities MetricCapabilities => EconomyHoldingsReducer.Clone(metricCapabilities);
    public bool HasPendingObservation => observationGate.HasPending;

    public void Initialize()
    {
        if (disposed) throw new ObjectDisposedException(nameof(NativeEconomyHoldingsAdapter));
        if (subscribed) return;
        var gameVersion = Application.version ?? string.Empty;
        if (!string.Equals(gameVersion, SupportedGameVersion, StringComparison.Ordinal))
        {
            DisableAll(
                $"Installed Duckov version '{gameVersion}' does not match verified holdings contract '{SupportedGameVersion}' build {SupportedGameBuild}.");
            return;
        }
        try
        {
            EconomyManager.OnMoneyChanged += OnMoneyChanged;
            EconomyManager.OnEconomyManagerLoaded += OnEconomyManagerLoaded;
            ItemUtilities.OnPlayerItemOperation += OnPlayerItemOperation;
            CharacterMainControl.OnMainCharacterInventoryChangedEvent += OnMainInventoryChanged;
            PlayerStorage.OnPlayerStorageChange += OnStorageChanged;
            PlayerStorage.OnLoadingFinished += OnStorageLoadingFinished;
            LevelManager.OnLevelBeginInitializing += OnLevelBeginInitializing;
            LevelManager.OnLevelInitialized += OnLevelInitialized;
            LevelManager.OnAfterLevelInitialized += OnAfterLevelInitialized;
            LevelManager.OnControllingCharacterChanged += OnControllingCharacterChanged;
            SceneLoader.onStartedLoadingScene += OnSceneLoadingStarted;
            SceneLoader.onAfterSceneInitialize += OnSceneAfterInitialize;
            subscribed = true;
            metricCapabilities = EconomyHoldingsNativeContractPolicy.Supported(
                MoneyProvenance,
                CashProvenance,
                LiquidProvenance);
            PublishCapabilities();
            moneyHydrationTrusted = EconomyManager.Instance != null && EconomyDataExists();
            ReconcilePetInventorySubscription();
            MarkMoneyDirty();
            MarkCashDirty();
            diagnostic(
                "Economy holdings observer subscribed; event changes coalesce for one tick and Cash enumeration occurs only after all three roots are ready.");
        }
        catch (Exception exception)
        {
            Dispose();
            DisableAll($"Economy holdings activation failed: {exception.GetType().Name}: {exception.Message}");
        }
    }

    public void Tick()
    {
        if (disposed || !subscribed || profileChanging) return;
        observationGate.Advance();
        ReconcilePetInventorySubscription();
        PublishDue(force: false);
    }

    public bool FlushPending()
    {
        if (disposed || !subscribed) return !HasPendingObservation;
        if (profileChanging) return true;
        ReconcilePetInventorySubscription();
        return PublishDue(force: true);
    }

    public void BeginProfileChange()
    {
        if (disposed || !subscribed) return;
        stalePublisher(
            true,
            true,
            "Duckov save generation or UDS profile is changing; the prior live roots are no longer current.");
        profileChanging = true;
        preTransitionEconomyInstance = EconomyManager.Instance;
        moneyHydrationTrusted = false;
        observationGate.Reset();
        UnsubscribePetInventory();
    }

    public void CompleteProfileChange()
    {
        if (disposed || !subscribed) return;
        profileChanging = false;
        moneyHydrationTrusted = EconomyDataExists()
                                || (EconomyManager.Instance != null
                                    && !ReferenceEquals(EconomyManager.Instance, preTransitionEconomyInstance));
        preTransitionEconomyInstance = null;
        ReconcilePetInventorySubscription();
        MarkMoneyDirty();
        MarkCashDirty();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (subscribed)
        {
            EconomyManager.OnMoneyChanged -= OnMoneyChanged;
            EconomyManager.OnEconomyManagerLoaded -= OnEconomyManagerLoaded;
            ItemUtilities.OnPlayerItemOperation -= OnPlayerItemOperation;
            CharacterMainControl.OnMainCharacterInventoryChangedEvent -= OnMainInventoryChanged;
            PlayerStorage.OnPlayerStorageChange -= OnStorageChanged;
            PlayerStorage.OnLoadingFinished -= OnStorageLoadingFinished;
            LevelManager.OnLevelBeginInitializing -= OnLevelBeginInitializing;
            LevelManager.OnLevelInitialized -= OnLevelInitialized;
            LevelManager.OnAfterLevelInitialized -= OnAfterLevelInitialized;
            LevelManager.OnControllingCharacterChanged -= OnControllingCharacterChanged;
            SceneLoader.onStartedLoadingScene -= OnSceneLoadingStarted;
            SceneLoader.onAfterSceneInitialize -= OnSceneAfterInitialize;
            subscribed = false;
        }
        UnsubscribePetInventory();
    }

    private bool PublishDue(bool force)
    {
        long? money = null;
        long? cash = null;
        if (observationGate.IsMoneyDue(force))
        {
            var moneyUnavailable = string.Empty;
            var moneyIncompatible = false;
            if (moneyHydrationTrusted
                && NativeEconomyHoldingsSnapshotBuilder.TryReadMoney(
                    out var observedMoney,
                    out moneyUnavailable,
                    out moneyIncompatible))
            {
                money = observedMoney;
                observationGate.ClearMoney();
            }
            else if (moneyHydrationTrusted && moneyIncompatible)
            {
                DisableMoney(moneyUnavailable);
                observationGate.ClearMoney();
            }
        }
        if (observationGate.IsCashDue(force))
        {
            if (NativeEconomyHoldingsSnapshotBuilder.TryReadCash(
                    out var observedCash,
                    out var cashUnavailable,
                    out var cashIncompatible))
            {
                cash = observedCash;
                observationGate.ClearCash();
            }
            else if (cashIncompatible)
            {
                DisableCash(cashUnavailable);
                observationGate.ClearCash();
            }
        }
        if (!money.HasValue && !cash.HasValue) return true;
        var generation = generationProvider();
        if (string.IsNullOrWhiteSpace(generation)) return false;
        var published = observationPublisher(new EconomyHoldingsMutation(
            generation,
            DateTime.UtcNow,
            money,
            cash,
            "event-coalesced authoritative native observation"));
        if (published) NativeHotPathDiagnostics.CountEconomyHoldingsPublication();
        if (!published)
        {
            if (money.HasValue) MarkMoneyDirty();
            if (cash.HasValue) MarkCashDirty();
        }
        return published;
    }

    private void OnMoneyChanged(long oldValue, long newValue)
    {
        if (disposed || !subscribed || profileChanging) return;
        moneyHydrationTrusted = true;
        MarkMoneyDirty();
    }

    private void OnEconomyManagerLoaded()
    {
        if (disposed || !subscribed) return;
        moneyHydrationTrusted = EconomyDataExists()
                                || (preTransitionEconomyInstance != null
                                    && EconomyManager.Instance != null
                                    && !ReferenceEquals(EconomyManager.Instance, preTransitionEconomyInstance));
        if (!profileChanging) MarkMoneyDirty();
    }

    private void OnPlayerItemOperation() { if (!profileChanging) MarkCashDirty(); }
    private void OnMainInventoryChanged(CharacterMainControl character, Inventory inventory, int index)
    {
        if (!profileChanging && character != null && character.IsMainCharacter
            && ReferenceEquals(character, CharacterMainControl.Main)) MarkCashDirty();
    }
    private void OnStorageChanged(PlayerStorage storage, Inventory inventory, int index) { if (!profileChanging) MarkCashDirty(); }
    private void OnStorageLoadingFinished() { if (!profileChanging) MarkCashDirty(); }
    private void OnPetInventoryChanged(Inventory inventory, int index) { if (!profileChanging) MarkCashDirty(); }
    private void OnControllingCharacterChanged(CharacterMainControl character)
    {
        if (profileChanging) return;
        ReconcilePetInventorySubscription();
        MarkCashDirty();
    }

    private void OnLevelBeginInitializing()
    {
        if (profileChanging) return;
        stalePublisher(true, true, "Duckov level initialization invalidated the previously observed native roots.");
        observationGate.ClearCash();
        UnsubscribePetInventory();
    }

    private void OnLevelInitialized() { if (!profileChanging) MarkCashDirty(); }
    private void OnAfterLevelInitialized()
    {
        if (profileChanging) return;
        ReconcilePetInventorySubscription();
        MarkMoneyDirty();
        MarkCashDirty();
    }

    private void OnSceneLoadingStarted(SceneLoadingContext context)
    {
        if (profileChanging) return;
        stalePublisher(true, true, "Duckov scene loading invalidated the previously observed native roots.");
        observationGate.Reset();
        UnsubscribePetInventory();
    }

    private void OnSceneAfterInitialize(SceneLoadingContext context)
    {
        if (profileChanging) return;
        ReconcilePetInventorySubscription();
        MarkMoneyDirty();
        MarkCashDirty();
    }

    private void MarkMoneyDirty()
    {
        if (metricCapabilities.Money.State != Core.Domain.AdapterCapabilityState.Supported) return;
        observationGate.SignalMoney();
        NativeHotPathDiagnostics.CountEconomyHoldingsDirtySignal();
    }

    private void MarkCashDirty()
    {
        if (metricCapabilities.Cash.State != Core.Domain.AdapterCapabilityState.Supported) return;
        observationGate.SignalCash();
        NativeHotPathDiagnostics.CountEconomyHoldingsDirtySignal();
    }

    private void ReconcilePetInventorySubscription()
    {
        var current = PetProxy.PetInventory;
        if (ReferenceEquals(current, subscribedPetInventory)) return;
        UnsubscribePetInventory();
        subscribedPetInventory = current;
        if (subscribedPetInventory != null) subscribedPetInventory.onContentChanged += OnPetInventoryChanged;
    }

    private void UnsubscribePetInventory()
    {
        if (subscribedPetInventory != null)
            subscribedPetInventory.onContentChanged -= OnPetInventoryChanged;
        subscribedPetInventory = null;
    }

    private void DisableCash(string provenance)
    {
        var updated = EconomyHoldingsReducer.Clone(metricCapabilities);
        updated.Cash = EconomyHoldingsNativeContractPolicy.Availability(
            Core.Domain.AdapterCapabilityState.DisabledIncompatible,
            provenance);
        updated.LiquidWealth = EconomyHoldingsNativeContractPolicy.Availability(
            Core.Domain.AdapterCapabilityState.DisabledIncompatible,
            provenance);
        metricCapabilities = updated;
        unavailablePublisher(false, true, provenance);
        PublishCapabilities();
        diagnostic($"Cash holdings disabled independently: {provenance}");
    }

    private void DisableMoney(string provenance)
    {
        var updated = EconomyHoldingsReducer.Clone(metricCapabilities);
        updated.Money = EconomyHoldingsNativeContractPolicy.Availability(
            Core.Domain.AdapterCapabilityState.DisabledIncompatible,
            provenance);
        updated.LiquidWealth = EconomyHoldingsNativeContractPolicy.Availability(
            Core.Domain.AdapterCapabilityState.DisabledIncompatible,
            provenance);
        metricCapabilities = updated;
        unavailablePublisher(true, false, provenance);
        PublishCapabilities();
        diagnostic($"Money holdings disabled independently: {provenance}");
    }

    private void DisableAll(string provenance)
    {
        metricCapabilities = EconomyHoldingsNativeContractPolicy.Unavailable(provenance);
        unavailablePublisher(true, true, provenance);
        PublishCapabilities();
        diagnostic(provenance);
    }

    private void PublishCapabilities() => capabilityPublisher(
        EconomyHoldingsNativeContractPolicy.ToRecords(metricCapabilities, AdapterVersion),
        metricCapabilities);

    private static bool EconomyDataExists()
    {
        try { return SavesSystem.KeyExisits("EconomyData"); }
        catch { return false; }
    }
}
