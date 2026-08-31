using Duckov.Utilities;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UltimateDuckovStatistics.UI;

/// <summary>
/// Owns the reusable M17 retained-mode shell. Statistics view bodies remain in
/// the legacy renderer until Gate 2; this type deliberately contains no metric
/// projection or persistence logic.
/// </summary>
internal sealed class RetainedStatisticsShell : IDisposable
{
    private readonly Dictionary<StatisticsPanelTab, Button> tabButtons = new();
    private readonly Dictionary<StatisticsPanelTab, TextMeshProUGUI> tabLabels = new();
    private readonly Dictionary<StatisticsPanelTab, LayoutElement> tabLayouts = new();
    private readonly List<TextMeshProUGUI> allText = new();
    private GameObject? root;
    private Canvas? canvas;
    private RectTransform? frame;
    private RectTransform? header;
    private RectTransform? tabViewport;
    private RectTransform? tabContent;
    private RectTransform? contentHost;
    private ScrollRect? tabScroll;
    private HorizontalLayoutGroup? tabLayout;
    private Button? closeButton;
    private TextMeshProUGUI? title;
    private TextMeshProUGUI? placeholderTitle;
    private TextMeshProUGUI? placeholderBody;
    private StatisticsPanelTab selectedTab;
    private int lastScreenWidth = -1;
    private int lastScreenHeight = -1;
    private float lastCanvasScale = -1f;
    private RetainedShellLayout currentLayout = RetainedShellLayoutPolicy.Create(2560f, 1440f);

    public bool IsCreated => root != null;

    public bool IsUsable => root != null
                            && root.activeInHierarchy
                            && canvas != null
                            && canvas.enabled
                            && canvas.gameObject.activeInHierarchy;

