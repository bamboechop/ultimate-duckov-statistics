using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UI.ProceduralImage;

namespace UltimateDuckovStatistics.UI;

/// <summary>
/// Owns the exact retained surfaces introduced through M17 visual correction Gate 07.
/// The root graphic remains the modal dimmer; its children are the frozen header/back/title/bar and nine tabs.
/// </summary>
internal sealed class RetainedStatisticsShell : IDisposable
{
    private sealed class RetainedTabControl
    {
        public RetainedTabControl(
            RetainedTabSpecification specification,
            RectTransform rect,
            ProceduralImage background,
            RetainedTabVisualState<ProceduralImage> visualState,
            OnlyOneEdgeModifier modifier,
            Button button,
            RectTransform labelRect,
            TextMeshProUGUI label)
        {
            Specification = specification;
            Rect = rect;
            Background = background;
            VisualState = visualState;
            Modifier = modifier;
            Button = button;
            LabelRect = labelRect;
            Label = label;
        }

        public RetainedTabSpecification Specification { get; }
        public RectTransform Rect { get; }
        public ProceduralImage Background { get; }
        public RetainedTabVisualState<ProceduralImage> VisualState { get; }
        public OnlyOneEdgeModifier Modifier { get; }
        public Button Button { get; }
        public RectTransform LabelRect { get; }
        public TextMeshProUGUI Label { get; }
    }

    private GameObject? root;
    private Canvas? canvas;
    private RectTransform? shellRoot;
    private RectTransform? headerRect;
    private UniformModifier? headerModifier;
    private readonly List<RetainedTabControl> tabControls = new();
    private RetainedListenerLease? tabListenerLease;
    private RetainedTabLabelMaterial? tabLabelMaterial;
    private RectTransform? headerBottomBarRect;
    private RectMask2D? headerBottomBarMask;
    private RectTransform? headerBottomBarSurfaceRect;
    private ProceduralImage? headerBottomBarGraphic;
    private UniformModifier? headerBottomBarModifier;
    private RectTransform? backButtonRect;
    private UniformModifier? backButtonModifier;
    private RectTransform? backArrowRect;
    private RetainedBackArrowAsset? backArrowAsset;
    private RectTransform? headerTitleRect;
    private TextMeshProUGUI? headerTitleGraphic;
    private RetainedVisualCanvasLayout? lastAppliedVisualLayout;
    private float lastViewportPixelWidth = float.NaN;
    private float lastViewportPixelHeight = float.NaN;
    private float lastCanvasScaleFactor = float.NaN;
    private StatisticsPanelTab selectedTab;

    public bool IsCreated => root != null;

    public StatisticsPanelTab SelectedTab => selectedTab;

    public bool IsUsable => root != null
                            && root.activeInHierarchy
                            && canvas != null
                            && canvas.enabled
                            && canvas.gameObject.activeInHierarchy;

    public bool TryCreate(
        Canvas targetCanvas,
        StatisticsPanelTab initialTab,
        Action<StatisticsPanelTab> selectTab,
        Action close,
        out string? error)
    {
        if (targetCanvas == null) throw new ArgumentNullException(nameof(targetCanvas));
        if (selectTab == null) throw new ArgumentNullException(nameof(selectTab));
        if (close == null) throw new ArgumentNullException(nameof(close));
        if (!PanelInteractionState.NavigationOrder.Contains(initialTab))
            throw new ArgumentOutOfRangeException(nameof(initialTab));

        error = null;
        if (root != null) return true;

        try
        {
            if (!NativeHeaderTitleTypographyResolver.TryResolve(
                    targetCanvas,
                    out var headerTitleTypography,
                    out var typographyError)
                || headerTitleTypography == null)
            {
                error = typographyError ?? "Duckov's exact native major-heading typography was unavailable.";
                return false;
            }

            canvas = targetCanvas;
            selectedTab = initialTab;
            tabListenerLease = new RetainedListenerLease();
            tabLabelMaterial = RetainedTabLabelMaterial.Create(headerTitleTypography.Material);
            backArrowAsset = RetainedBackArrowAsset.Create();
            root = new GameObject(
                RetainedDimmerPolicy.RootName,
                typeof(RectTransform));
            root.hideFlags = HideFlags.DontSave;
            root.SetActive(false);

            var rootRect = (RectTransform)root.transform;
            rootRect.SetParent(targetCanvas.transform, worldPositionStays: false);
            Stretch(rootRect);
            rootRect.ForceUpdateRectTransforms();
            shellRoot = rootRect;

            var blocker = root.AddComponent<Image>();
            blocker.color = new Color(
                RetainedDimmerPolicy.Red,
                RetainedDimmerPolicy.Green,
                RetainedDimmerPolicy.Blue,
                RetainedDimmerPolicy.VisualAlpha);
            blocker.raycastTarget = RetainedDimmerPolicy.BlocksRaycasts;

            headerRect = CreateHeaderBackground(
                rootRect,
                out var headerGraphic,
                out var createdHeaderModifier);
            headerModifier = createdHeaderModifier;
            foreach (var specification in RetainedTabStripPolicy.Specifications)
            {
                var tabRect = CreateTab(
                    rootRect,
                    specification,
                    headerTitleTypography,
                    tabLabelMaterial.Instance,
                    selectTab,
                    tabListenerLease,
                    out var tabBackground,
                    out var tabModifier,
                    out var tabButton,
                    out var tabLabelRect,
                    out var tabLabel);
                var visualState = new RetainedTabVisualState<ProceduralImage>(
                    tabBackground,
                    specification.Tab,
                    ApplyTabColor);
                visualState.Apply(selectedTab);
                tabControls.Add(new RetainedTabControl(
                    specification,
                    tabRect,
                    tabBackground,
                    visualState,
                    tabModifier,
                    tabButton,
                    tabLabelRect,
                    tabLabel));
            }
            headerBottomBarRect = CreateHeaderBottomBar(
                rootRect,
                out var createdHeaderBottomBarMask,
                out var createdHeaderBottomBarSurfaceRect,
                out var createdHeaderBottomBarGraphic,
                out var createdHeaderBottomBarModifier);
            headerBottomBarMask = createdHeaderBottomBarMask;
            headerBottomBarSurfaceRect = createdHeaderBottomBarSurfaceRect;
            headerBottomBarGraphic = createdHeaderBottomBarGraphic;
            headerBottomBarModifier = createdHeaderBottomBarModifier;
            backButtonRect = CreateBackControl(
                rootRect,
                backArrowAsset.Sprite,
                close,
                out var backButtonGraphic,
                out var createdBackButtonModifier,
                out var backButton,
                out var createdBackArrowRect,
                out var backArrowGraphic);
            backButtonModifier = createdBackButtonModifier;
            backArrowRect = createdBackArrowRect;
            headerTitleRect = CreateHeaderTitle(
                rootRect,
                headerTitleTypography,
                out var createdHeaderTitleGraphic);
            headerTitleGraphic = createdHeaderTitleGraphic;
            var visualLayout = RefreshVisualLayout(force: true);

            ValidateSurfaceComposition(
                rootRect,
                blocker,
                headerRect,
                headerGraphic,
                createdHeaderModifier,
                tabControls,
                tabLabelMaterial,
                headerBottomBarRect,
                createdHeaderBottomBarMask,
                createdHeaderBottomBarSurfaceRect,
                createdHeaderBottomBarGraphic,
                createdHeaderBottomBarModifier,
                backButtonRect,
                backButtonGraphic,
                createdBackButtonModifier,
                backButton,
                createdBackArrowRect,
                backArrowGraphic,
                headerTitleRect,
                createdHeaderTitleGraphic,
                visualLayout,
                selectedTab,
                targetCanvas.scaleFactor);
            rootRect.SetAsLastSibling();
            root.SetActive(true);
            return true;
        }
        catch (Exception exception)
        {
            error = $"{exception.GetType().Name}: {exception.Message}";
            DestroyRoot();
            return false;
        }
    }

