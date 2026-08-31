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
    private readonly Dictionary<TextMeshProUGUI, (NativeTypographyRole Role, float Scale)> textSizing = new();
    private GameObject? root;
    private Canvas? canvas;
    private RectTransform? frame;
    private RectTransform? header;
    private RectTransform? navigationRail;
    private RectTransform? tabViewport;
    private RectTransform? tabContent;
    private RectTransform? contentHost;
    private RectTransform? contentViewport;
    private RectTransform? contentRoot;
    private ScrollRect? tabScroll;
    private ScrollRect? contentScroll;
    private HorizontalLayoutGroup? tabLayout;
    private CanvasGroup? tabLeadingCue;
    private CanvasGroup? tabTrailingCue;
    private CanvasGroup? contentLeadingCue;
    private CanvasGroup? contentTrailingCue;
    private Button? backButton;
    private TextMeshProUGUI? title;
    private TextMeshProUGUI? placeholderTitle;
    private TextMeshProUGUI? placeholderBody;
    private NativeTypographyRoles? typography;
    private StatisticsPanelTab selectedTab;
    private int lastScreenWidth = -1;
    private int lastScreenHeight = -1;
    private float lastCanvasScale = -1f;
    private RetainedShellLayout currentLayout = RetainedShellLayoutPolicy.Create(2560f, 1440f);

    public bool IsCreated => root != null;

    public RectTransform? ContentRoot => contentRoot;

    public string? TypographySummary => typography?.Describe();

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
        NativeTextTemplateSnapshot? preferredMenuTypography,
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
            if (!NativeTextTemplateSnapshot.TryCapture(
                    nativeTextTemplate,
                    "GameplayDataSettings.UIStyle.TemplateTextUGUI",
                    out var publicTypography)
                || publicTypography == null)
            {
                error = "Duckov's public UI text template could not be captured safely.";
                return false;
            }

            typography = new NativeTypographyRoles(publicTypography, preferredMenuTypography);

            canvas = targetCanvas;
            root = CreateRect("UltimateDuckovStatisticsRetainedShell", targetCanvas.transform).gameObject;
            root.SetActive(false);
            root.AddComponent<CanvasGroup>().blocksRaycasts = true;
            var blocker = root.AddComponent<Image>();
            blocker.color = new Color(0.005f, 0.015f, 0.035f, 0.48f);
            blocker.raycastTarget = true;

            frame = CreateRect("Frame", root.transform);
            var frameImage = frame.gameObject.AddComponent<Image>();
            frameImage.color = new Color(0.012f, 0.055f, 0.095f, 0.9f);
            frameImage.raycastTarget = true;

            header = CreateRect("Header", frame);
            title = CreateText(
                "Title",
                header,
                UiText.Get("ui.title"),
                NativeTypographyRole.Title,
                TextAlignmentOptions.MidlineLeft,
                new Color(0.98f, 0.96f, 0.86f, 1f));
            title.enableWordWrapping = false;

            backButton = CreateBackButton(header, close);

            tabViewport = CreateRect("TabViewport", frame);
            tabViewport.gameObject.AddComponent<RectMask2D>();
            tabScroll = tabViewport.gameObject.AddComponent<ScrollRect>();
            ApplyNativeScrollSettings(tabScroll);
            tabScroll.movementType = ScrollRect.MovementType.Clamped;
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
            tabScroll.onValueChanged.AddListener(_ => UpdateTabOverflowCues());
            tabLeadingCue = CreateDirectionalCue("TabLeadingCue", tabViewport, "<");
            tabTrailingCue = CreateDirectionalCue("TabTrailingCue", tabViewport, ">");

            navigationRail = CreateRect("NavigationRail", frame);
            var navigationRailImage = navigationRail.gameObject.AddComponent<Image>();
            navigationRailImage.color = new Color(0.08f, 0.78f, 0.9f, 0.96f);
            navigationRailImage.raycastTarget = false;

            foreach (var tab in PanelInteractionState.NavigationOrder)
            {
                var capturedTab = tab;
                var tabButton = CreateButton(
                    $"Tab{tab}",
                    tabContent,
                    TabLabel(tab),
                    () => selectTab(capturedTab));
                var layoutElement = tabButton.gameObject.AddComponent<LayoutElement>();
                tabButtons.Add(tab, tabButton);
                tabLayouts.Add(tab, layoutElement);
                var tabLabel = tabButton.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
                tabLabel.enableWordWrapping = false;
                tabLabels.Add(tab, tabLabel);
            }

            contentHost = CreateRect("ContentPlaceholder", frame);
            var contentImage = contentHost.gameObject.AddComponent<Image>();
            contentImage.color = new Color(0.008f, 0.035f, 0.06f, 0.78f);
            contentImage.raycastTarget = false;

            contentViewport = CreateRect("ContentViewport", contentHost);
            contentViewport.gameObject.AddComponent<RectMask2D>();
            contentScroll = contentViewport.gameObject.AddComponent<ScrollRect>();
            ApplyNativeScrollSettings(contentScroll);
            contentScroll.movementType = ScrollRect.MovementType.Clamped;
            contentScroll.horizontal = false;
            contentScroll.vertical = true;
            contentRoot = CreateRect("ContentRoot", contentViewport);
            Stretch(contentRoot, 0f, 0f, 0f, 0f);
            contentScroll.viewport = contentViewport;
            contentScroll.content = contentRoot;
            contentScroll.verticalNormalizedPosition = 1f;
            contentScroll.onValueChanged.AddListener(_ => UpdateContentOverflowCues());
            placeholderTitle = CreateText(
                "PlaceholderTitle",
                contentRoot,
                TabLabel(initialTab),
                NativeTypographyRole.Title,
                TextAlignmentOptions.Center,
                new Color(0.18f, 0.86f, 0.96f, 1f),
                scale: 0.6f);
            placeholderBody = CreateText(
                "PlaceholderBody",
                contentRoot,
                UiText.Get("ui.shell_placeholder"),
                NativeTypographyRole.Body,
                TextAlignmentOptions.Top,
                new Color(0.78f, 0.84f, 0.88f, 1f));
            contentLeadingCue = CreateDirectionalCue("ContentLeadingCue", contentHost, "^");
            contentTrailingCue = CreateDirectionalCue("ContentTrailingCue", contentHost, "v");

            SetSelectedTab(initialTab, ensureVisible: false);
            RefreshLayout(force: true);
            root.transform.SetAsLastSibling();
            root.SetActive(true);
            Canvas.ForceUpdateCanvases();
            EnsureSelectedVisible();
            UpdateContentOverflowCues();
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
        UpdateTabOverflowCues();
        UpdateContentOverflowCues();
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
                label.color = selected
                    ? new Color(0.99f, 1f, 1f, 1f)
                    : new Color(0.91f, 0.95f, 0.97f, 1f);
        }

        if (!ensureVisible || root == null) return;
        Canvas.ForceUpdateCanvases();
        EnsureSelectedVisible();
        FocusSelectedTab();
    }

    private void RefreshLayout(bool force)
    {
        if (root == null || canvas == null || frame == null || header == null || navigationRail == null
            || tabViewport == null
            || tabContent == null || contentHost == null || contentViewport == null || tabLayout == null)
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
        AnchorTop(
            navigationRail,
            innerMargin,
            innerMargin,
            innerMargin + headerHeight + tabHeight - currentLayout.NavigationRailPixels * unit,
            currentLayout.NavigationRailPixels * unit);
        Stretch(
            contentHost,
            innerMargin,
            innerMargin,
            innerMargin + headerHeight + tabHeight + 10f * unit,
            innerMargin);
        Stretch(contentViewport, 1f * unit, 1f * unit, 1f * unit, 1f * unit);

        if (title != null)
        {
            Stretch(
                title.rectTransform,
                (currentLayout.BackControlPixels + 24f) * unit,
                16f * unit,
                0f,
                0f);
        }
        if (backButton != null)
        {
            var rect = backButton.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0f, 0.5f);
            rect.anchorMax = new Vector2(0f, 0.5f);
            rect.pivot = new Vector2(0f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = Vector2.one * (currentLayout.BackControlPixels * unit);
            LayoutBackArrow(backButton.transform, currentLayout.BackControlPixels * unit);
        }

        tabLayout.spacing = currentLayout.TabSpacingPixels * unit;
        tabLayout.padding = new RectOffset(
            Mathf.RoundToInt(currentLayout.TabPaddingPixels * unit),
            Mathf.RoundToInt(currentLayout.TabPaddingPixels * unit),
            0,
            0);
        foreach (var entry in textSizing)
        {
            if (entry.Key == null) continue;
            entry.Key.fontSize = ResolveFontPixels(entry.Value.Role) * entry.Value.Scale * unit;
        }

        foreach (var entry in tabLayouts)
        {
            var preferredTextWidthPixels = tabLabels.TryGetValue(entry.Key, out var label) && label != null
                ? label.GetPreferredValues(label.text).x * canvasScale
                : 0f;
            var tabWidthPixels = RetainedTabWidthPolicy.Resolve(
                currentLayout.TabWidthPixels,
                preferredTextWidthPixels,
                horizontalPaddingPixels: 38f);
            entry.Value.minWidth = tabWidthPixels * unit;
            entry.Value.preferredWidth = tabWidthPixels * unit;
            entry.Value.minHeight = currentLayout.TabHeightPixels * unit;
            entry.Value.preferredHeight = currentLayout.TabHeightPixels * unit;
        }

        tabContent.sizeDelta = new Vector2(tabContent.sizeDelta.x, 0f);
        PositionHorizontalCue(tabLeadingCue, leading: true, 26f * unit);
        PositionHorizontalCue(tabTrailingCue, leading: false, 26f * unit);
        PositionVerticalCue(contentLeadingCue, leading: true, 24f * unit);
        PositionVerticalCue(contentTrailingCue, leading: false, 24f * unit);
        if (placeholderTitle != null)
        {
            placeholderTitle.rectTransform.anchorMin = new Vector2(0.08f, 0.54f);
            placeholderTitle.rectTransform.anchorMax = new Vector2(0.92f, 0.68f);
            placeholderTitle.rectTransform.offsetMin = Vector2.zero;
            placeholderTitle.rectTransform.offsetMax = Vector2.zero;
        }
        if (placeholderBody != null)
        {
            placeholderBody.rectTransform.anchorMin = new Vector2(0.12f, 0.3f);
            placeholderBody.rectTransform.anchorMax = new Vector2(0.88f, 0.52f);
            placeholderBody.rectTransform.offsetMin = Vector2.zero;
            placeholderBody.rectTransform.offsetMax = Vector2.zero;
        }

        Canvas.ForceUpdateCanvases();
        EnsureSelectedVisible();
        UpdateContentOverflowCues();
    }

    private void EnsureSelectedVisible()
    {
        if (root == null || !root.activeInHierarchy) return;
        if (tabScroll == null || tabViewport == null || tabContent == null) return;
        if (!tabButtons.TryGetValue(selectedTab, out var selectedButton) || selectedButton == null) return;
        var viewportWidth = tabViewport.rect.width;
        var contentWidth = tabContent.rect.width;
        var selectedRect = selectedButton.GetComponent<RectTransform>();
        var selectedBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(tabContent, selectedRect);
        var selectedLeft = selectedBounds.min.x - tabContent.rect.xMin;
        var selectedWidth = selectedBounds.size.x;
        var currentOffset = tabScroll.horizontalNormalizedPosition * Math.Max(0f, contentWidth - viewportWidth);
        if (!RuntimeTabStripScrollPolicy.TryEnsureVisible(
                viewportWidth,
                contentWidth,
                selectedLeft,
                selectedWidth,
                currentOffset,
                out var targetOffset))
        {
            return;
        }
        var overflow = Math.Max(0f, contentWidth - viewportWidth);
        tabScroll.horizontalNormalizedPosition = overflow <= 0f ? 0f : targetOffset / overflow;
        UpdateTabOverflowCues();
    }

    private float ResolveFontPixels(NativeTypographyRole role) => role switch
    {
        NativeTypographyRole.Title => currentLayout.TitleFontPixels,
        NativeTypographyRole.Navigation => currentLayout.NavigationFontPixels,
        NativeTypographyRole.Body => currentLayout.BodyFontPixels,
        NativeTypographyRole.Secondary => currentLayout.SecondaryFontPixels,
        _ => throw new ArgumentOutOfRangeException(nameof(role))
    };

    private void UpdateTabOverflowCues()
    {
        if (tabScroll == null || tabViewport == null || tabContent == null) return;
        var overflow = Math.Max(0f, tabContent.rect.width - tabViewport.rect.width);
        var state = OverflowCuePolicy.Resolve(
            Math.Max(1f, tabViewport.rect.width),
            Math.Max(0f, tabContent.rect.width),
            tabScroll.horizontalNormalizedPosition * overflow);
        SetCueVisible(tabLeadingCue, state.ShowLeading);
        SetCueVisible(tabTrailingCue, state.ShowTrailing);
    }

    private void UpdateContentOverflowCues()
    {
        if (contentScroll == null || contentViewport == null || contentRoot == null) return;
        var overflow = Math.Max(0f, contentRoot.rect.height - contentViewport.rect.height);
        var state = OverflowCuePolicy.Resolve(
            Math.Max(1f, contentViewport.rect.height),
            Math.Max(0f, contentRoot.rect.height),
            (1f - contentScroll.verticalNormalizedPosition) * overflow);
        SetCueVisible(contentLeadingCue, state.ShowLeading);
        SetCueVisible(contentTrailingCue, state.ShowTrailing);
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
        NativeTypographyRole role,
        TextAlignmentOptions alignment,
        Color color,
        float scale = 1f)
    {
        if (typography == null) throw new InvalidOperationException("Native typography roles were not initialized.");
        if (scale <= 0f) throw new ArgumentOutOfRangeException(nameof(scale));
        var rect = CreateRect(name, parent);
        var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
        typography.Resolve(role).Apply(text);
        text.enableWordWrapping = true;
        text.overflowMode = TextOverflowModes.Ellipsis;
        text.raycastTarget = false;
        text.alignment = alignment;
        text.color = color;
        text.text = value;
        textSizing.Add(text, (role, scale));
        return text;
    }

    private Button CreateButton(
        string name,
        Transform parent,
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
            NativeTypographyRole.Navigation,
            TextAlignmentOptions.Center,
            Color.white);
        Stretch(text.rectTransform, 8f, 8f, 2f, 2f);
        return button;
    }

    private static Button CreateBackButton(Transform parent, Action clicked)
    {
        var rect = CreateRect("Back", parent);
        var image = rect.gameObject.AddComponent<Image>();
        image.raycastTarget = true;
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
        button.colors = new ColorBlock
        {
            normalColor = new Color(0.08f, 0.76f, 0.88f, 1f),
            highlightedColor = new Color(0.23f, 0.9f, 0.98f, 1f),
            pressedColor = new Color(0.05f, 0.55f, 0.68f, 1f),
            selectedColor = new Color(0.16f, 0.84f, 0.94f, 1f),
            disabledColor = new Color(0.25f, 0.34f, 0.39f, 0.72f),
            colorMultiplier = 1f,
            fadeDuration = 0.08f
        };
        button.onClick.AddListener(() => clicked());
        CreateArrowSegment("Shaft", rect, 0f);
        CreateArrowSegment("HeadUpper", rect, 45f);
        CreateArrowSegment("HeadLower", rect, -45f);
        return button;
    }

    private static void CreateArrowSegment(string name, Transform parent, float rotation)
    {
        var rect = CreateRect(name, parent);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = Color.white;
        image.raycastTarget = false;
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.localRotation = Quaternion.Euler(0f, 0f, rotation);
    }

    private static void LayoutBackArrow(Transform back, float controlSize)
    {
        var thickness = Math.Max(3f, controlSize * 0.065f);
        var shaft = back.Find("Shaft") as RectTransform;
        var upper = back.Find("HeadUpper") as RectTransform;
        var lower = back.Find("HeadLower") as RectTransform;
        if (shaft != null)
        {
            shaft.anchoredPosition = new Vector2(controlSize * 0.07f, 0f);
            shaft.sizeDelta = new Vector2(controlSize * 0.48f, thickness);
        }
        if (upper != null)
        {
            upper.anchoredPosition = new Vector2(-controlSize * 0.13f, controlSize * 0.12f);
            upper.sizeDelta = new Vector2(controlSize * 0.3f, thickness);
        }
        if (lower != null)
        {
            lower.anchoredPosition = new Vector2(-controlSize * 0.13f, -controlSize * 0.12f);
            lower.sizeDelta = new Vector2(controlSize * 0.3f, thickness);
        }
    }

    private CanvasGroup CreateDirectionalCue(
        string name,
        Transform parent,
        string marker)
    {
        var rect = CreateRect(name, parent);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = new Color(0.02f, 0.5f, 0.58f, 0.82f);
        image.raycastTarget = false;
        var group = rect.gameObject.AddComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = false;
        group.blocksRaycasts = false;
        var text = CreateText(
            "Marker",
            rect,
            marker,
            NativeTypographyRole.Secondary,
            TextAlignmentOptions.Center,
            Color.white);
        Stretch(text.rectTransform, 0f, 0f, 0f, 0f);
        return group;
    }

    private static void PositionHorizontalCue(CanvasGroup? cue, bool leading, float width)
    {
        if (cue == null) return;
        var rect = cue.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(leading ? 0f : 1f, 0f);
        rect.anchorMax = new Vector2(leading ? 0f : 1f, 1f);
        rect.pivot = new Vector2(leading ? 0f : 1f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(width, 0f);
        rect.SetAsLastSibling();
    }

    private static void PositionVerticalCue(CanvasGroup? cue, bool leading, float height)
    {
        if (cue == null) return;
        var rect = cue.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, leading ? 1f : 0f);
        rect.anchorMax = new Vector2(1f, leading ? 1f : 0f);
        rect.pivot = new Vector2(0.5f, leading ? 1f : 0f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(0f, height);
        rect.SetAsLastSibling();
    }

    private static void SetCueVisible(CanvasGroup? cue, bool visible)
    {
        if (cue == null) return;
        cue.alpha = visible ? 1f : 0f;
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
                highlightedColor = new Color(0.28f, 0.92f, 0.98f, 1f),
                pressedColor = new Color(0.06f, 0.58f, 0.7f, 1f),
                selectedColor = new Color(0.2f, 0.86f, 0.94f, 1f),
                disabledColor = new Color(0.28f, 0.34f, 0.35f, 0.78f),
                colorMultiplier = 1f,
                fadeDuration = 0.08f
            }
            : new ColorBlock
            {
                normalColor = new Color(0.045f, 0.13f, 0.2f, 0.98f),
                highlightedColor = new Color(0.08f, 0.34f, 0.45f, 1f),
                pressedColor = new Color(0.035f, 0.24f, 0.34f, 1f),
                selectedColor = new Color(0.07f, 0.29f, 0.39f, 1f),
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
        navigationRail = null;
        tabViewport = null;
        tabContent = null;
        contentHost = null;
        contentViewport = null;
        contentRoot = null;
        tabScroll = null;
        contentScroll = null;
        tabLayout = null;
        tabLeadingCue = null;
        tabTrailingCue = null;
        contentLeadingCue = null;
        contentTrailingCue = null;
        backButton = null;
        title = null;
        placeholderTitle = null;
        placeholderBody = null;
        typography = null;
        tabButtons.Clear();
        tabLabels.Clear();
        tabLayouts.Clear();
        textSizing.Clear();
        lastScreenWidth = -1;
        lastScreenHeight = -1;
        lastCanvasScale = -1f;
    }

    public void Dispose() => DestroyRoot();
}
