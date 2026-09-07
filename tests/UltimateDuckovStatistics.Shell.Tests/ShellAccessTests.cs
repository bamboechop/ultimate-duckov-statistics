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
        NativeUiIntegration.TargetCanvas = canvas;
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
        UiText.ConfigureNativeResolver(key => key + " " + new string('W', 240));
        using var panel = new NativeStatisticsPanel(coordinator);
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
        Assert.Empty(coordinator.Reports);
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
        Assert.True(NativeUiIntegration.Last.Disposed);
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

    private static GameObject Find(string name) => Assert.Single(GameObject.Live, go => go.name == name);
    private static void Open(NativeStatisticsPanel panel, PanelAccessSurface surface)
    {
        if (surface == PanelAccessSurface.Hotkey) Press(panel, KeyCode.F8);
        else NativeUiIntegration.Last.Activate(surface);
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
        NativeRaidContext.InRaid = false;
        Directory.Delete(fixtureRoot, recursive: true);
    }
}
