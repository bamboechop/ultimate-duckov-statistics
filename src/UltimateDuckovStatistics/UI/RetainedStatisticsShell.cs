using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UI.ProceduralImage;

namespace UltimateDuckovStatistics.UI;

/// <summary>
/// Owns the exact retained surfaces introduced through M17 visual correction Step 02.
/// The root graphic remains the modal dimmer; its children are the frozen header and native-arrow back control.
/// </summary>
internal sealed class RetainedStatisticsShell : IDisposable
{
    private GameObject? root;
    private Canvas? canvas;
    private RectTransform? shellRoot;
    private RectTransform? headerRect;
    private UniformModifier? headerModifier;
    private RectTransform? backButtonRect;
    private UniformModifier? backButtonModifier;
    private RectTransform? backArrowRect;
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
            if (!NativeBackArrowResolver.TryResolve(
                    targetCanvas,
                    out var nativeBackArrow,
                    out _,
                    out var resolutionError)
                || nativeBackArrow == null)
            {
                error = resolutionError ?? "Duckov's audited native back-arrow presentation was unavailable.";
                return false;
            }

            canvas = targetCanvas;
            selectedTab = initialTab;
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
            backButtonRect = CreateBackControl(
                rootRect,
                nativeBackArrow,
                close,
                out var backButtonGraphic,
                out var createdBackButtonModifier,
                out var backButton,
                out var createdBackArrowRect,
                out var backArrowGraphic);
            backButtonModifier = createdBackButtonModifier;
            backArrowRect = createdBackArrowRect;
            var visualLayout = RefreshVisualLayout(force: true);

            ValidateSurfaceComposition(
                rootRect,
                blocker,
                headerRect,
                headerGraphic,
                createdHeaderModifier,
                backButtonRect,
                backButtonGraphic,
                createdBackButtonModifier,
                backButton,
                createdBackArrowRect,
                backArrowGraphic,
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

    private static RectTransform CreateBackControl(
        RectTransform parent,
        Sprite nativeBackArrow,
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
        arrowGraphic.sprite = nativeBackArrow;
        arrowGraphic.overrideSprite = nativeBackArrow;
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

    private RetainedVisualCanvasLayout RefreshVisualLayout(bool force)
    {
        if (canvas == null || shellRoot == null || headerRect == null || headerModifier == null
            || backButtonRect == null || backButtonModifier == null || backArrowRect == null)
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
        backButtonRect.anchoredPosition = new Vector2(layout.BackControl.Left, -layout.BackControl.Top);
        backButtonRect.sizeDelta = new Vector2(layout.BackControl.Width, layout.BackControl.Height);
        backButtonModifier.Radius = layout.BackControl.CornerRadius;
        backArrowRect.anchoredPosition = new Vector2(
            layout.BackControl.ArrowLeft - layout.BackControl.Left,
            -(layout.BackControl.ArrowTop - layout.BackControl.Top));
        backArrowRect.sizeDelta = new Vector2(
            layout.BackControl.ArrowWidth,
            layout.BackControl.ArrowHeight);
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
        RectTransform backButtonRect,
        ProceduralImage backButtonGraphic,
        UniformModifier backButtonModifier,
        Button backButton,
        RectTransform backArrowRect,
        Image backArrowGraphic,
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
            || backButtonRect.parent != shellRoot
            || backButtonRect.childCount != RetainedShellCompositionPolicy.BackButtonChildCount
            || backArrowRect.parent != backButtonRect
            || backArrowRect.childCount != RetainedShellCompositionPolicy.BackArrowChildCount
            || graphics.Length != RetainedShellCompositionPolicy.GraphicCount
            || buttons.Length != 1
            || buttons[0] != backButton
            || backButton.targetGraphic != backButtonGraphic
            || backButton.transition != Selectable.Transition.None
            || backButton.onClick.GetPersistentEventCount() != 0
            || !ReferenceEquals(visualLayout.ReferenceTransform, visualLayout.Header.ReferenceTransform)
            || !ReferenceEquals(visualLayout.ReferenceTransform, visualLayout.BackControl.ReferenceTransform)
            || !graphics.Contains(blocker)
            || !graphics.Contains(headerGraphic)
            || !graphics.Contains(backButtonGraphic)
            || !graphics.Contains(backArrowGraphic))
        {
            throw new InvalidOperationException(
                "The Step 02 shell must contain only the frozen dimmer, HeaderBackground, BackButton, and BackArrow.");
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
            || !NativeBackArrowPolicy.IsExpectedSpriteName(backArrowGraphic.sprite.name))
        {
            throw new InvalidOperationException(
                "BackButton did not retain its exact reference geometry, circular background, or native arrow.");
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
        backButtonRect = null;
        backButtonModifier = null;
        backArrowRect = null;
        lastAppliedVisualLayout = null;
        lastViewportPixelWidth = float.NaN;
        lastViewportPixelHeight = float.NaN;
        lastCanvasScaleFactor = float.NaN;
    }

    public void Dispose() => DestroyRoot();
}
