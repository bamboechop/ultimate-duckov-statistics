using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Diagnostics;
using UltimateDuckovStatistics.Core.Encounters;
using UnityEngine;

namespace UltimateDuckovStatistics.UI;

/// <summary>
/// Coordinates access, exact-generation refresh, input, focus, and retained lifecycle.
/// </summary>
internal sealed class NativeStatisticsPanel : IDisposable
{
    private static readonly KeyCode[] HotkeyCandidates = (KeyCode[])Enum.GetValues(typeof(KeyCode));
    private readonly NativeProfileCoordinator coordinator;
    private readonly NativeUiIntegration nativeUi;
    private readonly NativePanelShortcutGuard shortcutGuard;
    private readonly NativeEntityDisplayNames entityNames = new();
    private readonly KillDistanceHighlightsQuery killDistances = new();
    private readonly RetainedStatisticsShell shell = new();
    private readonly RetainedShellLifecycleState lifecycle = new();
    private readonly PanelInteractionState interaction = new();
    private readonly AtomicJsonStore<UserSettings> settingsStore = new();
    private readonly string settingsPath;
    private readonly PanelOperationController operations;
    private KeyCode hotkey = KeyCode.F8;
    private PanelAccessSurface? openSurface;
    private bool disposed;
    private bool cursorStateCaptured;
    private bool priorCursorVisible;
    private CursorLockMode priorCursorLockMode;
    private GameObject? priorSelectedGameObject;
    private GameObject? inputBlockSource;
    private InputManager? blockedInputManager;
    private PauseMenu? ownedPauseMenu;
    private PauseMenu? panelPauseMenu;
    private string presentedGeneration = string.Empty;
    private bool projectionDirty;
    private int openAfterFrame;
    private float nextProjectionRefresh;
    private StatisticsPanelProjection? presentedProjection;
    private DiagnosticsPresentation? diagnostics;
    private long presentedRevision = -1, diagnosticsRevision = -1;
    private ProfileSaveReceipt? diagnosticReceipt;
    private bool diagnosticWriteFailed;
    private DiagnosticEntry? lastDiagnosticEntry;
    private int diagnosticCount = -1;
    private NativeMenuIntegrationState lastMainMenu, lastBaseMenu, lastShortcutState;
    private bool capturingHotkey;
    private string hotkeyWarning = "";
    private int hotkeyCaptureFrame;
#if UDS_ENCOUNTER_DIAGNOSTICS
    internal bool EncounterPreviewIsOpen { get; set; }
    internal bool CanShowEncounterPreview => lifecycle.IsOpen && !operations.ModalVisible && !capturingHotkey;
#endif
#if UDS_PERFORMANCE_DIAGNOSTICS
    public bool TimingPanelIsOpen => lifecycle.IsOpen;
#endif

    public NativeStatisticsPanel(NativeProfileCoordinator coordinator)
    {
        this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        shortcutGuard = new NativePanelShortcutGuard(message => coordinator.ReportUiDiagnostic(message, "Warning"));
        settingsPath = Path.Combine(coordinator.DataRoot, "settings.json");
        LoadSettings();
        nativeUi = new NativeUiIntegration(coordinator, RequestOpen, HandleSurfaceClosed);
        nativeUi.Initialize();
        entityNames.Changed += HandleLanguageChanged;
        operations = new PanelOperationController(interaction, () => coordinator.CurrentGenerationId,
            () => coordinator.HasPendingProfileTransition, coordinator.BeginExportCurrent, coordinator.ResetCurrent,
            () => coordinator.LastUserResetAttempt,
            TryCopyPath, HandleOperationNotice);
        operations.ConfigureRestore(coordinator.ListRestoreSourcesAsync, coordinator.PreviewRestoreAsync, coordinator.RestoreCurrent);
        coordinator.ProfileChanging += HandleProfileChanging;
        coordinator.ProfileChanged += HandleProfileChanged;
    }

