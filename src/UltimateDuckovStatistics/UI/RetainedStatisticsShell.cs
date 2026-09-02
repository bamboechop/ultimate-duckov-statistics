using UnityEngine;
using UnityEngine.UI;

namespace UltimateDuckovStatistics.UI;

/// <summary>
/// Owns the blank retained-mode baseline for M17 visual correction Step -1.
/// The single transparent graphic provides modal pointer blocking without
/// contributing visible pixels; later visual layers can be parented to this root.
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
                BlankRetainedShellPolicy.RootName,
                typeof(RectTransform));
            root.hideFlags = HideFlags.DontSave;
            root.SetActive(false);

            var rootRect = (RectTransform)root.transform;
            rootRect.SetParent(targetCanvas.transform, worldPositionStays: false);
            Stretch(rootRect);

            var blocker = root.AddComponent<Image>();
            blocker.color = new Color(0f, 0f, 0f, BlankRetainedShellPolicy.VisualAlpha);
            blocker.raycastTarget = BlankRetainedShellPolicy.BlocksRaycasts;

            ValidateBlankComposition(root, blocker);
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

    private static void ValidateBlankComposition(GameObject shellRoot, Image blocker)
    {
        var graphics = shellRoot.GetComponentsInChildren<Graphic>(includeInactive: true);
        if (!BlankRetainedShellPolicy.IsValidComposition(
                shellRoot.transform.childCount,
                graphics.Length,
                blocker.color.a,
                blocker.raycastTarget)
            || graphics[0] != blocker)
        {
            throw new InvalidOperationException(
                "The Step -1 shell must contain only one transparent raycast blocker and no child hierarchy.");
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
