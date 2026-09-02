using UnityEngine;
using UnityEngine.UI;

namespace UltimateDuckovStatistics.UI;

/// <summary>
/// Owns the exact full-screen dimmer for M17 visual correction Step 0.
/// The single black graphic provides the permitted visual layer and modal pointer
/// blocking; later visual layers can be parented to this root.
/// </summary>
internal sealed class RetainedStatisticsShell : IDisposable
{
    private GameObject? root;
    private Canvas? canvas;
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

            var blocker = root.AddComponent<Image>();
            blocker.color = new Color(
                RetainedDimmerPolicy.Red,
                RetainedDimmerPolicy.Green,
                RetainedDimmerPolicy.Blue,
                RetainedDimmerPolicy.VisualAlpha);
            blocker.raycastTarget = RetainedDimmerPolicy.BlocksRaycasts;

            ValidateDimmerComposition(rootRect, blocker);
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

    private static void ValidateDimmerComposition(RectTransform shellRoot, Image blocker)
    {
        var graphics = shellRoot.GetComponentsInChildren<Graphic>(includeInactive: true);
        if (!RetainedDimmerPolicy.IsValidComposition(
                shellRoot.childCount,
                graphics.Length,
                blocker.color.r,
                blocker.color.g,
                blocker.color.b,
                blocker.color.a,
                blocker.raycastTarget)
            || graphics[0] != blocker)
        {
            throw new InvalidOperationException(
                "The Step 0 shell must contain only one black 50% dimmer and no child hierarchy.");
        }

        if (shellRoot.anchorMin != Vector2.zero
            || shellRoot.anchorMax != Vector2.one
            || shellRoot.offsetMin != Vector2.zero
            || shellRoot.offsetMax != Vector2.zero)
        {
            throw new InvalidOperationException("The Step 0 dimmer must stretch edge to edge with zero offsets.");
        }
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
    }

    public void Dispose() => DestroyRoot();
}
