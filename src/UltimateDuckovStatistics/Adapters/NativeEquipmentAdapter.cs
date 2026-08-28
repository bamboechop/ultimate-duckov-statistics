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
    internal const string AdapterVersion = "native-equipment/2.3.30+public-item-tree-v9+lossless-slot-state";
    private const string SupportedGameVersion = "2.3.30";
    internal const double ReconciliationIntervalSeconds = 1;
    private readonly Func<bool> runActiveProvider;
    private readonly Func<EquipmentSnapshot, bool> snapshotHandler;
    private readonly Func<bool> invalidationHandler;
    private readonly Action<IReadOnlyList<CapabilityRecord>> capabilityHandler;
    private readonly Action<string> diagnosticHandler;
    private readonly Func<double> monotonicSecondsProvider;
    private readonly Func<string?> observationContextProvider;
    private readonly MonotonicCadenceGate cadence = new(ReconciliationIntervalSeconds);
    private readonly NativeCallbackLifetime callbackLifetime = new();
    private CharacterMainControl? observedMain;
    private Item? observedCharacterItem;
    private EquipmentSnapshot? latestSnapshot;
    private string? latestDisplayMetadataSignature;
    private string? latestObservationContextId;
    private bool hasLatestObservationContext;
    private EquipmentMetricCapabilities metricCapabilities =
        EquipmentNativeContractPolicy.CreateUnavailableCapabilities("Equipment tracking has not been initialized.");

    public NativeEquipmentAdapter(
        Func<bool> runActiveProvider,
        Func<EquipmentSnapshot, bool> snapshotHandler,
        Func<bool> invalidationHandler,
        Action<IReadOnlyList<CapabilityRecord>> capabilityHandler,
        Action<string> diagnosticHandler,
        Func<double>? monotonicSecondsProvider = null,
        Func<string?>? observationContextProvider = null)
    {
        this.runActiveProvider = runActiveProvider ?? throw new ArgumentNullException(nameof(runActiveProvider));
        this.snapshotHandler = snapshotHandler ?? throw new ArgumentNullException(nameof(snapshotHandler));
        this.invalidationHandler = invalidationHandler ?? throw new ArgumentNullException(nameof(invalidationHandler));
        this.capabilityHandler = capabilityHandler ?? throw new ArgumentNullException(nameof(capabilityHandler));
        this.diagnosticHandler = diagnosticHandler ?? throw new ArgumentNullException(nameof(diagnosticHandler));
        if (monotonicSecondsProvider == null)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            this.monotonicSecondsProvider = () => clock.Elapsed.TotalSeconds;
        }
        else
        {
            this.monotonicSecondsProvider = monotonicSecondsProvider;
        }
        this.observationContextProvider = observationContextProvider
            ?? (() => runActiveProvider() ? "active" : null);
    }

    public EquipmentMetricCapabilities MetricCapabilities => EquipmentStatisticsReducer.CloneCapabilities(metricCapabilities);

    public EquipmentMetricCapabilities CaptureCapabilitiesForRunStart()
    {
        if (callbackLifetime.CanHandleCallbacks)
        {
            SynchronizeMain();
            RefreshSlotStateCapabilities();
        }
        return MetricCapabilities;
    }

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
            cadence.MarkCompleted(monotonicSecondsProvider());
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
        var now = monotonicSecondsProvider();
        if (!callbackLifetime.CanHandleCallbacks || !cadence.IsDue(now)) return;
        cadence.MarkCompleted(now);
        SynchronizeMain();
        ObserveNow();
    }

    public EquipmentEventAssociation CaptureAssociation()
    {
        if (!callbackLifetime.CanHandleCallbacks || !runActiveProvider()) return new EquipmentEventAssociation();
        NativeHotPathDiagnostics.CountEquipmentAssociationRequest();
        SynchronizeMain();
        var observationContextId = observationContextProvider();
        if (latestSnapshot == null
            || !hasLatestObservationContext
            || !string.Equals(latestObservationContextId, observationContextId, StringComparison.Ordinal))
        {
            ObserveNow();
        }
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
        latestDisplayMetadataSignature = null;
        latestObservationContextId = null;
        hasLatestObservationContext = false;
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
        latestDisplayMetadataSignature = null;
        latestObservationContextId = null;
        hasLatestObservationContext = false;
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
        {
            latestSnapshot = null;
            latestDisplayMetadataSignature = null;
            latestObservationContextId = null;
            hasLatestObservationContext = false;
            RefreshSlotStateCapabilities();
            return;
        }
        if (observedMain == null || observedCharacterItem == null || !observedMain.IsMainCharacter)
        { InvalidateObservation(); return; }
        try
        {
            // Segment identity is a deduplication scope, not proof that the
            // overall equipment observation is valid. Route degradation leaves
            // it empty while the main duck and loadout remain fully observable.
            var observationContextId = observationContextProvider();
            NativeHotPathDiagnostics.CountEquipmentSnapshotBuild();
            var snapshot = NativeEquipmentSnapshotBuilder.Build(observedMain, observedCharacterItem);
            var displayMetadataSignature = NativeEquipmentSnapshotBuilder.DisplayMetadataSignature(snapshot);
            UpdateSlotStateCapabilities(snapshot);
            // The same immutable loadout still has to be published once for every
            // run segment so its duration and event associations have a local root.
            // A missing segment is also a stable overall-only context so route
            // loss cannot suspend established run-level equipment tracking.
            var unchanged = hasLatestObservationContext
                            && string.Equals(latestObservationContextId, observationContextId, StringComparison.Ordinal)
                            && string.Equals(latestSnapshot?.SnapshotId, snapshot.SnapshotId, StringComparison.Ordinal)
                            && string.Equals(latestDisplayMetadataSignature, displayMetadataSignature, StringComparison.Ordinal);
            latestSnapshot = snapshot;
            latestDisplayMetadataSignature = displayMetadataSignature;
            latestObservationContextId = observationContextId;
            hasLatestObservationContext = true;
            if (unchanged)
            {
                NativeHotPathDiagnostics.CountEquipmentUnchangedPublication();
                return;
            }
            if (snapshotHandler(snapshot)) NativeHotPathDiagnostics.CountEquipmentChangedPublication();
            else NativeHotPathDiagnostics.CountEquipmentUnchangedPublication();
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
        latestDisplayMetadataSignature = null;
        latestObservationContextId = null;
        hasLatestObservationContext = false;
        invalidationHandler();
    }

    private void RefreshSlotStateCapabilities()
    {
        if (observedMain == null || observedCharacterItem == null || !observedMain.IsMainCharacter) return;
        try
        {
            NativeHotPathDiagnostics.CountEquipmentSnapshotBuild();
            UpdateSlotStateCapabilities(NativeEquipmentSnapshotBuilder.Build(observedMain, observedCharacterItem));
        }
        catch (Exception exception)
        {
            diagnosticHandler($"Equipment capability refresh failed safely: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void UpdateSlotStateCapabilities(EquipmentSnapshot snapshot)
    {
        var supported = EquipmentNativeContractPolicy.CreateSupportedCapabilities();
        var characterSlotState = snapshot.CharacterSlotStateComplete
            ? supported.CharacterSlotState
            : new MetricAvailability
            {
                State = AdapterCapabilityState.DisabledIncompatible,
                Provenance = "The native character-slot collection was not completely enumerable; missing evidence remains unavailable rather than being reported as empty."
            };
        var nestedSlotState = snapshot.NestedSlotStateComplete
                              && snapshot.Items.All(value => value.NestedSlotStateComplete)
            ? supported.NestedSlotState
            : new MetricAvailability
            {
                State = AdapterCapabilityState.DisabledIncompatible,
                Provenance = "At least one equipped-item nested-slot tree was not completely enumerable; missing paths remain unavailable rather than being reported as empty."
            };
        var changed = false;
        if (!AvailabilityEquals(metricCapabilities.CharacterSlotState, characterSlotState))
        {
            metricCapabilities.CharacterSlotState = characterSlotState;
            changed = true;
        }
        if (!AvailabilityEquals(metricCapabilities.NestedSlotState, nestedSlotState))
        {
            metricCapabilities.NestedSlotState = nestedSlotState;
            changed = true;
        }
        if (!changed) return;
        capabilityHandler(EquipmentNativeContractPolicy.ToRecords(metricCapabilities, AdapterVersion));
        diagnosticHandler(
            characterSlotState.State == AdapterCapabilityState.Supported
            && nestedSlotState.State == AdapterCapabilityState.Supported
                ? "Equipment slot-state capabilities restored after a complete native enumeration; prior degraded run scopes remain unchanged."
                : "Equipment slot-state capability degraded after incomplete native enumeration; independent readable dimensions continue.");
    }

    private static bool AvailabilityEquals(MetricAvailability left, MetricAvailability right) =>
        left.State == right.State
        && string.Equals(left.Provenance, right.Provenance, StringComparison.Ordinal);

    private void SetDisabled(string detail)
    {
        metricCapabilities = EquipmentNativeContractPolicy.CreateUnavailableCapabilities(detail);
        capabilityHandler(EquipmentNativeContractPolicy.ToRecords(metricCapabilities, AdapterVersion));
        diagnosticHandler(detail);
    }
}
