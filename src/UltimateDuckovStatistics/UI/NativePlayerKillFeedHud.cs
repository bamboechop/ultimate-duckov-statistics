using System.Globalization;
using Saves;
using SodaCraft.Localizations;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Tracking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UI.ProceduralImage;

namespace UltimateDuckovStatistics.UI;

// Passive native HUD. Combat callbacks only copy evidence into the bounded queue.
internal sealed class NativePlayerKillFeedHud : IDisposable
{
    private readonly Func<KillFeedSettings> settings;
    private readonly Action<string> diagnostic;
    private readonly PlayerKillFeed feed = new();
    private readonly NativeEntityDisplayNames names = new();
    private readonly KillFeedIcons icons = new();
    private readonly Vector3[] corners = new Vector3[4];
    private readonly List<Row> rows = new(6);
    private TimeOfDayDisplay? owner;
    private RectTransform? root, canvasRect;
    private KillFeedSettings? displayedSettings;
    private bool disposed, failed, languageDirty;
    private float nextDiscovery;
    private int discoveryAttempts = 10;
    private string? username;

    internal NativePlayerKillFeedHud(Func<KillFeedSettings> settings, Action<string> diagnostic)
    {
        this.settings = settings; this.diagnostic = diagnostic;
        SceneLoader.onStartedLoadingScene += Loading;
        SceneLoader.onFinishedLoadingScene += Ready;
        LevelManager.OnLevelInitialized += ScheduleDiscovery;
        SavesSystem.OnSetFile += Reset;
        SavesSystem.OnSaveDeleted += Reset;
        LocalizationManager.OnSetLanguage += LanguageChanged;
    }

    internal void Record(CombatRecorded value, double? meters)
    {
        if (!disposed && !failed) feed.Add(value, meters, Time.unscaledTimeAsDouble, settings());
    }