    public void SetSelectedTab(StatisticsPanelTab tab)
    {
        if (!PanelInteractionState.NavigationOrder.Contains(tab))
            throw new ArgumentOutOfRangeException(nameof(tab));
        if (selectedTab == tab) return;
        selectedTab = tab;
        foreach (var control in tabControls) control.VisualState.Apply(selectedTab);
    }

    public bool Tick(out string? error)
    {
        error = null;
        try
        {
            RefreshVisualLayout(force: false);
            return true;
        }
        catch (Exception exception)
        {
            error = $"{exception.GetType().Name}: {exception.Message}";
            return false;
        }
    }

    private static RectTransform CreateHeaderBackground(
        RectTransform parent,
        out ProceduralImage image,
        out UniformModifier modifier)
    {
        var header = new GameObject(
            RetainedHeaderPolicy.Name,
            typeof(RectTransform));
        var rect = (RectTransform)header.transform;
        rect.SetParent(parent, worldPositionStays: false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.localScale = Vector3.one;

        image = header.AddComponent<ProceduralImage>();
        image.color = new Color(
            RetainedHeaderPolicy.Red,
            RetainedHeaderPolicy.Green,
            RetainedHeaderPolicy.Blue,
            RetainedHeaderPolicy.VisualAlpha);
        image.BorderWidth = 0f;
        image.FalloffDistance = 1f;
        image.raycastTarget = RetainedHeaderPolicy.BlocksRaycasts;

        modifier = header.AddComponent<UniformModifier>();
        return rect;
    }

    private static RectTransform CreateTab(
        RectTransform parent,
        RetainedTabSpecification specification,
        NativeHeaderTitleTypography typography,
        Material labelMaterial,
        Action<StatisticsPanelTab> selectTab,
        RetainedListenerLease listenerLease,
        out ProceduralImage background,
        out OnlyOneEdgeModifier modifier,
        out Button button,
        out RectTransform labelRect,
        out TextMeshProUGUI label)
    {
        if (specification == null) throw new ArgumentNullException(nameof(specification));
        var tab = new GameObject(
            specification.BackgroundName,
            typeof(RectTransform));
        var rect = (RectTransform)tab.transform;
        rect.SetParent(parent, worldPositionStays: false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.localScale = Vector3.one;

        background = tab.AddComponent<ProceduralImage>();
        background.color = Color.clear;
        background.BorderWidth = 0f;
        background.FalloffDistance = 1f;
        background.sprite = null;
        background.overrideSprite = null;
        background.type = Image.Type.Simple;
        background.raycastTarget = RetainedOverviewTabPolicy.BackgroundBlocksRaycasts;

        modifier = tab.AddComponent<OnlyOneEdgeModifier>();
        modifier.Side = OnlyOneEdgeModifier.ProceduralImageEdge.Top;

        button = tab.AddComponent<Button>();
        button.targetGraphic = background;
        button.transition = Selectable.Transition.None;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick = new Button.ButtonClickedEvent();
        var activation = new RetainedTabActivation(selectTab, specification.Tab);
        button.onClick.AddListener(activation.Invoke);
        listenerLease.Register(button.onClick.RemoveAllListeners);

        var labelObject = new GameObject(
            specification.LabelName,
            typeof(RectTransform));
        labelRect = (RectTransform)labelObject.transform;
        labelRect.SetParent(rect, worldPositionStays: false);
        labelRect.anchorMin = new Vector2(0f, 1f);
        labelRect.anchorMax = new Vector2(0f, 1f);
        labelRect.pivot = new Vector2(0f, 1f);
        labelRect.localScale = Vector3.one;

        label = labelObject.AddComponent<TextMeshProUGUI>();
        label.font = typography.Font;
        label.fontSharedMaterial = labelMaterial;
        label.text = UiText.Get(specification.TextKey);
        label.fontStyle = FontStyles.Normal;
        label.fontWeight = FontWeight.Regular;
        label.characterSpacing = RetainedOverviewTabPolicy.CharacterSpacing;
        label.wordSpacing = RetainedOverviewTabPolicy.WordSpacing;
        label.lineSpacing = RetainedOverviewTabPolicy.LineSpacing;
        label.paragraphSpacing = RetainedOverviewTabPolicy.ParagraphSpacing;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = RetainedOverviewTabPolicy.WordWrapping;
        label.enableAutoSizing = RetainedOverviewTabPolicy.AutoSizing;
        label.overflowMode = TextOverflowModes.Overflow;
        label.margin = Vector4.zero;
        label.color = new Color(
            RetainedOverviewTabPolicy.LabelRed,
            RetainedOverviewTabPolicy.LabelGreen,
            RetainedOverviewTabPolicy.LabelBlue,
            RetainedOverviewTabPolicy.LabelAlpha);
        label.raycastTarget = RetainedOverviewTabPolicy.LabelBlocksRaycasts;
        return rect;
    }

    private static void ApplyTabColor(
        ProceduralImage target,
        RetainedRgbaColor color)
    {
        target.color = new Color(color.Red, color.Green, color.Blue, color.Alpha);
    }

    private static RectTransform CreateHeaderBottomBar(
        RectTransform parent,
        out RectMask2D mask,
        out RectTransform surfaceRect,
        out ProceduralImage image,
        out UniformModifier modifier)
    {
        var bar = new GameObject(
            RetainedHeaderBottomBarPolicy.Name,
            typeof(RectTransform));
        var rect = (RectTransform)bar.transform;
        rect.SetParent(parent, worldPositionStays: false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.localScale = Vector3.one;

        mask = bar.AddComponent<RectMask2D>();
        mask.padding = Vector4.zero;
        mask.softness = Vector2Int.zero;

        var roundedSurface = new GameObject(
            RetainedHeaderBottomBarPolicy.GraphicName,
            typeof(RectTransform));
        surfaceRect = (RectTransform)roundedSurface.transform;
        surfaceRect.SetParent(rect, worldPositionStays: false);
        surfaceRect.anchorMin = new Vector2(0f, 1f);
        surfaceRect.anchorMax = new Vector2(0f, 1f);
        surfaceRect.pivot = new Vector2(0f, 1f);
        surfaceRect.localScale = Vector3.one;

        image = roundedSurface.AddComponent<ProceduralImage>();
        image.color = new Color(
            RetainedHeaderBottomBarPolicy.Red,
            RetainedHeaderBottomBarPolicy.Green,
            RetainedHeaderBottomBarPolicy.Blue,
            RetainedHeaderBottomBarPolicy.Alpha);
        image.BorderWidth = 0f;
        image.FalloffDistance = 1f;
        image.type = Image.Type.Simple;
        image.maskable = true;
        image.raycastTarget = RetainedHeaderBottomBarPolicy.BlocksRaycasts;

        modifier = roundedSurface.AddComponent<UniformModifier>();
        return rect;
    }

    private static RectTransform CreateBackControl(
        RectTransform parent,
        Sprite backArrowSprite,
        Action close,
        out ProceduralImage background,
        out UniformModifier modifier,
        out Button button,
        out RectTransform arrowRect,
        out Image arrowGraphic)
    {
        var back = new GameObject(
            RetainedBackControlPolicy.ButtonName,
            typeof(RectTransform));
        var rect = (RectTransform)back.transform;
        rect.SetParent(parent, worldPositionStays: false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.localScale = Vector3.one;

        background = back.AddComponent<ProceduralImage>();
        background.color = new Color(
            RetainedBackControlPolicy.BackgroundRed,
            RetainedBackControlPolicy.BackgroundGreen,
            RetainedBackControlPolicy.BackgroundBlue,
            RetainedBackControlPolicy.BackgroundAlpha);
        background.BorderWidth = 0f;
        background.FalloffDistance = 1f;
        background.sprite = null;
        background.overrideSprite = null;
        background.raycastTarget = RetainedBackControlPolicy.BackgroundBlocksRaycasts;
        modifier = back.AddComponent<UniformModifier>();

        button = back.AddComponent<Button>();
        button.targetGraphic = background;
        button.transition = Selectable.Transition.None;
        button.navigation = new Navigation { mode = Navigation.Mode.Automatic };
        button.onClick = new Button.ButtonClickedEvent();
        var activation = new RetainedBackControlActivation(close);
        button.onClick.AddListener(activation.Invoke);

        var arrow = new GameObject(
            RetainedBackControlPolicy.ArrowName,
            typeof(RectTransform));
        arrowRect = (RectTransform)arrow.transform;
        arrowRect.SetParent(rect, worldPositionStays: false);
        arrowRect.anchorMin = new Vector2(0f, 1f);
        arrowRect.anchorMax = new Vector2(0f, 1f);
        arrowRect.pivot = new Vector2(0f, 1f);
        arrowRect.localScale = Vector3.one;
        arrowGraphic = arrow.AddComponent<Image>();
        arrowGraphic.sprite = backArrowSprite;
        arrowGraphic.overrideSprite = backArrowSprite;
        arrowGraphic.type = Image.Type.Simple;
        arrowGraphic.preserveAspect = RetainedBackControlPolicy.PreserveArrowAspect;
        arrowGraphic.color = new Color(
            RetainedBackControlPolicy.ArrowRed,
            RetainedBackControlPolicy.ArrowGreen,
            RetainedBackControlPolicy.ArrowBlue,
            RetainedBackControlPolicy.ArrowAlpha);
        arrowGraphic.raycastTarget = RetainedBackControlPolicy.ArrowBlocksRaycasts;
        return rect;
    }

    private static RectTransform CreateHeaderTitle(
        RectTransform parent,
        NativeHeaderTitleTypography typography,
        out TextMeshProUGUI text)
    {
        var title = new GameObject(
            RetainedHeaderTitlePolicy.Name,
            typeof(RectTransform));
        var rect = (RectTransform)title.transform;
        rect.SetParent(parent, worldPositionStays: false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.localScale = Vector3.one;

        text = title.AddComponent<TextMeshProUGUI>();
        text.font = typography.Font;
        text.fontSharedMaterial = typography.Material;
        text.text = RetainedHeaderTitlePolicy.Text;
        text.fontStyle = FontStyles.Normal;
        text.fontWeight = FontWeight.Regular;
        text.characterSpacing = 0f;
        text.wordSpacing = 0f;
        text.lineSpacing = 0f;
        text.paragraphSpacing = 0f;
        text.alignment = TextAlignmentOptions.Left;
        text.enableWordWrapping = RetainedHeaderTitlePolicy.WordWrapping;
        text.enableAutoSizing = RetainedHeaderTitlePolicy.AutoSizing;
        text.overflowMode = TextOverflowModes.Overflow;
        text.color = new Color(
            RetainedHeaderTitlePolicy.Red,
            RetainedHeaderTitlePolicy.Green,
            RetainedHeaderTitlePolicy.Blue,
            RetainedHeaderTitlePolicy.Alpha);
        text.raycastTarget = RetainedHeaderTitlePolicy.BlocksRaycasts;
        return rect;
    }

    private RetainedVisualCanvasLayout RefreshVisualLayout(bool force)
    {
        if (canvas == null || shellRoot == null || headerRect == null || headerModifier == null
            || tabControls.Count != RetainedTabStripPolicy.Specifications.Count
            || headerBottomBarRect == null || headerBottomBarMask == null
            || headerBottomBarSurfaceRect == null
            || headerBottomBarGraphic == null || headerBottomBarModifier == null
            || backButtonRect == null || backButtonModifier == null || backArrowRect == null
            || headerTitleRect == null || headerTitleGraphic == null)
        {
            throw new InvalidOperationException("The retained visual layout is not fully initialized.");
        }

        var canvasScaleFactor = canvas.scaleFactor;
        var viewportPixelWidth = shellRoot.rect.width * canvasScaleFactor;
        var viewportPixelHeight = shellRoot.rect.height * canvasScaleFactor;
        if (!force
            && viewportPixelWidth == lastViewportPixelWidth
            && viewportPixelHeight == lastViewportPixelHeight
            && canvasScaleFactor == lastCanvasScaleFactor
            && lastAppliedVisualLayout != null)
        {
            return lastAppliedVisualLayout;
        }

        var referenceTransform = RetainedReferenceTransformPolicy.Create(
            viewportPixelWidth,
            viewportPixelHeight,
            canvasScaleFactor);
        var preferredReferenceWidths = MeasureTabReferenceWidths(
            referenceTransform,
            canvasScaleFactor);
        var layout = RetainedVisualLayoutPolicy.Create(referenceTransform, preferredReferenceWidths);
        headerRect.anchoredPosition = new Vector2(layout.Header.Left, -layout.Header.Top);
        headerRect.sizeDelta = new Vector2(layout.Header.Width, layout.Header.Height);
        headerModifier.Radius = layout.Header.CornerRadius;
        for (var index = 0; index < tabControls.Count; index++)
        {
            var control = tabControls[index];
            var tab = layout.TabStrip.Tabs[index];
            control.Rect.anchoredPosition = new Vector2(tab.Left, -tab.Top);
            control.Rect.sizeDelta = new Vector2(tab.Width, tab.Height);
            control.Modifier.Radius = tab.CornerRadius;
            control.LabelRect.anchoredPosition = new Vector2(
                tab.LabelLeft - tab.Left,
                -(tab.LabelTop - tab.Top));
            control.LabelRect.sizeDelta = new Vector2(tab.LabelWidth, tab.LabelHeight);
            control.Label.fontSize = tab.FontSize;
        }
        headerBottomBarRect.anchoredPosition = new Vector2(
            layout.HeaderBottomBar.Left,
            -layout.HeaderBottomBar.Top);
        headerBottomBarRect.sizeDelta = new Vector2(
            layout.HeaderBottomBar.Width,
            layout.HeaderBottomBar.Height);
        headerBottomBarSurfaceRect.anchoredPosition = new Vector2(
            layout.HeaderBottomBar.SurfaceLeft - layout.HeaderBottomBar.Left,
            -(layout.HeaderBottomBar.SurfaceTop - layout.HeaderBottomBar.Top));
        headerBottomBarSurfaceRect.sizeDelta = new Vector2(
            layout.HeaderBottomBar.SurfaceWidth,
            layout.HeaderBottomBar.SurfaceHeight);
        headerBottomBarModifier.Radius = layout.HeaderBottomBar.SurfaceCornerRadius;
        backButtonRect.anchoredPosition = new Vector2(layout.BackControl.Left, -layout.BackControl.Top);
        backButtonRect.sizeDelta = new Vector2(layout.BackControl.Width, layout.BackControl.Height);
        backButtonModifier.Radius = layout.BackControl.CornerRadius;
        backArrowRect.anchoredPosition = new Vector2(
            layout.BackControl.ArrowLeft - layout.BackControl.Left,
            -(layout.BackControl.ArrowTop - layout.BackControl.Top));
        backArrowRect.sizeDelta = new Vector2(
            layout.BackControl.ArrowWidth,
            layout.BackControl.ArrowHeight);
        headerTitleRect.anchoredPosition = new Vector2(
            layout.HeaderTitle.Left,
            -layout.HeaderTitle.Top);
        headerTitleRect.sizeDelta = new Vector2(
            layout.HeaderTitle.Width,
            layout.HeaderTitle.Height);
        headerTitleGraphic.fontSize = layout.HeaderTitle.FontSize;
        lastViewportPixelWidth = viewportPixelWidth;
        lastViewportPixelHeight = viewportPixelHeight;
        lastCanvasScaleFactor = canvasScaleFactor;
        lastAppliedVisualLayout = layout;
        return layout;
    }

    private float[] MeasureTabReferenceWidths(
        RetainedReferenceTransform referenceTransform,
        float canvasScaleFactor)
    {
        var temporaryLabelWidth = referenceTransform.CanvasLength(
            RetainedTabMeasurementPolicy.TemporaryLabelWidthPixels);
        var labelHeight = referenceTransform.CanvasLength(
            RetainedOverviewTabPolicy.NominalLabelHeightPixels);
        var tabHeight = referenceTransform.CanvasLength(RetainedOverviewTabPolicy.HeightPixels);
        var fontSize = referenceTransform.CanvasLength(RetainedOverviewTabPolicy.ReferenceFontSize);
        foreach (var control in tabControls)
        {
            control.Rect.sizeDelta = new Vector2(temporaryLabelWidth, tabHeight);
            control.LabelRect.sizeDelta = new Vector2(temporaryLabelWidth, labelHeight);
            control.Label.fontSize = fontSize;
        }

        Canvas.ForceUpdateCanvases();
        var normalizedWidths = new float[tabControls.Count];
        for (var index = 0; index < tabControls.Count; index++)
        {
            var control = tabControls[index];
            control.Label.ForceMeshUpdate(ignoreActiveState: true, forceTextReparsing: true);
            var measuredCanvasWidth = control.Label.GetPreferredValues(
                temporaryLabelWidth,
                labelHeight).x;
            var normalizedWidth = RetainedTabMeasurementPolicy.NormalizeCanvasWidth(
                measuredCanvasWidth,
                canvasScaleFactor,
                referenceTransform.ReferenceScale);
            if (string.Equals(
                    control.Label.text,
                    control.Specification.EnglishFallback,
                    StringComparison.Ordinal))
            {
                RetainedTabMeasurementPolicy.RequirePlausibleEnglishWidth(
                    control.Specification.EnglishFallback,
                    normalizedWidth,
                    control.Specification.AuditedEnglishPreferredWidthPixels);
            }

            normalizedWidths[index] = normalizedWidth;
        }

        return normalizedWidths;
    }

    private static void ValidateSurfaceComposition(
        RectTransform shellRoot,
        Image blocker,
        RectTransform headerRect,
        ProceduralImage headerGraphic,
        UniformModifier headerModifier,
        IReadOnlyList<RetainedTabControl> tabControls,
        RetainedTabLabelMaterial tabLabelMaterial,
        RectTransform headerBottomBarRect,
        RectMask2D headerBottomBarMask,
        RectTransform headerBottomBarSurfaceRect,
        ProceduralImage headerBottomBarGraphic,
        UniformModifier headerBottomBarModifier,
        RectTransform backButtonRect,
        ProceduralImage backButtonGraphic,
        UniformModifier backButtonModifier,
        Button backButton,
        RectTransform backArrowRect,
        Image backArrowGraphic,
        RectTransform headerTitleRect,
        TextMeshProUGUI headerTitleGraphic,
        RetainedVisualCanvasLayout visualLayout,
        StatisticsPanelTab selectedTab,
        float canvasScaleFactor)
    {
        if (tabControls.Count != RetainedTabStripPolicy.Specifications.Count)
            throw new InvalidOperationException("The retained tab strip is incomplete.");
        var overviewControl = tabControls[0];
        var overviewTabRect = overviewControl.Rect;
        var overviewTabGraphic = overviewControl.Background;
        var overviewTabModifier = overviewControl.Modifier;
        var overviewTabButton = overviewControl.Button;
        var overviewTabVisualState = overviewControl.VisualState;
        var overviewTabLabelRect = overviewControl.LabelRect;
        var overviewTabLabelGraphic = overviewControl.Label;
        var graphics = shellRoot.GetComponentsInChildren<Graphic>(includeInactive: true);
        var buttons = shellRoot.GetComponentsInChildren<Button>(includeInactive: true);
        var rectangularMasks = shellRoot.GetComponentsInChildren<RectMask2D>(includeInactive: true);
        var oneEdgeModifiers = shellRoot.GetComponentsInChildren<OnlyOneEdgeModifier>(includeInactive: true);
        ValidateTabStrip(
            shellRoot,
            tabControls,
            tabLabelMaterial,
            headerTitleGraphic,
            headerBottomBarRect,
            visualLayout,
            selectedTab,
            canvasScaleFactor);
        if (!RetainedDimmerPolicy.IsValidGraphic(
                blocker.color.r,
                blocker.color.g,
                blocker.color.b,
                blocker.color.a,
                blocker.raycastTarget)
            || !RetainedHeaderPolicy.IsValidGraphic(
                headerGraphic.color.r,
                headerGraphic.color.g,
                headerGraphic.color.b,
                headerGraphic.color.a,
                headerGraphic.raycastTarget)
            || !RetainedTabVisualStatePolicy.IsExactColor(
                RetainedTabVisualStatePolicy.Resolve(
                    selectedTab,
                    StatisticsPanelTab.Overview),
                overviewTabGraphic.color.r,
                overviewTabGraphic.color.g,
                overviewTabGraphic.color.b,
                overviewTabGraphic.color.a)
            || overviewTabGraphic.raycastTarget
                != RetainedOverviewTabPolicy.BackgroundBlocksRaycasts
            || !RetainedHeaderBottomBarPolicy.IsValidGraphic(
                headerBottomBarGraphic.color.r,
                headerBottomBarGraphic.color.g,
                headerBottomBarGraphic.color.b,
                headerBottomBarGraphic.color.a,
                headerBottomBarGraphic.raycastTarget)
            || !RetainedBackControlPolicy.IsValidBackgroundGraphic(
                backButtonGraphic.color.r,
                backButtonGraphic.color.g,
                backButtonGraphic.color.b,
                backButtonGraphic.color.a,
                backButtonGraphic.raycastTarget)
            || !RetainedBackControlPolicy.IsValidArrowGraphic(
                backArrowGraphic.color.r,
                backArrowGraphic.color.g,
                backArrowGraphic.color.b,
                backArrowGraphic.color.a,
                backArrowGraphic.raycastTarget,
                backArrowGraphic.preserveAspect)
            || shellRoot.childCount != RetainedShellCompositionPolicy.RootChildCount
            || headerRect.parent != shellRoot
            || headerRect.childCount != RetainedShellCompositionPolicy.HeaderChildCount
            || overviewTabRect.parent != shellRoot
            || overviewTabRect.childCount != RetainedShellCompositionPolicy.OverviewTabChildCount
            || overviewTabLabelRect.parent != overviewTabRect
            || overviewTabLabelRect.childCount != RetainedShellCompositionPolicy.OverviewTabLabelChildCount
            || headerBottomBarRect.parent != shellRoot
            || headerBottomBarRect.childCount != RetainedShellCompositionPolicy.HeaderBottomBarChildCount
            || headerBottomBarSurfaceRect.parent != headerBottomBarRect
            || headerBottomBarSurfaceRect.childCount
            != RetainedShellCompositionPolicy.HeaderBottomBarGraphicChildCount
            || backButtonRect.parent != shellRoot
            || backButtonRect.childCount != RetainedShellCompositionPolicy.BackButtonChildCount
            || backArrowRect.parent != backButtonRect
            || backArrowRect.childCount != RetainedShellCompositionPolicy.BackArrowChildCount
            || headerTitleRect.parent != shellRoot
            || headerTitleRect.childCount != RetainedShellCompositionPolicy.HeaderTitleChildCount
            || graphics.Length != RetainedShellCompositionPolicy.GraphicCount
            || rectangularMasks.Length != RetainedShellCompositionPolicy.RectMaskCount
            || rectangularMasks[0] != headerBottomBarMask
            || oneEdgeModifiers.Length != RetainedShellCompositionPolicy.OnlyOneEdgeModifierCount
            || tabControls.Any(control => !oneEdgeModifiers.Contains(control.Modifier))
            || buttons.Length != RetainedShellCompositionPolicy.ButtonCount
            || !buttons.Contains(backButton)
            || tabControls.Any(control => !buttons.Contains(control.Button))
            || backButton.targetGraphic != backButtonGraphic
            || backButton.transition != Selectable.Transition.None
            || backButton.onClick.GetPersistentEventCount() != 0
            || overviewTabButton.targetGraphic != overviewTabGraphic
            || overviewTabButton.transition != Selectable.Transition.None
            || overviewTabButton.navigation.mode != Navigation.Mode.None
            || overviewTabButton.onClick.GetPersistentEventCount() != 0
            || !ReferenceEquals(overviewTabVisualState.Target, overviewTabGraphic)
            || !ReferenceEquals(visualLayout.ReferenceTransform, visualLayout.Header.ReferenceTransform)
            || !ReferenceEquals(visualLayout.ReferenceTransform, visualLayout.TabStrip.ReferenceTransform)
            || !ReferenceEquals(visualLayout.ReferenceTransform, visualLayout.OverviewTab.ReferenceTransform)
            || !ReferenceEquals(
                visualLayout.ReferenceTransform,
                visualLayout.HeaderBottomBar.ReferenceTransform)
            || !ReferenceEquals(visualLayout.ReferenceTransform, visualLayout.HeaderTitle.ReferenceTransform)
            || !ReferenceEquals(visualLayout.ReferenceTransform, visualLayout.BackControl.ReferenceTransform)
            || !graphics.Contains(blocker)
            || !graphics.Contains(headerGraphic)
            || tabControls.Any(control =>
                !graphics.Contains(control.Background) || !graphics.Contains(control.Label))
            || !graphics.Contains(headerBottomBarGraphic)
            || !graphics.Contains(backButtonGraphic)
            || !graphics.Contains(backArrowGraphic)
            || !graphics.Contains(headerTitleGraphic))
        {
            throw new InvalidOperationException(
                "The Gate 07 shell must contain only the frozen visuals and the complete nine-tab strip.");
        }

        if (shellRoot.anchorMin != Vector2.zero
            || shellRoot.anchorMax != Vector2.one
            || shellRoot.offsetMin != Vector2.zero
            || shellRoot.offsetMax != Vector2.zero)
        {
            throw new InvalidOperationException("The Step 0 dimmer must stretch edge to edge with zero offsets.");
        }

        if (headerRect.gameObject.name != RetainedHeaderPolicy.Name
            || headerRect.anchorMin != new Vector2(0f, 1f)
            || headerRect.anchorMax != new Vector2(0f, 1f)
            || headerRect.pivot != new Vector2(0f, 1f)
            || headerGraphic.BorderWidth != 0f
            || !Approximately(headerRect.anchoredPosition.x, visualLayout.Header.Left)
            || !Approximately(headerRect.anchoredPosition.y, -visualLayout.Header.Top)
            || !Approximately(headerRect.sizeDelta.x, visualLayout.Header.Width)
            || !Approximately(headerRect.sizeDelta.y, visualLayout.Header.Height)
            || !Approximately(headerModifier.Radius, visualLayout.Header.CornerRadius)
            || !Approximately(
                headerModifier.Radius * canvasScaleFactor,
                RetainedHeaderPolicy.CornerRadiusPixels * visualLayout.ReferenceTransform.ReferenceScale))
        {
            throw new InvalidOperationException(
                "HeaderBackground did not retain its exact top-left pixel geometry or rounded-corner radius.");
        }

        if (overviewTabRect.gameObject.name != RetainedOverviewTabPolicy.BackgroundName
            || overviewTabRect.anchorMin != new Vector2(0f, 1f)
            || overviewTabRect.anchorMax != new Vector2(0f, 1f)
            || overviewTabRect.pivot != new Vector2(0f, 1f)
            || overviewTabRect.GetSiblingIndex() <= headerRect.GetSiblingIndex()
            || headerBottomBarRect.GetSiblingIndex() <= overviewTabRect.GetSiblingIndex()
            || !Approximately(overviewTabRect.anchoredPosition.x, visualLayout.OverviewTab.Left)
            || !Approximately(overviewTabRect.anchoredPosition.y, -visualLayout.OverviewTab.Top)
            || !Approximately(overviewTabRect.sizeDelta.x, visualLayout.OverviewTab.Width)
            || !Approximately(overviewTabRect.sizeDelta.y, visualLayout.OverviewTab.Height)
            || !Approximately(
                visualLayout.OverviewTab.Top + visualLayout.OverviewTab.Height,
                visualLayout.Header.Top + visualLayout.Header.Height)
            || !Approximately(
                visualLayout.OverviewTab.ExposedHeight,
                visualLayout.HeaderBottomBar.Top - visualLayout.OverviewTab.Top)
            || overviewTabGraphic.BorderWidth != 0f
            || overviewTabGraphic.FalloffDistance != 1f
            || overviewTabGraphic.sprite != null
            || overviewTabGraphic.overrideSprite != null
            || overviewTabGraphic.type != Image.Type.Simple
            || overviewTabModifier.Side != OnlyOneEdgeModifier.ProceduralImageEdge.Top
            || !Approximately(overviewTabModifier.Radius, visualLayout.OverviewTab.CornerRadius)
            || !Approximately(
                overviewTabModifier.Radius * canvasScaleFactor,
                RetainedOverviewTabPolicy.CornerRadiusPixels
                * visualLayout.ReferenceTransform.ReferenceScale)
            || overviewTabLabelRect.gameObject.name != RetainedOverviewTabPolicy.LabelName
            || overviewTabLabelRect.anchorMin != new Vector2(0f, 1f)
            || overviewTabLabelRect.anchorMax != new Vector2(0f, 1f)
            || overviewTabLabelRect.pivot != new Vector2(0f, 1f)
            || overviewTabLabelRect.localScale != Vector3.one
            || !Approximately(
                overviewTabLabelRect.anchoredPosition.x,
                visualLayout.OverviewTab.LeftPadding)
            || !Approximately(
                overviewTabLabelRect.anchoredPosition.y,
                -visualLayout.OverviewTab.TopPadding)
            || !Approximately(overviewTabLabelRect.sizeDelta.x, visualLayout.OverviewTab.LabelWidth)
            || !Approximately(overviewTabLabelRect.sizeDelta.y, visualLayout.OverviewTab.LabelHeight)
            || visualLayout.OverviewTab.Width <= visualLayout.OverviewTab.Height
            || !Approximately(
                visualLayout.OverviewTab.Width,
                visualLayout.OverviewTab.PreferredLabelWidth
                + visualLayout.OverviewTab.LeftPadding
                + visualLayout.OverviewTab.RightPadding)
            || !Approximately(
                visualLayout.OverviewTab.LabelHeight,
                visualLayout.OverviewTab.Height
                - visualLayout.OverviewTab.TopPadding
                - visualLayout.OverviewTab.BottomPadding)
            || !Approximately(overviewTabLabelGraphic.fontSize, visualLayout.OverviewTab.FontSize)
            || overviewTabLabelGraphic.text != UiText.Get(RetainedOverviewTabPolicy.TextKey)
            || overviewTabLabelGraphic.font == null
            || overviewTabLabelGraphic.font.name != RetainedOverviewTabPolicy.FontAssetName
            || !ReferenceEquals(overviewTabLabelGraphic.font, headerTitleGraphic.font)
            || overviewTabLabelGraphic.fontSharedMaterial == null
            || overviewTabLabelGraphic.fontSharedMaterial.name
                != RetainedTabLabelShadowPolicy.OwnedMaterialName
            || !ReferenceEquals(
                overviewTabLabelGraphic.fontSharedMaterial,
                tabLabelMaterial.Instance)
            || overviewTabLabelGraphic.fontSharedMaterial.shaderKeywords == null
            || !overviewTabLabelGraphic.fontSharedMaterial.shaderKeywords.Contains(
                RetainedOverviewTabPolicy.NativeUnderlayKeyword)
            || ReferenceEquals(
                overviewTabLabelGraphic.fontSharedMaterial,
                headerTitleGraphic.fontSharedMaterial)
            || !ReferenceEquals(tabLabelMaterial.Source, headerTitleGraphic.fontSharedMaterial)
            || !tabLabelMaterial.IsConfigured()
            || !tabLabelMaterial.IsSourceUnchanged()
            || overviewTabLabelGraphic.fontStyle != FontStyles.Normal
            || overviewTabLabelGraphic.fontWeight != FontWeight.Regular
            || !Approximately(
                overviewTabLabelGraphic.characterSpacing,
                RetainedOverviewTabPolicy.CharacterSpacing)
            || !Approximately(overviewTabLabelGraphic.wordSpacing, RetainedOverviewTabPolicy.WordSpacing)
            || !Approximately(overviewTabLabelGraphic.lineSpacing, RetainedOverviewTabPolicy.LineSpacing)
            || !Approximately(
                overviewTabLabelGraphic.paragraphSpacing,
                RetainedOverviewTabPolicy.ParagraphSpacing)
            || overviewTabLabelGraphic.alignment != TextAlignmentOptions.Center
            || overviewTabLabelGraphic.enableWordWrapping != RetainedOverviewTabPolicy.WordWrapping
            || overviewTabLabelGraphic.enableAutoSizing != RetainedOverviewTabPolicy.AutoSizing
            || overviewTabLabelGraphic.overflowMode != TextOverflowModes.Overflow
            || overviewTabLabelGraphic.margin != Vector4.zero
            || !Approximately(overviewTabLabelGraphic.color.r, RetainedOverviewTabPolicy.LabelRed)
            || !Approximately(overviewTabLabelGraphic.color.g, RetainedOverviewTabPolicy.LabelGreen)
            || !Approximately(overviewTabLabelGraphic.color.b, RetainedOverviewTabPolicy.LabelBlue)
            || !Approximately(overviewTabLabelGraphic.color.a, RetainedOverviewTabPolicy.LabelAlpha)
            || overviewTabLabelGraphic.raycastTarget != RetainedOverviewTabPolicy.LabelBlocksRaycasts)
        {
            throw new InvalidOperationException(
                "OverviewTab did not retain its exact unselected native typography, geometry, or Button state.");
        }

        if (headerBottomBarRect.gameObject.name != RetainedHeaderBottomBarPolicy.Name
            || headerBottomBarRect.anchorMin != new Vector2(0f, 1f)
            || headerBottomBarRect.anchorMax != new Vector2(0f, 1f)
            || headerBottomBarRect.pivot != new Vector2(0f, 1f)
            || headerBottomBarRect.GetSiblingIndex() <= headerRect.GetSiblingIndex()
            || !Approximately(headerBottomBarRect.anchoredPosition.x, visualLayout.HeaderBottomBar.Left)
            || !Approximately(headerBottomBarRect.anchoredPosition.y, -visualLayout.HeaderBottomBar.Top)
            || !Approximately(headerBottomBarRect.sizeDelta.x, visualLayout.HeaderBottomBar.Width)
            || !Approximately(headerBottomBarRect.sizeDelta.y, visualLayout.HeaderBottomBar.Height)
            || headerBottomBarMask.padding != Vector4.zero
            || headerBottomBarMask.softness != Vector2Int.zero
            || headerBottomBarSurfaceRect.gameObject.name != RetainedHeaderBottomBarPolicy.GraphicName
            || headerBottomBarSurfaceRect.anchorMin != new Vector2(0f, 1f)
            || headerBottomBarSurfaceRect.anchorMax != new Vector2(0f, 1f)
            || headerBottomBarSurfaceRect.pivot != new Vector2(0f, 1f)
            || !Approximately(
                headerBottomBarSurfaceRect.anchoredPosition.x,
                visualLayout.HeaderBottomBar.SurfaceLeft - visualLayout.HeaderBottomBar.Left)
            || !Approximately(
                headerBottomBarSurfaceRect.anchoredPosition.y,
                -(visualLayout.HeaderBottomBar.SurfaceTop - visualLayout.HeaderBottomBar.Top))
            || !Approximately(
                headerBottomBarSurfaceRect.sizeDelta.x,
                visualLayout.HeaderBottomBar.SurfaceWidth)
            || !Approximately(
                headerBottomBarSurfaceRect.sizeDelta.y,
                visualLayout.HeaderBottomBar.SurfaceHeight)
            || headerBottomBarGraphic.BorderWidth != 0f
            || headerBottomBarGraphic.FalloffDistance != 1f
            || headerBottomBarGraphic.type != Image.Type.Simple
            || !headerBottomBarGraphic.maskable
            || !Approximately(
                headerBottomBarModifier.Radius,
                visualLayout.HeaderBottomBar.SurfaceCornerRadius)
            || !Approximately(
                headerBottomBarModifier.Radius * canvasScaleFactor,
                RetainedHeaderBottomBarPolicy.SurfaceCornerRadiusPixels
                * visualLayout.ReferenceTransform.ReferenceScale)
            || !Approximately(
                visualLayout.HeaderBottomBar.Top + visualLayout.HeaderBottomBar.Height,
                visualLayout.Header.Top + visualLayout.Header.Height)
            || !Approximately(
                visualLayout.HeaderBottomBar.SurfaceTop + visualLayout.HeaderBottomBar.SurfaceHeight,
                visualLayout.Header.Top + visualLayout.Header.Height))
        {
            throw new InvalidOperationException(
                "HeaderBottomBar did not retain its exact strip geometry, colour, or rounded-bottom silhouette.");
        }

        if (backButtonRect.gameObject.name != RetainedBackControlPolicy.ButtonName
            || backButtonRect.anchorMin != new Vector2(0f, 1f)
            || backButtonRect.anchorMax != new Vector2(0f, 1f)
            || backButtonRect.pivot != new Vector2(0f, 1f)
            || backButtonGraphic.BorderWidth != 0f
            || backButtonGraphic.sprite != null
            || backButtonGraphic.overrideSprite != null
            || !Approximately(backButtonRect.anchoredPosition.x, visualLayout.BackControl.Left)
            || !Approximately(backButtonRect.anchoredPosition.y, -visualLayout.BackControl.Top)
            || !Approximately(backButtonRect.sizeDelta.x, visualLayout.BackControl.Width)
            || !Approximately(backButtonRect.sizeDelta.y, visualLayout.BackControl.Height)
            || !Approximately(backButtonModifier.Radius, visualLayout.BackControl.CornerRadius)
            || backArrowRect.gameObject.name != RetainedBackControlPolicy.ArrowName
            || backArrowRect.anchorMin != new Vector2(0f, 1f)
            || backArrowRect.anchorMax != new Vector2(0f, 1f)
            || backArrowRect.pivot != new Vector2(0f, 1f)
            || !Approximately(
                backArrowRect.anchoredPosition.x,
                visualLayout.BackControl.ArrowLeft - visualLayout.BackControl.Left)
            || !Approximately(
                backArrowRect.anchoredPosition.y,
                -(visualLayout.BackControl.ArrowTop - visualLayout.BackControl.Top))
            || !Approximately(backArrowRect.sizeDelta.x, visualLayout.BackControl.ArrowWidth)
            || !Approximately(backArrowRect.sizeDelta.y, visualLayout.BackControl.ArrowHeight)
            || backArrowGraphic.sprite == null
            || backArrowGraphic.overrideSprite != backArrowGraphic.sprite
            || backArrowGraphic.sprite.name != RetainedBackArrowAssetPolicy.SpriteName
            || backArrowGraphic.sprite.texture == null
            || backArrowGraphic.sprite.texture.name != RetainedBackArrowAssetPolicy.TextureName
            || backArrowGraphic.sprite.texture.width != RetainedBackArrowAssetPolicy.WidthPixels
            || backArrowGraphic.sprite.texture.height != RetainedBackArrowAssetPolicy.HeightPixels
            || !Approximately(backArrowGraphic.sprite.rect.width, RetainedBackArrowAssetPolicy.WidthPixels)
            || !Approximately(backArrowGraphic.sprite.rect.height, RetainedBackArrowAssetPolicy.HeightPixels))
        {
            throw new InvalidOperationException(
                "BackButton did not retain its exact reference geometry, circular background, or mock-matched arrow.");
        }

        if (headerTitleRect.gameObject.name != RetainedHeaderTitlePolicy.Name
            || headerTitleRect.anchorMin != new Vector2(0f, 1f)
            || headerTitleRect.anchorMax != new Vector2(0f, 1f)
            || headerTitleRect.pivot != new Vector2(0f, 1f)
            || headerTitleRect.GetSiblingIndex() <= headerRect.GetSiblingIndex()
            || !Approximately(headerTitleRect.anchoredPosition.x, visualLayout.HeaderTitle.Left)
            || !Approximately(headerTitleRect.anchoredPosition.y, -visualLayout.HeaderTitle.Top)
            || !Approximately(headerTitleRect.sizeDelta.x, visualLayout.HeaderTitle.Width)
            || !Approximately(headerTitleRect.sizeDelta.y, visualLayout.HeaderTitle.Height)
            || !Approximately(headerTitleGraphic.fontSize, visualLayout.HeaderTitle.FontSize)
            || headerTitleGraphic.text != RetainedHeaderTitlePolicy.Text
            || headerTitleGraphic.font == null
            || headerTitleGraphic.font.name != RetainedHeaderTitlePolicy.FontAssetName
            || headerTitleGraphic.fontSharedMaterial == null
            || headerTitleGraphic.fontSharedMaterial.name != RetainedHeaderTitlePolicy.MaterialName
            || headerTitleGraphic.fontStyle != FontStyles.Normal
            || headerTitleGraphic.fontWeight != FontWeight.Regular
            || !Approximately(headerTitleGraphic.characterSpacing, 0f)
            || !Approximately(headerTitleGraphic.wordSpacing, 0f)
            || !Approximately(headerTitleGraphic.lineSpacing, 0f)
            || !Approximately(headerTitleGraphic.paragraphSpacing, 0f)
            || headerTitleGraphic.alignment != TextAlignmentOptions.Left
            || headerTitleGraphic.enableWordWrapping != RetainedHeaderTitlePolicy.WordWrapping
            || headerTitleGraphic.enableAutoSizing != RetainedHeaderTitlePolicy.AutoSizing
            || headerTitleGraphic.overflowMode != TextOverflowModes.Overflow
            || !Approximately(headerTitleGraphic.color.r, RetainedHeaderTitlePolicy.Red)
            || !Approximately(headerTitleGraphic.color.g, RetainedHeaderTitlePolicy.Green)
            || !Approximately(headerTitleGraphic.color.b, RetainedHeaderTitlePolicy.Blue)
            || !Approximately(headerTitleGraphic.color.a, RetainedHeaderTitlePolicy.Alpha)
            || headerTitleGraphic.raycastTarget != RetainedHeaderTitlePolicy.BlocksRaycasts)
        {
            throw new InvalidOperationException(
                "HeaderTitle did not retain its exact native typography, reference geometry, or non-interactive state.");
        }
    }

    private static void ValidateTabStrip(
        RectTransform shellRoot,
        IReadOnlyList<RetainedTabControl> tabControls,
        RetainedTabLabelMaterial tabLabelMaterial,
        TextMeshProUGUI headerTitleGraphic,
        RectTransform headerBottomBarRect,
        RetainedVisualCanvasLayout visualLayout,
        StatisticsPanelTab selectedTab,
        float canvasScaleFactor)
    {
        if (tabControls.Count != RetainedShellCompositionPolicy.TabCount
            || visualLayout.TabStrip.Tabs.Count != RetainedShellCompositionPolicy.TabCount
            || tabControls.Count(control => control.Specification.Tab == selectedTab) != 1
            || !tabLabelMaterial.IsConfigured()
            || !tabLabelMaterial.IsSourceUnchanged())
        {
            throw new InvalidOperationException("The retained tab-strip ownership contract is invalid.");
        }

        var expectedGap = visualLayout.ReferenceTransform.CanvasLength(RetainedTabStripPolicy.GapPixels);
        RetainedTabCanvasLayout? previousLayout = null;
        for (var index = 0; index < tabControls.Count; index++)
        {
            var control = tabControls[index];
            var specification = RetainedTabStripPolicy.Specifications[index];
            var tab = visualLayout.TabStrip.Tabs[index];
            var expectedColor = RetainedTabVisualStatePolicy.Resolve(selectedTab, specification.Tab);
            if (!ReferenceEquals(control.Specification, specification)
                || specification.Tab != PanelInteractionState.NavigationOrder[index]
                || !ReferenceEquals(tab.Specification, specification)
                || !ReferenceEquals(tab.ReferenceTransform, visualLayout.ReferenceTransform)
                || control.Rect.parent != shellRoot
                || control.Rect.childCount != RetainedShellCompositionPolicy.TabChildCount
                || control.LabelRect.parent != control.Rect
                || control.LabelRect.childCount != RetainedShellCompositionPolicy.TabLabelChildCount
                || control.Rect.gameObject.name != specification.BackgroundName
                || control.LabelRect.gameObject.name != specification.LabelName
                || control.Rect.anchorMin != new Vector2(0f, 1f)
                || control.Rect.anchorMax != new Vector2(0f, 1f)
                || control.Rect.pivot != new Vector2(0f, 1f)
                || control.Rect.localScale != Vector3.one
                || !Approximately(control.Rect.anchoredPosition.x, tab.Left)
                || !Approximately(control.Rect.anchoredPosition.y, -tab.Top)
                || !Approximately(control.Rect.sizeDelta.x, tab.Width)
                || !Approximately(control.Rect.sizeDelta.y, tab.Height)
                || control.Background.BorderWidth != 0f
                || control.Background.FalloffDistance != 1f
                || control.Background.sprite != null
                || control.Background.overrideSprite != null
                || control.Background.type != Image.Type.Simple
                || control.Background.raycastTarget != RetainedOverviewTabPolicy.BackgroundBlocksRaycasts
                || !RetainedTabVisualStatePolicy.IsExactColor(
                    expectedColor,
                    control.Background.color.r,
                    control.Background.color.g,
                    control.Background.color.b,
                    control.Background.color.a)
                || !ReferenceEquals(control.VisualState.Target, control.Background)
                || control.Modifier.Side != OnlyOneEdgeModifier.ProceduralImageEdge.Top
                || !Approximately(control.Modifier.Radius, tab.CornerRadius)
                || !Approximately(
                    control.Modifier.Radius * canvasScaleFactor,
                    RetainedOverviewTabPolicy.CornerRadiusPixels
                    * visualLayout.ReferenceTransform.ReferenceScale)
                || control.Button.targetGraphic != control.Background
                || control.Button.transition != Selectable.Transition.None
                || control.Button.navigation.mode != Navigation.Mode.None
                || control.Button.onClick.GetPersistentEventCount() != 0
                || control.LabelRect.anchorMin != new Vector2(0f, 1f)
                || control.LabelRect.anchorMax != new Vector2(0f, 1f)
                || control.LabelRect.pivot != new Vector2(0f, 1f)
                || control.LabelRect.localScale != Vector3.one
                || !Approximately(control.LabelRect.anchoredPosition.x, tab.LeftPadding)
                || !Approximately(control.LabelRect.anchoredPosition.y, -tab.TopPadding)
                || !Approximately(control.LabelRect.sizeDelta.x, tab.LabelWidth)
                || !Approximately(control.LabelRect.sizeDelta.y, tab.LabelHeight)
                || !Approximately(tab.Width, tab.PreferredLabelWidth + tab.LeftPadding + tab.RightPadding)
                || !Approximately(tab.LabelHeight, tab.Height - tab.TopPadding - tab.BottomPadding)
                || !Approximately(control.Label.fontSize, tab.FontSize)
                || control.Label.text != UiText.Get(specification.TextKey)
                || control.Label.font == null
                || control.Label.font.name != RetainedOverviewTabPolicy.FontAssetName
                || !ReferenceEquals(control.Label.font, headerTitleGraphic.font)
                || !ReferenceEquals(control.Label.fontSharedMaterial, tabLabelMaterial.Instance)
                || ReferenceEquals(control.Label.fontSharedMaterial, headerTitleGraphic.fontSharedMaterial)
                || control.Label.fontStyle != FontStyles.Normal
                || control.Label.fontWeight != FontWeight.Regular
                || !Approximately(control.Label.characterSpacing, RetainedOverviewTabPolicy.CharacterSpacing)
                || !Approximately(control.Label.wordSpacing, RetainedOverviewTabPolicy.WordSpacing)
                || !Approximately(control.Label.lineSpacing, RetainedOverviewTabPolicy.LineSpacing)
                || !Approximately(control.Label.paragraphSpacing, RetainedOverviewTabPolicy.ParagraphSpacing)
                || control.Label.alignment != TextAlignmentOptions.Center
                || control.Label.enableWordWrapping != RetainedOverviewTabPolicy.WordWrapping
                || control.Label.enableAutoSizing != RetainedOverviewTabPolicy.AutoSizing
                || control.Label.overflowMode != TextOverflowModes.Overflow
                || control.Label.margin != Vector4.zero
                || !Approximately(control.Label.color.r, RetainedOverviewTabPolicy.LabelRed)
                || !Approximately(control.Label.color.g, RetainedOverviewTabPolicy.LabelGreen)
                || !Approximately(control.Label.color.b, RetainedOverviewTabPolicy.LabelBlue)
                || !Approximately(control.Label.color.a, RetainedOverviewTabPolicy.LabelAlpha)
                || control.Label.raycastTarget != RetainedOverviewTabPolicy.LabelBlocksRaycasts
                || control.Rect.GetSiblingIndex() >= headerBottomBarRect.GetSiblingIndex()
                || (index > 0
                    && control.Rect.GetSiblingIndex() <= tabControls[index - 1].Rect.GetSiblingIndex())
                || (index == 0 && !Approximately(
                    tab.Left,
                    visualLayout.ReferenceTransform.CanvasX(RetainedTabStripPolicy.FirstTabLeftPixels)))
                || (previousLayout != null && !Approximately(
                    tab.Left,
                    previousLayout.Left + previousLayout.Width + expectedGap)))
            {
                throw new InvalidOperationException(
                    $"The retained {specification.Tab} tab did not preserve the Gate 07 contract.");
            }

            previousLayout = tab;
        }
    }

    private static bool Approximately(float left, float right) => Math.Abs(left - right) <= 0.001f;

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }

    private void DestroyRoot()
    {
        if (root != null)
        {
            root.SetActive(false);
            UnityEngine.Object.Destroy(root);
        }

        root = null;
        canvas = null;
        shellRoot = null;
        headerRect = null;
        headerModifier = null;
        tabListenerLease?.Dispose();
        tabListenerLease = null;
        tabControls.Clear();
        tabLabelMaterial?.Dispose();
        tabLabelMaterial = null;
        headerBottomBarRect = null;
        headerBottomBarMask = null;
        headerBottomBarSurfaceRect = null;
        headerBottomBarGraphic = null;
        headerBottomBarModifier = null;
        backButtonRect = null;
        backButtonModifier = null;
        backArrowRect = null;
        backArrowAsset?.Dispose();
        backArrowAsset = null;
        headerTitleRect = null;
        headerTitleGraphic = null;
        lastAppliedVisualLayout = null;
        lastViewportPixelWidth = float.NaN;
        lastViewportPixelHeight = float.NaN;
        lastCanvasScaleFactor = float.NaN;
    }

    public void Dispose() => DestroyRoot();
}
