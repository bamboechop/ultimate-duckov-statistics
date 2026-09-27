using Saves;
using SodaCraft.Localizations;
using TMPro;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.UI;
using UnityEngine;
using UnityEngine.UI;
using Xunit;
using NativeObject = UnityEngine.Object;

namespace UltimateDuckovStatistics.Shell.Tests;

public sealed class NativePlayerKillFeedHudTests : IDisposable
{
    private readonly GameObject canvas;
    private readonly TimeOfDayDisplay display;
    private readonly RectTransform storm, weatherBlock;
    private readonly NativePlayerKillFeedHud hud;
    private readonly List<string> diagnostics = new();
    private KillFeedSettings settings = new();

    public NativePlayerKillFeedHudTests()
    {
        SceneLoader.FinishLoading(); Time.unscaledTime = 0;
        LocalizationManager.SetLanguage(SystemLanguage.English);
        UiText.ConfigureNativeResolver(key => LocalizationManager.CurrentLanguage == SystemLanguage.German
            ? UiText.GermanFallbacks.GetValueOrDefault(key) : UiText.EnglishFallbacks.GetValueOrDefault(key));
        canvas = new GameObject("KillFeedTestCanvas", typeof(RectTransform), typeof(Canvas));
        ((RectTransform)canvas.transform).sizeDelta = new Vector2(1920, 1080);
        display = Child(canvas.transform, "WeatherDisplay").AddComponent<TimeOfDayDisplay>();
        weatherBlock = (RectTransform)Child(display.transform, "WeatherTextBlock").transform;
        weatherBlock.TestWorldCorners = Corners(140, 930);
        display.weatherText = Child(weatherBlock, "Weather").AddComponent<TextMeshProUGUI>();
        display.weatherText.font = new TMP_FontAsset(); display.weatherText.fontSharedMaterial = new Material();
        storm = (RectTransform)Child(display.transform, "Storm").transform;
        storm.TestWorldCorners = Corners(12, 880); display.stormRoot = storm.gameObject;
        hud = new NativePlayerKillFeedHud(() => settings, diagnostics.Add);
    }

    [Fact]
    public void NativeStormBoundsControlAlignmentAndHiddenStormUsesWeatherBottom()
    {
        hud.Record(Kill("1"), 35.29); hud.Tick();
        var root = Root();
        Assert.Equal(12, root.localPosition.x); Assert.Equal(872, root.localPosition.y);
        Assert.False(root.GetComponent<CanvasGroup>().blocksRaycasts);
        Assert.True(root.GetComponent<LayoutElement>().ignoreLayout);
        Assert.Equal("bamboechop", Label("Attacker").text); Assert.Equal("Scavenger", Label("Victim").text);
        Assert.Equal(35.29.ToString("0.##", System.Globalization.CultureInfo.CurrentCulture) + " m", Label("Distance").text);
        Assert.Empty(root.GetComponentsInChildren<Button>(true));
        Assert.All(root.GetComponentsInChildren<Graphic>(true), item => Assert.False(item.raycastTarget));
        storm.gameObject.SetActive(false); hud.Tick();
        Assert.Equal(12, root.localPosition.x); Assert.Equal(922, root.localPosition.y);
        settings = settings with { AlignRight = true, OffsetX = -5, OffsetY = 10, Scale = 1.5f };
        hud.Tick();
        Assert.Equal(1903, root.localPosition.x); Assert.Equal(912, root.localPosition.y);
        Assert.Equal(1.5f, root.localScale.x);
        var row = root.GetChild(0) as RectTransform;
        Assert.Equal(-row!.sizeDelta.x, row.anchoredPosition.x);
    }

    [Fact]
    public void SettledHudDoesNotRediscoverOrRebindEveryFrameAndExpiresWithUnscaledTime()
    {
        hud.Record(Kill("1"), 5); hud.Tick();
        var searches = NativeObject.SceneSearches; var label = Label("Attacker"); var writes = label.TextWrites;
        for (var i = 0; i < 1000; i++) hud.Tick();
        Assert.Equal(searches, NativeObject.SceneSearches); Assert.Equal(writes, label.TextWrites);
        Time.unscaledTime = 10; hud.Tick(); Assert.False(Root().gameObject.activeSelf);
        hud.Record(Kill("2"), null); hud.Tick(); Assert.True(Root().gameObject.activeSelf);
        Assert.False(Label("Distance").gameObject.activeSelf);
    }

