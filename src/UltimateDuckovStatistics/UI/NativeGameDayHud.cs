using System.Globalization;
using Duckov.Utilities;
using Saves;
using SodaCraft.Localizations;
using TMPro;
using UnityEngine;

namespace UltimateDuckovStatistics.UI;

// A live HUD decoration. It does not read or mutate UDS statistics or Duckov saves.
internal sealed class NativeGameDayHud : IDisposable
{
    private const int DiscoveryAttempts = 10;
    private readonly Action<string> diagnostic;
    private readonly List<Binding> bindings = new();
    private GameClock? observedClock;
    private GameClock? previousSaveClock;
    private bool awaitingClock, loading, disposed, languageDirty = true, warned;
    private int discoveryRemaining;
    private float nextTick;
    private long? captionDay;
    private string caption = string.Empty;

    internal NativeGameDayHud(Action<string> diagnostic)
    {
        this.diagnostic = diagnostic ?? throw new ArgumentNullException(nameof(diagnostic));
        // Activation occurs outside GameClock.Awake. An existing live clock has
        // already loaded; later replacement clocks become trusted at their Load/Step event.
        observedClock = GameClock.Instance;
        loading = SceneLoader.IsSceneLoading;
        GameClock.OnGameClockStep += OnClockStep;
        LocalizationManager.OnSetLanguage += OnLanguageChanged;
        SavesSystem.OnSetFile += OnSaveChanged;
        SavesSystem.OnSaveDeleted += OnSaveChanged;
        SceneLoader.onStartedLoadingScene += OnSceneLoading;
        SceneLoader.onFinishedLoadingScene += OnSceneReady;
        LevelManager.OnLevelInitialized += ScheduleDiscovery;
        ScheduleDiscovery();
    }

    internal void Tick()
    {
        if (disposed || Time.unscaledTime < nextTick) return;
        nextTick = Time.unscaledTime + .5f;
        try
        {
            // The native loading event precedes its fade. Keep the existing label
            // with its HUD until that parent is hidden/unloaded, avoiding a layout jump.
            if (loading) return;
            if (observedClock == null || observedClock != GameClock.Instance || awaitingClock)
            {
                ClearLabels();
                return;
            }

            if (GameClock.Day < 0 || GameClock.Day == long.MaxValue)
            {
                ClearLabels();
                discoveryRemaining = DiscoveryAttempts;
                return;
            }

            for (var i = bindings.Count - 1; i >= 0; i--)
            {
                if (bindings[i].IsUsable) continue;
                bindings[i].Dispose();
                bindings.RemoveAt(i);
                discoveryRemaining = DiscoveryAttempts;
            }

            // Discovery is bounded after activation, a scene/clock transition, or
            // an owned HUD disappearing. A settled main menu never scans repeatedly.
            if (discoveryRemaining > 0)
            {
                discoveryRemaining--;
                Discover();
                if (bindings.Count > 0) discoveryRemaining = 0;
            }
            RefreshCaption();
        }
        catch (Exception exception)
        {
            ClearLabels();
            discoveryRemaining = 0;
            if (!warned)
            {
                warned = true;
                diagnostic($"Game-day HUD unavailable: {exception.GetType().Name}: {exception.Message}");
            }
        }
    }

    private void Discover()
    {
        foreach (var display in UnityEngine.Object.FindObjectsByType<TimeOfDayDisplay>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (display == null || display.weatherText == null || display.weatherText.transform.parent == null) continue;
            if (bindings.Exists(binding => binding.Owner == display)) continue;
            bindings.Add(new Binding(display));
        }
    }

    private void RefreshCaption()
    {
        var day = GameClock.Day;
        if (bindings.Count == 0) return;
        if (captionDay != day || languageDirty)
        {
            caption = string.Format(CultureInfo.CurrentCulture, UiText.Get("ui.game_day"), day + 1);
            captionDay = day;
            languageDirty = false;
        }
        foreach (var binding in bindings) binding.Show(caption);
    }

