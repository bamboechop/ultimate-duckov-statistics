using Duckov.Economy;
using Duckov.Quests;
using Duckov.Quests.Rewards;
using ItemStatsSystem;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UnityEngine;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class NativeEconomyAdapter : IDisposable
{
    internal const string AdapterVersion = "native-economy/2.3.30+public-events-v7";
    private const string SupportedGameVersion = "2.3.30";
    private const string SupportedGameBuild = "24013657";
    private const int CashItemTypeId = EconomyManager.CashItemID;
    private const int MaximumTransientItemIds = 512;
    private const int MaximumPendingMoneyFlows = 512;
    private readonly Func<string> generationProvider;
    private readonly Func<string?> runProvider;
    private readonly Func<string?> mapProvider;
    private readonly Func<string?> segmentProvider;
    private readonly Func<bool> runActiveProvider;
    private readonly Func<CurrencyFlowRecorded, bool> publisher;
    private readonly Action<IReadOnlyList<Core.Persistence.CapabilityRecord>> capabilityPublisher;
    private readonly Action<string> diagnostic;
    private readonly Func<bool> publicationGate;
    private readonly string activationId = Guid.NewGuid().ToString("N");
    private readonly List<PendingMoneyFlow> pendingMoney = new();
    private readonly Queue<int> pendingPickupIds = new();
    private readonly Dictionary<int, long> playerOriginatedCashAmounts = new();
    private Dictionary<int, long> ownedCashAmounts = new();
    private Dictionary<int, Item> ownedCashItems = new();
    private Inventory? subscribedPetInventory;
    private long eventSequence;
    private long lastMoneyBalance;
    private long lastMoneyOldValue;
    private long lastMoneyNewValue;
    private long cashBaseline;
    private long playerOriginatedCashOutsideOwned;
    private long pendingCompletedCashCostAmount;
    private int? pendingCashSaleAmount;
    private bool pendingCashSaleMatched;
    private bool pendingCashSaleObservedOwnershipMutation;
    private bool cashBaselineReady;
    private bool cashBaselineSuspended;
    private bool cashDirty;
    private bool moneyDisabled;
    private bool moneyBalanceObserved;
    private bool moneySemanticCorrelationDisabled;
    private bool cashDisabled;
    private bool cashAcquisitionDisabled;
    private ObservationContext? cashObservationContext;
    private bool subscribed;
    private bool disposed;

    public NativeEconomyAdapter(
        Func<string> generationProvider,
        Func<string?> runProvider,
        Func<string?> mapProvider,
        Func<string?> segmentProvider,
        Func<bool> runActiveProvider,
        Func<CurrencyFlowRecorded, bool> publisher,
        Action<IReadOnlyList<Core.Persistence.CapabilityRecord>> capabilityPublisher,
        Action<string> diagnostic,
        Func<bool>? publicationGate = null)
    {
        this.generationProvider = generationProvider ?? throw new ArgumentNullException(nameof(generationProvider));
        this.runProvider = runProvider ?? throw new ArgumentNullException(nameof(runProvider));
        this.mapProvider = mapProvider ?? throw new ArgumentNullException(nameof(mapProvider));
        this.segmentProvider = segmentProvider ?? throw new ArgumentNullException(nameof(segmentProvider));
        this.runActiveProvider = runActiveProvider ?? throw new ArgumentNullException(nameof(runActiveProvider));
        this.publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        this.capabilityPublisher = capabilityPublisher ?? throw new ArgumentNullException(nameof(capabilityPublisher));
        this.diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
        this.publicationGate = publicationGate ?? (() => true);
    }

    public EconomyMetricCapabilities MetricCapabilities { get; private set; } =
        EconomyNativeContractPolicy.Unavailable("Economy adapter has not been initialized.");

    internal string ActivationId => activationId;

    public void Initialize()
    {
        if (subscribed) { diagnostic("Duplicate economy adapter setup ignored."); return; }
        var gameVersion = Application.version ?? string.Empty;
        if (!string.Equals(gameVersion, SupportedGameVersion, StringComparison.Ordinal))
        {
            Disable($"Installed Duckov version {gameVersion} does not match verified economy contract {SupportedGameVersion} build {SupportedGameBuild}.");
            return;
        }

        EconomyManager.OnMoneyChanged += OnMoneyChanged;
        EconomyManager.OnMoneyPaid += OnMoneyPaid;
        EconomyManager.OnEconomyManagerLoaded += OnEconomyManagerLoaded;
        EconomyManager.OnCostPaid += OnCostPaid;
        StockShop.OnItemSoldByPlayer += OnItemSoldByPlayer;
        Reward.OnRewardClaimed += OnRewardClaimed;
        InteractablePickup.OnPickupSuccess += OnPickupSuccess;
        ItemUtilities.OnPlayerItemOperation += OnPlayerItemOperation;
        CharacterMainControl.OnMainCharacterInventoryChangedEvent += OnMainInventoryChanged;
        PlayerStorage.OnPlayerStorageChange += OnStorageChanged;
        LevelManager.OnLevelBeginInitializing += OnLevelBeginInitializing;
        LevelManager.OnAfterLevelInitialized += OnAfterLevelInitialized;
        LevelManager.OnControllingCharacterChanged += OnControllingCharacterChanged;
        subscribed = true;
        ResetBaselines();
        ReconcilePetInventorySubscription();
        MetricCapabilities = CreateSupportedCapabilities();
        PublishCapabilities();
        diagnostic($"Economy adapter subscribed using public Duckov events; Cash item type={CashItemTypeId}.");
    }

    public void Tick()
    {
        if (disposed || !subscribed) return;
        FlushPendingMoney();
        FlushCash();
    }

    public void FlushPendingForBoundary()
    {
        if (disposed || !subscribed) return;
        FlushPendingMoney();
        FlushCash();
    }

    private void FlushPendingMoney()
    {
        if (pendingMoney.Count == 0 || !PublicationReady()) return;
        var ready = pendingMoney.ToArray();
        pendingMoney.Clear();
        foreach (var flow in ready) Publish(CreateMoneyEvent(flow, "money"));
    }

    private bool FlushCash()
    {
        if (cashDisabled || cashBaselineSuspended) return false;
        if (!cashDirty && cashBaselineReady) return true;
        if (cashBaselineReady && !PublicationReady()) return false;
        cashDirty = false;
        var observationContext = cashObservationContext ?? CaptureContext();
        cashObservationContext = null;
        CashSnapshot snapshot;
        try { snapshot = ReadCashSnapshot(); }
        catch (Exception exception)
        {
            DisableCash($"Physical Cash ownership scan failed: {exception.GetType().Name}.");
            return false;
        }
        if (!cashBaselineReady)
        {
            cashBaseline = snapshot.Total;
            ownedCashAmounts = snapshot.ItemAmounts;
            ownedCashItems = snapshot.Items;
            cashBaselineReady = true;
            pendingPickupIds.Clear();
            pendingCompletedCashCostAmount = 0;
            return false;
        }
        if (pendingCashSaleAmount.HasValue
            && (snapshot.Total != cashBaseline || !CashOwnershipEqual(snapshot.ItemAmounts, ownedCashAmounts)))
            pendingCashSaleObservedOwnershipMutation = true;
        var playerOriginatedRemovedAmount = snapshot.Total != cashBaseline
            ? PublishCashDelta(snapshot, observationContext)
            : 0;
        if (playerOriginatedRemovedAmount > 0) ObserveRemovedCashIds(snapshot.ItemAmounts);
        cashBaseline = snapshot.Total;
        ownedCashAmounts = snapshot.ItemAmounts;
        ownedCashItems = snapshot.Items;
        pendingPickupIds.Clear();
        pendingCompletedCashCostAmount = 0;
        return true;
    }

    public void ResetBaselines()
    {
        pendingMoney.Clear();
        moneyBalanceObserved = false;
        lastMoneyBalance = 0;
        lastMoneyOldValue = 0;
        lastMoneyNewValue = 0;
        ResetCashBaseline();
    }

    private void ResetCashBaseline()
    {
        pendingPickupIds.Clear();
        pendingCashSaleAmount = null;
        pendingCashSaleMatched = false;
        pendingCashSaleObservedOwnershipMutation = false;
        pendingCompletedCashCostAmount = 0;
        cashBaseline = 0;
        cashBaselineReady = false;
        cashDirty = true;
        cashObservationContext = null;
        ownedCashAmounts.Clear();
        ownedCashItems.Clear();
        playerOriginatedCashAmounts.Clear();
        playerOriginatedCashOutsideOwned = 0;
    }

    public void Dispose()
    {
        if (disposed) return;
        try
        {
            FlushPendingForBoundary();
        }
        catch (Exception exception)
        {
            diagnostic($"Economy adapter could not publish all pending flows during cleanup: {exception.GetType().Name}.");
        }
        disposed = true;
        if (subscribed)
        {
            EconomyManager.OnMoneyChanged -= OnMoneyChanged;
            EconomyManager.OnMoneyPaid -= OnMoneyPaid;
            EconomyManager.OnEconomyManagerLoaded -= OnEconomyManagerLoaded;
            EconomyManager.OnCostPaid -= OnCostPaid;
            StockShop.OnItemSoldByPlayer -= OnItemSoldByPlayer;
            Reward.OnRewardClaimed -= OnRewardClaimed;
            InteractablePickup.OnPickupSuccess -= OnPickupSuccess;
            ItemUtilities.OnPlayerItemOperation -= OnPlayerItemOperation;
            CharacterMainControl.OnMainCharacterInventoryChangedEvent -= OnMainInventoryChanged;
            PlayerStorage.OnPlayerStorageChange -= OnStorageChanged;
            LevelManager.OnLevelBeginInitializing -= OnLevelBeginInitializing;
            LevelManager.OnAfterLevelInitialized -= OnAfterLevelInitialized;
            LevelManager.OnControllingCharacterChanged -= OnControllingCharacterChanged;
            subscribed = false;
        }
        if (subscribedPetInventory != null) subscribedPetInventory.onContentChanged -= OnPetInventoryChanged;
        subscribedPetInventory = null;
        pendingMoney.Clear();
    }

    private void OnMoneyChanged(long oldValue, long newValue)
    {
        if (disposed || !subscribed) return;
        // Duckov Pay(Cost) publishes this callback before it consumes any
        // physical Cash for Cost.money. Drain earlier drops at that exact
        // boundary so the later OnMoneyPaid scan contains only the spend.
        FlushCash();
        if (moneyDisabled || oldValue == newValue) return;
        if (oldValue < 0 || newValue < 0)
        {
            DisableMoney("Duckov reported a negative Money balance; exact normalized direction is unavailable.");
            return;
        }
        if (moneyBalanceObserved && oldValue != lastMoneyBalance)
        {
            if (oldValue == lastMoneyOldValue && newValue == lastMoneyNewValue)
            {
                diagnostic("Duplicate Money balance callback ignored without creating another flow.");
                return;
            }
            DisableMoney("Duckov reported a discontinuous Money balance callback; later exact deltas cannot be proven.");
            return;
        }
        moneyBalanceObserved = true;
        lastMoneyBalance = newValue;
        lastMoneyOldValue = oldValue;
        lastMoneyNewValue = newValue;
        var direction = newValue > oldValue ? CurrencyFlowDirection.Inflow : CurrencyFlowDirection.Outflow;
        var amount = direction == CurrencyFlowDirection.Inflow ? newValue - oldValue : oldValue - newValue;
        if (amount <= 0) { DisableMoney("Duckov Money delta overflowed or was not positive."); return; }
        var context = CaptureContext();
        if (pendingMoney.Count >= MaximumPendingMoneyFlows)
        {
            if (!PublicationReady())
            {
                DisableMoneyWhilePublicationBlocked(
                    $"The defensive {MaximumPendingMoneyFlows}-flow pending bound was reached while economy activation persistence was unavailable; later Money capture is disabled instead of discarding an unpublished exact flow.");
                return;
            }
            DisableMoneySemanticCorrelation(
                $"The defensive {MaximumPendingMoneyFlows}-flow semantic correlation bound was reached; exact Money amount/direction remains available but source-specific attribution is disabled.");
            var oldest = pendingMoney[0];
            pendingMoney.RemoveAt(0);
            Publish(CreateMoneyEvent(oldest, "money-bounded"));
        }
        pendingMoney.Add(new PendingMoneyFlow(amount, direction, context));
    }

    private void OnMoneyPaid(long amount)
    {
        if (disposed || !subscribed || amount <= 0) return;
        if (!cashAcquisitionDisabled)
        {
            if (pendingCompletedCashCostAmount > long.MaxValue - amount)
            {
                DisableCashAcquisition("Duckov reported an invalid completed Cost.money payment; drop/re-pickup acquisition attribution is disabled.");
            }
            else
            {
                // The public event follows Money-first payment and physical
                // Cash consumption. The observed Cash delta is therefore at
                // most this amount and is wholly player spending.
                pendingCompletedCashCostAmount += amount;
            }
        }
        MarkCashDirty();
        FlushCash();
    }

    private void OnRewardClaimed(Reward reward)
    {
        if (disposed || !subscribed) return;
        if (moneySemanticCorrelationDisabled) return;
        if (reward is not QuestReward_Money moneyReward || moneyReward.Amount <= 0) return;
        var pending = FindPendingMoney(CurrencyFlowDirection.Inflow, moneyReward.Amount);
        if (pending == null) return;
        pending.Source = CurrencySourceCategory.Reward;
        pending.Context = GameplayContext.Reward;
        pending.NativeSourceId = $"duckov:quest-reward-money:{reward.ID}";
        pending.SourceDisplayName = reward.Description;
    }

    private void OnItemSoldByPlayer(StockShop shop, Item item, int sellPrice)
    {
        if (disposed || !subscribed) return;
        if (sellPrice <= 0) return;
        pendingCashSaleAmount = sellPrice;
        pendingCashSaleMatched = false;
        pendingCashSaleObservedOwnershipMutation = false;
        // StockShop.Sell awaits a physical-Cash return before publishing this
        // callback. Observe Cash first so an unrelated same-amount Money delta
        // cannot claim the sale correlation.
        MarkCashDirty();
        var cashObservationCompleted = FlushCash();
        if (!pendingCashSaleMatched
            && !pendingCashSaleObservedOwnershipMutation
            && cashObservationCompleted
            && !moneySemanticCorrelationDisabled)
        {
            var money = FindPendingMoney(CurrencyFlowDirection.Inflow, sellPrice);
            if (money != null)
            {
                money.Source = CurrencySourceCategory.Sale;
                money.Context = GameplayContext.Shop;
                money.NativeSourceId = $"duckov:merchant:{shop.MerchantID}";
                money.SourceDisplayName = shop.DisplayName;
            }
        }
        ClearPendingCashSale();
    }

    private void OnCostPaid(Cost cost)
    {
        if (disposed || !subscribed) return;
        if (!cashAcquisitionDisabled)
        {
            try
            {
                foreach (var entry in cost.items ?? Array.Empty<Cost.ItemEntry>())
                {
                    if (entry.id != CashItemTypeId) continue;
                    if (entry.amount < 0 || pendingCompletedCashCostAmount > long.MaxValue - entry.amount)
                    {
                        DisableCashAcquisition("Duckov reported an invalid completed Cash cost; drop/re-pickup acquisition attribution is disabled.");
                        break;
                    }
                    pendingCompletedCashCostAmount += entry.amount;
                }
            }
            catch (Exception exception)
            {
                DisableCashAcquisition($"Duckov completed Cash cost inspection failed: {exception.GetType().Name}.");
            }
        }
        MarkCashDirty();
        FlushCash();
    }

    private void OnPickupSuccess(InteractablePickup pickup, CharacterMainControl character)
    {
        if (disposed || character == null || !character.IsMainCharacter
            || !ReferenceEquals(character, CharacterMainControl.Main) || !runActiveProvider()) return;
        var item = pickup?.ItemAgent?.Item;
        if (ReferenceEquals(item, null) || item.TypeID != CashItemTypeId) return;
        if (!cashAcquisitionDisabled)
        {
            if (pendingPickupIds.Count >= MaximumTransientItemIds)
            {
                DisableCashAcquisition(
                    $"The defensive {MaximumTransientItemIds}-pickup correlation bound was reached; exact Cash amount/direction remains available but external acquisition attribution is disabled.");
            }
            else
            {
                pendingPickupIds.Enqueue(item.GetInstanceID());
            }
        }
        MarkCashDirty();
        FlushCash();
    }

    private long PublishCashDelta(CashSnapshot snapshot, ObservationContext observationContext)
    {
        var newTotal = snapshot.Total;
        if (newTotal > cashBaseline)
        {
            var amount = newTotal - cashBaseline;
            if (pendingCashSaleAmount.HasValue && pendingCashSaleAmount.Value == amount)
            {
                pendingCashSaleMatched = true;
                PublishCash(amount, CurrencyFlowDirection.Inflow, CurrencySourceCategory.Sale, GameplayContext.Shop, false, "duckov:stock-shop-sale", observationContext);
                return 0;
            }
            var hadPickup = !cashAcquisitionDisabled
                            && pendingPickupIds.Count > 0
                            && !string.IsNullOrWhiteSpace(observationContext.RunId);
            // AddAndMerge can consume the picked item before this snapshot, so its
            // last exact owned amount is retained against the runtime identity.
            var repicked = hadPickup
                ? Math.Min(
                    amount,
                    Math.Min(
                        playerOriginatedCashOutsideOwned,
                        SaturatingSum(pendingPickupIds
                            .Distinct()
                            .Select(id => playerOriginatedCashAmounts.TryGetValue(id, out var stack) ? stack : 0))))
                : 0;
            if (repicked > 0)
            {
                playerOriginatedCashOutsideOwned -= repicked;
                PublishCash(repicked, CurrencyFlowDirection.Inflow, CurrencySourceCategory.UnknownAdjustment, GameplayContext.Raid, false, "duckov:cash-repickup", observationContext);
            }
            var remainder = amount - repicked;
            if (remainder > 0)
                PublishCash(
                    remainder,
                    CurrencyFlowDirection.Inflow,
                    hadPickup ? CurrencySourceCategory.LootOrPickup : CurrencySourceCategory.UnknownAdjustment,
                    observationContext.GameplayContext,
                    hadPickup,
                    hadPickup ? "duckov:world-pickup" : null,
                    observationContext);
            return 0;
        }
        var outflow = cashBaseline - newTotal;
        if (outflow <= 0) return 0;
        var playerOriginatedOutflow = 0L;
        if (!cashAcquisitionDisabled && !string.IsNullOrWhiteSpace(observationContext.RunId))
        {
            playerOriginatedOutflow = Math.Max(0, outflow - Math.Min(outflow, pendingCompletedCashCostAmount));
            playerOriginatedCashOutsideOwned = SaturatingAdd(playerOriginatedCashOutsideOwned, playerOriginatedOutflow);
        }
        PublishCash(outflow, CurrencyFlowDirection.Outflow, CurrencySourceCategory.UnknownAdjustment, observationContext.GameplayContext, false, null, observationContext);
        return playerOriginatedOutflow;
    }

    private void PublishCash(long amount, CurrencyFlowDirection direction, CurrencySourceCategory source, GameplayContext context, bool acquisition, string? sourceId, ObservationContext observationContext)
    {
        var identity = CreateEventIdentity("cash");
        Publish(new CurrencyFlowRecorded
        {
            EventId = identity.EventId,
            TimestampUtc = observationContext.TimestampUtc,
            SaveGenerationId = observationContext.GenerationId,
            RunId = observationContext.RunId,
            SegmentId = observationContext.SegmentId,
            MapId = observationContext.MapId,
            Currency = CurrencyKind.Cash,
            Direction = direction,
            Amount = amount,
            Source = source,
            NativeSourceId = sourceId,
            GameplayContext = context,
            IntegrityTags = observationContext.IntegrityTags,
            GameVersion = Application.version ?? string.Empty,
            GameBuild = SupportedGameBuild,
            AdapterVersion = AdapterVersion,
            ProvenExternalRaidAcquisition = acquisition,
            ProducerActivationId = activationId,
            ProducerSequence = identity.Sequence
        });
    }

    private void Publish(CurrencyFlowRecorded value)
    {
        if (string.IsNullOrWhiteSpace(value.SaveGenerationId) || value.Amount <= 0) return;
        publisher(value);
    }

    private bool PublicationReady()
    {
        try { return publicationGate(); }
        catch (Exception exception)
        {
            diagnostic($"Economy publication gate failed safely: {exception.GetType().Name}.");
            return false;
        }
    }

    private PendingMoneyFlow? FindPendingMoney(CurrencyFlowDirection direction, long amount) =>
        pendingMoney.LastOrDefault(flow => flow.Direction == direction && flow.Amount == amount && flow.Source == CurrencySourceCategory.UnknownAdjustment);

    private void ClearPendingCashSale()
    {
        pendingCashSaleAmount = null;
        pendingCashSaleMatched = false;
        pendingCashSaleObservedOwnershipMutation = false;
    }

    private static bool CashOwnershipEqual(
        Dictionary<int, long> left,
        Dictionary<int, long> right) =>
        left.Count == right.Count
        && left.All(entry => right.TryGetValue(entry.Key, out var amount) && amount == entry.Value);

    private static CashSnapshot ReadCashSnapshot()
    {
        var total = 0L;
        var amounts = new Dictionary<int, long>();
        var items = new Dictionary<int, Item>();
        foreach (var item in ItemUtilities.FindAllBelongsToPlayer(candidate => candidate != null && candidate.TypeID == CashItemTypeId))
        {
            if (item.StackCount < 0) throw new InvalidOperationException("Cash stack count was negative.");
            var id = item.GetInstanceID();
            if (amounts.TryGetValue(id, out var existing))
            {
                if (existing != item.StackCount)
                    throw new InvalidOperationException("One Cash item identity reported conflicting stack counts during a single ownership scan.");
                continue;
            }

            amounts.Add(id, item.StackCount);
            items.Add(id, item);
            total = SaturatingAdd(total, item.StackCount);
        }
        return new CashSnapshot(total, amounts, items);
    }

    private void ObserveRemovedCashIds(Dictionary<int, long> current)
    {
        if (cashAcquisitionDisabled) return;
        foreach (var entry in ownedCashAmounts.Where(entry => !current.ContainsKey(entry.Key)))
        {
            if (ownedCashItems.TryGetValue(entry.Key, out var removedItem)
                && (removedItem == null || removedItem.IsBeingDestroyed))
            {
                // ConsumeItems calls MarkDestroyed before OnCostPaid. A live
                // vanished item can still be a player drop; a destroyed one
                // cannot be re-picked and must not consume the identity bound.
                playerOriginatedCashAmounts.Remove(entry.Key);
                continue;
            }
            if (playerOriginatedCashAmounts.ContainsKey(entry.Key))
            {
                playerOriginatedCashAmounts[entry.Key] = entry.Value;
                continue;
            }
            if (playerOriginatedCashAmounts.Count >= MaximumTransientItemIds)
            {
                DisableCashAcquisition(
                    $"The defensive {MaximumTransientItemIds}-item drop/re-pickup correlation bound was reached; exact Cash amount/direction remains available but external acquisition attribution is disabled.");
                return;
            }
            playerOriginatedCashAmounts.Add(entry.Key, entry.Value);
        }
    }

    private void OnEconomyManagerLoaded()
    {
        if (disposed || !subscribed) return;
        ClearPendingCashSale();
        FlushPendingMoney();
        ResetBaselines();
        cashBaselineSuspended = true;
    }
    private void OnPlayerItemOperation() { if (!disposed && subscribed) MarkCashDirty(); }
    private void OnMainInventoryChanged(CharacterMainControl character, Inventory inventory, int index) { if (!disposed && subscribed && character != null && character.IsMainCharacter && ReferenceEquals(character, CharacterMainControl.Main)) MarkCashDirty(); }
    private void OnStorageChanged(PlayerStorage storage, Inventory inventory, int index) { if (!disposed && subscribed) MarkCashDirty(); }
    private void OnPetInventoryChanged(Inventory inventory, int index) { if (!disposed && subscribed) MarkCashDirty(); }
    private void OnLevelBeginInitializing()
    {
        if (disposed || !subscribed) return;
        FlushPendingForBoundary();
        ResetCashBaseline();
        cashBaselineSuspended = true;
    }

    private void OnAfterLevelInitialized()
    {
        if (disposed || !subscribed) return;
        FlushPendingMoney();
        if (!cashBaselineSuspended && cashBaselineReady) FlushCash();
        ReconcilePetInventorySubscription();
        ResetBaselines();
        cashBaselineSuspended = false;
        FlushCash();
    }
    private void OnControllingCharacterChanged(CharacterMainControl character) { if (!disposed && subscribed) { ReconcilePetInventorySubscription(); MarkCashDirty(); } }

    private void ReconcilePetInventorySubscription()
    {
        var current = PetProxy.PetInventory;
        if (ReferenceEquals(current, subscribedPetInventory)) return;
        if (subscribedPetInventory != null) subscribedPetInventory.onContentChanged -= OnPetInventoryChanged;
        subscribedPetInventory = current;
        if (subscribedPetInventory != null) subscribedPetInventory.onContentChanged += OnPetInventoryChanged;
    }

    private string? CurrentRun() => runActiveProvider() ? runProvider() : null;
    private string? CurrentSegment() => runActiveProvider() ? segmentProvider() : null;
    private string CurrentMap() => runActiveProvider() ? mapProvider() ?? MapIdentity.UnknownId : MapIdentity.UnknownId;
    private GameplayContext CurrentGameplayContext() => runActiveProvider() ? GameplayContext.Raid : GameplayContext.Base;
    private ObservationContext CaptureContext() => new(
        generationProvider(), CurrentRun(), CurrentSegment(), CurrentMap(), CurrentGameplayContext(), DateTime.UtcNow, NativeIntegrityProbe.Read());
    private void MarkCashDirty()
    {
        if (cashDisabled) return;
        if (!cashDirty) cashObservationContext = CaptureContext();
        cashDirty = true;
    }
    private CurrencyFlowRecorded CreateMoneyEvent(PendingMoneyFlow flow, string kind)
    {
        var identity = CreateEventIdentity(kind);
        return flow.ToEvent(identity.EventId, activationId, identity.Sequence);
    }

    private (string EventId, long Sequence) CreateEventIdentity(string kind)
    {
        if (string.IsNullOrWhiteSpace(kind) || kind.Contains(':'))
            throw new ArgumentException("An economy event kind must be non-empty and contain no separator.", nameof(kind));
        eventSequence = checked(eventSequence + 1);
        return ($"economy:{activationId}:{kind}:{eventSequence}", eventSequence);
    }

    private static EconomyMetricCapabilities CreateSupportedCapabilities()
    {
        var publicEvents = $"Duckov {SupportedGameVersion} build {SupportedGameBuild} public economy and owned-inventory events ({AdapterVersion}).";
        return new EconomyMetricCapabilities
        {
            MoneyAmountDirection = EconomyNativeContractPolicy.Availability(AdapterCapabilityState.Supported, publicEvents + " OnMoneyChanged emits exact old/new balances after mutation; load writes the field directly."),
            MoneySourceAttribution = EconomyNativeContractPolicy.Availability(AdapterCapabilityState.Experimental, publicEvents + " Completed StockShop sales and QuestReward_Money claims are semantic; purchases, fees, crafting, conversion, and other changes remain UnknownAdjustment."),
            MoneyContextAttribution = EconomyNativeContractPolicy.Availability(AdapterCapabilityState.Experimental, publicEvents + " Reward and sale contexts are semantic; other non-raid changes use Base and remain source-independent."),
            CashAmountDirection = EconomyNativeContractPolicy.Availability(AdapterCapabilityState.Supported, publicEvents + " Cash is item type 451; event-coalesced totals span storage, main inventory, and pet inventory, while full-scene inventory hydration is baselined only after level initialization completes."),
            CashExternalAcquisition = EconomyNativeContractPolicy.Availability(AdapterCapabilityState.Experimental, publicEvents + " successful exact-main world pickup plus owned-total delta, with bounded player-originated drop/re-pickup item-identity and last-owned-amount exclusion that remains exact when AddAndMerge consumes the picked item; verified OnMoneyPaid, OnCostPaid, and Item.IsBeingDestroyed boundaries exclude completed Cost.money and Cost.items Cash spending, including coalesced full-stack removal; corpse/container transfers remain exact UnknownAdjustment flows."),
            CashContextAttribution = EconomyNativeContractPolicy.Availability(AdapterCapabilityState.Supported, publicEvents + " context is captured at the accepted owned-total delta boundary."),
            CashTerminalOutcomes = EconomyNativeContractPolicy.Availability(AdapterCapabilityState.DisabledIncompatible, "Cash acquisition is supported, but installed-game public events do not prove terminal disposition across fungible main, pet, and storage ownership; acquired amounts remain unresolved."),
            RouteAttribution = EconomyNativeContractPolicy.Availability(AdapterCapabilityState.Supported, publicEvents + " active run/map/segment identity is captured at event time; route loss degrades only segment attribution.")
        };
    }

    private void Disable(string reason) { MetricCapabilities = EconomyNativeContractPolicy.Unavailable(reason); PublishCapabilities(); diagnostic(reason); }
    private void DisableMoney(string reason)
    {
        moneyDisabled = true;
        var canPublishPending = pendingMoney.Count == 0 || PublicationReady();
        var accepted = canPublishPending ? pendingMoney.ToArray() : Array.Empty<PendingMoneyFlow>();
        if (canPublishPending) pendingMoney.Clear();
        MetricCapabilities.MoneyAmountDirection = EconomyNativeContractPolicy.Availability(AdapterCapabilityState.DisabledIncompatible, reason);
        MetricCapabilities.MoneySourceAttribution = EconomyNativeContractPolicy.Availability(AdapterCapabilityState.DisabledIncompatible, reason);
        MetricCapabilities.MoneyContextAttribution = EconomyNativeContractPolicy.Availability(AdapterCapabilityState.DisabledIncompatible, reason);
        PublishCapabilities(); diagnostic(reason);
        foreach (var flow in accepted) Publish(CreateMoneyEvent(flow, "money-before-disable"));
    }
    private void DisableMoneyWhilePublicationBlocked(string reason)
    {
        moneyDisabled = true;
        MetricCapabilities.MoneyAmountDirection = EconomyNativeContractPolicy.Availability(
            AdapterCapabilityState.DisabledIncompatible,
            reason);
        MetricCapabilities.MoneySourceAttribution = EconomyNativeContractPolicy.Availability(
            AdapterCapabilityState.DisabledIncompatible,
            reason);
        MetricCapabilities.MoneyContextAttribution = EconomyNativeContractPolicy.Availability(
            AdapterCapabilityState.DisabledIncompatible,
            reason);
        PublishCapabilities();
        diagnostic(reason);
    }
    private void DisableMoneySemanticCorrelation(string reason)
    {
        if (moneySemanticCorrelationDisabled) return;
        moneySemanticCorrelationDisabled = true;
        MetricCapabilities.MoneySourceAttribution = EconomyNativeContractPolicy.Availability(
            AdapterCapabilityState.DisabledIncompatible,
            reason);
        MetricCapabilities.MoneyContextAttribution = EconomyNativeContractPolicy.Availability(
            AdapterCapabilityState.DisabledIncompatible,
            reason);
        PublishCapabilities();
        diagnostic(reason);
    }
    private void DisableCash(string reason)
    {
        cashDisabled = true;
        cashDirty = false;
        cashObservationContext = null;
        pendingPickupIds.Clear();
        ClearPendingCashSale();
        pendingCompletedCashCostAmount = 0;
        MetricCapabilities.CashAmountDirection = EconomyNativeContractPolicy.Availability(AdapterCapabilityState.DisabledIncompatible, reason);
        MetricCapabilities.CashExternalAcquisition = EconomyNativeContractPolicy.Availability(AdapterCapabilityState.DisabledIncompatible, reason);
        MetricCapabilities.CashContextAttribution = EconomyNativeContractPolicy.Availability(AdapterCapabilityState.DisabledIncompatible, reason);
        MetricCapabilities.CashTerminalOutcomes = EconomyNativeContractPolicy.Availability(AdapterCapabilityState.DisabledIncompatible, reason);
        PublishCapabilities(); diagnostic(reason);
    }
    private void DisableCashAcquisition(string reason)
    {
        if (cashAcquisitionDisabled) return;
        cashAcquisitionDisabled = true;
        pendingPickupIds.Clear();
        playerOriginatedCashAmounts.Clear();
        playerOriginatedCashOutsideOwned = 0;
        MetricCapabilities.CashExternalAcquisition = EconomyNativeContractPolicy.Availability(
            AdapterCapabilityState.DisabledIncompatible,
            reason);
        PublishCapabilities();
        diagnostic(reason);
    }
    private void PublishCapabilities() => capabilityPublisher(EconomyNativeContractPolicy.ToRecords(MetricCapabilities, AdapterVersion));
    private static long SaturatingAdd(long left, long right) => left > long.MaxValue - right ? long.MaxValue : left + right;
    private static long SaturatingSum(IEnumerable<long> values)
    {
        var result = 0L;
        foreach (var value in values) result = SaturatingAdd(result, value);
        return result;
    }

    private sealed class PendingMoneyFlow
    {
        public PendingMoneyFlow(long amount, CurrencyFlowDirection direction, ObservationContext context)
        { Amount = amount; Direction = direction; Context = context.GameplayContext; Observation = context; }
        public long Amount { get; }
        public CurrencyFlowDirection Direction { get; }
        public CurrencySourceCategory Source { get; set; } = CurrencySourceCategory.UnknownAdjustment;
        public GameplayContext Context { get; set; }
        public string? NativeSourceId { get; set; }
        public string? SourceDisplayName { get; set; }
        private ObservationContext Observation { get; }
        public CurrencyFlowRecorded ToEvent(string id, string producerActivationId, long producerSequence) => new()
        {
            EventId = id,
            TimestampUtc = Observation.TimestampUtc,
            SaveGenerationId = Observation.GenerationId,
            RunId = Observation.RunId,
            SegmentId = Observation.SegmentId,
            MapId = Observation.MapId,
            Currency = CurrencyKind.Money,
            Direction = Direction,
            Amount = Amount,
            Source = Source,
            NativeSourceId = NativeSourceId,
            SourceDisplayName = SourceDisplayName,
            GameplayContext = Context,
            IntegrityTags = Observation.IntegrityTags,
            GameVersion = Application.version ?? string.Empty,
            GameBuild = SupportedGameBuild,
            AdapterVersion = AdapterVersion,
            ProducerActivationId = producerActivationId,
            ProducerSequence = producerSequence
        };
    }

    private sealed class ObservationContext
    {
        public ObservationContext(
            string generationId,
            string? runId,
            string? segmentId,
            string mapId,
            GameplayContext gameplayContext,
            DateTime timestampUtc,
            IntegrityTags integrityTags)
        {
            GenerationId = generationId;
            RunId = runId;
            SegmentId = segmentId;
            MapId = mapId;
            GameplayContext = gameplayContext;
            TimestampUtc = timestampUtc;
            IntegrityTags = integrityTags;
        }
        public string GenerationId { get; }
        public string? RunId { get; }
        public string? SegmentId { get; }
        public string MapId { get; }
        public GameplayContext GameplayContext { get; }
        public DateTime TimestampUtc { get; }
        public IntegrityTags IntegrityTags { get; }
    }

    private sealed class CashSnapshot
    {
        public CashSnapshot(long total, Dictionary<int, long> itemAmounts, Dictionary<int, Item> items)
        {
            Total = total;
            ItemAmounts = itemAmounts;
            Items = items;
        }
        public long Total { get; }
        public Dictionary<int, long> ItemAmounts { get; }
        public Dictionary<int, Item> Items { get; }
    }
}
