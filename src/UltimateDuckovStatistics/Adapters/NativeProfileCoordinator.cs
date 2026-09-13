using Saves;
using Duckov.Scenes;
using Duckov.UI;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Diagnostics;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Export;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;
using UnityEngine;

namespace UltimateDuckovStatistics.Adapters;

internal sealed partial class NativeProfileCoordinator : IDisposable
{
    private const int DiagnosticCapacity = 200;
    private readonly MonotonicCadenceGate persistenceDiagnosticCadence = new(60);
    private readonly Func<double> monotonicClock;
    private readonly Func<string, Action<string>, ProfileRepository> repositoryFactory;
    private readonly string dataRoot;
    private DiagnosticStore? diagnostics;
    private ProfileRepository? repository;
    private readonly DeferredCheckpointWriter<CheckpointWrite> checkpointWriter;
    private readonly DeferredSnapshotWriter<ProfileWrite> profileWriter;
    private readonly EconomyActivationGate economyActivationGate;
    private readonly NativeProfileTransitionBoundary profileTransitionBoundary;
    private Func<bool>? activeRunCheckpointFlusher;
    private Func<bool>? economyBoundaryFlusher;
    private Func<bool>? economyHoldingsBoundaryFlusher;
    private Func<bool>? worldTimeBoundaryFlusher;
    private Func<bool>? craftingBoundaryFlusher;
    private Func<bool>? craftingProfileTransitionBoundaryFlusher;
    private bool subscribed;
    private bool nativeSaveBoundaryActive;
    private double nextStorageMaintenance;
    private double nextStorageOpportunity;
    private System.Threading.Tasks.Task<ProfileMaintenanceResult>? storageMaintenance;
    private bool saveResetAwaitingNewGameReport;
    private long profileTransitionSequence;
    private string lastOpenStatus = "Profile has not been opened.";
    private CapabilityRecord healingCapability = new()
    {
        AdapterId = NativeHealingAttributionAdapter.AdapterId,
        State = AdapterCapabilityState.DisabledIncompatible,
        Version = NativeHealingAttributionAdapter.AdapterVersion,
        Detail = "Healing attribution has not been initialized."
    };
    private List<CapabilityRecord> runCapabilities = new()
    {
        DisabledRunCapability(NativeRunLifecycleAdapter.LifecycleAdapterId, NativeRunLifecycleAdapter.LifecycleAdapterVersion),
        DisabledRunCapability(NativeRunLifecycleAdapter.MovementAdapterId, NativeRunLifecycleAdapter.MovementAdapterVersion),
        DisabledRunCapability(NativeRunLifecycleAdapter.MapAdapterId, NativeRunLifecycleAdapter.MapAdapterVersion),
        DisabledRunCapability(NativeRunLifecycleAdapter.RouteAdapterId, NativeRunLifecycleAdapter.RouteAdapterVersion)
    };
    private List<CapabilityRecord> weaponCapabilities = WeaponCapabilityIds.All
        .Select(id => new CapabilityRecord
        {
            AdapterId = id,
            State = AdapterCapabilityState.DisabledIncompatible,
            Version = NativeWeaponFireAdapter.AdapterVersion,
            Detail = "Weapon capability has not been initialized."
        })
        .ToList();
    private List<CapabilityRecord> combatCapabilities = CombatNativeContractPolicy.ToRecords(
        CombatNativeContractPolicy.CreateUnavailableCapabilities("Combat capability has not been initialized."),
        NativeCombatAttributionAdapter.AdapterVersion).ToList();
    private List<CapabilityRecord> equipmentCapabilities = EquipmentNativeContractPolicy.ToRecords(
        EquipmentNativeContractPolicy.CreateUnavailableCapabilities("Equipment capability has not been initialized."),
        NativeEquipmentAdapter.AdapterVersion).ToList();
    private List<CapabilityRecord> containerCapabilities = new()
    {
        ContainerNativeContractPolicy.ToRecord(
            ContainerNativeContractPolicy.Unavailable("Container capability has not been initialized."),
            NativeContainerAdapter.AdapterVersion)
    };
    private List<CapabilityRecord> economyCapabilities = EconomyNativeContractPolicy.ToRecords(
        EconomyNativeContractPolicy.Unavailable(EconomyNativeContractPolicy.BootstrapProvenance),
        NativeEconomyAdapter.AdapterVersion).ToList();
    private EconomyMetricCapabilities economyMetricCapabilities =
        EconomyNativeContractPolicy.Unavailable(EconomyNativeContractPolicy.BootstrapProvenance);
    private List<CapabilityRecord> worldTimeCapabilities = WorldTimeNativeContractPolicy.ToRecords(
        WorldTimeNativeContractPolicy.Unavailable(WorldTimeNativeContractPolicy.BootstrapProvenance),
        NativeWorldTimeAdapter.AdapterVersion).ToList();
    private WorldTimeMetricCapabilities worldTimeMetricCapabilities =
        WorldTimeNativeContractPolicy.Unavailable(WorldTimeNativeContractPolicy.BootstrapProvenance);
    private List<CapabilityRecord> craftingCapabilities = CraftingNativeContractPolicy.ToRecords(
        CraftingNativeContractPolicy.Unavailable(CraftingNativeContractPolicy.BootstrapProvenance),
        NativeCraftingAdapter.AdapterVersion).ToList();
    private CraftingMetricCapabilities craftingMetricCapabilities =
        CraftingNativeContractPolicy.Unavailable(CraftingNativeContractPolicy.BootstrapProvenance);
    private List<CapabilityRecord> economyHoldingsCapabilities = EconomyHoldingsNativeContractPolicy.ToRecords(
        EconomyHoldingsNativeContractPolicy.Unavailable(EconomyHoldingsNativeContractPolicy.BootstrapProvenance),
        NativeEconomyHoldingsAdapter.AdapterVersion).ToList();
    private EconomyHoldingsMetricCapabilities economyHoldingsMetricCapabilities =
        EconomyHoldingsNativeContractPolicy.Unavailable(EconomyHoldingsNativeContractPolicy.BootstrapProvenance);

