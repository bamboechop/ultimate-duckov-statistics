using Duckov.UI.Animations;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UI.ProceduralImage;

namespace UltimateDuckovStatistics.UI;

/// <summary>
/// Owns the exact retained surfaces introduced through M17 visual correction Gate 15.
/// The root graphic remains the modal dimmer; its children are the frozen header controls and tab-owned views.
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

    private sealed class RetainedStatisticsRowTextElements
    {
        public RetainedStatisticsRowTextElements(
            RectTransform labelRect,
            TextMeshProUGUI label,
            RectTransform valueRect,
            TextMeshProUGUI value,
            RectTransform? secondaryValueRect,
            TextMeshProUGUI? secondaryValue)
        {
            LabelRect = labelRect;
            Label = label;
            ValueRect = valueRect;
            Value = value;
            SecondaryValueRect = secondaryValueRect;
            SecondaryValue = secondaryValue;
        }

        public RectTransform LabelRect { get; }
        public TextMeshProUGUI Label { get; }
        public RectTransform ValueRect { get; }
        public TextMeshProUGUI Value { get; }
        public RectTransform? SecondaryValueRect { get; }
        public TextMeshProUGUI? SecondaryValue { get; }
    }

    private sealed class RetainedStatisticsRowControl
    {
        public RetainedStatisticsRowControl(
            RectTransform rect,
            UniformModifier modifier,
            RectTransform contentRect,
            RetainedStatisticsRowTextElements text)
        {
            Rect = rect;
            Modifier = modifier;
            ContentRect = contentRect;
            Text = text;
        }

        public RectTransform Rect { get; }
        public UniformModifier Modifier { get; }
        public RectTransform ContentRect { get; }
        public RetainedStatisticsRowTextElements Text { get; }
    }

    private sealed class RetainedOverviewHighlightRowControl
    {
        public RetainedOverviewHighlightRowControl(
            RectTransform rect,
            UniformModifier modifier,
            RectTransform labelRect,
            TextMeshProUGUI label,
            RectTransform valueRect,
            TextMeshProUGUI value)
        {
            Rect = rect;
            Modifier = modifier;
            LabelRect = labelRect;
            Label = label;
            ValueRect = valueRect;
            Value = value;
        }

        public RectTransform Rect { get; }
        public UniformModifier Modifier { get; }
        public RectTransform LabelRect { get; }
        public TextMeshProUGUI Label { get; }
        public RectTransform ValueRect { get; }
        public TextMeshProUGUI Value { get; }
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
    private GameObject? overviewContentView;
    private RetainedTabViewVisibility<GameObject>? overviewContentVisibility;
    private RectTransform? overviewLeftPanelRect;
    private UniformModifier? overviewLeftPanelModifier;
    private RectTransform? overviewRightPanelRect;
    private UniformModifier? overviewRightPanelModifier;
    private RectTransform? overviewLeftPanelContentRect;
    private RectTransform? overviewRightPanelContentRect;
    private RectTransform? overviewProfileSummaryHeadingRect;
    private TextMeshProUGUI? overviewProfileSummaryHeadingGraphic;
    private RectTransform? overviewHighlightsHeadingRect;
    private TextMeshProUGUI? overviewHighlightsHeadingGraphic;
    private RectTransform? overviewLatestRunHeadingRect;
    private TextMeshProUGUI? overviewLatestRunHeadingGraphic;
    private RectTransform? overviewLatestRunCardRect;
    private UniformModifier? overviewLatestRunCardModifier;
    private readonly List<RetainedOverviewHighlightRowControl> overviewHighlightRows = new();
    private readonly List<RetainedStatisticsRowControl> overviewProfileSummaryRows = new();
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
        StatisticsPanelProjection projection,
        StatisticsPanelTab initialTab,
        Action<StatisticsPanelTab> selectTab,
        Action close,
        out string? error)
    {
        if (targetCanvas == null) throw new ArgumentNullException(nameof(targetCanvas));
        if (projection == null) throw new ArgumentNullException(nameof(projection));
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

            overviewContentView = CreateOverviewContentView(
                rootRect,
                headerTitleTypography,
                tabLabelMaterial.Instance,
                out var createdOverviewLeftPanelRect,
                out var createdOverviewLeftPanelModifier,
                out var createdOverviewRightPanelRect,
                out var createdOverviewRightPanelModifier,
                out var createdOverviewLeftPanelContentRect,
                out var createdOverviewRightPanelContentRect,
                out var createdOverviewProfileSummaryHeadingRect,
                out var createdOverviewProfileSummaryHeadingGraphic,
                out var createdOverviewHighlightsHeadingRect,
                out var createdOverviewHighlightsHeadingGraphic,
                out var createdOverviewLatestRunHeadingRect,
                out var createdOverviewLatestRunHeadingGraphic,
                out var createdOverviewLatestRunCardRect,
                out var createdOverviewLatestRunCardModifier,
                out var createdOverviewHighlightRows,
                out var createdOverviewProfileSummaryRows,
                projection);
            overviewLeftPanelRect = createdOverviewLeftPanelRect;
            overviewLeftPanelModifier = createdOverviewLeftPanelModifier;
            overviewRightPanelRect = createdOverviewRightPanelRect;
            overviewRightPanelModifier = createdOverviewRightPanelModifier;
            overviewLeftPanelContentRect = createdOverviewLeftPanelContentRect;
            overviewRightPanelContentRect = createdOverviewRightPanelContentRect;
            overviewProfileSummaryHeadingRect = createdOverviewProfileSummaryHeadingRect;
            overviewProfileSummaryHeadingGraphic = createdOverviewProfileSummaryHeadingGraphic;
            overviewHighlightsHeadingRect = createdOverviewHighlightsHeadingRect;
            overviewHighlightsHeadingGraphic = createdOverviewHighlightsHeadingGraphic;
            overviewLatestRunHeadingRect = createdOverviewLatestRunHeadingRect;
            overviewLatestRunHeadingGraphic = createdOverviewLatestRunHeadingGraphic;
            overviewLatestRunCardRect = createdOverviewLatestRunCardRect;
            overviewLatestRunCardModifier = createdOverviewLatestRunCardModifier;
            overviewHighlightRows.AddRange(createdOverviewHighlightRows);
            overviewProfileSummaryRows.AddRange(createdOverviewProfileSummaryRows);
            overviewContentVisibility = new RetainedTabViewVisibility<GameObject>(
                overviewContentView,
                RetainedOverviewLeftPanelPolicy.OwnerTab,
                static (target, visible) => target.SetActive(visible));
            overviewContentVisibility.Apply(selectedTab);

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
            root.SetActive(RetainedTabMeasurementPolicy.RequiresActiveHierarchy);
            if (!root.activeInHierarchy)
            {
                throw new InvalidOperationException(
                    "The retained tab labels could not enter the active hierarchy for native TMP measurement.");
            }
            RefreshVisualLayout(force: true);
            rootRect.SetAsLastSibling();
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
        overviewContentVisibility?.Apply(selectedTab);
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

    private static GameObject CreateOverviewContentView(
        RectTransform parent,
        NativeHeaderTitleTypography typography,
        Material headingMaterial,
        out RectTransform leftPanelRect,
        out UniformModifier leftPanelModifier,
        out RectTransform rightPanelRect,
        out UniformModifier rightPanelModifier,
        out RectTransform leftPanelContentRect,
        out RectTransform rightPanelContentRect,
        out RectTransform profileSummaryHeadingRect,
        out TextMeshProUGUI profileSummaryHeadingGraphic,
        out RectTransform highlightsHeadingRect,
        out TextMeshProUGUI highlightsHeadingGraphic,
        out RectTransform latestRunHeadingRect,
        out TextMeshProUGUI latestRunHeadingGraphic,
        out RectTransform latestRunCardRect,
        out UniformModifier latestRunCardModifier,
        out List<RetainedOverviewHighlightRowControl> highlightRows,
        out List<RetainedStatisticsRowControl> profileSummaryRows,
        StatisticsPanelProjection projection)
    {
        var view = new GameObject(
            RetainedOverviewLeftPanelPolicy.ViewName,
            typeof(RectTransform));
        var viewRect = (RectTransform)view.transform;
        viewRect.SetParent(parent, worldPositionStays: false);
        Stretch(viewRect);

        leftPanelRect = CreateOverviewPanel(
            viewRect,
            RetainedOverviewLeftPanelPolicy.BackgroundName,
            out leftPanelModifier);
        rightPanelRect = CreateOverviewPanel(
            viewRect,
            RetainedOverviewRightPanelPolicy.BackgroundName,
            out rightPanelModifier);
        leftPanelContentRect = CreateOverviewLeftPanelContent(leftPanelRect);
        rightPanelContentRect = CreateOverviewRightPanelContent(rightPanelRect);
        profileSummaryHeadingRect = CreateOverviewProfileSummaryHeading(
            leftPanelContentRect,
            typography,
            headingMaterial,
            out profileSummaryHeadingGraphic);
        highlightsHeadingRect = CreateOverviewHighlightsHeading(
            rightPanelContentRect,
            typography,
            headingMaterial,
            out highlightsHeadingGraphic);
        var highlightPresentations = OverviewHighlightsPresentationFactory.Create(projection, UiText.Get);
        highlightRows = new List<RetainedOverviewHighlightRowControl>(highlightPresentations.Count);
        for (var index = 0; index < highlightPresentations.Count; index++)
        {
            highlightRows.Add(CreateOverviewHighlightRow(
                rightPanelContentRect,
                RetainedOverviewHighlightsRowsPolicy.Specifications[index],
                highlightPresentations[index],
                typography,
                headingMaterial));
        }
        latestRunHeadingRect = CreateOverviewLatestRunHeading(
            rightPanelContentRect,
            typography,
            headingMaterial,
            out latestRunHeadingGraphic);
        latestRunCardRect = CreateOverviewPanel(
            rightPanelContentRect,
            RetainedOverviewLatestRunCardPolicy.Name,
            out latestRunCardModifier);
        var presentations = ProfileSummaryPresentationFactory.Create(projection, UiText.Get);
        profileSummaryRows = new List<RetainedStatisticsRowControl>(presentations.Count);
        for (var index = 0; index < presentations.Count; index++)
        {
            profileSummaryRows.Add(CreateOverviewStatisticsRow(
                leftPanelContentRect,
                RetainedProfileSummaryRowsPolicy.Specifications[index],
                presentations[index],
                typography,
                headingMaterial));
        }
        return view;
    }

    private static RectTransform CreateOverviewPanel(
        RectTransform parent,
        string name,
        out UniformModifier panelModifier)
    {
        var panel = new GameObject(
            name,
            typeof(RectTransform));
        var panelRect = (RectTransform)panel.transform;
        panelRect.SetParent(parent, worldPositionStays: false);
        panelRect.anchorMin = new Vector2(0f, 1f);
        panelRect.anchorMax = new Vector2(0f, 1f);
        panelRect.pivot = new Vector2(0f, 1f);
        panelRect.localScale = Vector3.one;

        var background = panel.AddComponent<ProceduralImage>();
        background.color = new Color(
            RetainedOverviewPanelStylePolicy.Red,
            RetainedOverviewPanelStylePolicy.Green,
            RetainedOverviewPanelStylePolicy.Blue,
            RetainedOverviewPanelStylePolicy.LayerAlpha);
        background.BorderWidth = 0f;
        background.FalloffDistance = 1f;
        background.sprite = null;
        background.overrideSprite = null;
        background.type = Image.Type.Simple;
        background.raycastTarget = RetainedOverviewPanelStylePolicy.BlocksRaycasts;
        panelModifier = panel.AddComponent<UniformModifier>();
        return panelRect;
    }

    private static RectTransform CreateOverviewLeftPanelContent(RectTransform parent)
    {
        var content = new GameObject(
            RetainedOverviewLeftPanelPolicy.ContentName,
            typeof(RectTransform));
        var rect = (RectTransform)content.transform;
        rect.SetParent(parent, worldPositionStays: false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.localScale = Vector3.one;
        return rect;
    }

    private static RectTransform CreateOverviewRightPanelContent(RectTransform parent)
    {
        var content = new GameObject(
            RetainedOverviewRightPanelPolicy.ContentName,
            typeof(RectTransform));
        var rect = (RectTransform)content.transform;
        rect.SetParent(parent, worldPositionStays: false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.localScale = Vector3.one;
        return rect;
    }

    private static RectTransform CreateOverviewProfileSummaryHeading(
        RectTransform parent,
        NativeHeaderTitleTypography typography,
        Material material,
        out TextMeshProUGUI text)
    {
        var heading = new GameObject(
            RetainedOverviewProfileSummaryHeadingPolicy.Name,
            typeof(RectTransform));
        var rect = (RectTransform)heading.transform;
        rect.SetParent(parent, worldPositionStays: false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.localScale = Vector3.one;

        text = heading.AddComponent<TextMeshProUGUI>();
        text.font = typography.Font;
        text.fontSharedMaterial = material;
        text.text = UiText.Get(RetainedOverviewProfileSummaryHeadingPolicy.TextKey);
        text.fontStyle = FontStyles.Normal;
        text.fontWeight = FontWeight.Regular;
        text.characterSpacing = 0f;
        text.wordSpacing = 0f;
        text.lineSpacing = 0f;
        text.paragraphSpacing = 0f;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.enableWordWrapping = RetainedOverviewProfileSummaryHeadingPolicy.WordWrapping;
        text.enableAutoSizing = RetainedOverviewProfileSummaryHeadingPolicy.AutoSizing;
        text.overflowMode = TextOverflowModes.Overflow;
        text.margin = Vector4.zero;
        text.color = new Color(
            RetainedOverviewProfileSummaryHeadingPolicy.Red,
            RetainedOverviewProfileSummaryHeadingPolicy.Green,
            RetainedOverviewProfileSummaryHeadingPolicy.Blue,
            RetainedOverviewProfileSummaryHeadingPolicy.Alpha);
        text.raycastTarget = RetainedOverviewProfileSummaryHeadingPolicy.BlocksRaycasts;
        return rect;
    }

    private static RectTransform CreateOverviewHighlightsHeading(
        RectTransform parent,
        NativeHeaderTitleTypography typography,
        Material material,
        out TextMeshProUGUI text)
    {
        var heading = new GameObject(
            RetainedOverviewHighlightsHeadingPolicy.Name,
            typeof(RectTransform));
        var rect = (RectTransform)heading.transform;
        rect.SetParent(parent, worldPositionStays: false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.localScale = Vector3.one;

        text = heading.AddComponent<TextMeshProUGUI>();
        text.font = typography.Font;
        text.fontSharedMaterial = material;
        text.text = UiText.Get(RetainedOverviewHighlightsHeadingPolicy.TextKey);
        text.fontStyle = FontStyles.Normal;
        text.fontWeight = FontWeight.Regular;
        text.characterSpacing = 0f;
        text.wordSpacing = 0f;
        text.lineSpacing = 0f;
        text.paragraphSpacing = 0f;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.enableWordWrapping = RetainedOverviewHighlightsHeadingPolicy.WordWrapping;
        text.enableAutoSizing = RetainedOverviewHighlightsHeadingPolicy.AutoSizing;
        text.overflowMode = TextOverflowModes.Overflow;
        text.margin = Vector4.zero;
        text.color = new Color(
            RetainedOverviewHighlightsHeadingPolicy.Red,
            RetainedOverviewHighlightsHeadingPolicy.Green,
            RetainedOverviewHighlightsHeadingPolicy.Blue,
            RetainedOverviewHighlightsHeadingPolicy.Alpha);
        text.raycastTarget = RetainedOverviewHighlightsHeadingPolicy.BlocksRaycasts;
        return rect;
    }

    private static RetainedOverviewHighlightRowControl CreateOverviewHighlightRow(
        RectTransform parent,
        RetainedOverviewHighlightRowSpecification specification,
        OverviewHighlightPresentation presentation,
        NativeHeaderTitleTypography typography,
        Material material)
    {
        var row = new GameObject(
            specification.RowName,
            typeof(RectTransform));
        var rect = (RectTransform)row.transform;
        rect.SetParent(parent, worldPositionStays: false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.localScale = Vector3.one;

        AddOverviewStatisticsRowBackground(row, out var modifier);

        var labelRect = CreateStatisticsRowText(
            rect,
            specification.LabelName,
            presentation.Label,
            typography,
            material,
            out var label);
        var valueRect = CreateStatisticsRowText(
            rect,
            specification.ValueName,
            presentation.Value,
            typography,
            material,
            out var value);
        return new RetainedOverviewHighlightRowControl(rect, modifier, labelRect, label, valueRect, value);
    }

    private static RectTransform CreateOverviewLatestRunHeading(
        RectTransform parent,
        NativeHeaderTitleTypography typography,
        Material material,
        out TextMeshProUGUI text)
    {
        var heading = new GameObject(
            RetainedOverviewLatestRunHeadingPolicy.Name,
            typeof(RectTransform));
        var rect = (RectTransform)heading.transform;
        rect.SetParent(parent, worldPositionStays: false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.localScale = Vector3.one;

        text = heading.AddComponent<TextMeshProUGUI>();
        text.font = typography.Font;
        text.fontSharedMaterial = material;
        text.text = UiText.Get(RetainedOverviewLatestRunHeadingPolicy.TextKey);
        text.fontStyle = FontStyles.Normal;
        text.fontWeight = FontWeight.Regular;
        text.characterSpacing = RetainedOverviewLatestRunHeadingPolicy.CharacterSpacing;
        text.wordSpacing = RetainedOverviewLatestRunHeadingPolicy.WordSpacing;
        text.lineSpacing = RetainedOverviewLatestRunHeadingPolicy.LineSpacing;
        text.paragraphSpacing = RetainedOverviewLatestRunHeadingPolicy.ParagraphSpacing;
        text.alignment = TextAlignmentOptions.TopLeft;
        text.enableWordWrapping = RetainedOverviewLatestRunHeadingPolicy.WordWrapping;
        text.enableAutoSizing = RetainedOverviewLatestRunHeadingPolicy.AutoSizing;
        text.overflowMode = TextOverflowModes.Overflow;
        text.margin = Vector4.zero;
        text.color = new Color(
            RetainedOverviewLatestRunHeadingPolicy.Red,
            RetainedOverviewLatestRunHeadingPolicy.Green,
            RetainedOverviewLatestRunHeadingPolicy.Blue,
            RetainedOverviewLatestRunHeadingPolicy.Alpha);
        text.raycastTarget = RetainedOverviewLatestRunHeadingPolicy.BlocksRaycasts;
        return rect;
    }

    private static RetainedStatisticsRowControl CreateOverviewStatisticsRow(
        RectTransform parent,
        RetainedProfileSummaryRowSpecification specification,
        ProfileSummaryRowPresentation presentation,
        NativeHeaderTitleTypography typography,
        Material textMaterial)
    {
        var row = new GameObject(
            specification.BackgroundName,
            typeof(RectTransform));
        var rect = (RectTransform)row.transform;
        rect.SetParent(parent, worldPositionStays: false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.localScale = Vector3.one;

        AddOverviewStatisticsRowBackground(row, out var modifier);

        var content = new GameObject(
            specification.ContentName,
            typeof(RectTransform));
        var contentRect = (RectTransform)content.transform;
        contentRect.SetParent(rect, worldPositionStays: false);
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(0f, 1f);
        contentRect.pivot = new Vector2(0f, 1f);
        contentRect.localScale = Vector3.one;

        var labelRect = CreateStatisticsRowText(
            contentRect,
            specification.LabelName,
            presentation.Label,
            typography,
            textMaterial,
            out var label);
        var valueRect = CreateStatisticsRowText(
            contentRect,
            specification.ValueName,
            presentation.Value,
            typography,
            textMaterial,
            out var value);
        RectTransform? secondaryValueRect = null;
        TextMeshProUGUI? secondaryValue = null;
        if (specification.HasSecondaryValue)
        {
            secondaryValueRect = CreateStatisticsRowText(
                contentRect,
                specification.SecondaryValueName!,
                presentation.SecondaryValue ?? string.Empty,
                typography,
                textMaterial,
                out secondaryValue);
        }

        var textElements = new RetainedStatisticsRowTextElements(
            labelRect,
            label,
            valueRect,
            value,
            secondaryValueRect,
            secondaryValue);
        return new RetainedStatisticsRowControl(rect, modifier, contentRect, textElements);
    }

    private static void AddOverviewStatisticsRowBackground(
        GameObject row,
        out UniformModifier modifier)
    {
        var background = row.AddComponent<ProceduralImage>();
        background.color = new Color(
            RetainedOverviewFirstStatisticsRowPolicy.Red,
            RetainedOverviewFirstStatisticsRowPolicy.Green,
            RetainedOverviewFirstStatisticsRowPolicy.Blue,
            RetainedOverviewFirstStatisticsRowPolicy.LayerAlpha);
        background.BorderWidth = 0f;
        background.FalloffDistance = 1f;
        background.sprite = null;
        background.overrideSprite = null;
        background.type = Image.Type.Simple;
        background.raycastTarget = RetainedOverviewFirstStatisticsRowPolicy.BlocksRaycasts;
        modifier = row.AddComponent<UniformModifier>();
    }

    private static RectTransform CreateStatisticsRowText(
        RectTransform parent,
        string name,
        string value,
        NativeHeaderTitleTypography typography,
        Material material,
        out TextMeshProUGUI text)
    {
        var textObject = new GameObject(name, typeof(RectTransform));
        var rect = (RectTransform)textObject.transform;
        rect.SetParent(parent, worldPositionStays: false);
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.localScale = Vector3.one;

        text = textObject.AddComponent<TextMeshProUGUI>();
        text.font = typography.Font;
        text.fontSharedMaterial = material;
        text.text = value;
        text.fontStyle = FontStyles.Normal;
        text.fontWeight = FontWeight.Regular;
        text.characterSpacing = RetainedOverviewFirstStatisticsRowEntryPolicy.CharacterSpacing;
        text.wordSpacing = RetainedOverviewFirstStatisticsRowEntryPolicy.WordSpacing;
        text.lineSpacing = RetainedOverviewFirstStatisticsRowEntryPolicy.LineSpacing;
        text.paragraphSpacing = RetainedOverviewFirstStatisticsRowEntryPolicy.ParagraphSpacing;
        text.alignment = TextAlignmentOptions.MidlineLeft;
        text.enableWordWrapping = RetainedOverviewFirstStatisticsRowEntryPolicy.WordWrapping;
        text.enableAutoSizing = RetainedOverviewFirstStatisticsRowEntryPolicy.AutoSizing;
        text.overflowMode = TextOverflowModes.Overflow;
        text.margin = Vector4.zero;
        text.color = new Color(
            RetainedOverviewFirstStatisticsRowEntryPolicy.Red,
            RetainedOverviewFirstStatisticsRowEntryPolicy.Green,
            RetainedOverviewFirstStatisticsRowEntryPolicy.Blue,
            RetainedOverviewFirstStatisticsRowEntryPolicy.Alpha);
        text.raycastTarget = RetainedOverviewFirstStatisticsRowEntryPolicy.BlocksRaycasts;
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
        NativeButtonInteractionFeedbackPolicy.AttachIfMissing(
            tab,
            static target => target.GetComponent<ButtonAnimation>() != null,
            static target => _ = target.AddComponent<ButtonAnimation>());

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
        NativeButtonInteractionFeedbackPolicy.AttachIfMissing(
            back,
            static target => target.GetComponent<ButtonAnimation>() != null,
            static target => _ = target.AddComponent<ButtonAnimation>());

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
            || headerTitleRect == null || headerTitleGraphic == null
            || overviewLeftPanelRect == null || overviewLeftPanelModifier == null
            || overviewRightPanelRect == null || overviewRightPanelModifier == null
            || overviewLeftPanelContentRect == null
            || overviewRightPanelContentRect == null
            || overviewProfileSummaryHeadingRect == null
            || overviewProfileSummaryHeadingGraphic == null
            || overviewHighlightsHeadingRect == null
            || overviewHighlightsHeadingGraphic == null
            || overviewLatestRunHeadingRect == null
            || overviewLatestRunHeadingGraphic == null
            || overviewLatestRunCardRect == null
            || overviewLatestRunCardModifier == null
            || overviewHighlightRows.Count != RetainedOverviewHighlightsRowsPolicy.RowCount
            || overviewProfileSummaryRows.Count != RetainedProfileSummaryRowsPolicy.RowCount)
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
        var leftPanelRect = overviewLeftPanelRect!;
        var leftPanelModifier = overviewLeftPanelModifier!;
        var rightPanelRect = overviewRightPanelRect!;
        var rightPanelModifier = overviewRightPanelModifier!;
        var leftPanelContentRect = overviewLeftPanelContentRect!;
        var rightPanelContentRect = overviewRightPanelContentRect!;
        var profileSummaryHeadingRect = overviewProfileSummaryHeadingRect!;
        var profileSummaryHeadingGraphic = overviewProfileSummaryHeadingGraphic!;
        var highlightsHeadingRect = overviewHighlightsHeadingRect!;
        var highlightsHeadingGraphic = overviewHighlightsHeadingGraphic!;
        var latestRunHeadingRect = overviewLatestRunHeadingRect!;
        var latestRunHeadingGraphic = overviewLatestRunHeadingGraphic!;
        var latestRunCardRect = overviewLatestRunCardRect!;
        var latestRunCardModifier = overviewLatestRunCardModifier!;
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
        leftPanelRect.anchoredPosition = new Vector2(
            layout.OverviewLeftPanel.Left,
            -layout.OverviewLeftPanel.Top);
        leftPanelRect.sizeDelta = new Vector2(
            layout.OverviewLeftPanel.Width,
            layout.OverviewLeftPanel.Height);
        leftPanelModifier.Radius = layout.OverviewLeftPanel.CornerRadius;
        rightPanelRect.anchoredPosition = new Vector2(
            layout.OverviewRightPanel.Left,
            -layout.OverviewRightPanel.Top);
        rightPanelRect.sizeDelta = new Vector2(
            layout.OverviewRightPanel.Width,
            layout.OverviewRightPanel.Height);
        rightPanelModifier.Radius = layout.OverviewRightPanel.CornerRadius;
        leftPanelContentRect.anchoredPosition = new Vector2(
            layout.OverviewLeftPanel.ContentLeft - layout.OverviewLeftPanel.Left,
            -(layout.OverviewLeftPanel.ContentTop - layout.OverviewLeftPanel.Top));
        leftPanelContentRect.sizeDelta = new Vector2(
            layout.OverviewLeftPanel.ContentWidth,
            layout.OverviewLeftPanel.ContentHeight);
        rightPanelContentRect.anchoredPosition = new Vector2(
            layout.OverviewRightPanel.ContentLeft - layout.OverviewRightPanel.Left,
            -(layout.OverviewRightPanel.ContentTop - layout.OverviewRightPanel.Top));
        rightPanelContentRect.sizeDelta = new Vector2(
            layout.OverviewRightPanel.ContentWidth,
            layout.OverviewRightPanel.ContentHeight);
        profileSummaryHeadingRect.anchoredPosition = new Vector2(
            layout.OverviewProfileSummaryHeading.Left
            - layout.OverviewLeftPanel.ContentLeft
            + layout.OverviewProfileSummaryHeading.OpticalOffsetX,
            -(layout.OverviewProfileSummaryHeading.Top - layout.OverviewLeftPanel.ContentTop)
            + layout.OverviewProfileSummaryHeading.OpticalOffsetY);
        profileSummaryHeadingRect.sizeDelta = new Vector2(
            layout.OverviewProfileSummaryHeading.Width,
            layout.OverviewProfileSummaryHeading.Height);
        profileSummaryHeadingGraphic.fontSize = layout.OverviewProfileSummaryHeading.FontSize;
        highlightsHeadingRect.anchoredPosition = new Vector2(
            layout.OverviewHighlightsHeading.Left
            - layout.OverviewRightPanel.ContentLeft
            + layout.OverviewHighlightsHeading.OpticalOffsetX,
            -(layout.OverviewHighlightsHeading.Top - layout.OverviewRightPanel.ContentTop)
            + layout.OverviewHighlightsHeading.OpticalOffsetY);
        highlightsHeadingRect.sizeDelta = new Vector2(
            layout.OverviewHighlightsHeading.Width,
            layout.OverviewHighlightsHeading.Height);
        highlightsHeadingGraphic.fontSize = layout.OverviewHighlightsHeading.FontSize;
        for (var index = 0; index < overviewHighlightRows.Count; index++)
        {
            var control = overviewHighlightRows[index];
            var row = layout.OverviewHighlightRows[index];
            var entry = layout.OverviewHighlightEntries[index];
            control.Rect.anchoredPosition = new Vector2(
                row.Left - layout.OverviewRightPanel.ContentLeft,
                -(row.Top - layout.OverviewRightPanel.ContentTop));
            control.Rect.sizeDelta = new Vector2(row.Width, row.Height);
            control.Modifier.Radius = row.CornerRadius;
            ApplyStatisticsRowTextLayout(
                control.LabelRect,
                control.Label,
                entry.LabelLeft - row.Left,
                entry.LabelTop - row.Top,
                entry.LabelWidth,
                entry.LabelHeight,
                entry.FontSize);
            ApplyStatisticsRowTextLayout(
                control.ValueRect,
                control.Value,
                entry.ValueLeft - row.Left,
                entry.ValueTop - row.Top,
                entry.ValueWidth,
                entry.ValueHeight,
                entry.FontSize);
        }
        latestRunHeadingRect.anchoredPosition = new Vector2(
            layout.OverviewLatestRunHeading.Left
            - layout.OverviewRightPanel.ContentLeft
            + layout.OverviewLatestRunHeading.OpticalOffsetX,
            -(layout.OverviewLatestRunHeading.Top - layout.OverviewRightPanel.ContentTop)
            + layout.OverviewLatestRunHeading.OpticalOffsetY);
        latestRunHeadingRect.sizeDelta = new Vector2(
            layout.OverviewLatestRunHeading.Width,
            layout.OverviewLatestRunHeading.Height);
        latestRunHeadingGraphic.fontSize = layout.OverviewLatestRunHeading.FontSize;
        latestRunCardRect.anchoredPosition = new Vector2(
            layout.OverviewLatestRunCard.Left - layout.OverviewRightPanel.ContentLeft,
            -(layout.OverviewLatestRunCard.Top - layout.OverviewRightPanel.ContentTop));
        latestRunCardRect.sizeDelta = new Vector2(
            layout.OverviewLatestRunCard.Width,
            layout.OverviewLatestRunCard.Height);
        latestRunCardModifier.Radius = layout.OverviewLatestRunCard.CornerRadius;
        for (var index = 0; index < overviewProfileSummaryRows.Count; index++)
            ApplyStatisticsRowLayout(
                overviewProfileSummaryRows[index],
                layout.OverviewProfileSummaryRows[index],
                layout.OverviewLeftPanel);
        lastViewportPixelWidth = viewportPixelWidth;
        lastViewportPixelHeight = viewportPixelHeight;
        lastCanvasScaleFactor = canvasScaleFactor;
        lastAppliedVisualLayout = layout;
        return layout;
    }

    private static void ApplyStatisticsRowLayout(
        RetainedStatisticsRowControl control,
        RetainedProfileSummaryRowCanvasLayout layout,
        RetainedOverviewPanelCanvasLayout parentPanel)
    {
        var row = layout.Surface;
        var entry = layout.Entry;
        control.Rect.anchoredPosition = new Vector2(
            row.Left - parentPanel.ContentLeft,
            -(row.Top - parentPanel.ContentTop));
        control.Rect.sizeDelta = new Vector2(row.Width, row.Height);
        control.Modifier.Radius = row.CornerRadius;
        control.ContentRect.anchoredPosition = new Vector2(
            row.ContentLeft - row.Left,
            -(row.ContentTop - row.Top));
        control.ContentRect.sizeDelta = new Vector2(row.ContentWidth, row.ContentHeight);
        ApplyStatisticsRowTextLayout(
            control.Text.LabelRect,
            control.Text.Label,
            entry.LabelLeft - row.ContentLeft,
            entry.LabelTop - row.ContentTop,
            entry.LabelWidth,
            entry.LabelHeight,
            entry.FontSize);
        ApplyStatisticsRowTextLayout(
            control.Text.ValueRect,
            control.Text.Value,
            entry.ValueLeft - row.ContentLeft,
            entry.ValueTop - row.ContentTop,
            entry.ValueWidth,
            entry.ValueHeight,
            entry.FontSize);
        if (entry.HasSecondaryValue
            && control.Text.SecondaryValueRect != null
            && control.Text.SecondaryValue != null)
        {
            ApplyStatisticsRowTextLayout(
                control.Text.SecondaryValueRect,
                control.Text.SecondaryValue,
                entry.SecondaryValueLeft - row.ContentLeft,
                entry.SecondaryValueTop - row.ContentTop,
                entry.SecondaryValueWidth,
                entry.SecondaryValueHeight,
                entry.FontSize);
        }
    }

    private static void ApplyStatisticsRowTextLayout(
        RectTransform rect,
        TextMeshProUGUI text,
        float left,
        float top,
        float width,
        float height,
        float fontSize)
    {
        rect.anchoredPosition = new Vector2(left, -top);
        rect.sizeDelta = new Vector2(width, height);
        text.fontSize = fontSize;
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
            normalizedWidths[index] = normalizedWidth;
        }

        return normalizedWidths;
    }

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
        overviewContentView = null;
        overviewContentVisibility = null;
        overviewLeftPanelRect = null;
        overviewLeftPanelModifier = null;
        overviewRightPanelRect = null;
        overviewRightPanelModifier = null;
        overviewLeftPanelContentRect = null;
        overviewRightPanelContentRect = null;
        overviewProfileSummaryHeadingRect = null;
        overviewProfileSummaryHeadingGraphic = null;
        overviewHighlightsHeadingRect = null;
        overviewHighlightsHeadingGraphic = null;
        overviewLatestRunHeadingRect = null;
        overviewLatestRunHeadingGraphic = null;
        overviewLatestRunCardRect = null;
        overviewLatestRunCardModifier = null;
        overviewHighlightRows.Clear();
        overviewProfileSummaryRows.Clear();
        lastAppliedVisualLayout = null;
        lastViewportPixelWidth = float.NaN;
        lastViewportPixelHeight = float.NaN;
        lastCanvasScaleFactor = float.NaN;
    }

    public void Dispose() => DestroyRoot();
}