    private void OnClockStep()
    {
        if (disposed) return;
        var clock = GameClock.Instance;
        if (clock == null || (awaitingClock && ReferenceEquals(clock, previousSaveClock))) return;
        if (!ReferenceEquals(observedClock, clock))
        {
            observedClock = clock;
            previousSaveClock = null;
            awaitingClock = false;
            captionDay = null;
            ScheduleDiscovery();
        }
    }

    private void OnLanguageChanged(SystemLanguage _) { if (!disposed) { languageDirty = true; nextTick = 0; } }

    private void OnSaveChanged()
    {
        if (disposed) return;
        // SetFile is published before the replacement scene clock has loaded.
        // The outgoing clock's subsequent steps must not expose the previous save's day.
        previousSaveClock = GameClock.Instance;
        observedClock = null;
        awaitingClock = true;
        ClearLabels();
    }

    private void OnSceneLoading(SceneLoadingContext _) { if (!disposed) loading = true; }
    private void OnSceneReady(SceneLoadingContext _) { if (!disposed) { loading = false; ScheduleDiscovery(); } }
    private void ScheduleDiscovery() { if (!disposed) { discoveryRemaining = DiscoveryAttempts; nextTick = 0; } }

    private void ClearLabels()
    {
        foreach (var binding in bindings) binding.Dispose();
        bindings.Clear();
        captionDay = null;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        GameClock.OnGameClockStep -= OnClockStep;
        LocalizationManager.OnSetLanguage -= OnLanguageChanged;
        SavesSystem.OnSetFile -= OnSaveChanged;
        SavesSystem.OnSaveDeleted -= OnSaveChanged;
        SceneLoader.onStartedLoadingScene -= OnSceneLoading;
        SceneLoader.onFinishedLoadingScene -= OnSceneReady;
        LevelManager.OnLevelInitialized -= ScheduleDiscovery;
        ClearLabels();
        observedClock = previousSaveClock = null;
    }

    private sealed class Binding : IDisposable
    {
        internal TimeOfDayDisplay Owner { get; }
        private readonly TextMeshProUGUI weather;
        private readonly Transform parent;
        private readonly TextMeshProUGUI label;

        internal bool IsUsable => Owner != null && weather != null && Owner.weatherText == weather
            && parent != null && weather.transform.parent == parent && label != null;

        internal Binding(TimeOfDayDisplay owner)
        {
            Owner = owner;
            weather = owner.weatherText;
            parent = weather.transform.parent;
            // The native text template owns its standard shadow; no shared shadow
            // settings or font materials are mutated or instantiated by UDS.
            label = UnityEngine.Object.Instantiate(GameplayDataSettings.UIStyle.TemplateTextUGUI, parent, false);
            try
            {
                label.gameObject.name = "UDSGameDay";
                label.gameObject.SetActive(false);
                label.transform.localScale = Vector3.one;
                label.transform.SetAsLastSibling();
                label.font = weather.font;
                label.fontSharedMaterial = weather.fontSharedMaterial;
                label.fontSize = 24;
                label.color = weather.color;
                label.alignment = TextAlignmentOptions.Left;
                label.enableAutoSizing = false;
                label.enableWordWrapping = false;
                label.raycastTarget = false;
                label.text = string.Empty;
            }
            catch { Dispose(); throw; }
        }

        internal void Show(string value)
        {
            if (!string.Equals(label.text, value, StringComparison.Ordinal)) label.text = value;
            var visible = Owner.isActiveAndEnabled && weather.isActiveAndEnabled;
            if (label.gameObject.activeSelf != visible) label.gameObject.SetActive(visible);
        }

        public void Dispose()
        {
            if (label == null) return;
            label.gameObject.SetActive(false);
            UnityEngine.Object.Destroy(label.gameObject);
        }
    }
}
