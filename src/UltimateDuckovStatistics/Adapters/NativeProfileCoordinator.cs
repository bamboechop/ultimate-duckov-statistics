using Saves;
using UltimateDuckovStatistics.Core.Diagnostics;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Export;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Tracking;
using UnityEngine;

namespace UltimateDuckovStatistics.Adapters;

internal sealed class NativeProfileCoordinator : IDisposable
{
    private const int DiagnosticCapacity = 200;
    private readonly string dataRoot;
    private DiagnosticStore? diagnostics;
    private ProfileRepository? repository;
    private bool subscribed;
    private bool saveResetAwaitingNewGameReport;

    public NativeProfileCoordinator()
    {
        dataRoot = Path.Combine(Application.persistentDataPath, Core.ProductInfo.ModId);
    }

    public event Action? ProfileChanged;

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
        repository.SetCapabilities(new[]
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
                Detail = "Duckov public SavesSystem and LevelManager events"
            }
        });

        SavesSystem.OnSetFile += OnSetFile;
        SavesSystem.OnSaveDeleted += OnSaveDeleted;
        LevelManager.OnNewGameReport += OnNewGameReport;
        subscribed = true;
        WriteDiagnostic(
            $"Profile opened slot={repository.Current.Slot} generation={repository.CurrentGenerationId} " +
            $"created={openResult.CreatedNew} rotated={openResult.RotatedGeneration} " +
            $"recovered={openResult.RecoveredSnapshot} migrated={openResult.MigratedSchema} " +
            $"interrupted={openResult.InterruptedSessionRecovered}.");
    }

    public void HandleItemUse(ItemUseCompletion completion)
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

            return;
        }

        try
        {
            if (repository?.Record(completion.NormalizedEvent) == true)
            {
                WriteDiagnostic(
                    $"Counted raid item use; total={repository.Current.Statistics.Overall.ActivationCount}.");
            }
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            WriteDiagnostic($"Failed to persist item use: {exception.GetType().Name}.", "Error");
        }
    }

    public void Flush()
    {
        try
        {
            repository?.Flush();
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
            LevelManager.OnNewGameReport -= OnNewGameReport;
            subscribed = false;
        }

        try
        {
            repository?.CloseClean();
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
            saveResetAwaitingNewGameReport = false;
            var result = repository!.Open(ReadIdentity(), "SaveSlotSelected");
            OpenDiagnosticsForCurrentGeneration();
            WriteDiagnostic(
                $"Save slot selected slot={repository.Current.Slot} generation={repository.CurrentGenerationId} " +
                $"created={result.CreatedNew} rotated={result.RotatedGeneration}.");
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

    private void OnNewGameReport()
    {
        try
        {
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

    private static SaveIdentitySnapshot ReadIdentity()
    {
        var slot = SavesSystem.CurrentSlot;
        var savePath = Path.Combine(Application.persistentDataPath, SavesSystem.GetFilePath(slot));
        var file = new FileInfo(savePath);
        file.Refresh();
        return new SaveIdentitySnapshot
        {
            Slot = slot,
            SaveFilePresent = file.Exists,
            SaveFileCreationUtcTicks = file.Exists ? file.CreationTimeUtc.Ticks : null,
            ObservedWriteUtcTicks = file.Exists ? file.LastWriteTimeUtc.Ticks : null,
            ObservedLength = file.Exists ? file.Length : null,
            GameVersion = Application.version ?? string.Empty
        };
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
