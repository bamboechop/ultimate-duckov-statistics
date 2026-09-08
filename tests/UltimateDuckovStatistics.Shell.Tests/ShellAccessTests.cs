using TMPro;
using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.UI;
using UnityEngine;
using UnityEngine.UI;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace UltimateDuckovStatistics.Shell.Tests;

public sealed class ShellAccessTests : IDisposable
{
    [Fact]
    public void OpenShellRefreshesNativeMapNameOnLanguageChangeAndReopenWithoutProfileMutation()
    {
        var run = new UltimateDuckovStatistics.Core.Domain.RunSummary
        {
            RunId = "localized-run",
            SaveGenerationId = coordinator.CurrentGenerationId,
            StartingMapId = "duckov:map:Scene_Zero",
            StartingMapDisplayName = "Nullpunkt",
            StartingMapKnown = true,
            Outcome = UltimateDuckovStatistics.Core.Domain.RunOutcome.Extracted
        };
        coordinator.Current.Statistics.Runs.Add(run);
        SceneInfoCollection.Scenes["Scene_Zero"] = new() { ID = "Scene_Zero", DisplayNameRaw = "MapZero" };
        SodaCraft.Localizations.LocalizationManager.Translations[(SystemLanguage.English, "MapZero")] = "Ground Zero";
        SodaCraft.Localizations.LocalizationManager.Translations[(SystemLanguage.German, "MapZero")] = "Nullpunkt";
        SodaCraft.Localizations.LocalizationManager.SetLanguage(SystemLanguage.English);
        var original = System.Text.Json.JsonSerializer.Serialize(coordinator.Current);
        using var panel = new NativeStatisticsPanel(coordinator);
        Press(panel, KeyCode.F8);
        bool Has(string value) => GameObject.Live.SelectMany(go => go.GetComponents<TextMeshProUGUI>()).Any(t => t.text == value);
        Assert.True(Has("Ground Zero"));
        SodaCraft.Localizations.LocalizationManager.SetLanguage(SystemLanguage.German);
        panel.Tick();
        Assert.True(Has("Nullpunkt")); Assert.False(Has("Ground Zero"));
        Press(panel, KeyCode.Escape);
        SodaCraft.Localizations.LocalizationManager.SetLanguage(SystemLanguage.English);
        Press(panel, KeyCode.F8);
        Assert.True(Has("Ground Zero"));
        Assert.Equal(original, System.Text.Json.JsonSerializer.Serialize(coordinator.Current));
        SceneInfoCollection.Scenes.Clear(); SodaCraft.Localizations.LocalizationManager.Translations.Clear();
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
        diagnostics.onClick.Invoke();
        Assert.True(tabs.anchoredPosition.x < 0);
        Assert.True(Find("DiagnosticsView").activeInHierarchy);
        Assert.Contains(root.GetComponentsInChildren<TextMeshProUGUI>(true), text => text.text.Length > 240);

        ((RectTransform)canvas.transform).sizeDelta = new Vector2(960, 540);
        panel.Tick();
        Assert.True(root.activeInHierarchy);
        Assert.True(diagnostics.interactable);
        Assert.DoesNotContain(coordinator.Reports, report => report.Contains("unavailable", StringComparison.OrdinalIgnoreCase));
        Press(panel, KeyCode.Escape);
        Assert.True(root.Destroyed);
        Assert.Equal(0, UIInputManager.CancelListeners);
        Assert.Empty(InputManager.Blocks);
    }

    [Fact]
    public void RepeatedOpenCloseReleasesListenersMaterialAndInputOwnerAndClosedTicksDoNoMeasurement()
    {
        using var panel = new NativeStatisticsPanel(coordinator);
        var priorFocus = new GameObject("Prior native menu selection");
        GameManager.EventSystem!.SetSelectedGameObject(priorFocus);
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
        var baselineObjects = GameObject.Live.Count;
        for (var cycle = 0; cycle < 25; cycle++)
        {
            Press(panel, KeyCode.F8);
            var root = Find(RetainedDimmerPolicy.RootName);
            var tabButton = Find("OverviewTab").GetComponent<Button>();
            var ownedMaterial = Find("OverviewTabLabel").GetComponent<TextMeshProUGUI>().fontSharedMaterial;
            Assert.Equal(1, UIInputManager.CancelListeners);
            Assert.Single(InputManager.Blocks);
            Assert.True(Cursor.visible);
            Press(panel, KeyCode.F8);
            Assert.True(root.Destroyed);
            Assert.True(ownedMaterial.Destroyed);
            Assert.False(NativeHeaderTitleTypographyResolver.Typography.Material.Destroyed);
            Assert.Equal(0, tabButton.onClick.ListenerCount);
            Assert.Equal(baselineObjects, GameObject.Live.Count);
            Assert.Equal(0, UIInputManager.CancelListeners);
            Assert.Empty(InputManager.Blocks);
            Assert.Same(priorFocus, GameManager.EventSystem.currentSelectedGameObject);
            Assert.False(Cursor.visible);
            Assert.Equal(CursorLockMode.Locked, Cursor.lockState);
        }
        var measured = TextMeshProUGUI.Measurements;
        for (var tick = 0; tick < 10_000; tick++) panel.Tick();
        Assert.Equal(measured, TextMeshProUGUI.Measurements);
        panel.Dispose();
        Assert.Equal(0, coordinator.ProfileListeners);
        Assert.Equal(0, MainMenu.Listeners);
        Assert.Equal(0, PauseMenu.Listeners);
        Assert.Empty(coordinator.Reports);
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
        panel.Tick();
        Assert.True(root.Destroyed);
        Press(panel, KeyCode.F8);
        Assert.DoesNotContain(GameObject.Live, go => go.name == RetainedDimmerPolicy.RootName);
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
            Assert.True(firstRoot.Destroyed);
            PauseMenu.Show();
            injected.onClick.Invoke();
            var root = Find(RetainedDimmerPolicy.RootName);
            Assert.Same(pauseCanvas.transform, root.transform.parent);
            Assert.True(root.activeInHierarchy);
            Assert.Single(InputManager.Blocks);
            Press(panel, KeyCode.Escape);
            Assert.True(root.Destroyed);
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
        panel.Tick();
        Assert.Equal("Error", Writes());
        coordinator.HasProfilePersistenceFailure = false;
        panel.Tick();
        Assert.Equal("Pending", Writes());
    }

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
    }
    private static void Press(NativeStatisticsPanel panel, KeyCode key)
    {
        Input.Down.Add(key);
        try { panel.Tick(); }
        finally { Input.Down.Clear(); Time.frameCount++; }
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
