using ItemStatsSystem;
using ItemStatsSystem.Items;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;
using UnityEngine;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class NativeEquipmentAdapter : IDisposable, IRetryableCleanup
{
    internal const string AdapterVersion = "native-equipment/2.3.30+public-item-tree-v3";
    private const string SupportedGameVersion = "2.3.30";
    private const double ReconciliationIntervalSeconds = 0.2;
    private readonly Func<bool> runActiveProvider;
    private readonly Func<EquipmentSnapshot, bool> snapshotHandler;
    private readonly Func<bool> invalidationHandler;
    private readonly Action<IReadOnlyList<CapabilityRecord>> capabilityHandler;
    private readonly Action<string> diagnosticHandler;
    private readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    private readonly MonotonicCadenceGate cadence = new(ReconciliationIntervalSeconds);
    private readonly NativeCallbackLifetime callbackLifetime = new();
    private CharacterMainControl? observedMain;
    private Item? observedCharacterItem;
    private EquipmentSnapshot? latestSnapshot;
    private EquipmentMetricCapabilities metricCapabilities =
        EquipmentNativeContractPolicy.CreateUnavailableCapabilities("Equipment tracking has not been initialized.");

    public NativeEquipmentAdapter(
        Func<bool> runActiveProvider,
        Func<EquipmentSnapshot, bool> snapshotHandler,
        Func<bool> invalidationHandler,
        Action<IReadOnlyList<CapabilityRecord>> capabilityHandler,
        Action<string> diagnosticHandler)
    {
        this.runActiveProvider = runActiveProvider ?? throw new ArgumentNullException(nameof(runActiveProvider));
        this.snapshotHandler = snapshotHandler ?? throw new ArgumentNullException(nameof(snapshotHandler));
        this.invalidationHandler = invalidationHandler ?? throw new ArgumentNullException(nameof(invalidationHandler));
        this.capabilityHandler = capabilityHandler ?? throw new ArgumentNullException(nameof(capabilityHandler));
        this.diagnosticHandler = diagnosticHandler ?? throw new ArgumentNullException(nameof(diagnosticHandler));
    }

    public EquipmentMetricCapabilities MetricCapabilities => EquipmentStatisticsReducer.CloneCapabilities(metricCapabilities);

    public IReadOnlyList<CapabilityRecord> Initialize()
    {
        if (callbackLifetime.DisposalStarted) throw new ObjectDisposedException(nameof(NativeEquipmentAdapter));
        if (callbackLifetime.IsActive) return EquipmentNativeContractPolicy.ToRecords(metricCapabilities, AdapterVersion);
        if (!string.Equals(Application.version ?? string.Empty, SupportedGameVersion, StringComparison.Ordinal))
        {
            SetDisabled($"Installed Duckov version '{Application.version}' does not match verified equipment contract version '{SupportedGameVersion}'.");
            return EquipmentNativeContractPolicy.ToRecords(metricCapabilities, AdapterVersion);
        }

        try
        {
            var slotChanged = callbackLifetime.Guard<CharacterMainControl, Slot>((_, _) => ObserveNow());
            var holdChanged = callbackLifetime.Guard<CharacterMainControl, DuckovItemAgent>((_, _) => ObserveNow());
            var inventoryChanged = callbackLifetime.Guard<CharacterMainControl, Inventory, int>((_, _, _) => ObserveNow());
            callbackLifetime.Activate(new[]
            {
                new SubscriptionBinding(
                    () => CharacterMainControl.OnMainCharacterSlotContentChangedEvent += slotChanged,
                    () => CharacterMainControl.OnMainCharacterSlotContentChangedEvent -= slotChanged),
                new SubscriptionBinding(
                    () => CharacterMainControl.OnMainCharacterChangeHoldItemAgentEvent += holdChanged,
                    () => CharacterMainControl.OnMainCharacterChangeHoldItemAgentEvent -= holdChanged),
                new SubscriptionBinding(
                    () => CharacterMainControl.OnMainCharacterInventoryChangedEvent += inventoryChanged,
                    () => CharacterMainControl.OnMainCharacterInventoryChangedEvent -= inventoryChanged)
            });
            metricCapabilities = EquipmentNativeContractPolicy.CreateSupportedCapabilities();
            capabilityHandler(EquipmentNativeContractPolicy.ToRecords(metricCapabilities, AdapterVersion));
            SynchronizeMain();
            ObserveNow();
            diagnosticHandler("Native equipment hooks subscribed; tote activation remains deliberately unavailable.");
        }
        catch (Exception exception)
        {
            TryCleanup();
            SetDisabled($"Equipment hook activation failed: {exception.GetType().Name}: {exception.Message}");
        }
        return EquipmentNativeContractPolicy.ToRecords(metricCapabilities, AdapterVersion);
    }

    public void Tick()
    {
        var now = clock.Elapsed.TotalSeconds;
        if (!callbackLifetime.CanHandleCallbacks || !cadence.IsDue(now)) return;
        cadence.MarkCompleted(now);
        SynchronizeMain();
        ObserveNow();
    }

    public EquipmentEventAssociation CaptureAssociation()
    {
        if (!callbackLifetime.CanHandleCallbacks || !runActiveProvider()) return new EquipmentEventAssociation();
        ObserveNow();
        var snapshot = latestSnapshot;
        return snapshot == null ? new EquipmentEventAssociation() : new EquipmentEventAssociation
        {
            LoadoutId = snapshot.LoadoutId,
            SelectedWeaponId = snapshot.SelectedWeaponId,
            SelectedWeaponSlotId = snapshot.SelectedWeaponSlotId,
            TotemSetId = snapshot.TotemSetId
        };
    }

    public bool TryCleanup()
    {
        UnsubscribeCharacterTree();
        var cleaned = callbackLifetime.TryCleanup(() => true, out var failure);
        if (failure != null) diagnosticHandler($"Equipment cleanup remains retryable: {failure.GetType().Name}: {failure.Message}");
        latestSnapshot = null;
        observedMain = null;
        return cleaned;
    }

    public void Dispose() => TryCleanup();

    private void SynchronizeMain()
    {
        var current = CharacterMainControl.Main;
        if (current != null && !current.IsMainCharacter) current = null;
        var characterItem = current?.CharacterItem;
        if (ReferenceEquals(current, observedMain) && ReferenceEquals(characterItem, observedCharacterItem)) return;
        UnsubscribeCharacterTree();
        observedMain = current;
        observedCharacterItem = characterItem;
        if (observedCharacterItem != null) observedCharacterItem.onItemTreeChanged += OnItemTreeChanged;
        latestSnapshot = null;
    }

    private void UnsubscribeCharacterTree()
    {
        if (observedCharacterItem != null) observedCharacterItem.onItemTreeChanged -= OnItemTreeChanged;
        observedCharacterItem = null;
    }

    private void OnItemTreeChanged(Item _) => ObserveNow();

    private void ObserveNow()
    {
        if (!callbackLifetime.CanHandleCallbacks) return;
        SynchronizeMain();
        if (!runActiveProvider())
        { latestSnapshot = null; return; }
        if (observedMain == null || observedCharacterItem == null || !observedMain.IsMainCharacter)
        { InvalidateObservation(); return; }
        try
        {
            var snapshot = NativeEquipmentSnapshotBuilder.Build(observedMain, observedCharacterItem);
            latestSnapshot = snapshot;
            snapshotHandler(snapshot);
        }
        catch (Exception exception)
        {
            InvalidateObservation();
            diagnosticHandler($"Equipment observation failed safely: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void InvalidateObservation()
    {
        latestSnapshot = null;
        invalidationHandler();
    }

    private void SetDisabled(string detail)
    {
        metricCapabilities = EquipmentNativeContractPolicy.CreateUnavailableCapabilities(detail);
        capabilityHandler(EquipmentNativeContractPolicy.ToRecords(metricCapabilities, AdapterVersion));
        diagnosticHandler(detail);
    }
}