    public void Tick()
    {
        if (disposed) return;
#if UDS_PERFORMANCE_DIAGNOSTICS
        using var timing = NativeHotPathDiagnostics.Measure(
            lifecycle.IsOpen ? NativeHotPathArea.PanelOpenTick : NativeHotPathArea.PanelClosedTick);
#endif
        operations.Tick();
        if (lifecycle.IsOpen && !shell.IsUsable)
        {
            Close();
            shell.Dispose();
            return;
        }
        if (lifecycle.IsOpen && openSurface == PanelAccessSurface.BasePauseMenu
            && (panelPauseMenu == null || PauseMenu.Instance != panelPauseMenu || !panelPauseMenu.Shown))
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

        if (lifecycle.IsOpen && Time.frameCount > openAfterFrame && !coordinator.HasPendingProfileTransition
            && StatisticsPanelProjectionFactory.HasProvableGeneration(coordinator.Current, coordinator.CurrentGenerationId))
        {
            try
            {
                if (killDistances.Refresh(coordinator.Current!) && presentedProjection != null && !projectionDirty
                    && presentedGeneration == coordinator.CurrentGenerationId && presentedRevision == coordinator.Current!.Revision)
                {
                    BindKillDistances(presentedProjection);
                    shell.RefreshKillDistanceHighlights(presentedProjection);
                }
            }
            catch (Exception exception)
            {
                var surface = openSurface ?? PanelAccessSurface.Hotkey;
                Close();
                ReportShellFailure(surface, $"highlight refresh failed: {exception.GetType().Name}: {exception.Message}");
                return;
            }
        }

        if (lifecycle.IsOpen && Time.frameCount > openAfterFrame && !coordinator.HasPendingProfileTransition
            && (projectionDirty || Time.unscaledTime >= nextProjectionRefresh)
            && (projectionDirty || presentedGeneration != coordinator.CurrentGenerationId || presentedRevision != coordinator.Current?.Revision))
        {
            var current = coordinator.Current;
            var generation = coordinator.CurrentGenerationId;
            if (!StatisticsPanelProjectionFactory.HasProvableGeneration(current, generation))
            {
                Close();
                nativeUi.ShowToast(UiText.Get("ui.profile_unavailable"));
                return;
            }
            try
            {
#if UDS_PERFORMANCE_DIAGNOSTICS
                using var projectionTiming = NativeHotPathDiagnostics.Measure(NativeHotPathArea.PanelProjectionRefresh);
#endif
                var projection = StatisticsPanelProjectionFactory.Create(current!, coordinator.CurrentEconomyCapabilities,
                    coordinator.CurrentCraftingCapabilities, coordinator.CurrentWorldTimeCapabilities, entityNames.Names);
                BindKillDistances(projection);
                shell.RefreshProjection(projection, generation);
                presentedProjection = projection;
            }
            catch (Exception exception)
            {
                var surface = openSurface ?? PanelAccessSurface.Hotkey;
                Close();
                ReportShellFailure(surface, $"projection refresh failed: {exception.GetType().Name}: {exception.Message}");
                return;
            }
            presentedGeneration = generation;
            presentedRevision = current!.Revision;
            projectionDirty = false;
            nextProjectionRefresh = Time.unscaledTime + .25f;
        }

        if (lifecycle.IsOpen)
        {
            shortcutGuard.Refresh();
            RefreshDiagnostics();
            shell.SyncModal(operations.ModalVisible, capturingHotkey, diagnostics?.ProfileLabel ?? UiText.Get("ui.unavailable"), hotkeyWarning);
        }

        if (lifecycle.IsOpen && !shell.Tick(out var layoutError))
        {
            var surface = openSurface ?? PanelAccessSurface.Hotkey;
            Close();
            ReportShellFailure(surface, layoutError ?? "unknown retained-mode layout failure");
            return;
        }

#if UDS_ENCOUNTER_DIAGNOSTICS
        // The evidence viewer borrows this panel's input/cursor ownership. Its
        // full-screen raycast shield blocks clicks; suppress keyboard navigation too.
        // F5 has preview priority over a user-configured F5 panel toggle. F6 is
        // reserved for capture controls in this opt-in build only.
        if (Input.GetKeyDown(KeyCode.F6) || CanShowEncounterPreview && Input.GetKeyDown(KeyCode.F5)) return;
        if (lifecycle.IsOpen && EncounterPreviewIsOpen)
        {
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(hotkey)) Close();
            return;
        }
#endif
        if (lifecycle.IsOpen && Input.GetKeyDown(KeyCode.Escape))
        {
            if (operations.CancelConfirmation()) return;
            if (capturingHotkey) { CancelHotkeyCapture(); return; }
            Close();
            return;
        }