    internal void Tick()
    {
        if (disposed || failed) return;
        try
        {
            var options = settings();
            var now = Time.unscaledTimeAsDouble;
            feed.Trim(now, options);
            if (rows.Count > 0 && (root == null || owner == null || canvasRect == null))
            { DestroyView(); ScheduleDiscovery(); }
            if (root == null && options.Enabled && !SceneLoader.IsSceneLoading
                && discoveryAttempts > 0 && Time.unscaledTime >= nextDiscovery)
            {
                discoveryAttempts--; nextDiscovery = Time.unscaledTime + .5f;
                Discover();
            }
            if (root == null || owner == null || canvasRect == null) return;
            var visible = feed.Entries.Count > 0 && options.Enabled && owner.isActiveAndEnabled
                && owner.weatherText != null && owner.weatherText.isActiveAndEnabled && !SceneLoader.IsSceneLoading;
            if (root.gameObject.activeSelf != visible) root.gameObject.SetActive(visible);
            if (!visible || owner.weatherText == null) return;

            // World corners keep this independent of resolution, canvas scale, and
            // native weather/day layout changes. No native layout is edited.
            var storm = owner.stormRoot != null ? owner.stormRoot.transform as RectTransform : null;
            var weatherBlock = owner.weatherText.transform.parent as RectTransform;
            var anchor = storm != null && storm.gameObject.activeInHierarchy ? storm : weatherBlock;
            if (anchor == null) { root.gameObject.SetActive(false); return; }
            anchor.GetWorldCorners(corners);
            var bottom = canvasRect.InverseTransformPoint(corners[0]);
            var left = bottom.x;
            if (storm != null)
            {
                storm.GetWorldCorners(corners);
                left = canvasRect.InverseTransformPoint(corners[0]).x;
            }
            var edge = options.AlignRight ? canvasRect.rect.xMax - (left - canvasRect.rect.xMin) : left;
            var position = new Vector3(edge + options.OffsetX, bottom.y - 8 - options.OffsetY, 0);
            if (root.localPosition.x != position.x || root.localPosition.y != position.y) root.localPosition = position;
            if (root.localScale.x != options.Scale) root.localScale = Vector3.one * options.Scale;
            var availableWidth = Math.Max(160, canvasRect.rect.width / options.Scale - 24);
            var refresh = languageDirty || displayedSettings != options;
            displayedSettings = options; languageDirty = false;
            var y = 0f;
            for (var i = 0; i < rows.Count; i++)
            {
                var show = i < feed.Entries.Count;
                if (rows[i].Root.gameObject.activeSelf != show) rows[i].Root.gameObject.SetActive(show);
                if (!show) continue;
                var entry = feed.Entries[i];
                rows[i].Bind(entry, options, PlayerName(), names.Names, icons, availableWidth, refresh);
                var rowPosition = new Vector2(options.AlignRight ? -rows[i].Root.sizeDelta.x : 0, -y);
                if (rows[i].Root.anchoredPosition.x != rowPosition.x || rows[i].Root.anchoredPosition.y != rowPosition.y)
                    rows[i].Root.anchoredPosition = rowPosition;
                var alpha = PlayerKillFeed.Opacity(entry, now, options.DurationSeconds);
                if (rows[i].Group.alpha != alpha) rows[i].Group.alpha = alpha;
                y += rows[i].Root.sizeDelta.y + 8;
            }
        }
        catch (Exception exception)
        {
            failed = true; feed.Clear(); DestroyView();
            diagnostic($"Kill-feed HUD unavailable: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private string PlayerName()
    {
        if (username == null)
        {
            try { if (SteamManager.Initialized) username = Steamworks.SteamFriends.GetPersonaName(); }
            catch { /* Offline/native identity unavailable: use the localized role. */ }
            username ??= string.Empty;
        }
        return string.IsNullOrWhiteSpace(username) ? UiText.Get("ui.killfeed_you") : username;
    }

    private void Discover()
    {
        foreach (var display in UnityEngine.Object.FindObjectsByType<TimeOfDayDisplay>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (display == null || !display.isActiveAndEnabled || display.weatherText == null) continue;
            var canvas = display.GetComponentInParent<Canvas>();
            if (canvas == null || canvas.rootCanvas.transform is not RectTransform parent) continue;
            owner = display; canvasRect = parent;
            root = Node(parent, "UDSPlayerKillFeed");
            root.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            var group = root.gameObject.AddComponent<CanvasGroup>();
            group.interactable = false; group.blocksRaycasts = false;
            root.gameObject.SetActive(false);
            for (var i = 0; i < 6; i++) rows.Add(new Row(root, display.weatherText));
            discoveryAttempts = 0;
            return;
        }
    }

    private void Loading(SceneLoadingContext _) { Reset(); }
    private void Ready(SceneLoadingContext _) => ScheduleDiscovery();
    private void ScheduleDiscovery() { if (!disposed) { discoveryAttempts = 10; nextDiscovery = 0; } }
    private void LanguageChanged(SystemLanguage _) { languageDirty = true; }
    private void Reset()
    {
        feed.Clear(); DestroyView(); username = null; ScheduleDiscovery();
    }
    private void DestroyView()
    {
        rows.Clear();
        if (root != null) { root.gameObject.SetActive(false); UnityEngine.Object.Destroy(root.gameObject); }
        root = canvasRect = null; owner = null; displayedSettings = null;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        SceneLoader.onStartedLoadingScene -= Loading;
        SceneLoader.onFinishedLoadingScene -= Ready;
        LevelManager.OnLevelInitialized -= ScheduleDiscovery;
        SavesSystem.OnSetFile -= Reset;
        SavesSystem.OnSaveDeleted -= Reset;
        LocalizationManager.OnSetLanguage -= LanguageChanged;
        feed.Clear(); DestroyView(); icons.Dispose(); names.Dispose();
    }

    private static RectTransform Node(Transform parent, string name)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false); rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        rect.sizeDelta = Vector2.zero;
        return rect;
    }
    private static void Place(RectTransform rect, float x, float y, float w, float h)
    { rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(w, h); }

    private sealed class Row
    {
        internal RectTransform Root { get; }
        internal CanvasGroup Group { get; }
        private readonly TextMeshProUGUI first, last, distance, unknownWeapon;
        private readonly Image weapon, headshot;
        private KillFeedEntry? bound;
        private float lastAvailableWidth;

        internal Row(RectTransform parent, TextMeshProUGUI template)
        {
            Root = Node(parent, "Entry");
            var background = Root.gameObject.AddComponent<ProceduralImage>();
            background.color = new Color(0, 0, 0, .55f); background.raycastTarget = false;
            Root.gameObject.AddComponent<UniformModifier>().Radius = 12;
            Group = Root.gameObject.AddComponent<CanvasGroup>();
            first = Text("Attacker", 24); last = Text("Victim", 24);
            distance = Text("Distance", 18); unknownWeapon = Text("UnknownWeapon", 24);
            distance.alignment = unknownWeapon.alignment = TextAlignmentOptions.Center;
            weapon = Node(Root, "Weapon").gameObject.AddComponent<Image>();
            headshot = Node(Root, "Headshot").gameObject.AddComponent<Image>();
            weapon.raycastTarget = headshot.raycastTarget = false;
            weapon.preserveAspect = headshot.preserveAspect = true;

            TextMeshProUGUI Text(string name, float size)
            {
                var label = Node(Root, name).gameObject.AddComponent<TextMeshProUGUI>();
                label.font = template.font; label.fontSharedMaterial = template.fontSharedMaterial;
                label.fontSize = size; label.enableAutoSizing = false; label.enableWordWrapping = false;
                label.richText = false; label.raycastTarget = false; label.color = Color.white;
                label.alignment = TextAlignmentOptions.Left; label.overflowMode = TextOverflowModes.Ellipsis;
                return label;
            }
        }

        internal void Bind(KillFeedEntry entry, KillFeedSettings settings, string player,
            EntityDisplayNames names, KillFeedIcons icons, float availableWidth, bool refresh)
        {
            if (ReferenceEquals(bound, entry) && !refresh && Math.Abs(lastAvailableWidth - availableWidth) < .5f) return;
            bound = entry; lastAvailableWidth = availableWidth;
            var actor = entry.ActorId.EndsWith(":environment", StringComparison.Ordinal) ? UiText.Get("ui.killfeed_environment")
                : entry.ActorId.EndsWith(":unknown", StringComparison.Ordinal) ? UiText.Get("ui.killfeed_unknown")
                : names.Get(entry.ActorId, entry.ActorName);
            first.text = entry.PlayerDied ? actor : player; last.text = entry.PlayerDied ? player : actor;
            var blue = new Color32(126, 166, 222, 255); var yellow = new Color32(218, 183, 96, 255);
            first.color = entry.PlayerDied ? yellow : blue; last.color = entry.PlayerDied ? blue : yellow;
            var showHeadshot = settings.ShowHeadshots && entry.Headshot;
            var showDistance = settings.ShowDistance && entry.Meters.HasValue;
            headshot.gameObject.SetActive(showHeadshot);
            if (showHeadshot) headshot.sprite = icons.Headshot;
            distance.gameObject.SetActive(showDistance);
            distance.text = showDistance ? entry.Meters!.Value.ToString("0.##", CultureInfo.CurrentCulture) + " m" : string.Empty;
            weapon.sprite = icons.Weapon(entry.WeaponId);
            weapon.gameObject.SetActive(weapon.sprite != null);
            unknownWeapon.gameObject.SetActive(weapon.sprite == null); unknownWeapon.text = "—";
            var weaponWidth = Math.Max(48, Math.Min(96, distance.GetPreferredValues(distance.text).x + 8));
            var nameLimit = Math.Max(32, Math.Min(260, (availableWidth - weaponWidth - (showHeadshot ? 36 : 0) - 48) / 2));
            var a = Math.Min(nameLimit, first.GetPreferredValues(first.text).x);
            var b = Math.Min(nameLimit, last.GetPreferredValues(last.text).x);
            var x = 12f;
            Place(first.rectTransform, x, 4, a, 40); x += a + 8;
            Place(weapon.rectTransform, x + (weaponWidth - 42) / 2, 2, 42, 42);
            Place(unknownWeapon.rectTransform, x, 4, weaponWidth, 40);
            Place(distance.rectTransform, x, 42, weaponWidth, 20); x += weaponWidth + 8;
            if (showHeadshot) { Place(headshot.rectTransform, x, 10, 28, 28); x += 36; }
            Place(last.rectTransform, x, 4, b, 40);
            Root.sizeDelta = new Vector2(x + b + 12, showDistance ? 66 : 48);
        }
    }
}