    public bool TryCreate(
        Canvas targetCanvas,
        StatisticsPanelTab initialTab,
        Action close,
        Action<StatisticsPanelTab> selectTab,
        out string? error)
    {
        if (targetCanvas == null) throw new ArgumentNullException(nameof(targetCanvas));
        if (close == null) throw new ArgumentNullException(nameof(close));
        if (selectTab == null) throw new ArgumentNullException(nameof(selectTab));
        error = null;
        if (root != null) return true;

        try
        {
            var nativeTextTemplate = GameplayDataSettings.UIStyle?.TemplateTextUGUI;
            if (nativeTextTemplate == null || nativeTextTemplate.font == null)
            {
                error = "Duckov's public UI text template or font was unavailable.";
                return false;
            }

            canvas = targetCanvas;
            root = CreateRect("UltimateDuckovStatisticsRetainedShell", targetCanvas.transform).gameObject;
            root.SetActive(false);
            root.AddComponent<CanvasGroup>().blocksRaycasts = true;
            var blocker = root.AddComponent<Image>();
            blocker.color = new Color(0.01f, 0.02f, 0.025f, 0.42f);
            blocker.raycastTarget = true;

            frame = CreateRect("Frame", root.transform);
            var frameImage = frame.gameObject.AddComponent<Image>();
            frameImage.color = new Color(0.025f, 0.045f, 0.047f, 0.96f);
            frameImage.raycastTarget = true;

            header = CreateRect("Header", frame);
            title = CreateText(
                "Title",
                header,
                UiText.Get("ui.title"),
                nativeTextTemplate,
                TextAlignmentOptions.Center,
                new Color(0.96f, 0.93f, 0.74f, 1f));

            closeButton = CreateButton("Close", header, nativeTextTemplate, UiText.Get("ui.close"), close);

            tabViewport = CreateRect("TabViewport", frame);
            tabViewport.gameObject.AddComponent<RectMask2D>();
            tabScroll = tabViewport.gameObject.AddComponent<ScrollRect>();
            ApplyNativeScrollSettings(tabScroll);
            tabScroll.horizontal = true;
            tabScroll.vertical = false;

            tabContent = CreateRect("TabContent", tabViewport);
            tabContent.anchorMin = new Vector2(0f, 0f);
            tabContent.anchorMax = new Vector2(0f, 1f);
            tabContent.pivot = new Vector2(0f, 0.5f);
            tabContent.anchoredPosition = Vector2.zero;
            tabLayout = tabContent.gameObject.AddComponent<HorizontalLayoutGroup>();
            tabLayout.childAlignment = TextAnchor.MiddleLeft;
            tabLayout.childControlWidth = true;
            tabLayout.childControlHeight = true;
            tabLayout.childForceExpandWidth = false;
            tabLayout.childForceExpandHeight = false;
            var fitter = tabContent.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            tabScroll.viewport = tabViewport;
            tabScroll.content = tabContent;

            foreach (var tab in PanelInteractionState.NavigationOrder)
            {
                var capturedTab = tab;
                var tabButton = CreateButton(
                    $"Tab{tab}",
                    tabContent,
                    nativeTextTemplate,
                    TabLabel(tab),
                    () => selectTab(capturedTab));
                var layoutElement = tabButton.gameObject.AddComponent<LayoutElement>();
                tabButtons.Add(tab, tabButton);
                tabLayouts.Add(tab, layoutElement);
                tabLabels.Add(tab, tabButton.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true));
            }

            contentHost = CreateRect("ContentPlaceholder", frame);
            var contentImage = contentHost.gameObject.AddComponent<Image>();
            contentImage.color = new Color(0.035f, 0.065f, 0.068f, 0.9f);
            contentImage.raycastTarget = false;
            placeholderTitle = CreateText(
                "PlaceholderTitle",
                contentHost,
                TabLabel(initialTab),
                nativeTextTemplate,
                TextAlignmentOptions.Center,
                new Color(0.15f, 0.82f, 0.9f, 1f));
            placeholderBody = CreateText(
                "PlaceholderBody",
                contentHost,
                UiText.Get("ui.shell_placeholder"),
                nativeTextTemplate,
                TextAlignmentOptions.Top,
                new Color(0.74f, 0.78f, 0.74f, 1f));

            SetSelectedTab(initialTab, ensureVisible: false);
            RefreshLayout(force: true);
            root.transform.SetAsLastSibling();
            root.SetActive(true);
            Canvas.ForceUpdateCanvases();
            EnsureSelectedVisible();
            FocusSelectedTab();
            return true;
        }
        catch (Exception exception)
        {
            error = $"{exception.GetType().Name}: {exception.Message}";
            DestroyRoot();
            return false;
        }
    }

    public void Tick()
    {
        if (root == null) return;
        RefreshLayout(force: false);
    }

    public void SetSelectedTab(StatisticsPanelTab tab, bool ensureVisible = true)
    {
        if (!PanelInteractionState.NavigationOrder.Contains(tab))
            throw new ArgumentOutOfRangeException(nameof(tab));
        selectedTab = tab;
        if (placeholderTitle != null) placeholderTitle.text = TabLabel(tab);
        foreach (var entry in tabButtons)
        {
            var selected = entry.Key == tab;
            ApplyButtonColors(entry.Value, selected);
            if (tabLabels.TryGetValue(entry.Key, out var label) && label != null)
                label.color = selected ? new Color(0.015f, 0.09f, 0.11f, 1f) : Color.white;
        }

        if (!ensureVisible || root == null) return;
        Canvas.ForceUpdateCanvases();
        EnsureSelectedVisible();
        FocusSelectedTab();
    }

    private void RefreshLayout(bool force)
    {
        if (root == null || canvas == null || frame == null || header == null || tabViewport == null
            || tabContent == null || contentHost == null || tabLayout == null)
        {
            return;
        }

        var canvasScale = Math.Max(0.01f, canvas.scaleFactor);
        if (!force
            && lastScreenWidth == Screen.width
            && lastScreenHeight == Screen.height
            && Math.Abs(lastCanvasScale - canvasScale) < 0.001f)
        {
            return;
        }

        lastScreenWidth = Screen.width;
        lastScreenHeight = Screen.height;
        lastCanvasScale = canvasScale;
        currentLayout = RetainedShellLayoutPolicy.Create(Screen.width, Screen.height);
        var unit = 1f / canvasScale;
        var margin = currentLayout.MarginPixels * unit;
        var headerHeight = currentLayout.HeaderHeightPixels * unit;
        var tabHeight = currentLayout.TabRowHeightPixels * unit;
        var innerMargin = currentLayout.InnerMarginPixels * unit;

        Stretch(root.GetComponent<RectTransform>(), 0f, 0f, 0f, 0f);
        Stretch(frame, margin, margin, margin, margin);
        AnchorTop(header, innerMargin, innerMargin, innerMargin, headerHeight);
        AnchorTop(tabViewport, innerMargin, innerMargin, innerMargin + headerHeight, tabHeight);
        Stretch(
            contentHost,
            innerMargin,
            innerMargin,
            innerMargin + headerHeight + tabHeight + 10f * unit,
            innerMargin);

        if (title != null)
        {
            Stretch(title.rectTransform, 130f * unit, 130f * unit, 0f, 0f);
            title.fontSize = 34f * unit;
        }
        if (closeButton != null)
        {
            var rect = closeButton.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(1f, 0.5f);
            rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-5f * unit, 0f);
            rect.sizeDelta = new Vector2(112f * unit, 50f * unit);
        }

        tabLayout.spacing = currentLayout.TabSpacingPixels * unit;
        tabLayout.padding = new RectOffset(
            Mathf.RoundToInt(currentLayout.TabPaddingPixels * unit),
            Mathf.RoundToInt(currentLayout.TabPaddingPixels * unit),
            0,
            0);
        foreach (var layoutElement in tabLayouts.Values)
        {
            layoutElement.minWidth = currentLayout.TabWidthPixels * unit;
            layoutElement.preferredWidth = currentLayout.TabWidthPixels * unit;
            layoutElement.minHeight = currentLayout.TabHeightPixels * unit;
            layoutElement.preferredHeight = currentLayout.TabHeightPixels * unit;
        }

        tabContent.sizeDelta = new Vector2(tabContent.sizeDelta.x, 0f);
        if (placeholderTitle != null)
        {
            placeholderTitle.rectTransform.anchorMin = new Vector2(0.08f, 0.54f);
            placeholderTitle.rectTransform.anchorMax = new Vector2(0.92f, 0.68f);
            placeholderTitle.rectTransform.offsetMin = Vector2.zero;
            placeholderTitle.rectTransform.offsetMax = Vector2.zero;
            placeholderTitle.fontSize = 38f * unit;
        }
        if (placeholderBody != null)
        {
            placeholderBody.rectTransform.anchorMin = new Vector2(0.12f, 0.3f);
            placeholderBody.rectTransform.anchorMax = new Vector2(0.88f, 0.52f);
            placeholderBody.rectTransform.offsetMin = Vector2.zero;
            placeholderBody.rectTransform.offsetMax = Vector2.zero;
            placeholderBody.fontSize = 21f * unit;
        }

        foreach (var text in allText.Where(value => value != null && value != title
                                                       && value != placeholderTitle && value != placeholderBody))
        {
            text.fontSize = 20f * unit;
        }

        Canvas.ForceUpdateCanvases();
        EnsureSelectedVisible();
    }

    private void EnsureSelectedVisible()
    {
        if (tabScroll == null || tabViewport == null || tabContent == null) return;
        var selectedIndex = Array.IndexOf(PanelInteractionState.NavigationOrder.ToArray(), selectedTab);
        if (selectedIndex < 0) return;
        var unit = 1f / Math.Max(0.01f, lastCanvasScale);
        var viewportWidth = tabViewport.rect.width;
        var contentWidth = tabContent.rect.width;
        var selectedLeft = (currentLayout.TabPaddingPixels
                            + selectedIndex * (currentLayout.TabWidthPixels + currentLayout.TabSpacingPixels)) * unit;
        var selectedWidth = currentLayout.TabWidthPixels * unit;
        var currentOffset = tabScroll.horizontalNormalizedPosition * Math.Max(0f, contentWidth - viewportWidth);
        var targetOffset = TabStripScrollPolicy.EnsureVisible(
            viewportWidth,
            contentWidth,
            selectedLeft,
            selectedWidth,
            currentOffset);
        var overflow = Math.Max(0f, contentWidth - viewportWidth);
        tabScroll.horizontalNormalizedPosition = overflow <= 0f ? 0f : targetOffset / overflow;
    }

    private void FocusSelectedTab()
    {
        if (!tabButtons.TryGetValue(selectedTab, out var button) || button == null) return;
        var eventSystem = GameManager.EventSystem;
        if (eventSystem != null) eventSystem.SetSelectedGameObject(button.gameObject);
    }

    private TextMeshProUGUI CreateText(
        string name,
        Transform parent,
        string value,
        TextMeshProUGUI nativeTemplate,
        TextAlignmentOptions alignment,
        Color color)
    {
        var rect = CreateRect(name, parent);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        text.font = nativeTemplate.font;
        text.fontSharedMaterial = nativeTemplate.fontSharedMaterial;
        text.fontStyle = nativeTemplate.fontStyle;
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        text.alignment = alignment;
        text.color = color;
        text.text = value;
        allText.Add(text);
        return text;
    }

    private Button CreateButton(
        string name,
        Transform parent,
        TextMeshProUGUI nativeTextTemplate,
        string label,
        Action clicked)
    {
        var rect = CreateRect(name, parent);
        var image = rect.gameObject.AddComponent<Image>();
        image.raycastTarget = true;
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
        ApplyNativeButtonPresentation(button, image);
        ApplyButtonColors(button, selected: false);
        button.onClick.AddListener(() => clicked());
        var text = CreateText(
            "Label",
            rect,
            label,
            nativeTextTemplate,
            TextAlignmentOptions.Center,
            Color.white);
        Stretch(text.rectTransform, 8f, 8f, 2f, 2f);
        return button;
    }

    private static void ApplyNativeButtonPresentation(Button button, Image image)
    {
        var native = GameplayDataSettings.UIPrefabs?.Button;
        if (native == null) return;
        button.transition = Selectable.Transition.ColorTint;
        if (native.targetGraphic is not Image nativeImage) return;
        image.sprite = nativeImage.sprite;
        image.overrideSprite = nativeImage.overrideSprite;
        image.material = nativeImage.material;
        image.type = nativeImage.type;
        image.fillCenter = nativeImage.fillCenter;
        image.pixelsPerUnitMultiplier = nativeImage.pixelsPerUnitMultiplier;
    }

    private static void ApplyNativeScrollSettings(ScrollRect target)
    {
        var native = GameplayDataSettings.UIPrefabs?.ScrollRect;
        if (native == null) return;
        target.movementType = native.movementType;
        target.elasticity = native.elasticity;
        target.inertia = native.inertia;
        target.decelerationRate = native.decelerationRate;
        target.scrollSensitivity = native.scrollSensitivity;
    }

    private static void ApplyButtonColors(Button button, bool selected)
    {
        button.colors = selected
            ? new ColorBlock
            {
                normalColor = new Color(0.12f, 0.78f, 0.86f, 1f),
                highlightedColor = new Color(0.25f, 0.91f, 0.96f, 1f),
                pressedColor = new Color(0.07f, 0.56f, 0.65f, 1f),
                selectedColor = new Color(0.17f, 0.83f, 0.9f, 1f),
                disabledColor = new Color(0.28f, 0.34f, 0.35f, 0.78f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f
            }
            : new ColorBlock
            {
                normalColor = new Color(0.08f, 0.15f, 0.16f, 0.98f),
                highlightedColor = new Color(0.12f, 0.42f, 0.46f, 1f),
                pressedColor = new Color(0.06f, 0.3f, 0.34f, 1f),
                selectedColor = new Color(0.1f, 0.36f, 0.4f, 1f),
                disabledColor = new Color(0.22f, 0.25f, 0.25f, 0.72f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f
            };
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        var gameObject = new GameObject(name, typeof(RectTransform));
        gameObject.hideFlags = HideFlags.DontSave;
        var rect = gameObject.GetComponent<RectTransform>();
        rect.SetParent(parent, worldPositionStays: false);
        return rect;
    }

    private static void Stretch(RectTransform rect, float left, float right, float top, float bottom)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.offsetMin = new Vector2(left, bottom);
        rect.offsetMax = new Vector2(-right, -top);
    }

    private static void AnchorTop(RectTransform rect, float left, float right, float top, float height)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(left, -top - height);
        rect.offsetMax = new Vector2(-right, -top);
    }

    private static string TabLabel(StatisticsPanelTab tab) => tab switch
    {
        StatisticsPanelTab.Overview => UiText.Get("ui.overview"),
        StatisticsPanelTab.Runs => UiText.Get("ui.runs"),
        StatisticsPanelTab.Records => UiText.Get("ui.records"),
        StatisticsPanelTab.Combat => UiText.Get("ui.combat"),
        StatisticsPanelTab.Equipment => UiText.Get("ui.equipment"),
        StatisticsPanelTab.Economy => UiText.Get("ui.economy"),
        StatisticsPanelTab.Crafting => UiText.Get("ui.crafting"),
        StatisticsPanelTab.ItemUse => UiText.Get("ui.item_use"),
        StatisticsPanelTab.Diagnostics => UiText.Get("ui.diagnostics"),
        _ => throw new ArgumentOutOfRangeException(nameof(tab))
    };

    private void DestroyRoot()
    {
        if (root != null)
        {
            root.SetActive(false);
            UnityEngine.Object.Destroy(root);
        }
        root = null;
        canvas = null;
        frame = null;
        header = null;
        tabViewport = null;
        tabContent = null;
        contentHost = null;
        tabScroll = null;
        tabLayout = null;
        closeButton = null;
        title = null;
        placeholderTitle = null;
        placeholderBody = null;
        tabButtons.Clear();
        tabLabels.Clear();
        tabLayouts.Clear();
        allText.Clear();
        lastScreenWidth = -1;
        lastScreenHeight = -1;
        lastCanvasScale = -1f;
    }

    public void Dispose() => DestroyRoot();
}
