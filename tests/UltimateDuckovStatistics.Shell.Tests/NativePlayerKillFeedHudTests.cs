using Duckov.Scenes;
using Saves;
using SodaCraft.Localizations;
using TMPro;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.UI;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UI.ProceduralImage;
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
        MultiSceneCore.Instance = null;
        KillFeedIcons.Weapons.Clear();
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
    public void RowsBorrowNativeStormAppearanceWithoutChangingItsComponents()
    {
        var nativeBackground = storm.gameObject.AddComponent<ProceduralImage>();
        nativeBackground.raycastTarget = true;
        nativeBackground.color = new Color(.12f, .2f, .3f, .427f);
        nativeBackground.FalloffDistance = 1.5f; nativeBackground.BorderWidth = .5f;
        var nativeRadius = storm.gameObject.AddComponent<UniformModifier>(); nativeRadius.Radius = 17;
        var nativeLayout = storm.gameObject.AddComponent<HorizontalLayoutGroup>();
        nativeLayout.padding = new RectOffset(11, 19, 7, 9);
        hud.Record(Kill("1"), 15.65); hud.Tick();
        var row = (RectTransform)Root().GetChild(0);
        var background = row.GetComponent<ProceduralImage>();
        Assert.Equal(nativeBackground.color, background.color);
        Assert.Equal(1.5f, background.FalloffDistance); Assert.Equal(.5f, background.BorderWidth);
        Assert.Equal(17, row.GetComponent<UniformModifier>().Radius);
        Assert.Equal(11, Label("Attacker").rectTransform.anchoredPosition.x);
        var last = Label("Victim").rectTransform;
        Assert.Equal(19, row.sizeDelta.x - last.anchoredPosition.x - last.sizeDelta.x);
        var weapon = (RectTransform)row.Find("Weapon")!;
        Assert.Equal(7, -weapon.anchoredPosition.y);
        var distance = Label("Distance").rectTransform;
        Assert.Equal(9, row.sizeDelta.y + distance.anchoredPosition.y - distance.sizeDelta.y);
        Assert.True(nativeBackground.raycastTarget); Assert.Equal(.427f, nativeBackground.color.a);
        Assert.False(background.raycastTarget); Assert.False(nativeBackground.Destroyed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DistanceFitsMeasuredLineAndRowCentersAllColumns(bool showDistance)
    {
        settings = settings with { ShowDistance = showDistance };
        hud.Record(Kill("1"), 35.29); hud.Tick();
        var row = (RectTransform)Root().GetChild(0);
        var distance = Label("Distance");
        var weapon = (RectTransform)row.Find("Weapon")!;
        var center = row.sizeDelta.y / 2;
        foreach (var name in new[] { "Attacker", "Victim", "Headshot" })
        {
            var child = (RectTransform)row.Find(name)!;
            Assert.Equal(center, -child.anchoredPosition.y + child.sizeDelta.y / 2);
        }
        Assert.Equal(showDistance, distance.gameObject.activeSelf);
        if (showDistance)
        {
            // TMP line height exceeds font size, including for the compact caption.
            Assert.True(distance.preferredHeight > distance.fontSize);
            Assert.True(distance.rectTransform.sizeDelta.y >= distance.preferredHeight);
            Assert.True(distance.rectTransform.sizeDelta.x >= distance.preferredWidth);
            Assert.Equal(TextOverflowModes.Overflow, distance.overflowMode);
            Assert.True(-distance.rectTransform.anchoredPosition.y > -weapon.anchoredPosition.y + weapon.sizeDelta.y);
            Assert.True(row.sizeDelta.y >= -distance.rectTransform.anchoredPosition.y + distance.rectTransform.sizeDelta.y + 5);
        }
        else Assert.Equal(weapon.sizeDelta.y + 10, row.sizeDelta.y); // Only icon height and the native top/bottom padding.
    }

    [Fact]
    public void WideWeaponsGrowSidewaysWithCenteredDistanceAndUnchangedRowHeight()
    {
        KillFeedIcons.Weapons["pistol"] = new Sprite { rect = new Rect(0, 0, 60, 80) };
        // Simulate the GPU readback with a wide weapon inside a square inventory icon.
        var pixels = new Color32[128 * 128];
        for (var y = 52; y < 76; y++)
            for (var x = 16; x < 112; x++) pixels[y * 128 + x] = new Color32(255, 255, 255, 255);
        KillFeedIcons.Weapons["rifle"] = new Sprite { rect = KillFeedIconGeometry.VisibleBounds(pixels, 128, 128) };
        hud.Record(Kill("pistol") with { WeaponId = "pistol" }, 9.02); hud.Tick();
        var pistolRow = (RectTransform)Root().GetChild(0);
        var rowHeight = pistolRow.sizeDelta.y;
        var pistolWidth = ((RectTransform)pistolRow.Find("Weapon")!).sizeDelta.x;
        var captionY = Label("Distance").rectTransform.anchoredPosition.y;

        hud.Record(Kill("rifle") with { WeaponId = "rifle" }, 9.02); hud.Tick();
        var rifleRow = (RectTransform)Root().GetChild(0);
        var weapon = (RectTransform)rifleRow.Find("Weapon")!;
        var caption = Label("Distance").rectTransform;
        Assert.True(weapon.sizeDelta.x > pistolWidth);
        Assert.Equal(64, weapon.sizeDelta.x); // Extreme aspect ratios stop at the width cap.
        Assert.Equal(4, weapon.sizeDelta.x / weapon.sizeDelta.y);
        Assert.Equal(rowHeight, rifleRow.sizeDelta.y);
        Assert.Equal(captionY, caption.anchoredPosition.y);
        Assert.Equal(weapon.anchoredPosition.x + weapon.sizeDelta.x / 2, caption.anchoredPosition.x + caption.sizeDelta.x / 2);
        Assert.True(-caption.anchoredPosition.y > -weapon.anchoredPosition.y + weapon.sizeDelta.y);
        Assert.True(weapon.anchoredPosition.x + weapon.sizeDelta.x < ((RectTransform)rifleRow.Find("Headshot")!).anchoredPosition.x);
        Assert.True(weapon.GetComponent<Image>().preserveAspect);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeSubsceneTransitionClearsRecentEntriesAndRediscoversAfterLoading(bool replaceDisplay)
    {
        var core = new MultiSceneCore(); MultiSceneCore.Instance = core;
        settings = settings with { DurationSeconds = 30 };
        hud.Record(Kill("old-map"), 5); hud.Tick();
        Assert.True(Root().gameObject.activeSelf);
        var searches = NativeObject.SceneSearches;

        core.BeginSubSceneLoad();
        Assert.False(SceneLoader.IsSceneLoading); // This native route never raises SceneLoader events.
        Assert.DoesNotContain(GameObject.Live, go => go.name == "UDSPlayerKillFeed");
        if (replaceDisplay) display.gameObject.SetActive(false);
        for (var i = 1; i <= 14; i++) { Time.unscaledTime = i * .5f; hud.Tick(); }
        Assert.Equal(searches, NativeObject.SceneSearches); // Loading must not consume the discovery budget.
        Assert.DoesNotContain(GameObject.Live, go => go.name == "UDSPlayerKillFeed");

        if (replaceDisplay)
        {
            var next = Child(canvas.transform, "NextWeatherDisplay").AddComponent<TimeOfDayDisplay>();
            next.weatherText = Child(next.transform, "Weather").AddComponent<TextMeshProUGUI>();
            next.weatherText.font = display.weatherText.font;
            next.weatherText.fontSharedMaterial = display.weatherText.fontSharedMaterial;
            next.stormRoot = Child(next.transform, "NextStorm");
            ((RectTransform)next.stormRoot.transform).TestWorldCorners = Corners(12, 880);
        }
        core.FinishSubSceneLoad(); hud.Tick();
        Assert.False(Root().gameObject.activeSelf); // The old entry is younger than its expiry but must be gone.
        hud.Record(Kill("new-map") with { TargetDisplayName = "New enemy" }, 8); hud.Tick();
        Assert.True(Root().gameObject.activeSelf);
        Assert.Equal("New enemy", Label("Victim").text);
        Assert.Single(Root().Children, child => child.gameObject.activeSelf);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void LanguageRefreshAndDisposeReleaseOwnedRowsAndSubscriptions()
    {
        hud.Record(Kill("1") with { PlayerDeaths = 1, KillsByYou = 0, AttackerId = "duckov:attacker:environment" }, null);
        hud.Tick(); Assert.Equal("Environment", Label("Attacker").text);
        LocalizationManager.SetLanguage(SystemLanguage.German); hud.Tick();
        Assert.Equal("Umgebung", Label("Attacker").text); Assert.Equal("bamboechop", Label("Victim").text);
        var beforeScenes = SceneLoader.Listeners; var beforeSaves = SavesSystem.Listeners;
        var beforeSubScenes = MultiSceneCore.Listeners;
        hud.Dispose(); hud.Dispose();
        Assert.Equal(beforeScenes - 2, SceneLoader.Listeners); Assert.Equal(beforeSaves - 2, SavesSystem.Listeners);
        Assert.Equal(beforeSubScenes - 2, MultiSceneCore.Listeners);
        var core = new MultiSceneCore(); MultiSceneCore.Instance = core;
        core.BeginSubSceneLoad(); core.FinishSubSceneLoad();
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
        KillFeedIcons.Weapons.Clear();
        hud.Dispose(); NativeObject.Destroy(canvas); UiText.ConfigureNativeResolver(null);
        LocalizationManager.SetLanguage(SystemLanguage.English);
        SceneLoader.FinishLoading(); Time.unscaledTime = 0;
        MultiSceneCore.Instance = null;
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
