using System.Globalization;
using System.Reflection;
using Duckov.UI;
using Duckov.UI.Animations;
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
    private readonly Func<PanelAccessSurface, bool> openPanel;
    private readonly Action<PanelAccessSurface> closePanel;
    private readonly Dictionary<int, GameObject> injectedByRoot = new();
    private readonly Dictionary<PanelAccessSurface, Canvas> panelCanvases = new();
    private readonly HashSet<string> registeredLocalizationKeys = new(StringComparer.Ordinal);
    private Texture2D? menuIconTexture;
    private Sprite? menuIconSprite;
    private bool initialized;
    private bool mainMenuAnchorWarningWritten;
    private bool pauseMenuAnchorWarningWritten;

    public NativeMenuIntegrationState MainMenuState { get; private set; }

    public NativeMenuIntegrationState BasePauseMenuState { get; private set; }

    public NativeUiIntegration(
        NativeProfileCoordinator coordinator,
        Func<PanelAccessSurface, bool> openPanel,
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
            LocalizationManager.OnSetLanguage += HandleLanguageChanged;
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

    public bool TryResolvePanelCanvas(PanelAccessSurface surface, out Canvas? canvas)
    {
        try
        {
            if (surface == PanelAccessSurface.BasePauseMenu)
            {
                // Duckov places PauseMenu on a child of its screen-space canvas.
                // Resolve that live owner on every menu activation: closing clears
                // the cache while the injected button survives for reuse.
                canvas = PauseMenu.Instance == null ? null : PauseMenu.Instance
                    .GetComponentsInParent<Canvas>(includeInactive: false)
                    .FirstOrDefault(IsUsablePanelCanvas);
                if (canvas == null) return false;
                panelCanvases[surface] = canvas;
                return true;
            }
            if (panelCanvases.TryGetValue(surface, out var exact) && IsUsablePanelCanvas(exact))
            {
                canvas = exact;
                return true;
            }
            panelCanvases.Remove(surface);

            if (surface == PanelAccessSurface.Hotkey)
            {
                foreach (var knownSurface in new[] { PanelAccessSurface.MainMenu, PanelAccessSurface.BasePauseMenu })
                {
                    if (panelCanvases.TryGetValue(knownSurface, out var known) && IsUsablePanelCanvas(known))
                    {
                        canvas = known;
                        return true;
                    }
                }
            }

            IEnumerable<Canvas> candidates = Array.Empty<Canvas>();
            if (surface != PanelAccessSurface.BasePauseMenu)
            {
                var mainMenu = Resources.FindObjectsOfTypeAll<MainMenu>()
                    .FirstOrDefault(value => value != null && value.gameObject.scene.IsValid());
                if (mainMenu != null)
                {
                    candidates = mainMenu.gameObject.scene.GetRootGameObjects()
                        .SelectMany(rootObject => rootObject.GetComponentsInChildren<Canvas>(includeInactive: false));
                }
            }

            if (!candidates.Any() && surface != PanelAccessSurface.MainMenu && PauseMenu.Instance != null)
                candidates = PauseMenu.Instance.GetComponentsInChildren<Canvas>(includeInactive: false);
            if (!candidates.Any()) candidates = Resources.FindObjectsOfTypeAll<Canvas>();

            canvas = candidates
                .Where(IsUsablePanelCanvas)
                .OrderByDescending(ScorePanelCanvas)
                .ThenBy(value => value.gameObject.name, StringComparer.Ordinal)
                .FirstOrDefault();
            if (canvas == null) return false;
            panelCanvases[surface] = canvas;
            return true;
        }
        catch (Exception exception)
        {
            canvas = null;
            coordinator.ReportUiDiagnostic(
                $"M17 retained-mode canvas discovery failed for {surface}: {exception.GetType().Name}: {exception.Message}",
                "Warning");
            return false;
        }
    }

    private void HandleMainMenuAwake() => TryInjectExistingMainMenu();

    private void HandleMainMenuDestroy()
    {
        closePanel(PanelAccessSurface.MainMenu);
        MainMenuState = NativeMenuIntegrationState.NotObserved;
        panelCanvases.Remove(PanelAccessSurface.MainMenu);
        RemoveDestroyedEntries();
    }

    private void HandlePauseMenuOpened() => TryInjectPauseMenu();

    private void HandlePauseMenuClosed()
    {
        closePanel(PanelAccessSurface.BasePauseMenu);
        panelCanvases.Remove(PanelAccessSurface.BasePauseMenu);
        RemoveDestroyedEntries();
    }

    private void TryInjectExistingMainMenu()
    {
        var mainMenu = Resources.FindObjectsOfTypeAll<MainMenu>()
            .FirstOrDefault(value => value != null && value.gameObject.scene.IsValid());
        if (mainMenu == null) return;
        if (TryInjectButton(mainMenu.gameObject, PanelAccessSurface.MainMenu))
        {
            if (MainMenuState != NativeMenuIntegrationState.Available)
                MainMenuState = NativeMenuIntegrationState.AttachedUnverified;
            return;
        }

        MainMenuState = NativeMenuIntegrationState.Unavailable;
        if (!mainMenuAnchorWarningWritten)
        {
            mainMenuAnchorWarningWritten = true;
            coordinator.ReportUiDiagnostic(
                "M17 native main-menu entry could not be attached; the configured hotkey remains available.",
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
            if (BasePauseMenuState != NativeMenuIntegrationState.Available)
                BasePauseMenuState = NativeMenuIntegrationState.AttachedUnverified;
            return;
        }

        BasePauseMenuState = NativeMenuIntegrationState.Unavailable;
        if (!pauseMenuAnchorWarningWritten)
        {
            pauseMenuAnchorWarningWritten = true;
            coordinator.ReportUiDiagnostic(
                "M17 native base-pause entry could not be attached; the configured hotkey remains available.",
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
            coordinator.ReportUiDiagnostic("M17 native base-pause anchor selection was ambiguous between equally ranked native buttons.", "Warning");
            return false;
        }
        if (anchor == null || anchor.transform.parent == null)
        {
            coordinator.ReportUiDiagnostic($"M17 native {surface} has no version-checked Settings/Options/Mods button anchor with a parent.", "Warning");
            return false;
        }

        try
        {
            var button = UnityEngine.Object.Instantiate(
                anchor,
                anchor.transform.parent,
                worldPositionStays: false);
            var clone = button.gameObject;
            clone.SetActive(false);
            clone.name = "UltimateDuckovStatisticsButton";
            clone.transform.SetSiblingIndex(Math.Min(anchor.transform.GetSiblingIndex() + 1, clone.transform.parent.childCount - 1));
            var removedActionBehaviours = RemoveInheritedActionBehaviours(clone, button, surface);
            if (surface == PanelAccessSurface.MainMenu || surface == PanelAccessSurface.BasePauseMenu)
            {
                NativeButtonInteractionFeedbackPolicy.AttachIfMissing(
                    clone,
                    static target => target.GetComponents<ButtonAnimation>()
                        .Any(component => component != null && component.enabled),
                    static target => _ = target.AddComponent<ButtonAnimation>());
            }
            button.onClick = new Button.ButtonClickedEvent();
            var activation = new NativeMenuButtonActivation(() => HandleInjectedButtonActivated(surface));
            button.onClick.AddListener(activation.Invoke);
            ApplyLocalizedButtonText(clone);
            var iconApplied = ApplyStatisticsIcon(clone, surface);
            clone.SetActive(true);
            // The installed pause-menu button starts at 0x0: its native layout group
            // supplies geometry on the next layout pass. That is not an access failure.
            if (clone.transform.parent is RectTransform layoutRoot) LayoutRebuilder.MarkLayoutForRebuild(layoutRoot);
            var panelCanvas = clone.GetComponentsInParent<Canvas>(includeInactive: false)
                .Where(IsUsablePanelCanvas)
                .OrderByDescending(ScorePanelCanvas)
                .FirstOrDefault();
            if (IsUsablePanelCanvas(panelCanvas)) panelCanvases[surface] = panelCanvas;
            injectedByRoot[rootId] = clone;
            coordinator.ReportUiDiagnostic(
                $"M17 native {surface} statistics entry attached; activation has not yet been observed. " +
                $"Removed inherited action behaviours: {removedActionBehaviours}; generated icon applied: {iconApplied}.");
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

    private void HandleInjectedButtonActivated(PanelAccessSurface surface)
    {
        var opened = openPanel(surface);
        var state = opened ? NativeMenuIntegrationState.Available : NativeMenuIntegrationState.Unavailable;
        if (surface == PanelAccessSurface.MainMenu)
            MainMenuState = state;
        else if (surface == PanelAccessSurface.BasePauseMenu)
            BasePauseMenuState = state;
        coordinator.ReportUiDiagnostic(opened
            ? $"M17 native {surface} statistics entry opened successfully."
            : $"M17 native {surface} statistics entry could not open; the configured hotkey remains available.",
            opened ? "Info" : "Warning");
    }

    private static bool IsUsablePanelCanvas([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] Canvas? canvas)
    {
        return canvas != null
               && canvas.enabled
               && canvas.gameObject.activeInHierarchy
               && canvas.gameObject.scene.IsValid()
               && canvas.renderMode != RenderMode.WorldSpace
               && canvas.GetComponent<GraphicRaycaster>() != null
               && !canvas.gameObject.name.StartsWith("UltimateDuckovStatistics", StringComparison.Ordinal);
    }

    private static int ScorePanelCanvas(Canvas canvas)
    {
        var score = 0;
        if (canvas.isRootCanvas) score += 400;
        if (canvas.GetComponent<GraphicRaycaster>() != null) score += 300;
        if (canvas.GetComponent<CanvasScaler>() != null) score += 200;
        if (canvas.renderMode == RenderMode.ScreenSpaceOverlay) score += 100;
        return score + Math.Clamp(canvas.sortingOrder, -50, 50);
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

    private static int RemoveInheritedActionBehaviours(
        GameObject clone,
        Button primaryButton,
        PanelAccessSurface surface)
    {
        var removed = 0;
        var preservedUsableRootButtonAnimation = false;
        foreach (var component in clone.GetComponentsInChildren<Component>(includeInactive: true))
        {
            if (component == null || ReferenceEquals(component, primaryButton)) continue;
            if (component is not MonoBehaviour behaviour) continue;
            var hierarchy = TypeHierarchy(component.GetType()).ToArray();
            if (NativeMenuPresentationPolicy.PreservesUsableRootButtonAnimation(
                    surface,
                    hierarchy,
                    ReferenceEquals(component.gameObject, primaryButton.gameObject),
                    behaviour.enabled,
                    preservedUsableRootButtonAnimation))
            {
                preservedUsableRootButtonAnimation = true;
                continue;
            }
            if (IsPresentationBehaviour(component, surface, hierarchy)) continue;
            behaviour.enabled = false;
            UnityEngine.Object.Destroy(component);
            removed++;
        }
        return removed;
    }

    private static bool IsPresentationBehaviour(
        Component component,
        PanelAccessSurface surface,
        IEnumerable<string?> typeHierarchy)
    {
        return component is Graphic
               || component is LayoutGroup
               || component is LayoutElement
               || component is ContentSizeFitter
               || component is AspectRatioFitter
               || component is BaseMeshEffect
               || component is Mask
               || component is RectMask2D
               || NativeMenuPresentationPolicy.PreservesProceduralImageState(typeHierarchy)
               || NativeMenuPresentationPolicy.PreservesNativeInteractionDependency(surface, typeHierarchy)
               || string.Equals(component.GetType().Name, "TextLocalizor", StringComparison.Ordinal);
    }

    private static IEnumerable<string?> TypeHierarchy(Type type)
    {
        for (var current = type; current != null; current = current.BaseType) yield return current.FullName;
    }

    private bool ApplyStatisticsIcon(GameObject clone, PanelAccessSurface surface)
    {
        // Duckov 2.3.30 main-menu buttons use a direct Image child for the icon.
        // The root and Hovering children are ProceduralImages and must stay untouched.
        var images = clone.GetComponentsInChildren<Image>(includeInactive: true);
        var image = surface == PanelAccessSurface.MainMenu
            ? images.FirstOrDefault(candidate => candidate.GetType() == typeof(Image)
                && candidate.transform.parent == clone.transform && candidate.name == "Image" && candidate.sprite != null)
            : null;
        image ??= images.FirstOrDefault(candidate => candidate.GetType() == typeof(Image)
            && IsIconTransform(candidate.transform, clone.transform));
        if (image == null) return false;
        var sprite = GetStatisticsIcon();
        image.sprite = sprite;
        image.overrideSprite = sprite;
        image.preserveAspect = true;
        // Keep the cloned native icon tint (the main menu uses pale blue).
        return true;
    }

    private static bool IsIconTransform(Transform candidate, Transform root)
    {
        for (var current = candidate; current != null; current = current.parent)
        {
            if (current.gameObject.name.Contains("icon", StringComparison.OrdinalIgnoreCase)) return true;
            if (current == root) break;
        }
        return false;
    }

    private Sprite GetStatisticsIcon()
    {
        if (menuIconSprite != null) return menuIconSprite;
        const int size = 64;
        var pixels = new Color32[size * size];
        var white = new Color32(255, 255, 255, 255);
        DrawRectangle(pixels, size, 8, 8, 48, 4, white);
        DrawRectangle(pixels, size, 8, 8, 4, 48, white);
        DrawRectangle(pixels, size, 17, 12, 8, 14, white);
        DrawRectangle(pixels, size, 30, 12, 8, 26, white);
        DrawRectangle(pixels, size, 43, 12, 8, 38, white);
        menuIconTexture = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
        {
            name = "UltimateDuckovStatisticsMenuIcon",
            hideFlags = HideFlags.HideAndDontSave,
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear
        };
        menuIconTexture.SetPixels32(pixels);
        menuIconTexture.Apply(updateMipmaps: false, makeNoLongerReadable: true);
        menuIconSprite = Sprite.Create(
            menuIconTexture,
            new Rect(0, 0, size, size),
            new Vector2(0.5f, 0.5f),
            pixelsPerUnit: size);
        menuIconSprite.name = "UltimateDuckovStatisticsMenuIcon";
        menuIconSprite.hideFlags = HideFlags.HideAndDontSave;
        return menuIconSprite;
    }

    private static void DrawRectangle(Color32[] pixels, int size, int x, int y, int width, int height, Color32 color)
    {
        for (var row = Math.Max(0, y); row < Math.Min(size, y + height); row++)
            for (var column = Math.Max(0, x); column < Math.Min(size, x + width); column++)
                pixels[row * size + column] = color;
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
            registeredLocalizationKeys.Add(key);
        }
        ApplyLocalizationFallbacks();
    }

    private static void HandleLanguageChanged(SystemLanguage _)
    {
        ApplyLocalizationFallbacks();
    }

    private static void ApplyLocalizationFallbacks()
    {
        var german = LocalizationManager.CurrentLanguage == SystemLanguage.German;
        foreach (var entry in UiText.EnglishFallbacks)
        {
            var value = german && UiText.GermanFallbacks.TryGetValue(entry.Key, out var translation)
                ? translation
                : entry.Value;
            LocalizationManager.SetOverrideText(LocalizationPrefix + entry.Key, value);
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
        LocalizationManager.OnSetLanguage -= HandleLanguageChanged;
        foreach (var injected in injectedByRoot.Values.Where(value => value != null))
            UnityEngine.Object.Destroy(injected);
        injectedByRoot.Clear();
        panelCanvases.Clear();
        if (menuIconSprite != null) UnityEngine.Object.Destroy(menuIconSprite);
        if (menuIconTexture != null) UnityEngine.Object.Destroy(menuIconTexture);
        menuIconSprite = null;
        menuIconTexture = null;
        foreach (var key in registeredLocalizationKeys) LocalizationManager.RemoveOverrideText(key);
        registeredLocalizationKeys.Clear();
        UiText.ConfigureNativeResolver(null);
        initialized = false;
    }
}

internal sealed class NativeItemIconResolver
{
    private readonly Dictionary<string, Sprite?> cache = new(StringComparer.Ordinal);

    public Sprite? Resolve(string stableItemId) => NativeItemTypeIdPolicy.UseEmptyIcon(stableItemId)
        ? null : ResolveAvailable(stableItemId) ?? ResolveFallback();

    public Sprite? ResolveAvailable(string stableItemId)
    {
        if (string.IsNullOrWhiteSpace(stableItemId)) return null;
        if (cache.TryGetValue(stableItemId, out var cached) && cached != null) return cached;
        var icon = NativeItemTypeIdPolicy.Resolve(stableItemId, typeId =>
        {
            var metadata = ItemAssetsCollection.GetMetaData(typeId);
            return (metadata.id, metadata.icon);
        }, ResolveFallback());
        // Do not cache missing metadata: dynamic item registrations may become available later.
        if (icon == null) return null;
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
