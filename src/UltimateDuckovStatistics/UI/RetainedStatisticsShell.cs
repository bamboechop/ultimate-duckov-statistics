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
    private NativeShellTemplates? templates;
    private StatisticsPanelTab selectedTab;
    private int lastScreenWidth = -1;
    private int lastScreenHeight = -1;
    private float lastCanvasScale = -1f;
    private int lastTabGeometrySignature;
    private int geometryPollCountdown;
    private RetainedShellLayout currentLayout = RetainedShellLayoutPolicy.Create(2560f, 1440f);

    public bool IsCreated => root != null;

    public RectTransform? ContentRoot => contentRoot;

    public string? TypographySummary => typography == null
        ? null
        : $"{typography.Describe()}; shell templates: {templates?.Describe() ?? "unavailable"}";

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
        NativeShellTemplates shellTemplates,
        out string? error)
    {
        if (targetCanvas == null) throw new ArgumentNullException(nameof(targetCanvas));
        if (close == null) throw new ArgumentNullException(nameof(close));
        if (selectTab == null) throw new ArgumentNullException(nameof(selectTab));
        if (shellTemplates == null) throw new ArgumentNullException(nameof(shellTemplates));
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

            templates = shellTemplates;
            typography = new NativeTypographyRoles(
                publicTypography,
                shellTemplates.NavigationTypography,
                shellTemplates.HeadingTypography);

            canvas = targetCanvas;
            root = CreateRect("UltimateDuckovStatisticsRetainedShell", targetCanvas.transform).gameObject;
            root.SetActive(false);
            root.AddComponent<CanvasGroup>().blocksRaycasts = true;
            var blocker = root.AddComponent<Image>();
            blocker.color = new Color(0.005f, 0.015f, 0.035f, 0.48f);
            blocker.raycastTarget = true;

            frame = CreateSurface(
                "Frame",
                root.transform,
                shellTemplates.Surface,
                new Color(0.018f, 0.082f, 0.13f, 0.88f),
                raycastTarget: true);

            header = CreateRect("Header", frame);
            title = CreateText(
                "Title",
                header,
                UiText.Get("ui.title"),
                NativeTypographyRole.Title,
                TextAlignmentOptions.MidlineLeft,
                new Color(0.98f, 0.96f, 0.86f, 1f));
            title.enableWordWrapping = false;

            backButton = CreateBackButton(header, close, shellTemplates.BackControl);

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
            CreateGraphicPresentation(
                navigationRail,
                shellTemplates.NavigationRail,
                new Color(0.08f, 0.78f, 0.9f, 0.96f),
                raycastTarget: false);

            foreach (var tab in PanelInteractionState.NavigationOrder)
            {
                var capturedTab = tab;
                var tabButton = CreateButton(
                    $"Tab{tab}",
                    tabContent,
                    TabLabel(tab),
                    () => selectTab(capturedTab),
                    shellTemplates.TabButton);
                var layoutElement = tabButton.GetComponent<LayoutElement>()
                                    ?? tabButton.gameObject.AddComponent<LayoutElement>();
                layoutElement.enabled = true;
                tabButtons.Add(tab, tabButton);
                tabLayouts.Add(tab, layoutElement);
                var tabLabel = tabButton.GetComponentInChildren<TextMeshProUGUI>(includeInactive: true);
                tabLabel.enableWordWrapping = false;
                tabLabels.Add(tab, tabLabel);
            }

            contentHost = CreateSurface(
                "ContentPlaceholder",
                frame,
                shellTemplates.Surface,
                new Color(0.006f, 0.026f, 0.045f, 0.72f),
                raycastTarget: false);

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
            InvalidateLayoutCache();
            RefreshLayout(force: true);
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
        if (--geometryPollCountdown <= 0)
        {
            geometryPollCountdown = 30;
            var labelsChanged = RefreshLocalizedLabels();
            RefreshLayout(force: labelsChanged || TabGeometrySignature() != lastTabGeometrySignature);
        }
        else
        {
            RefreshLayout(force: false);
        }
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

        var preferredWidths = PanelInteractionState.NavigationOrder
            .Select(tab => tabLabels.TryGetValue(tab, out var label) && label != null
                ? Math.Max(0f, label.GetPreferredValues(label.text).x * canvasScale)
                : 0f)
            .ToArray();
        var viewportWidthPixels = tabViewport.rect.width > 1f
            ? tabViewport.rect.width * canvasScale
            : currentLayout.TabViewportWidthPixels;
        var tabGeometry = RetainedTabGeometryPolicy.Create(
            viewportWidthPixels,
            currentLayout.TabWidthPixels,
            currentLayout.TabSpacingPixels,
            currentLayout.TabPaddingPixels,
            labelPaddingPixels: 38f,
            preferredWidths);
        for (var index = 0; index < PanelInteractionState.NavigationOrder.Count; index++)
        {
            var tab = PanelInteractionState.NavigationOrder[index];
            if (!tabLayouts.TryGetValue(tab, out var layout)) continue;
            layout.minWidth = tabGeometry.Widths[index] * unit;
            layout.preferredWidth = tabGeometry.Widths[index] * unit;
            layout.minHeight = currentLayout.TabHeightPixels * unit;
            layout.preferredHeight = currentLayout.TabHeightPixels * unit;
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
        lastTabGeometrySignature = TabGeometrySignature();
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

    private int TabGeometrySignature()
    {
        unchecked
        {
            var hash = 17;
            foreach (var tab in PanelInteractionState.NavigationOrder)
            {
                if (!tabLabels.TryGetValue(tab, out var label) || label == null) continue;
                hash = hash * 31 + StringComparer.Ordinal.GetHashCode(label.text ?? string.Empty);
                hash = hash * 31 + (label.font == null ? 0 : label.font.GetInstanceID());
                hash = hash * 31 + label.fontSize.GetHashCode();
                hash = hash * 31 + label.characterSpacing.GetHashCode();
                hash = hash * 31 + label.wordSpacing.GetHashCode();
            }
            return hash;
        }
    }

    private bool RefreshLocalizedLabels()
    {
        var changed = false;
        foreach (var tab in PanelInteractionState.NavigationOrder)
        {
            if (!tabLabels.TryGetValue(tab, out var label) || label == null) continue;
            var localized = TabLabel(tab);
            if (string.Equals(label.text, localized, StringComparison.Ordinal)) continue;
            label.text = localized;
            changed = true;
        }
        if (changed && placeholderTitle != null) placeholderTitle.text = TabLabel(selectedTab);
        return changed;
    }

    private void InvalidateLayoutCache()
    {
        lastScreenWidth = -1;
        lastScreenHeight = -1;
        lastCanvasScale = -1f;
        lastTabGeometrySignature = 0;
    }

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
        Action clicked,
        Button? nativeTemplate)
    {
        Button button;
        RectTransform rect;
        if (nativeTemplate != null)
        {
            button = UnityEngine.Object.Instantiate(nativeTemplate, parent, worldPositionStays: false);
            rect = button.GetComponent<RectTransform>();
            button.gameObject.SetActive(false);
            button.gameObject.name = name;
            button.gameObject.hideFlags = HideFlags.DontSave;
            StripNativeActionAndLayoutBehaviours(button.gameObject, button);
        }
        else
        {
            rect = CreateRect(name, parent);
            var image = rect.gameObject.AddComponent<Image>();
            image.raycastTarget = true;
            button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            ApplyNativeButtonPresentation(button, image);
        }
        button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
        ApplyButtonColors(button, selected: false);
        button.onClick = new Button.ButtonClickedEvent();
        button.onClick.AddListener(() => clicked());
        var text = button.GetComponentsInChildren<TextMeshProUGUI>(includeInactive: true).FirstOrDefault();
        if (text == null)
        {
            text = CreateText(
                "Label",
                rect,
                label,
                NativeTypographyRole.Navigation,
                TextAlignmentOptions.Center,
                Color.white);
        }
        else
        {
            text.gameObject.name = "Label";
            typography!.Resolve(NativeTypographyRole.Navigation).Apply(text);
            text.text = label;
            text.alignment = TextAlignmentOptions.Center;
            text.color = Color.white;
            text.enableAutoSizing = false;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            textSizing.Add(text, (NativeTypographyRole.Navigation, 1f));
        }
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Overflow;
        Stretch(text.rectTransform, 8f, 8f, 2f, 2f);
        button.gameObject.SetActive(true);
        return button;
    }

    private static Button CreateBackButton(Transform parent, Action clicked, Button? nativeTemplate)
    {
        if (nativeTemplate != null)
        {
            var nativeButton = UnityEngine.Object.Instantiate(nativeTemplate, parent, worldPositionStays: false);
            nativeButton.gameObject.SetActive(false);
            nativeButton.gameObject.name = "Back";
            nativeButton.gameObject.hideFlags = HideFlags.DontSave;
            StripNativeActionAndLayoutBehaviours(nativeButton.gameObject, nativeButton);
            nativeButton.navigation = new Navigation { mode = Navigation.Mode.Automatic };
            nativeButton.onClick = new Button.ButtonClickedEvent();
            nativeButton.onClick.AddListener(() => clicked());
            nativeButton.gameObject.SetActive(true);
            return nativeButton;
        }

        var rect = CreateRect("Back", parent);
        var image = rect.gameObject.AddComponent<Image>();
        image.raycastTarget = true;
        var button = rect.gameObject.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
        button.colors = new ColorBlock
        {
            normalColor = new Color(0.035f, 0.15f, 0.2f, 0.96f),
            highlightedColor = new Color(0.08f, 0.38f, 0.46f, 1f),
            pressedColor = new Color(0.03f, 0.24f, 0.3f, 1f),
            selectedColor = new Color(0.07f, 0.3f, 0.36f, 1f),
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
        var nativeArrow = back.GetComponentsInChildren<Image>(includeInactive: true)
            .FirstOrDefault(image => image != null
                                     && image.transform != back
                                     && image.sprite != null);
        if (nativeArrow != null)
        {
            var nativeRect = nativeArrow.rectTransform;
            nativeRect.anchorMin = new Vector2(0.5f, 0.5f);
            nativeRect.anchorMax = new Vector2(0.5f, 0.5f);
            nativeRect.pivot = new Vector2(0.5f, 0.5f);
            nativeRect.anchoredPosition = Vector2.zero;
            nativeRect.sizeDelta = Vector2.one * controlSize * 0.46f;
            nativeArrow.preserveAspect = true;
            nativeArrow.raycastTarget = false;
            return;
        }

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

    private static RectTransform CreateSurface(
        string name,
        Transform parent,
        Graphic? nativeTemplate,
        Color color,
        bool raycastTarget)
    {
        if (nativeTemplate != null)
        {
            GameObject? clone = null;
            try
            {
                clone = UnityEngine.Object.Instantiate(nativeTemplate.gameObject, parent, worldPositionStays: false);
                clone.SetActive(false);
                clone.name = name;
                clone.hideFlags = HideFlags.DontSave;
                for (var childIndex = 0; childIndex < clone.transform.childCount; childIndex++)
                    clone.transform.GetChild(childIndex).gameObject.SetActive(false);
                var graphic = clone.GetComponent(nativeTemplate.GetType()) as Graphic
                              ?? clone.GetComponent<Graphic>();
                if (graphic == null) throw new InvalidOperationException("Native surface clone lost its Graphic.");
                StripNativeActionAndLayoutBehaviours(clone, primaryButton: null);
                graphic.color = color;
                graphic.raycastTarget = raycastTarget;
                clone.SetActive(true);
                return clone.GetComponent<RectTransform>();
            }
            catch
            {
                if (clone != null) UnityEngine.Object.Destroy(clone);
            }
        }

        var rect = CreateRect(name, parent);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = raycastTarget;
        return rect;
    }

    private static Image CreateGraphicPresentation(
        RectTransform target,
        Graphic? nativeTemplate,
        Color color,
        bool raycastTarget)
    {
        var image = target.gameObject.AddComponent<Image>();
        if (nativeTemplate is Image nativeImage)
        {
            image.sprite = nativeImage.sprite;
            image.overrideSprite = nativeImage.overrideSprite;
            image.material = nativeImage.material;
            image.type = nativeImage.type;
            image.fillCenter = nativeImage.fillCenter;
            image.pixelsPerUnitMultiplier = nativeImage.pixelsPerUnitMultiplier;
        }
        image.color = color;
        image.raycastTarget = raycastTarget;
        return image;
    }

    private static int StripNativeActionAndLayoutBehaviours(GameObject clone, Button? primaryButton)
    {
        var removed = 0;
        foreach (var component in clone.GetComponentsInChildren<Component>(includeInactive: true))
        {
            if (component == null || ReferenceEquals(component, primaryButton)) continue;
            if (component is not MonoBehaviour behaviour || IsRetainedPresentationBehaviour(component)) continue;
            behaviour.enabled = false;
            UnityEngine.Object.Destroy(component);
            removed++;
        }
        return removed;
    }

    private static bool IsRetainedPresentationBehaviour(Component component)
    {
        return component is Graphic
               || component is LayoutElement
               || component is BaseMeshEffect
               || component is Mask
               || component is RectMask2D
               || NativeMenuPresentationPolicy.PreservesProceduralImageState(TypeHierarchy(component.GetType()));
    }

    private static IEnumerable<string?> TypeHierarchy(Type type)
    {
        for (var current = type; current != null; current = current.BaseType) yield return current.FullName;
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
        templates = null;
        tabButtons.Clear();
        tabLabels.Clear();
        tabLayouts.Clear();
        textSizing.Clear();
        lastScreenWidth = -1;
        lastScreenHeight = -1;
        lastCanvasScale = -1f;
        lastTabGeometrySignature = 0;
        geometryPollCountdown = 0;
    }

    public void Dispose() => DestroyRoot();
}
