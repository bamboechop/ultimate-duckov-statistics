using TMPro;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.UI;
using UnityEngine;
using UnityEngine.UI;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace UltimateDuckovStatistics.Shell.Tests;

public sealed partial class ShellAccessTests : IDisposable
{
#if !UDS_ENCOUNTER_DIAGNOSTICS
    [Theory]
    [InlineData(KeyCode.F5)]
    [InlineData(KeyCode.F6)]
    public void OrdinaryBuildAllowsFormerDiagnosticKeysAsPanelShortcuts(KeyCode key)
    {
        using var panel = new NativeStatisticsPanel(coordinator);
        Press(panel, KeyCode.F8);
        typeof(NativeStatisticsPanel).GetMethod("BeginHotkeyCapture", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(panel, null);
        Time.frameCount++; // The assignment key arrives after the click that begins capture.
        Press(panel, key);
        Assert.False(Field<bool>(panel, "capturingHotkey"));
        Assert.Equal(key, Field<KeyCode>(panel, "hotkey"));
        Press(panel, KeyCode.Escape);
        Press(panel, key);
        Assert.True(Find(RetainedDimmerPolicy.RootName).activeInHierarchy);
    }
#endif

    [Theory]
    [InlineData(2560, 1440)]
    [InlineData(1280, 720)]
    [InlineData(720, 480)]
    public void OverviewDistanceRowsAndEconomyRemainReachableAndKeepScrollFocusOnRefresh(int width, int height)
    {
        ((RectTransform)canvas.transform).sizeDelta = new Vector2(width, height);
        using var panel = new NativeStatisticsPanel(coordinator);
        Press(panel, KeyCode.F8);
        var scroll = Find("OverviewSummaryScroll").GetComponent<ScrollRect>();
        Assert.True(scroll.content.rect.height > ((RectTransform)scroll.transform).rect.height);
        Find("OverviewTab").GetComponent<RunsFocusHandler>().Move!(UnityEngine.EventSystems.MoveDirection.Down);
        Assert.Same(scroll.gameObject, GameManager.EventSystem!.currentSelectedGameObject);
        for (var i = 0; i < 30; i++) scroll.GetComponent<RunsFocusHandler>().Move!(UnityEngine.EventSystems.MoveDirection.Down);
        var offset = scroll.content.anchoredPosition.y;
        Assert.Equal(scroll.content.rect.height - ((RectTransform)scroll.transform).rect.height, offset, 3);
        var shell = Field<RetainedStatisticsShell>(panel, "shell");
        shell.RefreshProjection(new StatisticsPanelProjection { Profile = coordinator.Current }, coordinator.CurrentGenerationId); Tick(panel);
        scroll = Find("OverviewSummaryScroll").GetComponent<ScrollRect>();
        Assert.Equal(offset, scroll.content.anchoredPosition.y, 3);
        Assert.Same(scroll.gameObject, GameManager.EventSystem.currentSelectedGameObject);
        Assert.Equal(3, scroll.GetComponentsInChildren<CombatTooltipTrigger>().Length);
        for (var i = 0; i < 30; i++) scroll.GetComponent<RunsFocusHandler>().Move!(UnityEngine.EventSystems.MoveDirection.Up);
        Assert.Same(Find("OverviewTab"), GameManager.EventSystem.currentSelectedGameObject);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AboutTranslationInvitationRefreshesWithoutActionButtons(bool visibleDuringSwitch)
    {
        using var panel = new NativeStatisticsPanel(coordinator); Press(panel, KeyCode.F8);
        Find("AboutTab").GetComponent<Button>().onClick.Invoke();
        Tick(panel);
        if (!visibleDuringSwitch) Find("OverviewTab").GetComponent<Button>().onClick.Invoke();
        Tick(panel);
        var invitation = Find("AboutInvitation").GetComponent<TextMeshProUGUI>();
        Assert.Equal(UiText.EnglishFallbacks["ui.about_translation"], invitation.text);
        try
        {
            SodaCraft.Localizations.LocalizationManager.SetLanguage(SystemLanguage.German);
            Tick(panel);
            if (!visibleDuringSwitch) Find("AboutTab").GetComponent<Button>().onClick.Invoke();
            Tick(panel);
            Assert.Equal(UiText.GermanFallbacks["ui.about_translation"], invitation.text);
            Assert.Empty(Find("AboutContentView").GetComponentsInChildren<Button>());
            Assert.False(invitation.raycastTarget);
            SodaCraft.Localizations.LocalizationManager.SetLanguage(SystemLanguage.English);
            Tick(panel);
            Assert.Equal(UiText.EnglishFallbacks["ui.about_translation"], invitation.text);
        }
        finally { SodaCraft.Localizations.LocalizationManager.SetLanguage(SystemLanguage.English); }
    }

    [Fact]
    public void AboutLongLocalizedContentCanBeScrolledAndFocusedAcrossResize()
    {
        using var panel = new NativeStatisticsPanel(coordinator);
        var longText = string.Join("\n", Enumerable.Repeat("Long localized About content", 40));
        UiText.ConfigureNativeResolver(key => key == "ui.about_description" || key == "ui.about_translation" ? longText : null);
        Press(panel, KeyCode.F8);
        Find("AboutTab").GetComponent<Button>().onClick.Invoke(); Tick(panel);
        var scroll = Find("AboutScroll").GetComponent<ScrollRect>();
        Assert.True(scroll.content.rect.height > ((RectTransform)scroll.transform).rect.height);
        Find("AboutTab").GetComponent<RunsFocusHandler>().Move!(UnityEngine.EventSystems.MoveDirection.Down);
        Assert.Same(scroll.gameObject, GameManager.EventSystem!.currentSelectedGameObject);
        scroll.GetComponent<RunsFocusHandler>().Move!(UnityEngine.EventSystems.MoveDirection.Down);
        Assert.True(scroll.content.anchoredPosition.y > 0);
        scroll.GetComponent<RunsFocusHandler>().Move!(UnityEngine.EventSystems.MoveDirection.Right);
        Assert.Same(scroll.gameObject, GameManager.EventSystem.currentSelectedGameObject);
        ((RectTransform)canvas.transform).sizeDelta = new Vector2(960, 540); Tick(panel);
        Assert.True(Find("AboutContentView").activeInHierarchy);
        Assert.Same(scroll.gameObject, GameManager.EventSystem.currentSelectedGameObject);
        var rect = (RectTransform)Find("AboutInvitation").transform;
        Assert.True(-rect.anchoredPosition.y + rect.rect.height <= scroll.content.rect.height);
        Assert.True(rect.rect.width <= scroll.content.rect.width - 60 + .01f);
        scroll.GetComponent<RunsFocusHandler>().Move!(UnityEngine.EventSystems.MoveDirection.Left);
        Assert.Same(Find("AboutTab"), GameManager.EventSystem.currentSelectedGameObject);
    }

    [Fact]
    public void AboutPrecedesDiagnosticsAndIsStaticWithAnEmptyProfileAndUnavailableAdaptersAcrossReopen()
    {
        var original = System.Text.Json.JsonSerializer.Serialize(coordinator.Current);
        using var panel = new NativeStatisticsPanel(coordinator);
        for (var cycle = 0; cycle < 3; cycle++)
        {
            Press(panel, KeyCode.F8);
            var tabRoot = Find("HorizontalTabs");
            Assert.Equal(10, tabRoot.transform.childCount);
            Assert.Equal("AboutTab", tabRoot.transform.GetChild(8).gameObject.name);
            Assert.Equal("DiagnosticsTab", tabRoot.transform.GetChild(9).gameObject.name);
            Find("AboutTab").GetComponent<Button>().onClick.Invoke(); Tick(panel);
            var about = Find("AboutContentView");
            foreach (var specification in RetainedTabStripPolicy.Specifications)
            {
                Find(specification.BackgroundName).GetComponent<Button>().onClick.Invoke();
                Tick(panel);
                Assert.Equal(specification.Tab, Field<PanelInteractionState>(panel, "interaction").SelectedTab);
                Assert.Equal(specification.Tab == StatisticsPanelTab.About, about.activeInHierarchy);
            }
            Find("AboutTab").GetComponent<Button>().onClick.Invoke(); Tick(panel);
            Assert.Same(about, Find("AboutContentView"));
            var objects = GameObject.Live.Count; var measurements = TextMeshProUGUI.Measurements;
            for (var i = 0; i < 30; i++) Tick(panel);
            Assert.Equal(objects, GameObject.Live.Count);
            Assert.Equal(measurements, TextMeshProUGUI.Measurements);
            Assert.Empty(about.GetComponentsInChildren<Button>());
            Find("AboutTab").GetComponent<RunsFocusHandler>().Move!(UnityEngine.EventSystems.MoveDirection.Down);
            Assert.Same(Find("AboutScroll"), GameManager.EventSystem!.currentSelectedGameObject);
            var scrollFocus = Find("AboutScroll").GetComponent<RunsFocusHandler>();
            scrollFocus.Move!(UnityEngine.EventSystems.MoveDirection.Up);
            Assert.Same(Find("AboutTab"), GameManager.EventSystem.currentSelectedGameObject);
            Press(panel, KeyCode.Escape);
            Assert.False(about.activeInHierarchy);
            Assert.NotNull(scrollFocus.Move);
        }
        Assert.Equal(original, System.Text.Json.JsonSerializer.Serialize(coordinator.Current));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ExportConfirmationCannotOutlivePanelOrProfile(bool profileChange, bool completeBeforeBoundary)
    {
        var completion = new TaskCompletionSource<UltimateDuckovStatistics.Core.Export.ProfileExportResult>();
        coordinator.Export = () => completion.Task;
        using var panel = new NativeStatisticsPanel(coordinator); Press(panel, KeyCode.F8);
        var operations = Field<PanelOperationController>(panel, "operations");
        Assert.True(operations.RequestExport()); Tick(panel);
        void Complete() { completion.SetResult(new("finished-export", Array.Empty<string>())); Tick(panel); }
        if (completeBeforeBoundary) { Complete(); Assert.NotNull(operations.LastNotice); }
        if (profileChange)
        {
            var next = new ProfileDocument { GenerationId = "next", Statistics = new() { SaveGenerationId = "next" } };
            coordinator.ChangeProfile(next); Tick(panel);
        }
        else { Press(panel, KeyCode.Escape); Press(panel, KeyCode.F8); }
        Assert.Null(operations.LastNotice);
        var messages = Duckov.UI.NotificationText.Messages.Count;
        if (!completeBeforeBoundary) Complete();
        Assert.Null(operations.LastNotice); Assert.Equal(PanelOperation.None, operations.Current);
        Assert.Equal(messages, Duckov.UI.NotificationText.Messages.Count);
        Assert.Contains(coordinator.Reports, r => r.Contains("export completed", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Level_GroundZero_1", "Level_GroundZero", "Ground Zero", "Nullpunkt")]
    [InlineData("Level_Farm_JLab_Facility", "Level_Farm_JLab_Facility", "J-Lab Facility", "J-Lab-Anlage")]
    public void OpenShellRefreshesNativeMapNameOnLanguageChangeAndReopenWithoutProfileMutation(
        string sceneId, string nameKey, string englishName, string germanName)
    {
        var run = new UltimateDuckovStatistics.Core.Domain.RunSummary
        {
            RunId = "localized-run",
            SaveGenerationId = coordinator.CurrentGenerationId,
            StartingMapId = "duckov:map:" + sceneId,
            StartingMapDisplayName = germanName,
            StartingMapKnown = true,
            Outcome = UltimateDuckovStatistics.Core.Domain.RunOutcome.Extracted
        };
        coordinator.Current.Statistics.Runs.Add(run);
        SceneInfoCollection.Scenes[sceneId] = new() { ID = sceneId, DisplayNameRaw = nameKey };
        SodaCraft.Localizations.LocalizationManager.Translations[(SystemLanguage.English, nameKey)] = englishName;
        SodaCraft.Localizations.LocalizationManager.Translations[(SystemLanguage.German, nameKey)] = germanName;
        SodaCraft.Localizations.LocalizationManager.SetLanguage(SystemLanguage.English);
        var original = System.Text.Json.JsonSerializer.Serialize(coordinator.Current);
        using var panel = new NativeStatisticsPanel(coordinator);
        Press(panel, KeyCode.F8);
        bool Has(string value) => GameObject.Live.SelectMany(go => go.GetComponents<TextMeshProUGUI>()).Any(t => t.text == value);
        Assert.True(Has(englishName));
        SodaCraft.Localizations.LocalizationManager.SetLanguage(SystemLanguage.German);
        Tick(panel);
        Assert.True(Has(germanName)); Assert.False(Has(englishName));
        Press(panel, KeyCode.Escape);
        SodaCraft.Localizations.LocalizationManager.SetLanguage(SystemLanguage.English);
        Press(panel, KeyCode.F8);
        Assert.True(Has(englishName));
        Assert.Equal(original, System.Text.Json.JsonSerializer.Serialize(coordinator.Current));
        SceneInfoCollection.Scenes.Clear(); SodaCraft.Localizations.LocalizationManager.Translations.Clear();
    }

    [Fact]
    public void StatisticsLocalizationKeepsEnglishKeysAndSwitchesCompleteGermanTable()
    {
        Assert.Equal(
            UiText.EnglishFallbacks.Keys.OrderBy(key => key),
            UiText.GermanFallbacks.Keys.OrderBy(key => key));
        Assert.Equal(UiText.EnglishFallbacks.Count, UiText.GermanFallbacks.Count);

        try
        {
            SodaCraft.Localizations.LocalizationManager.SetLanguage(SystemLanguage.English);
            using var panel = new NativeStatisticsPanel(coordinator);
            Press(panel, KeyCode.F8);
            bool HasText(string value) => GameObject.Live
                .SelectMany(go => go.GetComponents<TextMeshProUGUI>())
                .Any(text => text.text == value);
            Assert.True(HasText("Overview"));
            Assert.Equal("Overview", UiText.Get("ui.overview"));
            Assert.Equal("Overview", SodaCraft.Localizations.LocalizationManager.GetPlainText(
                "ultimate-duckov-statistics.ui.overview"));

            SodaCraft.Localizations.LocalizationManager.SetLanguage(SystemLanguage.German);
            Assert.Equal("Übersicht", UiText.Get("ui.overview"));
            Tick(panel);
            Assert.True(HasText("Übersicht"));
            Assert.False(HasText("Overview"));
            Assert.Equal("Übersicht", UiText.Get("ui.overview"));
            Assert.Equal("Übersicht", SodaCraft.Localizations.LocalizationManager.GetPlainText(
                "ultimate-duckov-statistics.ui.overview"));

            SodaCraft.Localizations.LocalizationManager.SetLanguage(SystemLanguage.English);
            Assert.Equal("Overview", UiText.Get("ui.overview"));
        }
        finally
        {
            SodaCraft.Localizations.LocalizationManager.SetLanguage(SystemLanguage.English);
        }
    }

    private readonly string fixtureRoot = Path.Combine(Path.GetTempPath(), "uds-shell-" + Guid.NewGuid().ToString("N"));
    private readonly NativeProfileCoordinator coordinator;
    private readonly Canvas canvas;

    public ShellAccessTests()
    {
        Directory.CreateDirectory(fixtureRoot);
        coordinator = new NativeProfileCoordinator(fixtureRoot)
        {
            Current = new ProfileDocument { GenerationId = "fixture-v1", CreatedUtc = DateTime.UtcNow, UpdatedUtc = DateTime.UtcNow }
        };
        coordinator.Current.Statistics.SaveGenerationId = coordinator.CurrentGenerationId;
        canvas = new GameObject("Native menu canvas").AddComponent<Canvas>();
        ((RectTransform)canvas.transform).sizeDelta = new Vector2(1280, 720);
        canvas.gameObject.AddComponent<GraphicRaycaster>();
        canvas.gameObject.AddComponent<CanvasScaler>();
        LevelManager.Instance = new LevelManager();
        NativeRaidContext.InRaid = false;
        Input.Down.Clear();
    }

    [Theory]
    [InlineData((int)PanelAccessSurface.MainMenu)]
    [InlineData((int)PanelAccessSurface.BasePauseMenu)]
    [InlineData((int)PanelAccessSurface.Hotkey)]
    public void LongLocalizedLabelsKeepShellOpenAndLastTabReachable(int surfaceValue)
    {
        // Real localization, access, shell construction and measurement call chain.
        // Finite oversized measurements are supplied by the TMP boundary double.
        if ((PanelAccessSurface)surfaceValue == PanelAccessSurface.BasePauseMenu) PreparePauseMenu(canvas);
        using var panel = new NativeStatisticsPanel(coordinator);
        UiText.ConfigureNativeResolver(key => key + " " + new string('W', 240));
        Open(panel, (PanelAccessSurface)surfaceValue);
        var root = Assert.Single(GameObject.Live, go => go.name == RetainedDimmerPolicy.RootName);
        Assert.True(root.activeInHierarchy, string.Join("\n", coordinator.Reports));
        var viewport = Find("HorizontalTabViewport").GetComponent<RectTransform>();
        var tabs = Find("HorizontalTabs").GetComponent<RectTransform>();
        Assert.True(tabs.rect.width > viewport.rect.width);
        var diagnostics = Find("DiagnosticsTab").GetComponent<Button>();
        diagnostics.onClick.Invoke(); Tick(panel);
        Assert.True(tabs.anchoredPosition.x < 0);
        Assert.True(Find("DiagnosticsView").activeInHierarchy);
        Assert.Contains(root.GetComponentsInChildren<TextMeshProUGUI>(true), text => text.text.Length > 240);

        ((RectTransform)canvas.transform).sizeDelta = new Vector2(960, 540);
        Tick(panel);
        Assert.True(root.activeInHierarchy);
        Assert.True(diagnostics.interactable);
        Assert.DoesNotContain(coordinator.Reports, report => report.Contains("unavailable", StringComparison.OrdinalIgnoreCase));
        Press(panel, KeyCode.Escape);
        Assert.False(root.activeInHierarchy);
        Assert.Equal(0, UIInputManager.CancelListeners);
        Assert.Empty(InputManager.Blocks);
    }

    [Fact]
    public void RepeatedOpenCloseRetainsViewsButReleasesInputAndFinalDisposalReleasesResources()
    {
        using var panel = new NativeStatisticsPanel(coordinator);
        var priorFocus = new GameObject("Prior native menu selection");
        GameManager.EventSystem!.SetSelectedGameObject(priorFocus);
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
        var baselineObjects = GameObject.Live.Count;
        Press(panel, KeyCode.F8);
        var root = Find(RetainedDimmerPolicy.RootName);
        var overview = Find("OverviewSummaryScroll");
        var tabButton = Find("OverviewTab").GetComponent<Button>();
        var ownedMaterial = Find("OverviewTabLabel").GetComponent<TextMeshProUGUI>().fontSharedMaterial;
        var retainedObjects = GameObject.Live.Count;
        for (var cycle = 0; cycle < 25; cycle++)
        {
            Assert.Equal(1, UIInputManager.CancelListeners);
            Assert.Single(InputManager.Blocks);
            Assert.True(Cursor.visible);
            Press(panel, KeyCode.F8);
            Assert.False(root.activeInHierarchy);
            Assert.False(root.Destroyed);
            Assert.False(ownedMaterial.Destroyed);
            Assert.False(NativeHeaderTitleTypographyResolver.Typography.Material.Destroyed);
            Assert.Equal(1, tabButton.onClick.ListenerCount);
            Assert.Equal(retainedObjects - 1, GameObject.Live.Count); // The input-block owner is released on close.
            Assert.Equal(0, UIInputManager.CancelListeners);
            Assert.Empty(InputManager.Blocks);
            Assert.Same(priorFocus, GameManager.EventSystem.currentSelectedGameObject);
            Assert.False(Cursor.visible);
            Assert.Equal(CursorLockMode.Locked, Cursor.lockState);
            var measured = TextMeshProUGUI.Measurements;
            Tick(panel, 400);
            Assert.Equal(measured, TextMeshProUGUI.Measurements);
            if (cycle < 24)
            {
                Press(panel, KeyCode.F8);
                Assert.Same(root, Find(RetainedDimmerPolicy.RootName));
                Assert.Same(overview, Find("OverviewSummaryScroll"));
            }
        }
        panel.Dispose();
        Assert.True(root.Destroyed);
        Assert.True(ownedMaterial.Destroyed);
        Assert.Equal(0, tabButton.onClick.ListenerCount);
        Assert.Equal(baselineObjects, GameObject.Live.Count);
        Assert.Equal(0, coordinator.ProfileListeners);
        Assert.Equal(0, MainMenu.Listeners);
        Assert.Equal(0, PauseMenu.Listeners);
        Assert.Empty(coordinator.Reports);
    }

    [Theory]
    [InlineData(2560, 1440)]
    [InlineData(720, 480)]
    public void IncompleteSleepCaptureWrapsInsideItsMeasuredCardOnOpenAndResize(int width, int height)
    {
        coordinator.Current.Statistics.WorldTime.CompletedSleepSessions = 1;
        coordinator.Current.Statistics.WorldTime.SleepAdvancedTimeTicks = TimeSpan.FromMinutes(59).Ticks;
        ((RectTransform)canvas.transform).sizeDelta = new Vector2(width, height);
        using var panel = new NativeStatisticsPanel(coordinator);
        Press(panel, KeyCode.F8);
        AssertWorldTimeFits();
        ((RectTransform)canvas.transform).sizeDelta = new Vector2(960, 540);
        Tick(panel);
        AssertWorldTimeFits();

        void AssertWorldTimeFits()
        {
            var label = Find(RetainedOverviewWorldTimeStatisticsPolicy.Name).GetComponent<TextMeshProUGUI>();
            var card = Find(RetainedOverviewWorldTimeCardPolicy.Name).GetComponent<RectTransform>();
            Assert.Contains("00:59:00 (capture incomplete)", label.text);
            Assert.True(label.enableWordWrapping);
            Assert.True(label.enableKerning);
            Assert.False(label.enableAutoSizing);
            var inset = label.rectTransform.anchoredPosition.x;
            Assert.True(label.rectTransform.rect.width + 2 * inset <= card.rect.width + .001f);
            Assert.True(label.preferredHeight <= label.rectTransform.rect.height + .001f);
            Assert.True(label.rectTransform.rect.height + 2 * inset <= card.rect.height + .001f);
            Assert.True(card.rect.height > RetainedOverviewWorldTimeCardPolicy.HeightPixels * (label.fontSize / RetainedOverviewWorldTimeStatisticsPolicy.ReferenceFontSize));
            Assert.True(Find(RetainedDimmerPolicy.RootName).activeInHierarchy);
        }
    }

    [Fact]
    public void UnprovenGenerationAndRaidKeepSupportedAccessRestrictions()
    {
        using var panel = new NativeStatisticsPanel(coordinator);
        coordinator.Current.Statistics.SaveGenerationId = "different-generation";
        Press(panel, KeyCode.F8);
        Assert.DoesNotContain(GameObject.Live, go => go.name == RetainedDimmerPolicy.RootName);
        coordinator.Current.Statistics.SaveGenerationId = coordinator.CurrentGenerationId;
        Open(panel, PanelAccessSurface.MainMenu);
        var root = Find(RetainedDimmerPolicy.RootName);
        NativeRaidContext.InRaid = true;
        Tick(panel);
        Assert.False(root.activeInHierarchy);
        Press(panel, KeyCode.F8);
        Assert.False(root.activeInHierarchy);
        Assert.Empty(InputManager.Blocks);
    }

    [Fact]
    public void ReopenedPauseMenuButtonUsesItsAncestorCanvasAboveOtherNativeCanvases()
    {
        // Installed resources.assets: PauseMenu lives on the Menu child below
        // the PauseMenu canvas, while DialogueInteractiveCanvas is a lower overlay.
        canvas.gameObject.name = "DialogueInteractiveCanvas";
        canvas.sortingOrder = 100;
        var pauseCanvas = new GameObject("PauseMenu").AddComponent<Canvas>();
        pauseCanvas.sortingOrder = 10000;
        pauseCanvas.gameObject.AddComponent<GraphicRaycaster>();
        pauseCanvas.gameObject.AddComponent<CanvasScaler>();
        ((RectTransform)pauseCanvas.transform).sizeDelta = new Vector2(1280, 720);
        PreparePauseMenu(pauseCanvas);
        using var panel = new NativeStatisticsPanel(coordinator);
        var injected = Find("UltimateDuckovStatisticsButton").GetComponent<Button>();
        injected.onClick.Invoke();
        var firstRoot = Find(RetainedDimmerPolicy.RootName);
        Assert.Same(pauseCanvas.transform, firstRoot.transform.parent);
        for (var cycle = 0; cycle < 3; cycle++)
        {
            PauseMenu.Hide();
            Assert.False(firstRoot.activeInHierarchy);
            PauseMenu.Show();
            injected.onClick.Invoke();
            var root = Find(RetainedDimmerPolicy.RootName);
            Assert.Same(pauseCanvas.transform, root.transform.parent);
            Assert.True(root.activeInHierarchy);
            Assert.Single(InputManager.Blocks);
            Press(panel, KeyCode.Escape);
            Assert.False(root.activeInHierarchy);
            Assert.Empty(InputManager.Blocks);
        }
        Assert.DoesNotContain(coordinator.Reports, report => report.Contains("unavailable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void UnavailablePauseCanvasDoesNotUseAnUnrelatedHostOrClaimSuccessfulAccess()
    {
        PreparePauseMenu(canvas);
        var other = new GameObject("DialogueInteractiveCanvas").AddComponent<Canvas>();
        other.gameObject.AddComponent<GraphicRaycaster>();
        other.gameObject.AddComponent<CanvasScaler>();
        using var panel = new NativeStatisticsPanel(coordinator);
        var integration = (NativeUiIntegration)typeof(NativeStatisticsPanel)
            .GetField("nativeUi", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(panel)!;
        var injected = Find("UltimateDuckovStatisticsButton").GetComponent<Button>();
        canvas.enabled = false;
        injected.onClick.Invoke();
        Assert.DoesNotContain(GameObject.Live, go => go.name == RetainedDimmerPolicy.RootName);
        Assert.Equal(NativeMenuIntegrationState.Unavailable, integration.BasePauseMenuState);
        Assert.Contains(coordinator.Reports, report => report.Contains("entry could not open", StringComparison.Ordinal));
        Assert.Empty(InputManager.Blocks);
        canvas.enabled = true;
        injected.onClick.Invoke();
        Assert.Same(canvas.transform, Find(RetainedDimmerPolicy.RootName).transform.parent);
        Assert.Equal(NativeMenuIntegrationState.Available, integration.BasePauseMenuState);
    }

    [Fact]
    public void WriterFailureRefreshesDiagnosticsWithoutANewRevisionReceiptOrLogEntry()
    {
        using var panel = new NativeStatisticsPanel(coordinator);
        Press(panel, KeyCode.F8);
        DiagnosticsPresentation Snapshot() => (DiagnosticsPresentation)typeof(NativeStatisticsPanel)
            .GetField("diagnostics", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(panel)!;
        string Writes() => Assert.Single(Snapshot().Systems.Single(s => s.Id == "storage").ExtraRows).Value;
        Assert.Equal("Pending", Writes());
        coordinator.HasProfilePersistenceFailure = true;
        Tick(panel);
        Assert.Equal("Error", Writes());
        coordinator.HasProfilePersistenceFailure = false;
        Tick(panel);
        Assert.Equal("Pending", Writes());
    }

    [Theory]
    [InlineData("OnUIInventoryInput")]
    [InlineData("OnUIMapInput")]
    [InlineData("OnUIQuestViewInput")]
    [InlineData("OnReloadInput")]
    public void NativeActionDispatchIsSuppressedOnlyWhileThePanelOwnsInput(string action)
    {
        var native = new CharacterInputControl();
        using var panel = new NativeStatisticsPanel(coordinator);
        native.Dispatch(action);
        Assert.Equal(1, native.Calls);
        for (var cycle = 0; cycle < 3; cycle++)
        {
            Press(panel, KeyCode.F8);
            native.Dispatch(action); native.Dispatch(action, performed: false);
            Assert.Equal(cycle + 1, native.Calls);
            var patches = HarmonyLib.Harmony.GetPatchInfo(typeof(CharacterInputControl).GetMethod(action)!)!;
            Assert.Single(patches.Prefixes);
            Assert.Equal(NativeMenuIntegrationState.Available, Field<NativePanelShortcutGuard>(panel, "shortcutGuard").State);
            // The native configured action is guarded, independent of its key binding.
            Input.Down.Add(KeyCode.LeftControl); Press(panel, KeyCode.Tab);
            Assert.True(Find("RunsView").activeInHierarchy);
            Input.Down.Add(KeyCode.LeftControl); Input.Down.Add(KeyCode.LeftShift); Press(panel, KeyCode.Tab);
            Assert.True(Find("OverviewContentView").activeInHierarchy);
            native.Dispatch(action);
            Assert.Equal(cycle + 1, native.Calls);
            Press(panel, KeyCode.Escape);
            native.Dispatch(action);
            Assert.Equal(cycle + 2, native.Calls);
        }
        Press(panel, KeyCode.F8);
        panel.Dispose();
        native.Dispatch(action);
        Assert.Equal(5, native.Calls);
        Assert.Empty(HarmonyLib.Harmony.GetPatchInfo(typeof(CharacterInputControl).GetMethod(action)!)!.Prefixes);
    }

    [Fact]
    public void NativeShortcutsStaySuppressedThroughHotkeyAndResetConfirmationModals()
    {
        var native = new CharacterInputControl();
        using var panel = new NativeStatisticsPanel(coordinator);
        Press(panel, KeyCode.F8);
        typeof(NativeStatisticsPanel).GetMethod("BeginHotkeyCapture", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(panel, null);
        Press(panel, KeyCode.Tab);
        native.Dispatch("OnUIInventoryInput");
        Assert.Equal(0, native.Calls);
        Press(panel, KeyCode.Escape);
        Assert.True(Find(RetainedDimmerPolicy.RootName).activeInHierarchy);
        Assert.False(Field<bool>(panel, "capturingHotkey"));
        Assert.True(Field<PanelOperationController>(panel, "operations").RequestResetConfirmation());
        Tick(panel);
        native.Dispatch("OnUIInventoryInput");
        Assert.Equal(0, native.Calls);
        Press(panel, KeyCode.Escape);
        Assert.True(Find(RetainedDimmerPolicy.RootName).activeInHierarchy);
        Press(panel, KeyCode.Escape);
        native.Dispatch("OnUIInventoryInput");
        Assert.Equal(1, native.Calls);
    }

    [Fact]
    public void FailedShellConstructionAndDestroyedShellReleaseNativeShortcuts()
    {
        var native = new CharacterInputControl();
        using var panel = new NativeStatisticsPanel(coordinator);
        NativeHeaderTitleTypographyResolver.Unavailable = true;
        try { Press(panel, KeyCode.F8); }
        finally { NativeHeaderTitleTypographyResolver.Unavailable = false; }
        Assert.DoesNotContain(GameObject.Live, go => go.name == RetainedDimmerPolicy.RootName);
        Assert.Empty(InputManager.Blocks);
        native.Dispatch("OnUIInventoryInput");
        Assert.Equal(1, native.Calls);
        Press(panel, KeyCode.F8);
        UnityEngine.Object.Destroy(Find(RetainedDimmerPolicy.RootName));
        Tick(panel);
        native.Dispatch("OnUIInventoryInput");
        Assert.Equal(2, native.Calls);
        Assert.Empty(InputManager.Blocks);
    }

    [Fact]
    public void IncompatibleShortcutPatchLeavesDiagnosticsAccessibleAndForeignOwnerUntouched()
    {
        var method = typeof(CharacterInputControl).GetMethod("OnUIInventoryInput")!;
        var foreign = new HarmonyLib.Harmony("fixture.foreign.shortcuts");
        foreign.Patch(method, new HarmonyLib.HarmonyMethod(typeof(ShellAccessTests).GetMethod(nameof(ForeignShortcut), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!), null, null, null);
        try
        {
            using var panel = new NativeStatisticsPanel(coordinator);
            Press(panel, KeyCode.F8);
            Find("DiagnosticsTab").GetComponent<Button>().onClick.Invoke(); Tick(panel);
            Assert.True(Find("DiagnosticsView").activeInHierarchy);
            Assert.Equal(NativeMenuIntegrationState.Unavailable, Field<NativePanelShortcutGuard>(panel, "shortcutGuard").State);
            var menu = Field<DiagnosticsPresentation>(panel, "diagnostics").Systems.Single(s => s.Id == "menu");
            Assert.Equal(DiagnosticsHealth.Limited, menu.Health);
            var runtime = (DiagnosticsRuntimeSnapshot)typeof(NativeStatisticsPanel)
                .GetMethod("CaptureDiagnosticsRuntime", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(panel, null)!;
            Assert.True(runtime.HarmonyLoaded);
            Assert.Equal("Limited", menu.Status); // A foreign patch is not a missing dependency.
            Assert.Contains(menu.ExtraRows, row => row.Label.Contains("reload", StringComparison.Ordinal) && row.Value == "Unavailable");
            for (var tick = 0; tick < 100; tick++) Tick(panel);
            Assert.Single(coordinator.Reports, report => report.Contains("shortcut isolation unavailable", StringComparison.Ordinal));
            panel.Dispose();
            Assert.Equal("fixture.foreign.shortcuts", Assert.Single(HarmonyLib.Harmony.GetPatchInfo(method)!.Prefixes).owner);
        }
        finally { foreign.UnpatchAll("fixture.foreign.shortcuts"); }
    }

    [Fact]
    public void RemovedGuardIsReportedAndFailedCleanupLeavesCallbacksPassiveUntilNextActivationRetries()
    {
        var native = new CharacterInputControl();
        var panel = new NativeStatisticsPanel(coordinator);
        try
        {
            Press(panel, KeyCode.F8);
            new HarmonyLib.Harmony("fixture.remove").UnpatchAll("at.bamboechop.ultimate-duckov-statistics.panel-shortcuts");
            Tick(panel);
            Assert.Equal(NativeMenuIntegrationState.Unavailable, Field<NativePanelShortcutGuard>(panel, "shortcutGuard").State);
            Assert.Contains(coordinator.Reports, report => report.Contains("patch state changed", StringComparison.Ordinal));
        }
        finally { panel.Dispose(); }
        using var replacement = new NativeStatisticsPanel(coordinator);
        Press(replacement, KeyCode.F8);
        HarmonyLib.Harmony.FailNextUnpatches(1);
        replacement.Dispose();
        native.Dispatch("OnUIInventoryInput");
        Assert.Equal(1, native.Calls);
        using var retry = new NativeStatisticsPanel(coordinator);
        Press(retry, KeyCode.F8);
        Assert.Equal(NativeMenuIntegrationState.Available, Field<NativePanelShortcutGuard>(retry, "shortcutGuard").State);
        Assert.Single(HarmonyLib.Harmony.GetPatchInfo(typeof(CharacterInputControl).GetMethod("OnUIInventoryInput")!)!.Prefixes);
        native.Dispatch("OnUIInventoryInput");
        Assert.Equal(1, native.Calls);
    }

    private static bool ForeignShortcut() => true;
    private static T Field<T>(object panel, string name) => (T)panel.GetType()
        .GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(panel)!;

    private static void PreparePauseMenu(Canvas host)
    {
        var menu = new GameObject("Menu"); menu.transform.SetParent(host.transform);
        PauseMenu.Instance = menu.AddComponent<PauseMenu>();
        var anchor = new GameObject("Options"); anchor.transform.SetParent(menu.transform);
        anchor.AddComponent<Button>();
        anchor.AddComponent<TextLocalizor>().Key = "UI_Menu_Options";
    }

    private static GameObject Find(string name) => Assert.Single(GameObject.Live, go => go.name == name);
    private static void Open(NativeStatisticsPanel panel, PanelAccessSurface surface)
    {
        if (surface == PanelAccessSurface.Hotkey) Press(panel, KeyCode.F8);
        else typeof(NativeStatisticsPanel).GetMethod("RequestOpen", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                .Invoke(panel, new object[] { surface });
        Tick(panel);
    }
    private static void Press(NativeStatisticsPanel panel, KeyCode key)
    {
        Input.Down.Add(key);
        try { Frame(panel); }
        finally { Input.Down.Clear(); }
        Tick(panel);
    }
    // Frame boundaries matter: opening, projection and binding deliberately run separately.
    private static void Frame(NativeStatisticsPanel panel)
    {
        Time.frameCount++;
        Time.unscaledTime += .02f;
        panel.Tick();
    }
    private static void Tick(NativeStatisticsPanel panel, int frames = 3)
    {
        for (var frame = 0; frame < frames; frame++) Frame(panel);
    }
    public void Dispose()
    {
        UiText.ConfigureNativeResolver(null);
        foreach (var go in GameObject.Live.ToArray()) UnityEngine.Object.Destroy(go);
        LevelManager.Instance = null;
        PauseMenu.Instance = null;
        Duckov.UI.NotificationText.Messages.Clear();
        NativeRaidContext.InRaid = false;
        Directory.Delete(fixtureRoot, recursive: true);
    }
}