    public NativeProfileCoordinator(Func<double>? monotonicClock = null,
        Func<string, Action<string>, ProfileRepository>? repositoryFactory = null)
    {
        this.repositoryFactory = repositoryFactory ?? NativeProfileStorage.Create;
        this.monotonicClock = monotonicClock ?? (() => (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency);
        profileTransitionBoundary = new NativeProfileTransitionBoundary(this.monotonicClock);
        dataRoot = Path.Combine(Application.persistentDataPath, Core.ProductInfo.ModId, Core.ProductInfo.DataDirectory);
        checkpointWriter = new DeferredCheckpointWriter<CheckpointWrite>(write =>
        {
            NativeHotPathDiagnostics.CountCheckpointStoreAttempt();
            if (write.Snapshot != null) write.Repository.SaveSnapshot(write.Snapshot);
            else write.Repository.SaveActiveRun(write.Checkpoint!);
            NativeHotPathDiagnostics.CountCheckpointStoreSuccess();
        });
        profileWriter = new DeferredSnapshotWriter<ProfileWrite>(CaptureProfileWrite, write =>
        {
            NativeHotPathDiagnostics.CountProfileStoreAttempt();
            write.Repository.SaveSnapshot(write.Snapshot);
            NativeHotPathDiagnostics.CountProfileStoreSuccess();
        }, this.monotonicClock);
        economyActivationGate = new EconomyActivationGate(
            activationId =>
            {
                var currentRepository = repository
                    ?? throw new InvalidOperationException("No profile generation is open for economy activation.");
                currentRepository.BeginEconomyActivation(activationId);
            },
            ReportEconomyActivationFailure);
    }

    public event Action? ProfileChanged;

    public event Action? ProfileChanging;

    public event Action<long>? EconomyHoldingsSaveSlotTransitionStarted;

    public event Action<long>? EconomyHoldingsSaveSlotTransitionCompleted;

    public event Action<long>? EconomyHoldingsProfileResetStarted;

    public event Action<long>? EconomyHoldingsProfileResetCompleted;

    public event Action<long>? WorldTimeProfileChangeAwaitingNativeLoadStarted;

    public event Action<long>? WorldTimeNewGameProfileChangeStarted;

    public event Action<long>? WorldTimeProfileChangeCompleted;

    public event Action<long>? WorldTimeSameProfileReopenCompleted;

    public event Action? WorldTimeProfileChangedWithCurrentClock;

    public event Action<long>? CraftingProfileChangeStarted;

    public event Action<long>? CraftingProfileChangeCompleted;

    public string DataRoot => dataRoot;

    public string CurrentProfilePath => repository?.CurrentProfilePath ?? string.Empty;

    public string LastOpenStatus => lastOpenStatus;

    public ProfileOpenResult? LastOpenResult => repository?.LastOpenResult;

    public ProfileSaveReceipt? LastSaveReceipt => repository?.LastSaveReceipt;

    public bool HasProfilePersistenceFailure => profileWriter.HasFailure;

    public long CompletedUserResetVersion { get; private set; }

    public string LastCompletedUserResetGeneration { get; private set; } = string.Empty;

    public NativeUserResetAttempt? LastUserResetAttempt { get; private set; }

    public bool HasPendingProfileTransition => profileTransitionBoundary.HasPendingTransition;

    public string CurrentGenerationId => repository?.CurrentGenerationId ?? string.Empty;

    public ProfileDocument? Current => repository == null ? null : repository.Current;

    public EconomyMetricCapabilities CurrentEconomyCapabilities =>
        EconomyStatisticsReducer.CloneCapabilities(economyMetricCapabilities);

    public WorldTimeMetricCapabilities CurrentWorldTimeCapabilities =>
        WorldTimeStatisticsReducer.CloneCapabilities(worldTimeMetricCapabilities);

    public CraftingMetricCapabilities CurrentCraftingCapabilities =>
        CraftingStatisticsReducer.CloneCapabilities(craftingMetricCapabilities);

    public EconomyHoldingsMetricCapabilities CurrentEconomyHoldingsCapabilities =>
        EconomyHoldingsReducer.Clone(economyHoldingsMetricCapabilities);

    public IReadOnlyList<DiagnosticEntry> DiagnosticEntries =>
        diagnostics?.Entries ?? Array.Empty<DiagnosticEntry>();

    public void ReportUiDiagnostic(string message, string severity = "Info")
    {
        if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("A UI diagnostic message is required.", nameof(message));
        WriteDiagnostic(message, severity);
    }

    public void Initialize()
    {
        if (subscribed)
        {
            WriteDiagnostic("Duplicate profile coordinator setup ignored.", "Warning");
            return;
        }

        Directory.CreateDirectory(dataRoot);
        repository = repositoryFactory(dataRoot, message => WriteDiagnostic(message));

        var openResult = repository.Open(ReadIdentity());
        lastOpenStatus = FormatOpenResult(openResult);
        repository.EnableDeferredItemPersistence();
        OpenDiagnosticsForCurrentGeneration();
        UpdateCapabilities();

        SavesSystem.OnSetFile += OnSetFile;
        SavesSystem.OnSaveDeleted += OnSaveDeleted;
        SavesSystem.OnCollectSaveData += OnCollectSaveData;
        LevelManager.OnNewGameReport += OnNewGameReport;
        SceneLoader.onBeforeSetSceneActive += OnStorageLoading;
        SleepView.OnAfterSleep += OnStorageSleep;
        subscribed = true;
        WriteDiagnostic(
            $"Profile opened slot={repository.Current.Slot} generation={repository.CurrentGenerationId} " +
            $"created={openResult.CreatedNew} rotated={openResult.RotatedGeneration} " +
            $"recovered={openResult.RecoveredSnapshot} migrated={openResult.NormalizedProfile} " +
            $"unsupportedArchived={openResult.UnsupportedSchemaArchived} " +
            $"interruptedSession={openResult.InterruptedSessionRecovered} " +
            $"interruptedRun={openResult.InterruptedRunRecovered}.");
    }

    public bool HandleItemUse(ItemUseCompletion completion)
    {
        if (completion == null)
        {
            throw new ArgumentNullException(nameof(completion));
        }

        if (!completion.ShouldCount || completion.NormalizedEvent == null)
        {
            if (completion.Disposition != ItemUseCompletionDisposition.MissingBegin)
            {
                WriteDiagnostic($"Item use not counted: {completion.Disposition}.");
            }

            return false;
        }

        try
        {
            var currentRepository = repository;
            if (currentRepository == null)
            {
                return false;
            }

            if (healingCapability.State != AdapterCapabilityState.Supported) currentRepository.MarkHealingCaptureIncomplete();
            var deferred = currentRepository.CanDeferItemPersistence(completion.NormalizedEvent.RunId);
            if (!deferred)
            {
                DrainProfileWriter();
            }

            if (currentRepository.RecordDeferred(completion.NormalizedEvent))
            {
                if (deferred)
                {
                    profileWriter.MarkDirty();
                }
                return true;
            }
        }
        catch (Exception exception)
        {
            if (repository != null)
            {
                // A reducer may have completed before its immediate durable save
                // failed. Retain a bounded deferred retry instead of losing that
                // already-applied in-memory mutation.
                profileWriter.MarkDirty();
            }
            Debug.LogException(exception);
            WriteDiagnostic($"Failed to persist item use: {exception.GetType().Name}.", "Error");
        }

        return false;
    }

    public void HandleHealing(HealingApplied healing)
    {
        if (healing == null)
        {
            throw new ArgumentNullException(nameof(healing));
        }

        try
        {
            var currentRepository = repository;
            if (currentRepository == null)
            {
                return;
            }

            var deferred = currentRepository.CanDeferItemPersistence(healing.RunId);
            if (!deferred)
            {
                DrainProfileWriter();
            }

            if (currentRepository.RecordDeferred(healing) && deferred)
            {
                profileWriter.MarkDirty();
            }
        }
        catch (Exception exception)
        {
            if (repository != null)
            {
                profileWriter.MarkDirty();
            }
            Debug.LogException(exception);
            WriteDiagnostic($"Failed to persist attributed healing: {exception.GetType().Name}.", "Error");
        }
    }

    public void SetHealingCapability(CapabilityRecord capability)
    {
        healingCapability = capability ?? throw new ArgumentNullException(nameof(capability));
        if (capability.State != AdapterCapabilityState.Supported) repository?.MarkHealingCaptureIncomplete();
        UpdateCapabilities();
    }

    public void SetRunCapabilities(IReadOnlyList<CapabilityRecord> capabilities)
    {
        if (capabilities == null)
        {
            throw new ArgumentNullException(nameof(capabilities));
        }

        runCapabilities = capabilities.Select(CloneCapability).ToList();
        UpdateCapabilities();
    }

    private CapabilityRecord throwableCapability = new()
    {
        AdapterId = ThrowableUseObservation.CapabilityId,
        State = AdapterCapabilityState.DisabledIncompatible,
        Detail = "Throwable tracking has not been initialized."
    };

    public void SetThrowableCapability(CapabilityRecord value)
    { throwableCapability = CloneCapability(value); UpdateCapabilities(); }

    public void SetWeaponCapabilities(IReadOnlyList<CapabilityRecord> capabilities)
    {
        if (capabilities == null)
        {
            throw new ArgumentNullException(nameof(capabilities));
        }

        weaponCapabilities = capabilities.Select(CloneCapability).ToList();
        UpdateCapabilities();
    }

    public void SetCombatCapabilities(IReadOnlyList<CapabilityRecord> capabilities)
    {
        if (capabilities == null) throw new ArgumentNullException(nameof(capabilities));
        combatCapabilities = capabilities.Select(CloneCapability).ToList();
        UpdateCapabilities();
    }

    public void SetEquipmentCapabilities(IReadOnlyList<CapabilityRecord> capabilities)
    {
        if (capabilities == null) throw new ArgumentNullException(nameof(capabilities));
        equipmentCapabilities = capabilities.Select(CloneCapability).ToList();
        UpdateCapabilities();
    }

    public void SetContainerCapabilities(IReadOnlyList<CapabilityRecord> capabilities)
    {
        if (capabilities == null) throw new ArgumentNullException(nameof(capabilities));
        containerCapabilities = capabilities.Select(CloneCapability).ToList();
        UpdateCapabilities();
    }

    public void SetEconomyCapabilities(
        IReadOnlyList<CapabilityRecord> capabilities,
        EconomyMetricCapabilities metricCapabilities)
    {
        if (capabilities == null) throw new ArgumentNullException(nameof(capabilities));
        if (metricCapabilities == null) throw new ArgumentNullException(nameof(metricCapabilities));
        economyCapabilities = capabilities.Select(CloneCapability).ToList();
        economyMetricCapabilities = EconomyStatisticsReducer.CloneCapabilities(metricCapabilities);
        UpdateCapabilities();
    }

    public void SetWorldTimeCapabilities(
        IReadOnlyList<CapabilityRecord> capabilities,
        WorldTimeMetricCapabilities metricCapabilities)
    {
        if (capabilities == null) throw new ArgumentNullException(nameof(capabilities));
        if (metricCapabilities == null) throw new ArgumentNullException(nameof(metricCapabilities));
        worldTimeCapabilities = capabilities.Select(CloneCapability).ToList();
        worldTimeMetricCapabilities = WorldTimeStatisticsReducer.CloneCapabilities(metricCapabilities);
        UpdateCapabilities();
    }

    public void SetCraftingCapabilities(
        IReadOnlyList<CapabilityRecord> capabilities,
        CraftingMetricCapabilities metricCapabilities)
    {
        if (capabilities == null) throw new ArgumentNullException(nameof(capabilities));
        if (metricCapabilities == null) throw new ArgumentNullException(nameof(metricCapabilities));
        craftingCapabilities = capabilities.Select(CloneCapability).ToList();
        craftingMetricCapabilities = CraftingStatisticsReducer.CloneCapabilities(metricCapabilities);
        UpdateCapabilities();
    }

    public void SetEconomyHoldingsCapabilities(
        IReadOnlyList<CapabilityRecord> capabilities,
        EconomyHoldingsMetricCapabilities metricCapabilities)
    {
        if (capabilities == null) throw new ArgumentNullException(nameof(capabilities));
        if (metricCapabilities == null) throw new ArgumentNullException(nameof(metricCapabilities));
        economyHoldingsCapabilities = capabilities.Select(CloneCapability).ToList();
        economyHoldingsMetricCapabilities = EconomyHoldingsReducer.Clone(metricCapabilities);
        UpdateCapabilities();
    }

    public bool HandleEconomyHoldings(EconomyHoldingsMutation mutation)
    {
        if (mutation == null || mutation.IsEmpty) return true;
        try
        {
            var currentRepository = repository;
            if (currentRepository == null) return false;
            if (currentRepository.RecordEconomyHoldingsDeferred(mutation)) profileWriter.MarkDirty();
            return true;
        }
        catch (Exception exception)
        {
            if (repository != null) profileWriter.MarkDirty();
            Debug.LogException(exception);
            WriteDiagnostic($"Failed to record economy holdings: {exception.GetType().Name}.", "Error");
            return false;
        }
    }

    public bool MarkEconomyHoldingsNotCurrent(bool money, bool cash, string provenance)
    {
        try
        {
            var currentRepository = repository;
            if (currentRepository == null) return false;
            if (currentRepository.MarkEconomyHoldingsNotCurrentDeferred(money, cash, provenance))
                profileWriter.MarkDirty();
            return true;
        }
        catch (Exception exception)
        {
            if (repository != null) profileWriter.MarkDirty();
            Debug.LogException(exception);
            WriteDiagnostic($"Failed to mark economy holdings stale: {exception.GetType().Name}.", "Error");
            return false;
        }
    }

    public bool MarkEconomyHoldingsUnavailable(bool money, bool cash, string provenance)
    {
        try
        {
            var currentRepository = repository;
            if (currentRepository == null) return false;
            if (currentRepository.MarkEconomyHoldingsUnavailableDeferred(money, cash, provenance))
                profileWriter.MarkDirty();
            return true;
        }
        catch (Exception exception)
        {
            if (repository != null) profileWriter.MarkDirty();
            Debug.LogException(exception);
            WriteDiagnostic($"Failed to make economy holdings unavailable: {exception.GetType().Name}.", "Error");
            return false;
        }
    }

    public bool HandleWorldTime(WorldTimeMutation mutation)
    {
        if (mutation.IsEmpty) return false;
        try
        {
            var currentRepository = repository;
            if (currentRepository == null) return false;
            // Keep the current in-memory projection responsive without turning
            // every aggregate publication into a full durable profile rewrite.
            currentRepository.RecordWorldTimeDeferred(mutation);
            return true;
        }
        catch (Exception exception)
        {
            if (repository != null) profileWriter.MarkDirty();
            Debug.LogException(exception);
            WriteDiagnostic($"Failed to persist world-time statistics: {exception.GetType().Name}.", "Error");
            return false;
        }
    }

    public bool RequestWorldTimePersistence()
    {
        if (repository == null) return false;
        profileWriter.MarkDirty();
        return true;
    }

    public bool HandleCrafting(CraftingMutation mutation)
    {
        if (mutation == null || mutation.IsEmpty) return true;
        try
        {
            var currentRepository = repository;
            if (currentRepository == null) return false;
            if (currentRepository.RecordCraftingDeferred(mutation)) profileWriter.MarkDirty();
            return true;
        }
        catch (Exception exception)
        {
            if (repository != null) profileWriter.MarkDirty();
            Debug.LogException(exception);
            WriteDiagnostic($"Failed to aggregate crafted-item completion: {exception.GetType().Name}.", "Error");
            return false;
        }
    }

    public bool RequestCraftingPersistence()
    {
        if (repository == null) return false;
        profileWriter.MarkDirty();
        return true;
    }

    public void BeginEconomyActivation(string activationId)
    {
        economyActivationGate.Begin(activationId);
    }

    public bool RetryPendingEconomyActivation() => economyActivationGate.EnsureReady();

    public void SetEconomyBoundaryBarrier(Func<bool> flusher)
    {
        economyBoundaryFlusher = flusher ?? throw new ArgumentNullException(nameof(flusher));
    }

    public void SetEconomyHoldingsBoundaryBarrier(Func<bool> flusher)
    {
        economyHoldingsBoundaryFlusher = flusher ?? throw new ArgumentNullException(nameof(flusher));
    }

    public void SetWorldTimeBoundaryBarrier(Func<bool> flusher)
    {
        worldTimeBoundaryFlusher = flusher ?? throw new ArgumentNullException(nameof(flusher));
    }

    public void SetCraftingBoundaryBarrier(Func<bool> flusher)
    {
        craftingBoundaryFlusher = flusher ?? throw new ArgumentNullException(nameof(flusher));
    }

    public void SetCraftingProfileTransitionBoundaryBarrier(Func<bool> flusher)
    {
        craftingProfileTransitionBoundaryFlusher = flusher ?? throw new ArgumentNullException(nameof(flusher));
    }

    public bool RetryPendingProfileTransition()
    {
        if (!profileTransitionBoundary.HasPendingTransition) return true;
        var completed = profileTransitionBoundary.Retry(
            FlushProfileTransitionBoundaries,
            message => WriteDiagnostic(message, "Error"));
        return completed;
    }

    public bool DrainPendingProfileTransitions() => profileTransitionBoundary.Drain(
        FlushProfileTransitionBoundaries,
        message => WriteDiagnostic(message, "Error"));

    public bool HandleCurrencyFlow(CurrencyFlowRecorded flow)
    {
        if (flow == null) throw new ArgumentNullException(nameof(flow));
        if (!economyActivationGate.EnsureReady()) return false;
        try
        {
            var currentRepository = repository;
            if (currentRepository == null) return false;
            if (!currentRepository.RecordDeferred(flow)) return false;
            profileWriter.MarkDirty();
            return true;
        }
        catch (Exception exception)
        {
            if (repository != null) profileWriter.MarkDirty();
            Debug.LogException(exception);
            WriteDiagnostic($"Failed to persist currency flow: {exception.GetType().Name}.", "Error");
            return false;
        }
    }

    public void SetActiveRunCheckpointBarrier(Func<bool> flusher)
    {
        activeRunCheckpointFlusher = flusher ?? throw new ArgumentNullException(nameof(flusher));
    }

    public bool HandleRunCheckpoint(ActiveRunCheckpoint checkpoint)
    {
        try
        {
            var currentRepository = repository;
            if (currentRepository == null)
            {
                return false;
            }

            return checkpointWriter.TryCaptureAndSubmit(() => new CheckpointWrite(currentRepository, checkpoint));
        }
        catch (Exception exception)
        {
            ReportPersistenceFailure(exception, "Failed to persist active-run checkpoint");
            return false;
        }
    }

    public bool HandleIncrementalRunCheckpoint(RunLifecycleTracker tracker, DateTime timestampUtc, double monotonicSeconds, RunOutcome? terminalOutcome)
    {
        try
        {
            var currentRepository = repository;
            if (currentRepository == null) return false;
            return checkpointWriter.TryCaptureAndSubmit(() =>
            {
                if (currentRepository.UsesIncrementalStorage)
                    return new CheckpointWrite(currentRepository, currentRepository.CaptureActiveRunPersistence(tracker, timestampUtc, monotonicSeconds, terminalOutcome));
                var checkpoint = tracker.CreateCheckpoint(timestampUtc, monotonicSeconds)
                    ?? throw new InvalidOperationException("The active checkpoint owner disappeared.");
                checkpoint.PendingTerminalOutcome = terminalOutcome;
                NativeHotPathDiagnostics.CountCheckpointClone();
                return new CheckpointWrite(currentRepository, checkpoint);
            });
        }
        catch (Exception exception)
        {
            ReportPersistenceFailure(exception, "Failed to capture incremental active-run checkpoint");
            return false;
        }
    }

    public DeferredWriteState PollRunCheckpoint() => ObserveCheckpointResult(checkpointWriter.Poll());

    public DeferredWriteState FlushRunCheckpoint() => ObserveCheckpointResult(checkpointWriter.Flush());

    public DeferredWriteState TickProfilePersistence(bool activeRunCheckpointCurrent = true)
    {
        TickStorageMaintenance();
        QueueBaseMovementPersistence();
        var result = ObserveProfileResult(profileWriter.Tick(activeRunCheckpointCurrent));
        if (result == DeferredWriteState.Succeeded && !profileWriter.IsDirty) baseMovementWriteQueued = false;
        return result;
    }

    private void TickStorageMaintenance()
    {
        ObserveStorageMaintenance();
        var now = monotonicClock();
        if (now < nextStorageMaintenance) return;
        nextStorageMaintenance = now + 60;
        RequestStorageMaintenance(ProfileMaintenanceReason.LongSession);
    }

    private void OnStorageLoading(SceneLoadingContext _) => OnStorageSleep();

    private void OnStorageSleep()
    {
        // Installed hooks follow an awaited Show and precede activation/hide.
        // Treat them as scheduling hints, never as proof rendering is black.
        var now = monotonicClock();
        if (now < nextStorageOpportunity) return;
        nextStorageOpportunity = now + 5;
        RequestStorageMaintenance(ProfileMaintenanceReason.LoadingOrSleep);
    }

    private void RequestStorageMaintenance(ProfileMaintenanceReason reason)
    {
        if (repository == null || HasPendingProfileTransition || storageMaintenance is { IsCompleted: false }) return;
        ObserveStorageMaintenance();
        try { storageMaintenance = repository.RequestStorageMaintenance(reason); }
        catch (Exception exception) { ReportPersistenceFailure(exception, "Storage maintenance could not start"); }
    }

    private void ObserveStorageMaintenance()
    {
        if (storageMaintenance is not { IsCompleted: true }) return;
        var completed = storageMaintenance; storageMaintenance = null;
        try { completed.GetAwaiter().GetResult(); }
        catch (Exception exception) { ReportPersistenceFailure(exception, "Storage maintenance remains incomplete"); }
    }

    public bool HandleRunCompleted(RunSummary summary)
    {
        try
        {
            WaitRunCheckpoint();
            DrainProfileWriter();
            return repository?.CompleteRun(summary) == true;
        }
        catch (Exception exception)
        {
            ReportPersistenceFailure(exception, "Failed to persist completed run");
            return false;
        }
    }

    public void Flush()
    {
        try
        {
            if (!FlushBaseMovement()) throw new IOException("Base movement remains pending at a profile boundary.");
            if (economyHoldingsBoundaryFlusher?.Invoke() == false)
                throw new IOException("Economy holdings remain pending during profile flush.");
            if (worldTimeBoundaryFlusher?.Invoke() == false)
                throw new IOException("World-time aggregate remains pending during profile flush.");
            if (craftingBoundaryFlusher?.Invoke() == false)
                throw new IOException("Crafting aggregate or capability publication remains pending during profile flush.");
            WaitRunCheckpoint();
            DrainProfileWriter();
            if (repository != null)
            {
                repository.RefreshIdentity(ReadIdentity(repository.Current.Slot));
                repository.Flush();
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            WriteDiagnostic($"Profile flush failed: {exception.GetType().Name}.", "Error");
        }
    }

    public ProfileExportResult ExportCurrent()
    {
        PrepareCurrentExport();
        using var snapshot = repository!.CaptureExportSnapshotAsync().GetAwaiter().GetResult();
        var result = ProfileExportWriter.Write(snapshot, DateTime.UtcNow);
        WriteDiagnostic($"Exported zipped JSON statistics to {result.Directory}.");
        return result;
    }

    public System.Threading.Tasks.Task<ProfileExportResult> BeginExportCurrent()
    {
        // Native barriers and identity reads remain on the main thread. Storage
        // pins this revision before later commands, then copies it off the save queue.
        PrepareCurrentExport();
        var capture = repository!.CaptureExportSnapshotAsync();
        var exportedUtc = DateTime.UtcNow;
        var exportRoot = Path.Combine(dataRoot, "exports");
        return capture.ContinueWith(completed =>
        {
            using var snapshot = completed.GetAwaiter().GetResult();
            return ProfileExportWriter.WriteToRoot(snapshot, exportRoot, exportedUtc);
        }, System.Threading.CancellationToken.None, System.Threading.Tasks.TaskContinuationOptions.None,
            System.Threading.Tasks.TaskScheduler.Default);
    }

    private void PrepareCurrentExport()
    {
        if (HasPendingProfileTransition)
            throw new InvalidOperationException("A profile transition is pending; export cannot select a generation.");
        if (repository?.CurrentProfilePath == null)
        {
            throw new InvalidOperationException("No profile is open for export.");
        }

        if (!FlushBaseMovement()) throw new IOException("Base movement remains pending before export.");
        if (economyHoldingsBoundaryFlusher?.Invoke() == false)
            throw new IOException("Economy holdings remain pending before export.");
        if (worldTimeBoundaryFlusher?.Invoke() == false)
            throw new IOException("World-time aggregate remains pending before export.");
        if (craftingBoundaryFlusher?.Invoke() == false)
            throw new IOException("Crafting aggregate or capability publication remains pending before export.");
        WaitRunCheckpoint();
        DrainProfileWriter();
        repository.RefreshIdentity(ReadIdentity(repository.Current.Slot));
        repository.Flush();
    }

    public System.Threading.Tasks.Task<IReadOnlyList<string>> ListRestoreSourcesAsync() =>
        System.Threading.Tasks.Task.Run<IReadOnlyList<string>>(() =>
        {
            var root = Path.Combine(dataRoot, "exports");
            if (!Directory.Exists(root)) return Array.Empty<string>();
            var directories = new[] { root }.Concat(Directory.EnumerateDirectories(root)
                .Where(path => (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0));
            return directories.SelectMany(Directory.EnumerateFiles)
                .Where(path => (Path.GetFileName(path) == "statistics.json" || Path.GetFileName(path) == "statistics.zip")
                    && (File.GetAttributes(path) & FileAttributes.ReparsePoint) == 0)
                .OrderByDescending(File.GetLastWriteTimeUtc).ThenBy(path => path, StringComparer.Ordinal).ToArray();
        });

    public System.Threading.Tasks.Task<StatisticsRestorePreview> PreviewRestoreAsync(string path, System.Threading.CancellationToken cancellationToken = default)
    {
        var slot = repository?.Current.Slot ?? throw new InvalidOperationException("No profile is open for restore.");
        return System.Threading.Tasks.Task.Run(() => StatisticsRestoreReader.Read(path, slot, cancellationToken), cancellationToken);
    }

    public bool RestoreCurrent(StatisticsRestorePreview preview)
    {
        if (preview == null) throw new ArgumentNullException(nameof(preview));
        if (NativeRaidContext.IsRaidMap()) throw new InvalidOperationException("Restore is only available outside raids.");
        return ReplaceCurrent(preview);
    }

    public bool ResetCurrent() => ReplaceCurrent(null);

    private bool ReplaceCurrent(StatisticsRestorePreview? restore)
    {
        if (repository == null)
        {
            throw new InvalidOperationException("No profile is open for replacement.");
        }

        if (HasPendingProfileTransition)
            throw new InvalidOperationException("A profile transition is already pending; replacement cannot select a generation.");

        var currentIdentity = ReadIdentity(repository.Current.Slot);
        var resetIdentity = ReadIdentity();
        if (restore != null && (restore.Slot != currentIdentity.Slot || restore.Slot != resetIdentity.Slot))
            throw new InvalidOperationException("The selected save slot changed before restore.");
        var profileTransitionId = NextProfileTransitionId();
        var requestedGeneration = repository.CurrentGenerationId;
        var operation = restore == null ? "User reset" : "User restore";
        LastUserResetAttempt = new NativeUserResetAttempt(profileTransitionId, requestedGeneration,
            NativeUserResetOutcome.Pending, string.Empty, operation + " is waiting for its persistence boundary.");
        lastOpenStatus = operation + " remains queued; completion has not been reported and Diagnostics contains the blocking boundary.";
        NativeProfileResetTransition.Queue(
            profileTransitionId,
            craftingProfileChangeStarted: transitionId =>
            {
                PublishProfileEvent(
                    () => EconomyHoldingsProfileResetStarted?.Invoke(transitionId),
                    "economy-holdings-profile-reset-started");
                PublishProfileEvent(
                    () => CraftingProfileChangeStarted?.Invoke(transitionId),
                    "crafting-profile-reset-started");
            },
            enqueueTransition: QueueProfileTransition,
            profileChanging: () => ProfileChanging?.Invoke(),
            waitRunCheckpoint: WaitRunCheckpoint,
            drainProfileWriter: DrainProfileWriterForUserReset,
            refreshIdentity: () => repository.RefreshIdentityForUserReset(currentIdentity),
            rotateRepository: () =>
            {
                if (restore == null) repository.Rotate(resetIdentity, "UserReset");
                else repository.RestoreStatistics(resetIdentity, restore, requestedGeneration);
            },
            openDiagnostics: OpenDiagnosticsForCurrentGeneration,
            worldTimeProfileChanged: () => PublishProfileEvent(
                WorldTimeProfileChangedWithCurrentClock,
                "world-time-profile-changed-current-clock"),
            craftingProfileChangeCompleted: transitionId =>
            {
                PublishProfileEvent(
                    () => CraftingProfileChangeCompleted?.Invoke(transitionId),
                    "crafting-profile-reset-completed");
                PublishProfileEvent(
                    () => EconomyHoldingsProfileResetCompleted?.Invoke(transitionId),
                    "economy-holdings-profile-reset-completed");
            },
            profileChanged: () => PublishProfileEvent(ProfileChanged, "profile-changed"),
            applyCurrentMetricCapabilities: ApplyCurrentCapabilities,
            writeDiagnostic: () =>
            {
                LastCompletedUserResetGeneration = repository.CurrentGenerationId;
                CompletedUserResetVersion++;
                LastUserResetAttempt = new NativeUserResetAttempt(profileTransitionId, requestedGeneration,
                    NativeUserResetOutcome.Success, repository.CurrentGenerationId, string.Empty);
                lastOpenStatus = operation + " completed; the prior UDS generation was archived read-only.";
                WriteDiagnostic($"{operation} created generation {repository.CurrentGenerationId}; prior data was archived read-only.");
            },
            resetFailed: failure =>
            {
                var detail = failure.InnerException == null ? failure.Message
                    : $"{failure.InnerException.GetType().Name}: {failure.InnerException.Message}";
                LastUserResetAttempt = new NativeUserResetAttempt(profileTransitionId, requestedGeneration,
                    NativeUserResetOutcome.Failure, failure.PreservedGenerationId, detail);
                lastOpenStatus = operation + " failed; the original UDS generation remains active.";
                WriteDiagnostic($"{operation} failed with the original generation preserved: {detail}", "Error");
            });
        return LastUserResetAttempt.Outcome == NativeUserResetOutcome.Success;
    }

    public void Dispose()
    {
        if (subscribed)
        {
            SavesSystem.OnSetFile -= OnSetFile;
            SavesSystem.OnSaveDeleted -= OnSaveDeleted;
            SavesSystem.OnCollectSaveData -= OnCollectSaveData;
            LevelManager.OnNewGameReport -= OnNewGameReport;
            SceneLoader.onBeforeSetSceneActive -= OnStorageLoading;
            SleepView.OnAfterSleep -= OnStorageSleep;
            subscribed = false;
        }

        try
        {
            WaitRunCheckpoint();
            DrainProfileWriter();
            if (repository != null)
            {
                repository.RefreshIdentity(ReadIdentity(repository.Current.Slot));
                if (!FlushBaseMovement()) throw new IOException("Base movement remains pending during disposal.");
                repository.CloseClean();
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }
        finally
        {
            // Resource release is unconditional; only CloseClean above can
            // acknowledge a clean session after the final durability boundary.
            try { repository?.Dispose(); }
            catch (Exception exception) { Debug.LogException(exception); }
            finally { repository = null; }
        }
    }

    private void OnSetFile()
    {
        try
        {
            var activeRepository = repository
                ?? throw new InvalidOperationException("No profile repository is active.");
            var observed = ReadIdentity();
            NativeWorldTimeProfilePreOpenState? preOpenState = null;
            ProfileOpenResult? result = null;
            var sameProfileReopened = false;
            var profileTransitionId = NextProfileTransitionId();
            PublishProfileEvent(
                () => WorldTimeProfileChangeAwaitingNativeLoadStarted?.Invoke(profileTransitionId),
                "world-time-profile-change-awaiting-load-started");
            PublishProfileEvent(
                () => CraftingProfileChangeStarted?.Invoke(profileTransitionId),
                "crafting-profile-change-started");
            QueueSaveSlotProfileTransition(
                profileTransitionId,
                "Save-slot transition",
                () => ProfileChanging?.Invoke(),
                WaitRunCheckpoint,
                DrainProfileWriter,
                () => saveResetAwaitingNewGameReport = false,
                () => preOpenState = NativeWorldTimeProfileReopenPolicy.CapturePreOpenState(
                    activeRepository,
                    observed,
                    ReadIdentity),
                () =>
                {
                    result = NativeWorldTimeProfileReopenPolicy.OpenAndDetermineCurrentClockReuse(
                        activeRepository,
                        observed,
                        preOpenState
                            ?? throw new InvalidOperationException("Selected-profile pre-open state was not captured."),
                        "SaveSlotSelected",
                        out sameProfileReopened);
                },
                OpenDiagnosticsForCurrentGeneration,
                () => PublishProfileEvent(
                    sameProfileReopened
                        ? () => WorldTimeSameProfileReopenCompleted?.Invoke(profileTransitionId)
                        : () => WorldTimeProfileChangeCompleted?.Invoke(profileTransitionId),
                    sameProfileReopened
                        ? "world-time-same-profile-reopen-completed"
                        : "world-time-profile-change-completed"),
                () => PublishProfileEvent(
                    () => CraftingProfileChangeCompleted?.Invoke(profileTransitionId),
                    "crafting-profile-change-completed"),
                () => PublishProfileEvent(
                    () => EconomyHoldingsSaveSlotTransitionCompleted?.Invoke(profileTransitionId),
                    "economy-holdings-save-slot-transition-completed"),
                () => PublishProfileEvent(ProfileChanged, "profile-changed"),
                ApplyCurrentCapabilities,
                () => WriteDiagnostic(
                    $"Save slot selected slot={activeRepository.Current.Slot} generation={activeRepository.CurrentGenerationId} " +
                    $"created={result!.CreatedNew} rotated={result.RotatedGeneration} " +
                    $"unsupportedArchived={result.UnsupportedSchemaArchived}."));
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            WriteDiagnostic($"Save-slot transition failed: {exception.GetType().Name}.", "Error");
        }
    }

    private void OnSaveDeleted()
    {
        try
        {
            var identity = ReadIdentity();
            var profileTransitionId = NextProfileTransitionId();
            PublishProfileEvent(
                () => WorldTimeProfileChangeAwaitingNativeLoadStarted?.Invoke(profileTransitionId),
                "world-time-profile-change-awaiting-load-started");
            PublishProfileEvent(
                () => CraftingProfileChangeStarted?.Invoke(profileTransitionId),
                "crafting-profile-change-started");
            QueueProfileTransition(
                "Save-deletion rotation",
                () => ProfileChanging?.Invoke(),
                WaitRunCheckpoint,
                DrainProfileWriter,
                () => repository!.Rotate(identity, "DuckovSaveDeleted"),
                () => saveResetAwaitingNewGameReport = true,
                OpenDiagnosticsForCurrentGeneration,
                () => PublishProfileEvent(
                    () => WorldTimeProfileChangeCompleted?.Invoke(profileTransitionId),
                    "world-time-profile-change-completed"),
                () => PublishProfileEvent(
                    () => CraftingProfileChangeCompleted?.Invoke(profileTransitionId),
                    "crafting-profile-change-completed"),
                () => PublishProfileEvent(ProfileChanged, "profile-changed"),
                ApplyCurrentCapabilities,
                () => WriteDiagnostic($"Duckov save deletion rotated to generation {repository!.CurrentGenerationId}."));
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            WriteDiagnostic($"Save-deletion rotation failed: {exception.GetType().Name}.", "Error");
        }
    }

    private void OnCollectSaveData()
    {
        // Nested native notifications are covered by the outer boundary's latest
        // snapshot. They must not drain or capture while it is being prepared.
        if (nativeSaveBoundaryActive) return;
        nativeSaveBoundaryActive = true;
        try
        {
            var currentRepository = repository;
            if (currentRepository == null) return;
            var generation = currentRepository.CurrentGenerationId;
            Exception? preparationFailure = null;
            var result = profileWriter.FlushForBoundary(() =>
                PrepareNativeSaveBoundary(currentRepository, generation, out preparationFailure));
            if (result.State is DeferredWriteState.Pending or DeferredWriteState.Failed || profileWriter.IsDirty)
            {
                ObserveProfileResult(result);
                throw result.Exception ?? new IOException("Native-save profile durability remains pending.");
            }

            baseMovementWriteQueued = false;
            if (preparationFailure != null) throw preparationFailure;
        }
        catch (Exception exception)
        {
            ReportPersistenceFailure(exception, "Native-save profile boundary failed");
        }
        finally { nativeSaveBoundaryActive = false; }
    }

    private bool PrepareNativeSaveBoundary(ProfileRepository currentRepository, string generation, out Exception? failure)
    {
        failure = null;
        try
        {
            if (!PublishBaseMovementForNativeSave()) throw new IOException("Base movement remains pending at a profile boundary.");
            if (economyHoldingsBoundaryFlusher?.Invoke() == false)
                throw new IOException("Economy holdings remain pending before native save collection.");
            if (worldTimeBoundaryFlusher?.Invoke() == false)
                throw new IOException("World-time aggregate remains pending before native save collection.");
            if (craftingBoundaryFlusher?.Invoke() == false)
                throw new IOException("Crafting aggregate or capability publication remains pending before native save collection.");
        }
        catch (Exception exception)
        {
            // Accepted publications now belong to Current, while each adapter
            // retains its unaccepted values. Persist the accepted subset if safe.
            profileWriter.MarkDirty();
            failure = exception;
        }

        if (activeRunCheckpointFlusher?.Invoke() == false) return false;
        if (!ReferenceEquals(repository, currentRepository) || currentRepository.CurrentGenerationId != generation)
            throw new IOException("The profile generation changed during native-save preparation.");
        if (failure != null) return true;

        try
        {
            var identity = ReadIdentity(currentRepository.Current.Slot);
            if (currentRepository.PrepareForNativeSaveDeferred(identity)) profileWriter.MarkDirty();
        }
        catch (Exception exception)
        {
            // A failed identity read must not discard accepted aggregate data or
            // manufacture a new lineage. Any partial staging retains a writer.
            profileWriter.MarkDirty();
            failure = exception;
        }
        return true;
    }

    private void OnNewGameReport()
    {
        try
        {
            var identity = ReadIdentity();
            var matchedDeletedGeneration = false;
            var profileTransitionId = NextProfileTransitionId();
            PublishProfileEvent(
                () => WorldTimeNewGameProfileChangeStarted?.Invoke(profileTransitionId),
                "world-time-new-game-profile-change-started");
            PublishProfileEvent(
                () => CraftingProfileChangeStarted?.Invoke(profileTransitionId),
                "crafting-profile-change-started");
            QueueProfileTransition(
                "New-game rotation",
                () => ProfileChanging?.Invoke(),
                WaitRunCheckpoint,
                DrainProfileWriter,
                () =>
                {
                    matchedDeletedGeneration = saveResetAwaitingNewGameReport;
                    if (matchedDeletedGeneration)
                    {
                        repository!.RefreshIdentity(identity);
                        saveResetAwaitingNewGameReport = false;
                    }
                    else
                    {
                        repository!.Rotate(identity, "DuckovNewGame");
                    }
                },
                () =>
                {
                    if (!matchedDeletedGeneration) OpenDiagnosticsForCurrentGeneration();
                },
                () => PublishProfileEvent(
                    () => WorldTimeProfileChangeCompleted?.Invoke(profileTransitionId),
                    "world-time-profile-change-completed"),
                () => PublishProfileEvent(
                    () => CraftingProfileChangeCompleted?.Invoke(profileTransitionId),
                    "crafting-profile-change-completed"),
                () => PublishProfileEvent(ProfileChanged, "profile-changed"),
                ApplyCurrentCapabilities,
                () => WriteDiagnostic(matchedDeletedGeneration
                    ? "New-game report matched the already-rotated deleted save generation."
                    : $"Duckov new game rotated to generation {repository!.CurrentGenerationId}."));
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            WriteDiagnostic($"New-game rotation failed: {exception.GetType().Name}.", "Error");
        }
    }

    private void ReportEconomyActivationFailure(Exception exception)
    {
        Debug.LogException(exception);
        WriteDiagnostic(
            $"Economy activation persistence failed and remains queued for retry: {exception.GetType().Name}.",
            "Error");
    }

    private void QueueProfileTransition(string description, params Action[] steps)
    {
        profileTransitionBoundary.Enqueue(description, steps);
        RetryPendingProfileTransition();
    }

    private void QueueSaveSlotProfileTransition(long transitionId, string description, params Action[] steps)
    {
        profileTransitionBoundary.Enqueue(description, steps);
        PublishProfileEvent(
            () => EconomyHoldingsSaveSlotTransitionStarted?.Invoke(transitionId),
            "economy-holdings-save-slot-transition-started");
        RetryPendingProfileTransition();
    }

    private long NextProfileTransitionId() => checked(++profileTransitionSequence);

    private bool FlushProfileTransitionBoundaries()
    {
        if (!FlushBaseMovement()) return false;
        if (economyHoldingsBoundaryFlusher?.Invoke() == false) return false;
        if (economyBoundaryFlusher?.Invoke() == false) return false;
        if (worldTimeBoundaryFlusher?.Invoke() == false) return false;
        return craftingProfileTransitionBoundaryFlusher?.Invoke()
            ?? craftingBoundaryFlusher?.Invoke()
            ?? true;
    }

    private void PublishProfileEvent(Action? subscribers, string eventName) =>
        ProfileChangePublication.PublishIndependently(
            subscribers,
            exception =>
            {
                Debug.LogException(exception);
                WriteDiagnostic(
                    $"A {eventName} subscriber failed without skipping the remaining subscribers: {exception.GetType().Name}.",
                    "Error");
            });

    private static SaveIdentitySnapshot ReadIdentity() => ReadIdentity(SavesSystem.CurrentSlot);

    private static SaveIdentitySnapshot ReadIdentity(int slot)
    {
        var savePath = Path.Combine(Application.persistentDataPath, SavesSystem.GetFilePath(slot));
        var file = new FileInfo(savePath);
        file.Refresh();
        var creationTicks = file.Exists ? file.CreationTimeUtc.Ticks : (long?)null;
        var writeTicks = file.Exists ? file.LastWriteTimeUtc.Ticks : (long?)null;
        var length = file.Exists ? file.Length : (long?)null;
        string? contentSha256 = null;
        long? saveTimeBinary = null;
        if (file.Exists)
        {
            TryReadStableSaveSnapshot(
                file,
                creationTicks!.Value,
                writeTicks!.Value,
                length!.Value,
                out contentSha256,
                out saveTimeBinary);
        }

        return new SaveIdentitySnapshot
        {
            Slot = slot,
            SaveFilePresent = file.Exists,
            SaveFileCreationUtcTicks = creationTicks,
            ObservedWriteUtcTicks = writeTicks,
            ObservedLength = length,
            GameVersion = Application.version ?? string.Empty,
            ContentSha256 = contentSha256,
            SaveTimeBinary = saveTimeBinary
        };
    }

    private static void TryReadStableSaveSnapshot(
        FileInfo file,
        long creationTicks,
        long writeTicks,
        long length,
        out string? contentSha256,
        out long? saveTimeBinary)
    {
        contentSha256 = null;
        saveTimeBinary = null;
        try
        {
            (string Hash, long? SaveTime) fingerprint;
            using (var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.SequentialScan))
            {
                fingerprint = NativeSaveFingerprint.Read(stream);
            }

            file.Refresh();
            if (!file.Exists
                || file.CreationTimeUtc.Ticks != creationTicks
                || file.LastWriteTimeUtc.Ticks != writeTicks
                || file.Length != length)
            {
                return;
            }

            contentSha256 = fingerprint.Hash;
            saveTimeBinary = fingerprint.SaveTime;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            contentSha256 = null;
            saveTimeBinary = null;
        }
    }

    private void OpenDiagnosticsForCurrentGeneration()
    {
        var profilePath = repository?.CurrentProfilePath
            ?? throw new InvalidOperationException("No current profile path is available for diagnostics.");
        var generationDirectory = Path.GetDirectoryName(profilePath)
            ?? throw new InvalidOperationException("The current profile path has no directory.");
        diagnostics = new DiagnosticStore(
            Path.Combine(generationDirectory, "diagnostics.json"),
            DiagnosticCapacity,
            () => DateTime.UtcNow);
    }

    private void UpdateCapabilities()
    {
        if (nativeSaveBoundaryActive)
        {
            profileWriter.MarkDirty();
            ApplyCurrentCapabilities(deferPersistence: true);
            return;
        }
        DrainProfileWriter();
        ApplyCurrentCapabilities();
    }

    private void ApplyCurrentCapabilities() => ApplyCurrentCapabilities(deferPersistence: false);

    private void ApplyCurrentCapabilities(bool deferPersistence)
    {
        var currentRepository = repository;
        if (currentRepository == null) return;
        var capabilities = new[]
        {
            new CapabilityRecord
            {
                AdapterId = "native-item-use",
                State = AdapterCapabilityState.Supported,
                Version = NativeItemUseAdapter.AdapterVersion,
                Detail = "Duckov public Item/UsageUtilities/CA_UseItem events"
            },
            new CapabilityRecord
            {
                AdapterId = "native-save-lifecycle",
                State = AdapterCapabilityState.Supported,
                Version = "native-save-lifecycle/2.3.30",
                Detail = "Duckov public SavesSystem and LevelManager events with read-only save-lineage verification"
            },
            healingCapability
        }.Concat(new[] { throwableCapability }).Concat(runCapabilities).Concat(weaponCapabilities).Concat(combatCapabilities).Concat(equipmentCapabilities).Concat(containerCapabilities).Concat(economyCapabilities).Concat(worldTimeCapabilities).Concat(craftingCapabilities).Concat(economyHoldingsCapabilities);
        if (deferPersistence)
            currentRepository.SetCapabilitySnapshotDeferred(capabilities, economyMetricCapabilities,
                worldTimeMetricCapabilities, craftingMetricCapabilities, economyHoldingsMetricCapabilities);
        else
            currentRepository.SetCapabilitySnapshot(capabilities, economyMetricCapabilities,
                worldTimeMetricCapabilities, craftingMetricCapabilities, economyHoldingsMetricCapabilities);
    }

    private void ReportPersistenceFailure(Exception exception, string operation)
    {
        var now = monotonicClock();
        if (!persistenceDiagnosticCadence.IsDue(now)) return;
        persistenceDiagnosticCadence.MarkCompleted(now);
        Debug.LogException(exception);
        WriteDiagnostic($"{operation}: {exception.GetType().Name}: {exception.Message}. "
            + "Pending data retained; repeated persistence diagnostics limited to once per 60s.", "Error");
    }

    private DeferredWriteState ObserveCheckpointResult(DeferredWriteResult result)
    {
        if (result.State != DeferredWriteState.Failed) return result.State;
        var exception = result.Exception ?? new IOException("Deferred active-run checkpoint failed without an exception.");
        ReportPersistenceFailure(exception, "Failed to persist active-run checkpoint");
        return DeferredWriteState.Failed;
    }

    private DeferredWriteState ObserveProfileResult(DeferredWriteResult result)
    {
        if (result.State != DeferredWriteState.Failed) return result.State;
        var exception = result.Exception ?? new IOException("Deferred profile persistence failed without an exception.");
        ReportPersistenceFailure(exception, "Failed to persist deferred profile snapshot");
        return DeferredWriteState.Failed;
    }

    private void WaitRunCheckpoint() => ObserveCheckpointResult(checkpointWriter.Wait());

    private void DrainProfileWriter()
    {
        if (activeRunCheckpointFlusher != null && !activeRunCheckpointFlusher())
        {
            throw new IOException(
                "Deferred profile persistence was not flushed because the active-run checkpoint barrier failed.");
        }

        var result = profileWriter.Flush();
        if (result.State is DeferredWriteState.None or DeferredWriteState.Succeeded) return;
        ObserveProfileResult(result);
        throw result.Exception ?? new IOException("Deferred profile persistence failed without an exception.");
    }

    private void DrainProfileWriterForUserReset()
    {
        // An unaccepted lifecycle checkpoint is a retryable boundary. A profile
        // writer that has actually failed storage is a rejected reset attempt:
        // its dirty data stays in that writer, against the unchanged generation.
        if (activeRunCheckpointFlusher != null && !activeRunCheckpointFlusher())
            throw new IOException("The active-run checkpoint barrier remains pending before reset.");
        var result = profileWriter.Flush();
        if (result.State is DeferredWriteState.None or DeferredWriteState.Succeeded) return;
        ObserveProfileResult(result);
        throw new UserProfileResetFailedException(repository!.CurrentGenerationId,
            result.Exception ?? new IOException("Deferred profile persistence failed without an exception."));
    }

    private ProfileWrite CaptureProfileWrite()
    {
        var currentRepository = repository
            ?? throw new InvalidOperationException("No profile repository is available for deferred persistence.");
        NativeHotPathDiagnostics.CountProfileSnapshotCapture();
        return new ProfileWrite(currentRepository, currentRepository.CapturePersistenceSnapshot());
    }

    private static CapabilityRecord DisabledRunCapability(string adapterId, string version) => new()
    {
        AdapterId = adapterId,
        State = AdapterCapabilityState.DisabledIncompatible,
        Version = version,
        Detail = "Run capability has not been initialized."
    };

    private static CapabilityRecord CloneCapability(CapabilityRecord source) => new()
    {
        AdapterId = source.AdapterId,
        State = source.State,
        Version = source.Version,
        Detail = source.Detail
    };

    private sealed class CheckpointWrite
    {
        public CheckpointWrite(ProfileRepository repository, ActiveRunCheckpoint checkpoint)
        {
            Repository = repository ?? throw new ArgumentNullException(nameof(repository));
            Checkpoint = checkpoint ?? throw new ArgumentNullException(nameof(checkpoint));
            Snapshot = repository.UsesIncrementalStorage ? repository.CaptureActiveRunPersistence(checkpoint) : null;
        }

        public CheckpointWrite(ProfileRepository repository, ProfilePersistenceSnapshot snapshot)
        { Repository = repository; Snapshot = snapshot; }

        public ProfileRepository Repository { get; }
        public ActiveRunCheckpoint? Checkpoint { get; }
        public ProfilePersistenceSnapshot? Snapshot { get; }
    }

    private sealed class ProfileWrite
    {
        public ProfileWrite(ProfileRepository repository, ProfilePersistenceSnapshot snapshot)
        {
            Repository = repository ?? throw new ArgumentNullException(nameof(repository));
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        }

        public ProfileRepository Repository { get; }
        public ProfilePersistenceSnapshot Snapshot { get; }
    }

    private static string FormatOpenResult(ProfileOpenResult result) =>
        $"created={result.CreatedNew}; rotated={result.RotatedGeneration}; recoveredSnapshot={result.RecoveredSnapshot}; "
        + $"migratedSchema={result.NormalizedProfile}; unsupportedSchemaArchived={result.UnsupportedSchemaArchived}; "
        + $"interruptedSessionRecovered={result.InterruptedSessionRecovered}; interruptedRunRecovered={result.InterruptedRunRecovered}; "
        + $"loadFailures={result.LoadFailures.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)}";

    private void WriteDiagnostic(string message, string severity = "Info")
    {
        Debug.Log($"[UDS] {message}");
        try
        {
            diagnostics?.Add(message, severity);
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[UDS] Diagnostic persistence failed: {exception.GetType().Name}.");
        }
    }
}
