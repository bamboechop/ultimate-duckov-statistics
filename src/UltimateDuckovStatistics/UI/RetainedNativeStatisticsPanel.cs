using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Persistence;
using UnityEngine;

namespace UltimateDuckovStatistics.UI;

/// <summary>
/// Coordinates access, input, focus, and lifecycle for the Gate 1 retained-mode
/// shell. Statistics bodies remain intentionally deferred to Gate 2.
/// </summary>
internal sealed class NativeStatisticsPanel : IDisposable
{
    private readonly NativeProfileCoordinator coordinator;
    private readonly NativeUiIntegration nativeUi;
    private readonly RetainedStatisticsShell shell = new();
    private readonly RetainedShellLifecycleState lifecycle = new();
    private readonly PanelInteractionState interaction = new();
    private readonly AtomicJsonStore<UserSettings> settingsStore = new();
    private readonly string settingsPath;
    private KeyCode hotkey = KeyCode.F8;
    private PanelAccessSurface? openSurface;
    private bool disposed;
    private bool cursorStateCaptured;
    private bool priorCursorVisible;
    private CursorLockMode priorCursorLockMode;
    private GameObject? priorSelectedGameObject;
    private string? reportedTypographySummary;

    public NativeStatisticsPanel(NativeProfileCoordinator coordinator)
    {
        this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        settingsPath = Path.Combine(coordinator.DataRoot, "settings.json");
        LoadSettings();
        nativeUi = new NativeUiIntegration(coordinator, RequestOpen, HandleSurfaceClosed);
        nativeUi.Initialize();
    }

    public void Tick()
    {
        if (disposed) return;
        if (lifecycle.IsOpen && !shell.IsUsable)
        {
            Close();
            return;
        }
        if (lifecycle.IsOpen && NativeRaidContext.IsRaidMap())
        {
            Close();
            nativeUi.ShowToast(UiText.Get("ui.raid_unavailable"));
            return;
        }

        if (lifecycle.IsOpen && Input.GetKeyDown(KeyCode.Escape))
        {
            Close();
            return;
        }

        if (lifecycle.IsOpen
            && Input.GetKeyDown(KeyCode.Tab)
            && (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl)))
        {
            var reverse = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            interaction.MoveTab(reverse ? -1 : 1);
            shell.SetSelectedTab(interaction.SelectedTab);
        }

        if (Input.GetKeyDown(hotkey))
        {
            if (lifecycle.IsOpen) Close();
            else RequestOpen(PanelAccessSurface.Hotkey);
        }

        shell.Tick();
    }

    private void RequestOpen(PanelAccessSurface surface)
    {
        if (disposed) return;
        var decision = StatisticsPanelAccessPolicy.Resolve(surface, NativeRaidContext.IsRaidMap());
        if (surface == PanelAccessSurface.BasePauseMenu
            && (LevelManager.Instance == null || !LevelManager.Instance.IsBaseLevel))
        {
            decision = new PanelAccessDecision { RejectionTextKey = "ui.raid_unavailable" };
        }

        if (!decision.CanOpen)
        {
            Close();
            nativeUi.ShowToast(UiText.Get(decision.RejectionTextKey ?? "ui.raid_unavailable"));
            return;
        }

        var profile = coordinator.Current;
        if (!StatisticsPanelProjectionFactory.HasProvableGeneration(profile, coordinator.CurrentGenerationId))
        {
            nativeUi.ShowToast(UiText.Get("ui.profile_unavailable"));
            return;
        }

        if (lifecycle.IsOpen)
        {
            shell.SetSelectedTab(interaction.SelectedTab);
            return;
        }

        if (!nativeUi.TryResolvePanelCanvas(surface, out var canvas) || canvas == null)
        {
            ReportShellFailure(surface, "no active supported screen-space Duckov canvas was found");
            return;
        }

        CaptureFocusAndCursor();
        if (!lifecycle.TryOpen())
        {
            RestoreFocusAndCursor();
            return;
        }

        if (!shell.TryCreate(
                canvas,
                interaction.SelectedTab,
                Close,
                tab =>
                {
                    interaction.SelectTab(tab);
                    shell.SetSelectedTab(tab);
                },
                nativeUi.ResolveTypographyTemplate(surface),
                out var error))
        {
            lifecycle.Close();
            RestoreFocusAndCursor();
            ReportShellFailure(surface, error ?? "unknown retained-mode construction failure");
            return;
        }

        openSurface = surface;
        if (!string.IsNullOrWhiteSpace(shell.TypographySummary)
            && !string.Equals(reportedTypographySummary, shell.TypographySummary, StringComparison.Ordinal))
        {
            reportedTypographySummary = shell.TypographySummary;
            coordinator.ReportUiDiagnostic($"M17 retained typography roles: {reportedTypographySummary}.");
        }
    }

    private void ReportShellFailure(PanelAccessSurface surface, string detail)
    {
        coordinator.ReportUiDiagnostic(
            $"M17 retained-mode {surface} shell was unavailable: {detail}.",
            "Warning");
        nativeUi.ShowToast(UiText.Get("ui.shell_unavailable"));
    }

    private void HandleSurfaceClosed(PanelAccessSurface surface)
    {
        if (openSurface == surface) Close();
    }

    private void Close()
    {
        if (!lifecycle.Close()) return;
        shell.Dispose();
        openSurface = null;
        RestoreFocusAndCursor();
    }

    private void CaptureFocusAndCursor()
    {
        if (cursorStateCaptured) return;
        priorCursorVisible = Cursor.visible;
        priorCursorLockMode = Cursor.lockState;
        priorSelectedGameObject = GameManager.EventSystem?.currentSelectedGameObject;
        cursorStateCaptured = true;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        GameManager.EventSystem?.SetSelectedGameObject(null);
    }

    private void RestoreFocusAndCursor()
    {
        if (!cursorStateCaptured) return;
        Cursor.visible = priorCursorVisible;
        Cursor.lockState = priorCursorLockMode;
        var eventSystem = GameManager.EventSystem;
        var selectedObject = priorSelectedGameObject;
        var priorObjectExists = selectedObject != null;
        var priorObjectActive = priorObjectExists && selectedObject!.activeInHierarchy;
        if (eventSystem != null
            && PanelFocusRestorePolicy.ShouldRestore(
                cursorStateCaptured,
                priorObjectExists,
                priorObjectActive))
        {
            eventSystem.SetSelectedGameObject(selectedObject!);
        }
        priorSelectedGameObject = null;
        cursorStateCaptured = false;
    }

    private void LoadSettings()
    {
        try
        {
            var settings = settingsStore.Load(settingsPath).Value ?? new UserSettings();
            if (!Enum.TryParse(settings.PanelHotkey, ignoreCase: true, out hotkey) || hotkey == KeyCode.None)
                hotkey = KeyCode.F8;
            settings.PanelHotkey = hotkey.ToString();
            settingsStore.Save(settingsPath, settings);
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            hotkey = KeyCode.F8;
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        Close();
        lifecycle.Dispose();
        shell.Dispose();
        nativeUi.Dispose();
        RestoreFocusAndCursor();
        disposed = true;
    }
}
