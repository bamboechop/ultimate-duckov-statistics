using Duckov.Utilities;
using Saves;
using SodaCraft.Localizations;
using TMPro;
using UltimateDuckovStatistics.UI;
using UnityEngine;
using Xunit;
using NativeObject = UnityEngine.Object;

namespace UltimateDuckovStatistics.Shell.Tests;

public sealed class NativeGameDayHudTests : IDisposable
{
    private readonly List<GameObject> roots = new();
    private readonly List<string> diagnostics = new();
    private readonly NativeGameDayHud hud;
    private readonly int languageListeners, clockListeners, sceneListeners, saveListeners, levelListeners;

    public NativeGameDayHudTests()
    {
        LocalizationManager.SetLanguage(SystemLanguage.English);
        UiText.ConfigureNativeResolver(key => LocalizationManager.CurrentLanguage == SystemLanguage.German
            ? UiText.GermanFallbacks.GetValueOrDefault(key) : UiText.EnglishFallbacks.GetValueOrDefault(key));
        GameplayDataSettings.UIStyle.TemplateTextUGUI = Root("DayTemplate").AddComponent<TextMeshProUGUI>();
        Time.unscaledTime = 0;
        NativeObject.SceneSearches = 0;
        GameClock.Instance = null;
        languageListeners = LocalizationManager.Listeners;
        clockListeners = GameClock.Listeners;
        sceneListeners = SceneLoader.Listeners;
        saveListeners = SavesSystem.Listeners;
        levelListeners = LevelManager.InitializedListeners;
        hud = new NativeGameDayHud(diagnostics.Add);
    }

    [Fact]
    public void ExistingSavedDayUsesNativeHudStylingAndLiveLocalizationWithoutRepeatedWork()
    {
        // A loaded save may already be decades old when UDS is first installed.
        var owner = Display();
        Clock(29);
        Tick();
        var label = Label();
        Assert.Equal("Day 30", label.text);
        Assert.Same(owner.weatherText.transform.parent, label.transform.parent);
        Assert.Same(owner.weatherText.font, label.font);
        Assert.Same(owner.weatherText.fontSharedMaterial, label.fontSharedMaterial);
        Assert.Equal(24, label.fontSize);
        Assert.False(label.raycastTarget);
        Assert.Equal("Native weather", owner.weatherText.text);
        LocalizationManager.SetLanguage(SystemLanguage.German);
        Tick();
        Assert.Same(label, Label());
        Assert.Equal("Tag 30", label.text);
        var writes = label.TextWrites;
        var reads = NativeObject.SceneSearches;
        for (var i = 0; i < 1000; i++) { GameClock.Step(); Tick(); }
        Assert.Equal(writes, label.TextWrites);
        Assert.Equal(reads, NativeObject.SceneSearches);
        Assert.Empty(diagnostics);
    }

    [Fact]
    public void MissingClockNeverFabricatesDayOneAndReplacementWaitsForNativeLoad()
    {
        Display();
        Tick();
        Assert.Empty(Labels());
        Assert.Equal(0, NativeObject.SceneSearches);
        Clock(0, publish: false);
        Tick();
        Assert.Empty(Labels());
        GameClock.Step(); Tick();
        Assert.Equal("Day 1", Label().text);
        LocalizationManager.SetLanguage(SystemLanguage.German); Tick();
        Assert.Equal("Tag 1", Label().text);
        NativeObject.Destroy(GameClock.Instance!.gameObject);
        Tick();
        Assert.Empty(Labels());
    }

