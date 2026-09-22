using TMPro;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.UI;
using UnityEngine;
using UnityEngine.UI;
using Xunit;

namespace UltimateDuckovStatistics.Shell.Tests;

public sealed partial class ShellAccessTests
{
    [Fact]
    public void FirstOpenPaintsShellBeforeProjectionAndBuildsOnlyTheSelectedView()
    {
        using var panel = new NativeStatisticsPanel(coordinator);
        Input.Down.Add(KeyCode.F8);
        try { Frame(panel); }
        finally { Input.Down.Clear(); }

        Assert.True(Find(RetainedDimmerPolicy.RootName).activeInHierarchy);
        Assert.True(Find("UdsViewLoading").activeInHierarchy);
        Assert.Null(Field<StatisticsPanelProjection?>(panel, "presentedProjection"));
        Assert.DoesNotContain(GameObject.Live, go => go.name == "OverviewSummaryScroll");
        Frame(panel);
        Assert.NotNull(Field<StatisticsPanelProjection?>(panel, "presentedProjection"));
        Assert.DoesNotContain(GameObject.Live, go => go.name == "OverviewSummaryScroll");
        Frame(panel);
        Assert.True(Find("OverviewSummaryScroll").activeInHierarchy);
        Assert.False(Find("UdsViewLoading").activeInHierarchy);
        Assert.DoesNotContain(GameObject.Live, go => go.name == "CombatContentView" || go.name == "RecordsContentView" || go.name == "RunsView");

        Find("CombatTab").GetComponent<Button>().onClick.Invoke();
        Find("RecordsTab").GetComponent<Button>().onClick.Invoke();
        Assert.True(Find("UdsViewLoading").activeInHierarchy);
        Frame(panel);
        var records = Find("RecordsContentView");
        Assert.True(records.activeInHierarchy);
        Assert.DoesNotContain(GameObject.Live, go => go.name == "CombatContentView");
        Find("OverviewTab").GetComponent<Button>().onClick.Invoke(); Tick(panel);
        Find("RecordsTab").GetComponent<Button>().onClick.Invoke(); Tick(panel);
        Assert.Same(records, Find("RecordsContentView"));
    }

