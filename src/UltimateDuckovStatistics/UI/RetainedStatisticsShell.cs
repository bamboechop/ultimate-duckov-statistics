using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UI.ProceduralImage;

namespace UltimateDuckovStatistics.UI;

/// <summary>
/// Owns the exact retained surfaces introduced through M17 visual correction Gate 04.
/// The root graphic remains the modal dimmer; its children are the frozen header/back/title and bottom bar.
/// </summary>
internal sealed class RetainedStatisticsShell : IDisposable
{
    private GameObject? root;
    private Canvas? canvas;
    private RectTransform? shellRoot;
    private RectTransform? headerRect;
    private UniformModifier? headerModifier;
    private RectTransform? headerBottomBarRect;
    private RectTransform? headerBottomBarShapingRect;
    private ProceduralImage? headerBottomBarGraphic;
    private OnlyOneEdgeModifier? headerBottomBarModifier;
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
        Action close,
        out string? error)
    {
        if (targetCanvas == null) throw new ArgumentNullException(nameof(targetCanvas));
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
            headerBottomBarRect = CreateHeaderBottomBar(
                rootRect,
                out var createdHeaderBottomBarShapingRect,
                out var createdHeaderBottomBarGraphic,
                out var createdHeaderBottomBarModifier);
            headerBottomBarShapingRect = createdHeaderBottomBarShapingRect;
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
                headerBottomBarRect,
                createdHeaderBottomBarShapingRect,
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
        selectedTab = tab;
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

    private static RectTransform CreateHeaderBottomBar(
        RectTransform parent,
        out RectTransform shapingRect,
        out ProceduralImage image,
        out OnlyOneEdgeModifier modifier)
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

        var shapingSurface = new GameObject(
            RetainedHeaderBottomBarPolicy.GraphicName,
            typeof(RectTransform));
        shapingRect = (RectTransform)shapingSurface.transform;
        shapingRect.SetParent(rect, worldPositionStays: false);
        shapingRect.anchorMin = new Vector2(0f, 1f);
        shapingRect.anchorMax = new Vector2(0f, 1f);
        shapingRect.pivot = new Vector2(0f, 1f);
        shapingRect.localScale = Vector3.one;

        image = shapingSurface.AddComponent<ProceduralImage>();
        image.color = new Color(
            RetainedHeaderBottomBarPolicy.Red,
            RetainedHeaderBottomBarPolicy.Green,
            RetainedHeaderBottomBarPolicy.Blue,
            RetainedHeaderBottomBarPolicy.Alpha);
        image.BorderWidth = 0f;
        image.FalloffDistance = 1f;
        image.type = Image.Type.Filled;
        image.fillMethod = Image.FillMethod.Vertical;
        image.fillOrigin = (int)Image.OriginVertical.Bottom;
        image.fillAmount = RetainedHeaderBottomBarPolicy.VerticalFillAmount;
        image.raycastTarget = RetainedHeaderBottomBarPolicy.BlocksRaycasts;

        modifier = shapingSurface.AddComponent<OnlyOneEdgeModifier>();
        modifier.Side = OnlyOneEdgeModifier.ProceduralImageEdge.Bottom;
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
            || headerBottomBarRect == null || headerBottomBarShapingRect == null
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

        var layout = RetainedVisualLayoutPolicy.Create(
            viewportPixelWidth,
            viewportPixelHeight,
            canvasScaleFactor);
        headerRect.anchoredPosition = new Vector2(layout.Header.Left, -layout.Header.Top);
        headerRect.sizeDelta = new Vector2(layout.Header.Width, layout.Header.Height);
        headerModifier.Radius = layout.Header.CornerRadius;
        headerBottomBarRect.anchoredPosition = new Vector2(
            layout.HeaderBottomBar.Left,
            -layout.HeaderBottomBar.Top);
        headerBottomBarRect.sizeDelta = new Vector2(
            layout.HeaderBottomBar.Width,
            layout.HeaderBottomBar.Height);
        headerBottomBarShapingRect.anchoredPosition = new Vector2(
            0f,
            -(layout.HeaderBottomBar.ShapingTop - layout.HeaderBottomBar.Top));
        headerBottomBarShapingRect.sizeDelta = new Vector2(
            layout.HeaderBottomBar.Width,
            layout.HeaderBottomBar.ShapingHeight);
        headerBottomBarModifier.Radius = layout.HeaderBottomBar.CornerRadius;
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

    private static void ValidateSurfaceComposition(
        RectTransform shellRoot,
        Image blocker,
        RectTransform headerRect,
        ProceduralImage headerGraphic,
        UniformModifier headerModifier,
        RectTransform headerBottomBarRect,
        RectTransform headerBottomBarShapingRect,
        ProceduralImage headerBottomBarGraphic,
        OnlyOneEdgeModifier headerBottomBarModifier,
        RectTransform backButtonRect,
        ProceduralImage backButtonGraphic,
        UniformModifier backButtonModifier,
        Button backButton,
        RectTransform backArrowRect,
        Image backArrowGraphic,
        RectTransform headerTitleRect,
        TextMeshProUGUI headerTitleGraphic,
        RetainedVisualCanvasLayout visualLayout,
        float canvasScaleFactor)
    {
        var graphics = shellRoot.GetComponentsInChildren<Graphic>(includeInactive: true);
        var buttons = shellRoot.GetComponentsInChildren<Button>(includeInactive: true);
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
            || headerBottomBarRect.parent != shellRoot
            || headerBottomBarRect.childCount != RetainedShellCompositionPolicy.HeaderBottomBarChildCount
            || headerBottomBarShapingRect.parent != headerBottomBarRect
            || headerBottomBarShapingRect.childCount
            != RetainedShellCompositionPolicy.HeaderBottomBarGraphicChildCount
            || backButtonRect.parent != shellRoot
            || backButtonRect.childCount != RetainedShellCompositionPolicy.BackButtonChildCount
            || backArrowRect.parent != backButtonRect
            || backArrowRect.childCount != RetainedShellCompositionPolicy.BackArrowChildCount
            || headerTitleRect.parent != shellRoot
            || headerTitleRect.childCount != RetainedShellCompositionPolicy.HeaderTitleChildCount
            || graphics.Length != RetainedShellCompositionPolicy.GraphicCount
            || buttons.Length != 1
            || buttons[0] != backButton
            || backButton.targetGraphic != backButtonGraphic
            || backButton.transition != Selectable.Transition.None
            || backButton.onClick.GetPersistentEventCount() != 0
            || !ReferenceEquals(visualLayout.ReferenceTransform, visualLayout.Header.ReferenceTransform)
            || !ReferenceEquals(
                visualLayout.ReferenceTransform,
                visualLayout.HeaderBottomBar.ReferenceTransform)
            || !ReferenceEquals(visualLayout.ReferenceTransform, visualLayout.HeaderTitle.ReferenceTransform)
            || !ReferenceEquals(visualLayout.ReferenceTransform, visualLayout.BackControl.ReferenceTransform)
            || !graphics.Contains(blocker)
            || !graphics.Contains(headerGraphic)
            || !graphics.Contains(headerBottomBarGraphic)
            || !graphics.Contains(backButtonGraphic)
            || !graphics.Contains(backArrowGraphic)
            || !graphics.Contains(headerTitleGraphic))
        {
            throw new InvalidOperationException(
                "The Gate 04 shell must contain only the frozen Step 00-03-B visuals and HeaderBottomBar.");
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

        if (headerBottomBarRect.gameObject.name != RetainedHeaderBottomBarPolicy.Name
            || headerBottomBarRect.anchorMin != new Vector2(0f, 1f)
            || headerBottomBarRect.anchorMax != new Vector2(0f, 1f)
            || headerBottomBarRect.pivot != new Vector2(0f, 1f)
            || headerBottomBarRect.GetSiblingIndex() <= headerRect.GetSiblingIndex()
            || !Approximately(headerBottomBarRect.anchoredPosition.x, visualLayout.HeaderBottomBar.Left)
            || !Approximately(headerBottomBarRect.anchoredPosition.y, -visualLayout.HeaderBottomBar.Top)
            || !Approximately(headerBottomBarRect.sizeDelta.x, visualLayout.HeaderBottomBar.Width)
            || !Approximately(headerBottomBarRect.sizeDelta.y, visualLayout.HeaderBottomBar.Height)
            || headerBottomBarShapingRect.gameObject.name != RetainedHeaderBottomBarPolicy.GraphicName
            || headerBottomBarShapingRect.anchorMin != new Vector2(0f, 1f)
            || headerBottomBarShapingRect.anchorMax != new Vector2(0f, 1f)
            || headerBottomBarShapingRect.pivot != new Vector2(0f, 1f)
            || !Approximately(headerBottomBarShapingRect.anchoredPosition.x, 0f)
            || !Approximately(
                headerBottomBarShapingRect.anchoredPosition.y,
                -(visualLayout.HeaderBottomBar.ShapingTop - visualLayout.HeaderBottomBar.Top))
            || !Approximately(headerBottomBarShapingRect.sizeDelta.x, visualLayout.HeaderBottomBar.Width)
            || !Approximately(
                headerBottomBarShapingRect.sizeDelta.y,
                visualLayout.HeaderBottomBar.ShapingHeight)
            || headerBottomBarGraphic.BorderWidth != 0f
            || headerBottomBarGraphic.FalloffDistance != 1f
            || headerBottomBarGraphic.type != Image.Type.Filled
            || headerBottomBarGraphic.fillMethod != Image.FillMethod.Vertical
            || headerBottomBarGraphic.fillOrigin != (int)Image.OriginVertical.Bottom
            || !Approximately(
                headerBottomBarGraphic.fillAmount,
                RetainedHeaderBottomBarPolicy.VerticalFillAmount)
            || headerBottomBarModifier.Side != OnlyOneEdgeModifier.ProceduralImageEdge.Bottom
            || !Approximately(headerBottomBarModifier.Radius, visualLayout.HeaderBottomBar.CornerRadius)
            || !Approximately(
                headerBottomBarModifier.Radius * canvasScaleFactor,
                RetainedHeaderBottomBarPolicy.CornerRadiusPixels
                * visualLayout.ReferenceTransform.ReferenceScale)
            || !Approximately(
                visualLayout.HeaderBottomBar.Top + visualLayout.HeaderBottomBar.Height,
                visualLayout.Header.Top + visualLayout.Header.Height)
            || !Approximately(
                visualLayout.HeaderBottomBar.ShapingTop + visualLayout.HeaderBottomBar.ShapingHeight,
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
        headerBottomBarRect = null;
        headerBottomBarShapingRect = null;
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
