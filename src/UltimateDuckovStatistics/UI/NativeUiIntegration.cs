using System.Globalization;
using System.Reflection;
using Duckov.UI;
using ItemStatsSystem;
using SodaCraft.Localizations;
using UltimateDuckovStatistics.Adapters;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UltimateDuckovStatistics.UI;

internal sealed class NativeUiIntegration : IDisposable
{
    private const string LocalizationPrefix = "ultimate-duckov-statistics.";
    private readonly NativeProfileCoordinator coordinator;
    private readonly Action<PanelAccessSurface> openPanel;
    private readonly Action<PanelAccessSurface> closePanel;
    private readonly Dictionary<int, GameObject> injectedByRoot = new();
    private readonly HashSet<string> registeredLocalizationKeys = new(StringComparer.Ordinal);
    private bool initialized;
    private bool mainMenuAnchorWarningWritten;
    private bool pauseMenuAnchorWarningWritten;

    public NativeMenuIntegrationState MainMenuState { get; private set; }

    public NativeMenuIntegrationState BasePauseMenuState { get; private set; }

    public NativeUiIntegration(
        NativeProfileCoordinator coordinator,
        Action<PanelAccessSurface> openPanel,
        Action<PanelAccessSurface> closePanel)
    {
        this.coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        this.openPanel = openPanel ?? throw new ArgumentNullException(nameof(openPanel));
        this.closePanel = closePanel ?? throw new ArgumentNullException(nameof(closePanel));
    }

    public void Initialize()
    {
        if (initialized) return;
        initialized = true;
        try
        {
            RegisterLocalizationFallbacks();
            UiText.ConfigureNativeResolver(ResolveLocalizedText);
            MainMenu.OnMainMenuAwake += HandleMainMenuAwake;
            MainMenu.OnMainMenuDestroy += HandleMainMenuDestroy;
            PauseMenu.onPauseMenuOn += HandlePauseMenuOpened;
            PauseMenu.onPauseMenuOff += HandlePauseMenuClosed;
            TryInjectExistingMainMenu();
            TryInjectPauseMenu();
        }
        catch (Exception exception)
        {
            if (MainMenuState == NativeMenuIntegrationState.NotObserved)
                MainMenuState = NativeMenuIntegrationState.Unavailable;
            if (BasePauseMenuState == NativeMenuIntegrationState.NotObserved)
                BasePauseMenuState = NativeMenuIntegrationState.Unavailable;
            coordinator.ReportUiDiagnostic(
                $"M17 native menu/localization integration degraded; F8 remains available: {exception.GetType().Name}: {exception.Message}",
                "Warning");
        }
    }

    public void ShowToast(string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        try
        {
            NotificationText.Push(message);
        }
        catch (Exception exception)
        {
            coordinator.ReportUiDiagnostic(
                $"Native UI toast failed: {exception.GetType().Name}: {exception.Message}",
                "Warning");
        }
    }

    private void HandleMainMenuAwake() => TryInjectExistingMainMenu();

    private void HandleMainMenuDestroy()
    {
        closePanel(PanelAccessSurface.MainMenu);
        RemoveDestroyedEntries();
    }

    private void HandlePauseMenuOpened() => TryInjectPauseMenu();

    private void HandlePauseMenuClosed()
    {
        closePanel(PanelAccessSurface.BasePauseMenu);
        RemoveDestroyedEntries();
    }

    private void TryInjectExistingMainMenu()
    {
        var mainMenu = Resources.FindObjectsOfTypeAll<MainMenu>()
            .FirstOrDefault(value => value != null && value.gameObject.scene.IsValid());
        if (mainMenu == null) return;
        if (TryInjectButton(mainMenu.gameObject, PanelAccessSurface.MainMenu))
        {
            MainMenuState = NativeMenuIntegrationState.Available;
            return;
        }

        MainMenuState = NativeMenuIntegrationState.Unavailable;
        if (!mainMenuAnchorWarningWritten)
        {
            mainMenuAnchorWarningWritten = true;
            coordinator.ReportUiDiagnostic(
                "M17 native main-menu entry was not injected because no version-checked Settings/Options/Mods button anchor was found; the configured hotkey remains available.",
                "Warning");
        }
    }