    [Fact]
    public void SaveAndSceneChangesClearEntriesAndNativeDestructionRecovers()
    {
        hud.Record(Kill("1"), 2); hud.Tick();
        NativeObject.Destroy(Root().gameObject); hud.Tick();
        Assert.True(Root().gameObject.activeSelf); // Rebound without replaying a callback.
        SavesSystem.SetFile(); hud.Tick(); Assert.False(Root().gameObject.activeSelf);
        hud.Record(Kill("2"), null); hud.Tick();
        SceneLoader.StartLoading(); hud.Tick();
        Assert.DoesNotContain(GameObject.Live, go => go.name == "UDSPlayerKillFeed");
        SceneLoader.FinishLoading(); hud.Tick(); Assert.False(Root().gameObject.activeSelf);
        display.gameObject.SetActive(false); hud.Record(Kill("3"), 2); hud.Tick();
        Assert.False(Root().gameObject.activeSelf);
    }

    [Fact]
    public void LanguageRefreshAndDisposeReleaseOwnedRowsAndSubscriptions()
    {
        hud.Record(Kill("1") with { PlayerDeaths = 1, KillsByYou = 0, AttackerId = "duckov:attacker:environment" }, null);
        hud.Tick(); Assert.Equal("Environment", Label("Attacker").text);
        LocalizationManager.SetLanguage(SystemLanguage.German); hud.Tick();
        Assert.Equal("Umgebung", Label("Attacker").text); Assert.Equal("bamboechop", Label("Victim").text);
        var beforeScenes = SceneLoader.Listeners; var beforeSaves = SavesSystem.Listeners;
        hud.Dispose(); hud.Dispose();
        Assert.Equal(beforeScenes - 2, SceneLoader.Listeners); Assert.Equal(beforeSaves - 2, SavesSystem.Listeners);
        hud.Record(Kill("late"), null); hud.Tick();
        Assert.DoesNotContain(GameObject.Live, go => go.name == "UDSPlayerKillFeed");
        Assert.False(canvas.Destroyed); Assert.False(display.Destroyed); Assert.Empty(diagnostics);
    }

    private RectTransform Root() => (RectTransform)Assert.Single(GameObject.Live, go => go.name == "UDSPlayerKillFeed").transform;
    private TextMeshProUGUI Label(string name) => Root().GetChild(0).Find(name)!.GetComponent<TextMeshProUGUI>();
    private static GameObject Child(Transform parent, string name)
    { var go = new GameObject(name, typeof(RectTransform)); go.transform.SetParent(parent); return go; }
    private static Vector3[] Corners(float x, float y) => new[] { new Vector3(x, y, 0), new Vector3(x, y + 40, 0), new Vector3(x + 300, y + 40, 0), new Vector3(x + 300, y, 0) };
    private static CombatRecorded Kill(string id) => new()
    {
        EventId = id,
        RunId = "run",
        SaveGenerationId = "save",
        GameplayContext = GameplayContext.Raid,
        IsFinalBlow = true,
        KillsByYou = 1,
        Ownership = CombatOwnership.Player,
        TargetIsEnemy = true,
        TargetId = "target",
        TargetDisplayName = "Scavenger",
        HeadshotFinalBlows = 1,
        Capabilities = new()
        {
            KillsByYou = new() { State = AdapterCapabilityState.Supported },
            PlayerDeaths = new() { State = AdapterCapabilityState.Supported },
            HeadshotFinalBlows = new() { State = AdapterCapabilityState.Supported }
        }
    };
    public void Dispose()
    {
        hud.Dispose(); NativeObject.Destroy(canvas); UiText.ConfigureNativeResolver(null);
        LocalizationManager.SetLanguage(SystemLanguage.English);
        SceneLoader.FinishLoading(); Time.unscaledTime = 0;
    }
}

public sealed partial class ShellAccessTests
{
    [Fact]
    public void KillFeedSettingsPersistWithoutChangingExistingPanelShortcut()
    {
        SaveBinding("Alt+F8");
        using (var panel = new NativeStatisticsPanel(coordinator))
        {
            Assert.Equal(10, panel.KillFeedSettings.DurationSeconds);
            var shell = Field<RetainedStatisticsShell>(panel, "shell");
            shell.SaveKillFeedSettings!(panel.KillFeedSettings with { DurationSeconds = 25, MaximumEntries = 3, AlignRight = true });
            Assert.Equal(25, shell.KillFeedSettingsProvider!().DurationSeconds);
        }
        using var reloaded = new NativeStatisticsPanel(coordinator);
        Assert.Equal("Alt+F8", Field<PanelHotkey>(reloaded, "hotkey").ToString());
        Assert.Equal(25, reloaded.KillFeedSettings.DurationSeconds);
        Assert.Equal(3, reloaded.KillFeedSettings.MaximumEntries); Assert.True(reloaded.KillFeedSettings.AlignRight);
    }
}
