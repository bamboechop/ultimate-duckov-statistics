using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UI.ProceduralImage;

namespace UltimateDuckovStatistics.UI;

/// <summary>
/// Owns the exact retained surfaces introduced through M17 visual correction Step 01.
/// The root graphic remains the modal dimmer; the sole child is the rounded header backdrop.
/// </summary>
internal sealed class RetainedStatisticsShell : IDisposable
{
    private GameObject? root;
    private Canvas? canvas;
    private RectTransform? shellRoot;
    private RectTransform? headerRect;
    private UniformModifier? headerModifier;
    private RetainedHeaderCanvasLayout? lastAppliedHeaderLayout;
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
        out string? error)
    {
        if (targetCanvas == null) throw new ArgumentNullException(nameof(targetCanvas));
        if (!PanelInteractionState.NavigationOrder.Contains(initialTab))
            throw new ArgumentOutOfRangeException(nameof(initialTab));

        error = null;
        if (root != null) return true;

        try
        {
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
            var headerLayout = RefreshHeaderLayout(force: true);

            ValidateSurfaceComposition(
                rootRect,
                blocker,
                headerRect,
                headerGraphic,
                createdHeaderModifier,
                headerLayout,
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
            RefreshHeaderLayout(force: false);
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

    private RetainedHeaderCanvasLayout RefreshHeaderLayout(bool force)
    {
        if (canvas == null || shellRoot == null || headerRect == null || headerModifier == null)
            throw new InvalidOperationException("The retained header layout is not fully initialized.");

        var canvasScaleFactor = canvas.scaleFactor;
        var viewportPixelWidth = shellRoot.rect.width * canvasScaleFactor;
        var viewportPixelHeight = shellRoot.rect.height * canvasScaleFactor;
        if (!force
            && viewportPixelWidth == lastViewportPixelWidth
            && viewportPixelHeight == lastViewportPixelHeight
            && canvasScaleFactor == lastCanvasScaleFactor
            && lastAppliedHeaderLayout != null)
        {
            return lastAppliedHeaderLayout;
        }

        var layout = RetainedHeaderPolicy.CreateCanvasLayout(
            viewportPixelWidth,
            viewportPixelHeight,
            canvasScaleFactor);
        headerRect.anchoredPosition = new Vector2(layout.Left, -layout.Top);
        headerRect.sizeDelta = new Vector2(layout.Width, layout.Height);
        headerModifier.Radius = layout.CornerRadius;
        lastViewportPixelWidth = viewportPixelWidth;
        lastViewportPixelHeight = viewportPixelHeight;
        lastCanvasScaleFactor = canvasScaleFactor;
        lastAppliedHeaderLayout = layout;
        return layout;
    }

    private static void ValidateSurfaceComposition(
        RectTransform shellRoot,
        Image blocker,
        RectTransform headerRect,
        ProceduralImage headerGraphic,
        UniformModifier headerModifier,
        RetainedHeaderCanvasLayout headerLayout,
        float canvasScaleFactor)
    {
        var graphics = shellRoot.GetComponentsInChildren<Graphic>(includeInactive: true);
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
            || shellRoot.childCount != RetainedHeaderPolicy.RootChildCount
            || headerRect.parent != shellRoot
            || headerRect.childCount != RetainedHeaderPolicy.HeaderChildCount
            || graphics.Length != RetainedHeaderPolicy.GraphicCount
            || !graphics.Contains(blocker)
            || !graphics.Contains(headerGraphic))
        {
            throw new InvalidOperationException(
                "The Step 01 shell must contain only the frozen dimmer and one HeaderBackground graphic.");
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
            || !Approximately(headerRect.anchoredPosition.x, headerLayout.Left)
            || !Approximately(headerRect.anchoredPosition.y, -headerLayout.Top)
            || !Approximately(headerRect.sizeDelta.x, headerLayout.Width)
            || !Approximately(headerRect.sizeDelta.y, headerLayout.Height)
            || !Approximately(headerModifier.Radius, headerLayout.CornerRadius)
            || !Approximately(
                headerModifier.Radius * canvasScaleFactor,
                RetainedHeaderPolicy.CornerRadiusPixels * headerLayout.ReferenceScale))
        {
            throw new InvalidOperationException(
                "HeaderBackground did not retain its exact top-left pixel geometry or rounded-corner radius.");
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
        lastAppliedHeaderLayout = null;
        lastViewportPixelWidth = float.NaN;
        lastViewportPixelHeight = float.NaN;
        lastCanvasScaleFactor = float.NaN;
    }

    public void Dispose() => DestroyRoot();
}