    private void TryInjectPauseMenu()
    {
        var pauseMenu = PauseMenu.Instance;
        if (pauseMenu == null) return;
        if (LevelManager.Instance == null || !LevelManager.Instance.IsBaseLevel)
        {
            RemoveInjectedButton(pauseMenu.gameObject.GetInstanceID());
            return;
        }

        if (TryInjectButton(pauseMenu.gameObject, PanelAccessSurface.BasePauseMenu))
        {
            BasePauseMenuState = NativeMenuIntegrationState.Available;
            return;
        }

        BasePauseMenuState = NativeMenuIntegrationState.Unavailable;
        if (!pauseMenuAnchorWarningWritten)
        {
            pauseMenuAnchorWarningWritten = true;
            coordinator.ReportUiDiagnostic(
                "M17 native base-pause entry was not injected because no version-checked Settings/Options/Mods button anchor was found; the configured hotkey remains available.",
                "Warning");
        }
    }

    private bool TryInjectButton(GameObject root, PanelAccessSurface surface)
    {
        if (root == null) return false;
        var rootId = root.GetInstanceID();
        if (injectedByRoot.TryGetValue(rootId, out var existing) && existing != null) return true;
        injectedByRoot.Remove(rootId);

        // The installed MainMenu component is a lifecycle marker, not a guaranteed
        // ancestor of the native menu canvas. Keep discovery inside its loaded scene.
        var searchRoots = surface == PanelAccessSurface.MainMenu && root.scene.IsValid()
            ? root.scene.GetRootGameObjects()
            : new[] { root };
        var candidates = searchRoots
            .SelectMany(searchRoot => searchRoot.GetComponentsInChildren<Button>(includeInactive: true))
            .Select(button => new { Button = button, Score = ScoreAnchor(button, surface) })
            .Where(value => value.Score > 0)
            .OrderByDescending(value => value.Score)
            .ThenBy(value => value.Button.gameObject.name, StringComparer.Ordinal)
            .ToArray();
        var anchor = candidates.FirstOrDefault()?.Button;
        if (surface == PanelAccessSurface.BasePauseMenu
            && candidates.Length > 1
            && candidates[0].Score == candidates[1].Score)
        {
            return false;
        }
        if (anchor == null || anchor.transform.parent == null) return false;

        try
        {
            var buttonTemplate = Duckov.Utilities.GameplayDataSettings.UIPrefabs.Button;
            if (buttonTemplate == null) return false;
            var button = UnityEngine.Object.Instantiate(
                buttonTemplate,
                anchor.transform.parent,
                worldPositionStays: false);
            var clone = button.gameObject;
            clone.name = "UltimateDuckovStatisticsButton";
            clone.transform.SetSiblingIndex(Math.Min(anchor.transform.GetSiblingIndex() + 1, clone.transform.parent.childCount - 1));
            RemoveInheritedOpenChildActions(clone);
            button.onClick = new Button.ButtonClickedEvent();
            button.onClick.AddListener(() => openPanel(surface));
            ApplyLocalizedButtonText(clone);
            clone.SetActive(true);
            injectedByRoot[rootId] = clone;
            coordinator.ReportUiDiagnostic($"M17 native {surface} statistics entry attached.");
            return true;
        }
        catch (Exception exception)
        {
            coordinator.ReportUiDiagnostic(
                $"M17 native {surface} entry failed: {exception.GetType().Name}: {exception.Message}",
                "Warning");
            return false;
        }
    }

    private static int ScoreAnchor(Button button, PanelAccessSurface surface)
    {
        var score = 0;
        foreach (var component in button.GetComponentsInChildren<Component>(includeInactive: true))
        {
            if (component == null || !string.Equals(component.GetType().Name, "TextLocalizor", StringComparison.Ordinal))
                continue;
            var key = ReadStringMember(component, component.GetType(), "Key");
            score = Math.Max(score, surface switch
            {
                PanelAccessSurface.MainMenu when string.Equals(key, "MainMenu_Settings", StringComparison.Ordinal) => 1200,
                PanelAccessSurface.MainMenu when string.Equals(key, "MainMenu_MODs", StringComparison.Ordinal) => 1100,
                PanelAccessSurface.BasePauseMenu when string.Equals(key, "UI_Menu_Options", StringComparison.Ordinal) => 1200,
                _ => 0
            });
        }

        if (score > 0) return score;
        score = NativeMenuAnchorPolicy.Score(button.gameObject.name);
        for (var current = button.transform.parent; current != null; current = current.parent)
            score = Math.Max(score, NativeMenuAnchorPolicy.Score(current.gameObject.name) - 20);
        return score;
    }