    [Fact]
    public void MidnightAndSleepAdvanceTheAbsoluteSavedDayWithoutRecreatingTheLabel()
    {
        Display(); var clock = Clock(29); Tick(); var label = Label();
        clock.Days = 30; GameClock.Step(); Tick();
        Assert.Same(label, Label()); Assert.Equal("Day 31", label.text);
        clock.Days = 34; GameClock.Step(); Tick();
        Assert.Same(label, Label()); Assert.Equal("Day 35", label.text);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SaveChangeHidesOutgoingClockUntilReplacementHasLoaded(bool deletion)
    {
        Display(); var oldClock = Clock(29); Tick();
        if (deletion) SavesSystem.DeleteSave(); else SavesSystem.SetFile();
        Assert.Empty(Labels());
        oldClock.Days++; GameClock.Step(); Tick();
        Assert.Empty(Labels());
        Clock(9, publish: false); Tick();
        Assert.Empty(Labels());
        GameClock.Step(); Tick();
        Assert.Equal("Day 10", Label().text);
    }

    [Fact]
    public void SceneTransitionRebindsTheNewHudAndDiscoveryStopsWhenNoHudExists()
    {
        var old = Display(); Clock(29); Tick();
        SceneLoader.StartLoading(); Assert.Empty(Labels());
        NativeObject.Destroy(old.gameObject); Tick();
        SceneLoader.FinishLoading();
        for (var i = 0; i < 12; i++) Tick();
        var searches = NativeObject.SceneSearches;
        for (var i = 0; i < 20; i++) Tick();
        Assert.Equal(searches, NativeObject.SceneSearches);
        var replacement = Display(); LevelManager.CompleteInitialization(); Tick();
        Assert.Same(replacement.weatherText.transform.parent, Label().transform.parent);
        Assert.Equal("Day 30", Label().text);
    }

    [Fact]
    public void HiddenHudReappearsWithoutDuplicateObjectsAndDestroyedLabelIsRebound()
    {
        var owner = Display(); Clock(2); Tick(); var label = Label();
        owner.weatherText.gameObject.SetActive(false); Tick();
        Assert.False(label.gameObject.activeSelf);
        LocalizationManager.SetLanguage(SystemLanguage.German); Tick();
        owner.weatherText.gameObject.SetActive(true); Tick();
        Assert.Same(label, Label()); Assert.True(label.gameObject.activeSelf); Assert.Equal("Tag 3", label.text);
        NativeObject.Destroy(label.gameObject); Tick();
        Assert.NotSame(label, Label()); Assert.Equal("Tag 3", Label().text);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(long.MaxValue)]
    public void InvalidDayIsHiddenAndValidLiveClockCanRecover(long day)
    {
        Display(); var clock = Clock(day); Tick(); Assert.Empty(Labels());
        clock.Days = 1; GameClock.Step(); Tick(); Assert.Equal("Day 2", Label().text);
    }

    [Fact]
    public void NativeCreationFailureIsContainedAndCanRecoverAtTheNextLifecycleBoundary()
    {
        Display(); Clock(1);
        GameObject.FailCreationOf = "DayTemplate";
        try { Tick(); Assert.Empty(Labels()); Assert.Single(diagnostics); Tick(); Assert.Single(diagnostics); }
        finally { GameObject.FailCreationOf = null; }
        LevelManager.CompleteInitialization(); Tick(); Assert.Equal("Day 2", Label().text);
    }

    [Fact]
    public void DisposalRemovesLabelsAndAllSubscriptionsAndReactivationUsesExistingClock()
    {
        Display(); Clock(29); Tick();
        hud.Dispose(); hud.Dispose();
        Assert.Empty(Labels());
        Assert.Equal(languageListeners, LocalizationManager.Listeners);
        Assert.Equal(clockListeners, GameClock.Listeners);
        Assert.Equal(sceneListeners, SceneLoader.Listeners);
        Assert.Equal(saveListeners, SavesSystem.Listeners);
        Assert.Equal(levelListeners, LevelManager.InitializedListeners);
        GameClock.Step(); SceneLoader.FinishLoading(); LevelManager.CompleteInitialization(); Tick();
        Assert.Empty(Labels());
        using var replacement = new NativeGameDayHud(diagnostics.Add);
        replacement.Tick(); Assert.Equal("Day 30", Label().text);
    }

    [Fact]
    public void ActivationDuringLoadingWaitsBeforeAttachingToTheHud()
    {
        hud.Dispose(); Display(); Clock(4); SceneLoader.StartLoading();
        using var activatedDuringLoad = new NativeGameDayHud(diagnostics.Add);
        activatedDuringLoad.Tick(); Assert.Empty(Labels());
        SceneLoader.FinishLoading(); activatedDuringLoad.Tick();
        Assert.Equal("Day 5", Label().text);
    }
    private GameObject Root(string name) { var root = new GameObject(name); roots.Add(root); return root; }
    private GameClock Clock(long day, bool publish = true)
    {
        var clock = Root("NativeClock").AddComponent<GameClock>();
        clock.Days = day; GameClock.Instance = clock;
        if (publish) GameClock.Step();
        return clock;
    }
    private TimeOfDayDisplay Display()
    {
        var root = Root("NativeTimeOfDay"); var display = root.AddComponent<TimeOfDayDisplay>();
        var weather = new GameObject("Weather"); weather.transform.SetParent(root.transform);
        display.weatherText = weather.AddComponent<TextMeshProUGUI>();
        display.weatherText.font = new TMP_FontAsset(); display.weatherText.fontSharedMaterial = new Material();
        display.weatherText.text = "Native weather";
        return display;
    }
    private void Tick() { Time.unscaledTime += .6f; hud.Tick(); }
    private static TextMeshProUGUI[] Labels() => GameObject.Live.Where(go => go.name == "UDSGameDay").Select(go => go.GetComponent<TextMeshProUGUI>()).ToArray();
    private static TextMeshProUGUI Label() => Assert.Single(Labels());
    public void Dispose()
    {
        hud.Dispose();
        foreach (var root in roots) NativeObject.Destroy(root);
        GameClock.Instance = null;
        UiText.ConfigureNativeResolver(null);
        LocalizationManager.SetLanguage(SystemLanguage.English);
    }
}
