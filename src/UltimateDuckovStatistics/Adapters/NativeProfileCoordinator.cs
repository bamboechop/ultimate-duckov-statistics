using Saves;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Diagnostics;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Export;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;
using UnityEngine;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class NativeProfileCoordinator : IDisposable
{
    private const int DiagnosticCapacity = 200;
    private static readonly Regex SaveTimePattern = new(
        "\\\"SaveTime\\\"\\s*:\\s*\\{[^{}]*?\\\"value\\\"\\s*:\\s*(-?\\d+)",
        RegexOptions.CultureInvariant);
    private readonly string dataRoot;
    private DiagnosticStore? diagnostics;
    private ProfileRepository? repository;
    private bool subscribed;
    private bool saveResetAwaitingNewGameReport;
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
        DisabledRunCapability(NativeRunLifecycleAdapter.MapAdapterId, NativeRunLifecycleAdapter.MapAdapterVersion)
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

    public NativeProfileCoordinator()
    {
        dataRoot = Path.Combine(Application.persistentDataPath, Core.ProductInfo.ModId);
    }

    public event Action? ProfileChanged;

    public event Action? ProfileChanging;

    public string DataRoot => dataRoot;

    public string CurrentGenerationId => repository?.CurrentGenerationId ?? string.Empty;

    public ProfileDocument? Current => repository == null ? null : repository.Current;

    public IReadOnlyList<DiagnosticEntry> DiagnosticEntries =>
        diagnostics?.Entries ?? Array.Empty<DiagnosticEntry>();

    public void Initialize()
    {
        if (subscribed)
        {
            WriteDiagnostic("Duplicate profile coordinator setup ignored.", "Warning");
            return;
        }

        Directory.CreateDirectory(dataRoot);
        repository = new ProfileRepository(
            dataRoot,
            () => DateTime.UtcNow,
            () => Guid.NewGuid().ToString("N"),
            message => WriteDiagnostic(message));

        var openResult = repository.Open(ReadIdentity());
        OpenDiagnosticsForCurrentGeneration();
        UpdateCapabilities();

        SavesSystem.OnSetFile += OnSetFile;
        SavesSystem.OnSaveDeleted += OnSaveDeleted;
        SavesSystem.OnCollectSaveData += OnCollectSaveData;
        LevelManager.OnNewGameReport += OnNewGameReport;
        subscribed = true;
        WriteDiagnostic(
            $"Profile opened slot={repository.Current.Slot} generation={repository.CurrentGenerationId} " +
            $"created={openResult.CreatedNew} rotated={openResult.RotatedGeneration} " +
            $"recovered={openResult.RecoveredSnapshot} migrated={openResult.MigratedSchema} " +
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
            if (repository?.Record(completion.NormalizedEvent) == true)
            {
                WriteDiagnostic(
                    $"Counted raid item use; total={repository.Current.Statistics.Overall.ActivationCount}.");
                return true;
            }
        }
        catch (Exception exception)
        {
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
            if (repository?.Record(healing) == true)
            {
                WriteDiagnostic(
                    $"Attributed {healing.ActualHealthRestored:0.###} actual HP to {healing.DisplayName}.");
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            WriteDiagnostic($"Failed to persist attributed healing: {exception.GetType().Name}.", "Error");
        }
    }

    public void SetHealingCapability(CapabilityRecord capability)
    {
        healingCapability = capability ?? throw new ArgumentNullException(nameof(capability));
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

    public bool HandleRunCheckpoint(ActiveRunCheckpoint checkpoint)
    {
        try
        {
            if (repository == null)
            {
                return false;
            }

            repository.SaveActiveRun(checkpoint);
            return true;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            WriteDiagnostic($"Failed to persist active-run checkpoint: {exception.GetType().Name}.", "Error");
            return false;
        }
    }

    public bool HandleRunCompleted(RunSummary summary)
    {
        try
        {
            return repository?.CompleteRun(summary) == true;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            WriteDiagnostic($"Failed to persist completed run: {exception.GetType().Name}.", "Error");
            return false;
        }
    }

    public void Flush()
    {
        try
        {
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
        if (repository?.CurrentProfilePath == null)
        {
            throw new InvalidOperationException("No profile is open for export.");
        }

        repository.RefreshIdentity(ReadIdentity(repository.Current.Slot));
        repository.Flush();
        var result = ProfileExportWriter.Write(repository.Current, repository.CurrentProfilePath, DateTime.UtcNow);
        WriteDiagnostic($"Exported JSON and CSV statistics to {result.Directory}.");
        return result;
    }

    public void ResetCurrent()
    {
        if (repository == null)
        {
            throw new InvalidOperationException("No profile is open for reset.");
        }

        ProfileChanging?.Invoke();
        repository.RefreshIdentity(ReadIdentity(repository.Current.Slot));
        repository.Rotate(ReadIdentity(), "UserReset");
        OpenDiagnosticsForCurrentGeneration();
        WriteDiagnostic($"User reset created generation {repository.CurrentGenerationId}; prior data was archived read-only.");
        ProfileChanged?.Invoke();
    }

    public void Dispose()
    {
        if (subscribed)
        {
            SavesSystem.OnSetFile -= OnSetFile;
            SavesSystem.OnSaveDeleted -= OnSaveDeleted;
            SavesSystem.OnCollectSaveData -= OnCollectSaveData;
            LevelManager.OnNewGameReport -= OnNewGameReport;
            subscribed = false;
        }

        try
        {
            if (repository != null)
            {
                repository.RefreshIdentity(ReadIdentity(repository.Current.Slot));
                repository.CloseClean();
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
        }

        repository = null;
    }

    private void OnSetFile()
    {
        try
        {
            ProfileChanging?.Invoke();
            saveResetAwaitingNewGameReport = false;
            var observed = ReadIdentity();
            if (repository!.Current.Slot != observed.Slot)
            {
                repository.RefreshIdentity(ReadIdentity(repository.Current.Slot));
            }

            var result = repository.Open(observed, "SaveSlotSelected");
            OpenDiagnosticsForCurrentGeneration();
            WriteDiagnostic(
                $"Save slot selected slot={repository.Current.Slot} generation={repository.CurrentGenerationId} " +
                $"created={result.CreatedNew} rotated={result.RotatedGeneration} " +
                $"unsupportedArchived={result.UnsupportedSchemaArchived}.");
            ProfileChanged?.Invoke();
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
            ProfileChanging?.Invoke();
            repository!.Rotate(ReadIdentity(), "DuckovSaveDeleted");
            OpenDiagnosticsForCurrentGeneration();
            saveResetAwaitingNewGameReport = true;
            WriteDiagnostic($"Duckov save deletion rotated to generation {repository.CurrentGenerationId}.");
            ProfileChanged?.Invoke();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            WriteDiagnostic($"Save-deletion rotation failed: {exception.GetType().Name}.", "Error");
        }
    }

    private void OnCollectSaveData()
    {
        try
        {
            if (repository != null)
            {
                repository.PrepareForNativeSave(ReadIdentity(repository.Current.Slot));
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            WriteDiagnostic($"Pre-save identity refresh failed: {exception.GetType().Name}.", "Error");
        }
    }

    private void OnNewGameReport()
    {
        try
        {
            ProfileChanging?.Invoke();
            var identity = ReadIdentity();
            if (saveResetAwaitingNewGameReport)
            {
                repository!.RefreshIdentity(identity);
                saveResetAwaitingNewGameReport = false;
                WriteDiagnostic("New-game report matched the already-rotated deleted save generation.");
            }
            else
            {
                repository!.Rotate(identity, "DuckovNewGame");
                OpenDiagnosticsForCurrentGeneration();
                WriteDiagnostic($"Duckov new game rotated to generation {repository.CurrentGenerationId}.");
            }

            ProfileChanged?.Invoke();
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            WriteDiagnostic($"New-game rotation failed: {exception.GetType().Name}.", "Error");
        }
    }

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
            byte[] hash;
            string content;
            using (var stream = new FileStream(
                       file.FullName,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete))
            using (var sha256 = SHA256.Create())
            {
                hash = sha256.ComputeHash(stream);
                stream.Position = 0;
                using var reader = new StreamReader(
                    stream,
                    Encoding.UTF8,
                    detectEncodingFromByteOrderMarks: true,
                    bufferSize: 4096,
                    leaveOpen: false);
                content = reader.ReadToEnd();
            }

            file.Refresh();
            if (!file.Exists
                || file.CreationTimeUtc.Ticks != creationTicks
                || file.LastWriteTimeUtc.Ticks != writeTicks
                || file.Length != length)
            {
                return;
            }

            contentSha256 = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
            var match = SaveTimePattern.Match(content);
            if (match.Success && long.TryParse(
                    match.Groups[1].Value,
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var parsedSaveTime))
            {
                saveTimeBinary = parsedSaveTime;
            }
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
        repository?.SetCapabilities(new[]
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
        }.Concat(runCapabilities).Concat(weaponCapabilities).Concat(combatCapabilities).Concat(equipmentCapabilities).Concat(containerCapabilities));
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