    private static string? ReadStringMember(object target, Type type, string name)
    {
        var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        if (property?.CanRead == true && property.PropertyType == typeof(string))
            return (string?)property.GetValue(target);
        var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public);
        return field?.FieldType == typeof(string) ? (string?)field.GetValue(target) : null;
    }

    private static void RemoveInheritedOpenChildActions(GameObject clone)
    {
        foreach (var component in clone.GetComponents<Component>())
        {
            if (component == null || component is Button) continue;
            if (string.Equals(component.GetType().Name, "UIPanelButton_OpenChildPanel", StringComparison.Ordinal))
                UnityEngine.Object.Destroy(component);
        }
    }

    private static void ApplyLocalizedButtonText(GameObject clone)
    {
        var localizationKey = LocalizationPrefix + "ui.menu_entry";
        foreach (var component in clone.GetComponentsInChildren<Component>(includeInactive: true))
        {
            if (component == null) continue;
            var type = component.GetType();
            if (string.Equals(type.Name, "TextLocalizor", StringComparison.Ordinal))
            {
                SetStringMember(component, type, "Key", localizationKey);
                continue;
            }

            if (string.Equals(type.Name, "Text", StringComparison.Ordinal)
                || string.Equals(type.Name, "TextMeshProUGUI", StringComparison.Ordinal))
            {
                SetStringMember(component, type, "text", UiText.Get("ui.menu_entry"));
            }
        }
    }

    private static void SetStringMember(object target, Type type, string name, string value)
    {
        var property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        if (property?.CanWrite == true && property.PropertyType == typeof(string))
        {
            property.SetValue(target, value);
            return;
        }

        var field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public);
        if (field?.FieldType == typeof(string)) field.SetValue(target, value);
    }

    private void RegisterLocalizationFallbacks()
    {
        foreach (var entry in UiText.EnglishFallbacks)
        {
            var key = LocalizationPrefix + entry.Key;
            LocalizationManager.SetOverrideText(key, entry.Value);
            registeredLocalizationKeys.Add(key);
        }
    }

    private static string? ResolveLocalizedText(string key)
    {
        var nativeKey = LocalizationPrefix + key;
        var value = LocalizationManager.GetPlainText(nativeKey);
        return string.IsNullOrWhiteSpace(value)
               || string.Equals(value, nativeKey, StringComparison.Ordinal)
               || string.Equals(value, $"*{nativeKey}*", StringComparison.Ordinal)
            ? null
            : value;
    }

    private void RemoveInjectedButton(int rootId)
    {
        if (!injectedByRoot.Remove(rootId, out var injected) || injected == null) return;
        UnityEngine.Object.Destroy(injected);
    }

    private void RemoveDestroyedEntries()
    {
        foreach (var rootId in injectedByRoot.Where(entry => entry.Value == null).Select(entry => entry.Key).ToArray())
            injectedByRoot.Remove(rootId);
    }

    public void Dispose()
    {
        if (!initialized) return;
        MainMenu.OnMainMenuAwake -= HandleMainMenuAwake;
        MainMenu.OnMainMenuDestroy -= HandleMainMenuDestroy;
        PauseMenu.onPauseMenuOn -= HandlePauseMenuOpened;
        PauseMenu.onPauseMenuOff -= HandlePauseMenuClosed;
        foreach (var injected in injectedByRoot.Values.Where(value => value != null))
            UnityEngine.Object.Destroy(injected);
        injectedByRoot.Clear();
        foreach (var key in registeredLocalizationKeys) LocalizationManager.RemoveOverrideText(key);
        registeredLocalizationKeys.Clear();
        UiText.ConfigureNativeResolver(null);
        initialized = false;
    }
}