        if (lifecycle.IsOpen && shell.ModalVisible)
        {
            if (Input.GetKeyDown(KeyCode.Tab)) shell.MoveModalFocus(Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift));
            if (capturingHotkey) CaptureHotkeyInput();
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
    }

    private bool RequestOpen(PanelAccessSurface surface)
    {
        try { return TryOpen(surface); }
        catch (Exception exception)
        {
            Close();
            ReportShellFailure(surface, $"panel activation failed: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
        finally
        {
            // Native pause activation can succeed before canvas/shell activation
            // fails. Roll back our input and pause ownership on every failed open.
            if (!lifecycle.IsOpen)
            {
                try { RestoreFocusAndCursor(); }
                finally { ReleaseOwnedPause(); }
            }
        }
    }

    private bool TryOpen(PanelAccessSurface surface)
    {
#if UDS_PERFORMANCE_DIAGNOSTICS
        using var timing = NativeHotPathDiagnostics.Measure(NativeHotPathArea.PanelOpen);
#endif
        if (disposed) return false;
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
            return false;
        }

        var profile = coordinator.Current;
        if (coordinator.HasPendingProfileTransition || !StatisticsPanelProjectionFactory.HasProvableGeneration(profile, coordinator.CurrentGenerationId))
        {
            nativeUi.ShowToast(UiText.Get("ui.profile_unavailable"));
            return false;
        }

        if (lifecycle.IsOpen)
        {
            if (surface != PanelAccessSurface.BasePauseMenu || openSurface == surface)
            {
                shell.SetSelectedTab(interaction.SelectedTab);
                return shell.IsUsable;
            }
            // A hotkey-opened shell can belong to the gameplay canvas, below the
            // pause menu. Reopen on the activated menu's canvas instead of hiding there.
            Close();
        }

        if (surface == PanelAccessSurface.Hotkey && LevelManager.Instance?.IsBaseLevel == true
            && !Resources.FindObjectsOfTypeAll<MainMenu>().Any(menu => menu != null
                && menu.gameObject.activeInHierarchy && menu.gameObject.scene.IsValid()))
        {
            var pause = PauseMenu.Instance;
            if (pause == null) throw new InvalidOperationException("Duckov's base pause menu is unavailable.");
            CaptureFocusAndCursor();
            if (!pause.Shown)
            {
                // Keep the exact native owner before Show: a subscriber can fail
                // after Duckov has already opened the menu.
                ownedPauseMenu = pause;
                PauseMenu.Show();
            }
            if (PauseMenu.Instance != pause || !pause.Shown)
                throw new InvalidOperationException("Duckov's base pause menu did not remain open.");
            surface = PanelAccessSurface.BasePauseMenu;
        }
        if (surface == PanelAccessSurface.BasePauseMenu) panelPauseMenu = PauseMenu.Instance;

        if (!nativeUi.TryResolvePanelCanvas(surface, out var canvas) || canvas == null)
        {
            ReportShellFailure(surface, "no active supported screen-space Duckov canvas was found");
            return false;
        }

        var reuse = shell.CanReuse(canvas, coordinator.CurrentGenerationId);
        if (!reuse)
        {
            shell.Dispose();
            entityNames.Invalidate();
            presentedProjection = null;
            presentedRevision = -1;
            projectionDirty = true;
        }
        openAfterFrame = Time.frameCount;
        nextProjectionRefresh = 0;
        killDistances.RetryFailed();
        CaptureFocusAndCursor();
        if (!lifecycle.TryOpen())
        {
            RestoreFocusAndCursor();
            return false;
        }

        if (reuse)
        {
            try { shell.Show(interaction.SelectedTab); }
            catch (Exception exception)
            {
                Close(); shell.Dispose();
                ReportShellFailure(surface, $"cached shell activation failed: {exception.Message}");
                return false;
            }
        }
        else if (!shell.TryCreate(
                canvas,
                new StatisticsPanelProjection { Profile = profile!, Names = entityNames.Names },
                interaction.SelectedTab,
                null,
                operations,
                BeginHotkeyCapture,
                CancelHotkeyCapture,
                CopyExportPath,
                CopyDataPath,
                HandleTabSelected,
                Close,
                out var error))
        {
            lifecycle.Close();
            RestoreFocusAndCursor();
            ReportShellFailure(surface, error ?? "unknown retained-mode construction failure");
            return false;
        }
        openSurface = surface;
        presentedGeneration = coordinator.CurrentGenerationId;
        diagnosticsRevision = -1;
        return true;
    }

    private void HandleLanguageChanged()
    {
        projectionDirty = true;
        // Rebuild on Tick, after all language callbacks have applied translation
        // overrides, even when the profile revision and runtime evidence are unchanged.
        diagnosticsRevision = -1;
        shell.RefreshStaticText();
    }

    private void HandleProfileChanging()
    {
        killDistances.Reset();
        operations.DismissExportResult();
        operations.CancelConfirmation(); capturingHotkey = false;
        projectionDirty = true;
        // A cached view must never show a previous save/reset generation.
        Close();
        shell.Dispose();
        presentedProjection = null;
        presentedGeneration = string.Empty;
        presentedRevision = -1;
        entityNames.Invalidate();
    }

    private void HandleProfileChanged() => projectionDirty = true;

    private void BindKillDistances(StatisticsPanelProjection projection)
    {
        projection.KillDistances = killDistances.Value;
        projection.KillDistancesLoading = killDistances.Loading;
        projection.KillDistancesFailed = killDistances.Failed;
    }

    private void HandleTabSelected(StatisticsPanelTab tab)
    {
        if (operations.ModalVisible || capturingHotkey) return;
        RetainedTabSelectionPolicy.SelectAndSynchronize(interaction, shell.SetSelectedTab, tab);
    }

    private DiagnosticsRuntimeSnapshot CaptureDiagnosticsRuntime() => new()
    {
        GenerationId = coordinator.CurrentGenerationId,
        DataRoot = coordinator.DataRoot,
        Hotkey = hotkey.ToString(),
        GameVersion = Application.version,
        HarmonyLoaded = ReflectiveHarmonyPatcher.IsHarmonyLoaded,
        OpenDetail = coordinator.LastOpenStatus,
        SaveReceipt = coordinator.LastSaveReceipt,
        ProfilePersistenceFailed = coordinator.HasProfilePersistenceFailure,
        OpenResult = coordinator.LastOpenResult,
        MainMenu = nativeUi.MainMenuState,
        BaseMenu = nativeUi.BasePauseMenuState,
        ShortcutIsolation = shortcutGuard.State,
        Entries = coordinator.DiagnosticEntries,
        TransitionPending = coordinator.HasPendingProfileTransition
    };

    private void RefreshDiagnostics(bool force = false)
    {
        if (presentedProjection == null || coordinator.HasPendingProfileTransition) return;
        var entries = coordinator.DiagnosticEntries;
        var newest = entries.Count > 0 ? entries[entries.Count - 1] : null;
        var revision = coordinator.Current?.Revision ?? -1;
        if (!force && diagnosticsRevision == revision && diagnosticReceipt == coordinator.LastSaveReceipt
            && diagnosticWriteFailed == coordinator.HasProfilePersistenceFailure
            && lastDiagnosticEntry == newest && diagnosticCount == entries.Count
            && lastMainMenu == nativeUi.MainMenuState && lastBaseMenu == nativeUi.BasePauseMenuState
            && lastShortcutState == shortcutGuard.State) return;
#if UDS_PERFORMANCE_DIAGNOSTICS
        using var timing = NativeHotPathDiagnostics.Measure(NativeHotPathArea.PanelDiagnostics);
#endif
        diagnostics = DiagnosticsPresentationFactory.Create(presentedProjection, coordinator.CurrentGenerationId, CaptureDiagnosticsRuntime());
        shell.RefreshDiagnostics(diagnostics);
        diagnosticsRevision = revision; diagnosticReceipt = coordinator.LastSaveReceipt;
        diagnosticWriteFailed = coordinator.HasProfilePersistenceFailure;
        lastDiagnosticEntry = newest; diagnosticCount = entries.Count;
        lastMainMenu = nativeUi.MainMenuState; lastBaseMenu = nativeUi.BasePauseMenuState;
        lastShortcutState = shortcutGuard.State;
    }

    private void HandleOperationNotice(PanelOperationNotice notice)
    {
        diagnosticsRevision = -1;
        if (notice.Outcome == PanelOperationOutcome.Running) return;
        if (notice.Outcome == PanelOperationOutcome.Pending)
        {
            coordinator.ReportUiDiagnostic($"UI {notice.Operation} awaiting completion. " + notice.Detail, "Warning");
            nativeUi.ShowToast(UiText.Get("ui.diag_operation_pending"));
            return;
        }
        if (notice.Outcome == PanelOperationOutcome.Success || notice.Outcome == PanelOperationOutcome.ClipboardUnavailable)
        {
            if (notice.Operation == PanelOperation.Reset || notice.Operation == PanelOperation.Restore)
            {
                projectionDirty = true;
                coordinator.ReportUiDiagnostic($"UI {notice.Operation} completed; the previous UDS generation was archived.");
                nativeUi.ShowToast(UiText.Get(notice.Operation == PanelOperation.Restore ? "ui.restore_success" : "ui.diag_reset_success"));
            }
            else
            {
                coordinator.ReportUiDiagnostic($"M17 UI export completed for generation {notice.GenerationId}: {notice.Path}.");
                if (notice.Outcome == PanelOperationOutcome.ClipboardUnavailable)
                    coordinator.ReportUiDiagnostic("M17 UI clipboard unavailable after successful export. " + notice.Detail, "Warning");
                if (notice.PresentResult) nativeUi.ShowToast(UiText.Get(notice.Outcome == PanelOperationOutcome.Success ? "ui.diag_export_success" : "ui.diag_export_clipboard"));
            }
            return;
        }
        var prefix = notice.Operation == PanelOperation.Export ? "M17 UI export failed. "
            : notice.PriorProfileStillActive ? $"UI {notice.Operation} failed; previous profile remains active. "
            : $"UI {notice.Operation} could not be completed for the requested generation. ";
        coordinator.ReportUiDiagnostic(prefix + notice.Detail, "Error");
        if (notice.PresentResult) nativeUi.ShowToast(UiText.Get("ui.diag_operation_failure"));
    }

    private static bool TryCopyPath(string path)
    {
        try { GUIUtility.systemCopyBuffer = path; return GUIUtility.systemCopyBuffer == path; }
        catch (Exception) { return false; }
    }

    private bool CopyExportPath()
    {
        var path = operations.LastNotice?.Path;
        if (!operations.CanStart || string.IsNullOrWhiteSpace(path)) return false;
        if (TryCopyPath(path!)) return true;
        else
        {
            coordinator.ReportUiDiagnostic("M17 UI clipboard unavailable while copying the completed export location.", "Warning");
            nativeUi.ShowToast(UiText.Get("ui.diag_export_clipboard"));
            diagnosticsRevision = -1;
            return false;
        }
    }

    private bool CopyDataPath()
    {
        return operations.CanStart && TryCopyPath(coordinator.DataRoot);
    }

    private void BeginHotkeyCapture()
    {
        if (!lifecycle.IsOpen || !operations.CanStart) return;
        capturingHotkey = true; hotkeyWarning = ""; hotkeyCaptureFrame = Time.frameCount;
    }

    private void CancelHotkeyCapture() { capturingHotkey = false; hotkeyWarning = ""; }

    private void CaptureHotkeyInput()
    {
        if (!Input.anyKeyDown || Time.frameCount == hotkeyCaptureFrame) return;
        foreach (var candidate in HotkeyCandidates)
        {
            if (!Input.GetKeyDown(candidate)) continue;
            if (!PanelHotkeyPolicy.IsAllowed(candidate.ToString()))
            { hotkeyWarning = UiText.Get("ui.diag_hotkey_invalid"); return; }
            try
            {
                settingsStore.Save(settingsPath, new UserSettings { PanelHotkey = candidate.ToString() });
                hotkey = candidate; CancelHotkeyCapture(); diagnosticsRevision = -1;
            }
            catch (Exception exception)
            {
                hotkeyWarning = UiText.Get("ui.diag_hotkey_failed");
                coordinator.ReportUiDiagnostic("M17 UI hotkey change failed; the previous key remains active. "
                    + exception.GetType().Name + ": " + exception.Message, "Warning");
            }
            return;
        }
    }

    private void ReportShellFailure(PanelAccessSurface surface, string detail)
    {
        // A failed construction/bind is not a reusable cached view.
        shell.Dispose();
        presentedProjection = null;
        presentedRevision = -1;
        projectionDirty = true;
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
#if UDS_ENCOUNTER_DIAGNOSTICS
        EncounterPreviewIsOpen = false;
#endif
        if (!lifecycle.Close()) return;
        try
        {
            operations.DismissExportResult();
#if UDS_PERFORMANCE_DIAGNOSTICS
            using var timing = NativeHotPathDiagnostics.Measure(NativeHotPathArea.PanelClose);
#endif
            operations.CancelConfirmation(); capturingHotkey = false;
            shell.Hide();
        }
        finally
        {
            openSurface = null;
            diagnostics = null; diagnosticsRevision = -1;
            try { RestoreFocusAndCursor(); }
            finally { ReleaseOwnedPause(); }
        }
    }

    private void ReleaseOwnedPause()
    {
        var pause = ownedPauseMenu;
        ownedPauseMenu = null;
        panelPauseMenu = null;
        // Clear ownership before Hide raises onPauseMenuOff. Never close a
        // replacement menu or a pause that was already present when UDS opened.
        if (pause == null || PauseMenu.Instance != pause || !pause.Shown) return;
        try { PauseMenu.Hide(); }
        catch (Exception exception)
        {
            coordinator.ReportUiDiagnostic($"Native pause cleanup failed: {exception.GetType().Name}: {exception.Message}", "Warning");
        }
    }

    private void CaptureFocusAndCursor()
    {
        if (cursorStateCaptured) return;
        priorCursorVisible = Cursor.visible;
        priorCursorLockMode = Cursor.lockState;
        priorSelectedGameObject = GameManager.EventSystem?.currentSelectedGameObject;
        cursorStateCaptured = true;
        shortcutGuard.SetOpen(true);
        UIInputManager.OnCancelEarly += ConsumeNativeCancel;
        blockedInputManager = LevelManager.Instance?.InputManager;
        if (blockedInputManager != null)
        {
            inputBlockSource = new GameObject("UDS native menu input owner");
            InputManager.DisableInput(inputBlockSource);
        }
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        GameManager.EventSystem?.SetSelectedGameObject(null);
    }

    private void RestoreFocusAndCursor()
    {
        shortcutGuard.SetOpen(false);
        UIInputManager.OnCancelEarly -= ConsumeNativeCancel;
        if (inputBlockSource != null)
        {
            // Release only our native blocker; other open menus retain their ownership.
            if (blockedInputManager != null && LevelManager.Instance?.InputManager == blockedInputManager)
                InputManager.ActiveInput(inputBlockSource);
            UnityEngine.Object.Destroy(inputBlockSource);
            inputBlockSource = null;
            blockedInputManager = null;
        }
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

    private void ConsumeNativeCancel(UIInputEventData eventData)
    {
        if (cursorStateCaptured) eventData.Use();
    }

    private void LoadSettings()
    {
        try
        {
            var settings = settingsStore.Load(settingsPath).Value ?? new UserSettings();
            if (!Enum.TryParse(settings.PanelHotkey, ignoreCase: true, out hotkey) || !Enum.IsDefined(typeof(KeyCode), hotkey)
                || !PanelHotkeyPolicy.IsAllowed(hotkey.ToString()))
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
        coordinator.ProfileChanging -= HandleProfileChanging;
        coordinator.ProfileChanged -= HandleProfileChanged;
        Close();
        operations.Dispose();
        killDistances.Dispose();
        lifecycle.Dispose();
        shell.Dispose();
        nativeUi.Dispose();
        entityNames.Dispose();
        shortcutGuard.Dispose();
        RestoreFocusAndCursor();
        disposed = true;
    }
}