    [Fact]
    public void ClosingBeforeDeferredBindingDoesNotBuildHiddenContent()
    {
        using var panel = new NativeStatisticsPanel(coordinator);
        typeof(NativeStatisticsPanel).GetMethod("RequestOpen", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(panel, new object[] { PanelAccessSurface.Hotkey });
        Find(RetainedBackControlPolicy.ButtonName).GetComponent<Button>().onClick.Invoke();
        var objects = GameObject.Live.Count;
        var measurements = TextMeshProUGUI.Measurements;
        Tick(panel, 100);
        Assert.Equal(objects, GameObject.Live.Count);
        Assert.Equal(measurements, TextMeshProUGUI.Measurements);
        Assert.Null(Field<StatisticsPanelProjection?>(panel, "presentedProjection"));
        Assert.Empty(InputManager.Blocks);
        Assert.DoesNotContain(GameObject.Live, go => go.name == "OverviewSummaryScroll");
        Press(panel, KeyCode.F8);
        Assert.True(Find("OverviewSummaryScroll").activeInHierarchy);
    }

    [Fact]
    public void ReopenShowsCachedViewThenRefreshesChangedStatistics()
    {
        using var panel = new NativeStatisticsPanel(coordinator);
        Press(panel, KeyCode.F8);
        var first = Field<StatisticsPanelProjection>(panel, "presentedProjection");
        var root = Find(RetainedDimmerPolicy.RootName);
        Press(panel, KeyCode.Escape);
        coordinator.Current.Revision++;
        Tick(panel, 100);
        Assert.Same(first, Field<StatisticsPanelProjection>(panel, "presentedProjection"));
        Input.Down.Add(KeyCode.F8);
        try { Frame(panel); }
        finally { Input.Down.Clear(); }
        Assert.Same(root, Find(RetainedDimmerPolicy.RootName));
        Assert.True(Find("OverviewSummaryScroll").activeInHierarchy);
        Assert.Same(first, Field<StatisticsPanelProjection>(panel, "presentedProjection"));
        Frame(panel);
        Assert.NotSame(first, Field<StatisticsPanelProjection>(panel, "presentedProjection"));
        Assert.Equal(coordinator.Current.Revision, Field<long>(panel, "presentedRevision"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProfileReplacementDiscardsCachedViewsAndReopensOnlyTheNewGeneration(bool closed)
    {
        using var panel = new NativeStatisticsPanel(coordinator);
        Press(panel, KeyCode.F8);
        var root = Find(RetainedDimmerPolicy.RootName);
        if (closed) Press(panel, KeyCode.Escape);
        coordinator.ChangeProfile(new ProfileDocument { GenerationId = "replacement", Statistics = new() { SaveGenerationId = "replacement" } });
        Assert.True(root.Destroyed);
        Assert.Empty(InputManager.Blocks);
        Assert.Null(Field<StatisticsPanelProjection?>(panel, "presentedProjection"));
        Tick(panel);
        Assert.DoesNotContain(GameObject.Live, go => go.name == RetainedDimmerPolicy.RootName);
        Press(panel, KeyCode.F8);
        Assert.NotSame(root, Find(RetainedDimmerPolicy.RootName));
        Assert.Equal("replacement", Field<string>(panel, "presentedGeneration"));
        Assert.Same(coordinator.Current, Field<StatisticsPanelProjection>(panel, "presentedProjection").Profile);
    }

    [Fact]
    public void HiddenLanguageChangeRefreshesRetainedChildBeforeItIsMeasuredOnReopen()
    {
        SodaCraft.Localizations.LocalizationManager.SetLanguage(SystemLanguage.English);
        using var panel = new NativeStatisticsPanel(coordinator);
        Press(panel, KeyCode.F8);
        Find("AboutTab").GetComponent<Button>().onClick.Invoke(); Tick(panel);
        var about = Find("AboutContentView");
        Press(panel, KeyCode.Escape);
        var measured = TextMeshProUGUI.Measurements;
        SodaCraft.Localizations.LocalizationManager.SetLanguage(SystemLanguage.German);
        Tick(panel);
        Assert.Equal(measured, TextMeshProUGUI.Measurements);
        Press(panel, KeyCode.F8);
        Assert.Same(about, Find("AboutContentView"));
        Assert.True(about.activeInHierarchy);
        Assert.Equal(UiText.GermanFallbacks["ui.about_translation"], Find("AboutInvitation").GetComponent<TextMeshProUGUI>().text);
        SodaCraft.Localizations.LocalizationManager.SetLanguage(SystemLanguage.English);
    }

    [Fact]
    public void RevisionsEveryFrameDoNotStarveDeferredBinding()
    {
        using var panel = new NativeStatisticsPanel(coordinator);
        Open(panel, PanelAccessSurface.Hotkey);
        Find("CombatTab").GetComponent<Button>().onClick.Invoke();
        for (var i = 0; i < 30; i++) { coordinator.Current.Revision++; Frame(panel); }
        Assert.True(Find("CombatContentView").activeInHierarchy);
        Assert.False(Find("UdsViewLoading").activeInHierarchy);
        Assert.InRange(coordinator.Current.Revision - Field<long>(panel, "presentedRevision"), 0, 13);
    }

    [Fact]
    public void OpeningOnAnotherNativeCanvasDisposesThePreviousCache()
    {
        using var panel = new NativeStatisticsPanel(coordinator);
        Press(panel, KeyCode.F8);
        var original = Find(RetainedDimmerPolicy.RootName);
        Press(panel, KeyCode.Escape);
        var pause = new GameObject("PauseMenuCanvas").AddComponent<Canvas>();
        pause.gameObject.AddComponent<GraphicRaycaster>();
        pause.gameObject.AddComponent<CanvasScaler>();
        ((RectTransform)pause.transform).sizeDelta = new Vector2(1280, 720);
        PreparePauseMenu(pause);
        Open(panel, PanelAccessSurface.BasePauseMenu);
        Assert.True(original.Destroyed);
        var replacement = Find(RetainedDimmerPolicy.RootName);
        Assert.NotSame(original, replacement);
        Assert.Same(pause.transform, replacement.transform.parent);
        Assert.Single(InputManager.Blocks);
        Assert.Empty(coordinator.Reports);
    }
}