internal enum NativeMenuIntegrationState
{
    NotObserved,
    Available,
    Unavailable
}

internal sealed class NativeItemIconResolver
{
    private const string DuckovItemPrefix = "duckov:item:";
    private readonly Dictionary<string, Sprite?> cache = new(StringComparer.Ordinal);

    public Sprite? Resolve(string stableItemId)
    {
        if (string.IsNullOrWhiteSpace(stableItemId)) return ResolveFallback();
        if (cache.TryGetValue(stableItemId, out var cached)) return cached;
        Sprite? icon = null;
        if (stableItemId.StartsWith(DuckovItemPrefix, StringComparison.Ordinal)
            && int.TryParse(stableItemId.AsSpan(DuckovItemPrefix.Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var typeId))
        {
            icon = ItemAssetsCollection.GetMetaData(typeId).icon;
        }

        icon ??= ResolveFallback();
        if (cache.Count >= 512) cache.Clear();
        cache[stableItemId] = icon;
        return icon;
    }

    private static Sprite? ResolveFallback()
    {
        try
        {
            return Duckov.Utilities.GameplayDataSettings.UIStyle.FallbackItemIcon;
        }
        catch
        {
            return null;
        }
    }
}

internal sealed class NativePanelTheme : IDisposable
{
    private readonly List<Texture2D> ownedTextures = new();
    private bool initialized;

    public GUIStyle Window { get; private set; } = new();
    public GUIStyle Tab { get; private set; } = new();
    public GUIStyle Section { get; private set; } = new();
    public GUIStyle Muted { get; private set; } = new();

    public void EnsureInitialized()
    {
        if (initialized) return;
        Window = new GUIStyle(GUI.skin.window)
        {
            padding = new RectOffset(18, 18, 28, 16),
            fontSize = 15
        };
        var windowBackground = Texture(new Color(0.075f, 0.09f, 0.085f, 0.98f));
        var windowTextColor = new Color(0.9f, 0.83f, 0.65f);
        ApplyState(Window.normal, windowBackground, windowTextColor);
        ApplyState(Window.hover, windowBackground, windowTextColor);
        ApplyState(Window.active, windowBackground, windowTextColor);
        ApplyState(Window.focused, windowBackground, windowTextColor);
        ApplyState(Window.onNormal, windowBackground, windowTextColor);
        ApplyState(Window.onHover, windowBackground, windowTextColor);
        ApplyState(Window.onActive, windowBackground, windowTextColor);
        ApplyState(Window.onFocused, windowBackground, windowTextColor);
        Tab = new GUIStyle(GUI.skin.button)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 13,
            fixedHeight = 34
        };
        Tab.normal.background = Texture(new Color(0.12f, 0.15f, 0.14f, 1f));
        Tab.normal.textColor = new Color(0.79f, 0.76f, 0.66f);
        Tab.hover.background = Texture(new Color(0.2f, 0.25f, 0.22f, 1f));
        Tab.hover.textColor = Color.white;
        Tab.onNormal.background = Texture(new Color(0.35f, 0.31f, 0.18f, 1f));
        Tab.onNormal.textColor = new Color(1f, 0.88f, 0.48f);
        Tab.onHover = Tab.onNormal;
        Section = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            fontStyle = FontStyle.Bold,
            normal = { textColor = new Color(0.96f, 0.82f, 0.45f) }
        };
        Muted = new GUIStyle(GUI.skin.label)
        {
            wordWrap = true,
            normal = { textColor = new Color(0.65f, 0.68f, 0.62f) }
        };
        initialized = true;
    }

    private static void ApplyState(GUIStyleState state, Texture2D background, Color textColor)
    {
        state.background = background;
        state.textColor = textColor;
    }

    private Texture2D Texture(Color color)
    {
        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, mipChain: false)
        {
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Point
        };
        texture.SetPixel(0, 0, color);
        texture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        ownedTextures.Add(texture);
        return texture;
    }

    public void Dispose()
    {
        foreach (var texture in ownedTextures.Where(value => value != null))
            UnityEngine.Object.Destroy(texture);
        ownedTextures.Clear();
        initialized = false;
    }
}
