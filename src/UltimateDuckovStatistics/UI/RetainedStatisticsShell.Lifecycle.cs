using TMPro;
using UnityEngine;

namespace UltimateDuckovStatistics.UI;

internal sealed partial class RetainedStatisticsShell
{
    private StatisticsPanelProjection? cachedProjection;
    private string cachedGeneration = string.Empty;
    private readonly Dictionary<StatisticsPanelTab, long> boundViews = new();
    private long contentVersion;
    private int contentAfterFrame;
    private readonly List<TextMeshProUGUI> loadingLabels = new();
    private float loadingLayoutUnit = float.NaN;
    private bool loadingLayoutHasContent;
    private float? loadingPendingSince;
    private string? frameCreationError;
    private PanelOperationController? cachedOperations;
    private Action? changeHotkeyAction, cancelHotkeyAction;
    private Func<bool>? copyExportAction, copyDataAction;
    private DiagnosticsPresentation? cachedDiagnostics;
    private (string Generation, string Id)? pendingRunRoute;

    public bool CanReuse(Canvas targetCanvas, string generation) => root != null && canvas == targetCanvas
        && cachedGeneration == generation && targetCanvas != null && targetCanvas.isActiveAndEnabled;

    public void Show(StatisticsPanelTab tab)
    {
        root!.SetActive(true);
        shellRoot!.SetAsLastSibling();
        selectedTab = tab;
        contentAfterFrame = Time.frameCount;
        loadingPendingSince = null;
        EnsureSelectedViewCreated();
        ApplyViewVisibility();
        RefreshStaticText();
        LayoutSelectedView(RefreshVisualLayout(force: false));
        FocusSelectedTab();
    }

    public void Hide()
    {
        if (root == null) return; // The native canvas may already have destroyed its children.
        loadingPendingSince = null;
        overviewDistanceTooltip?.Dismiss();
        modal?.Sync(false, false, string.Empty, string.Empty);
        // SetVisible also closes secondary evidence panels and pauses map work.
        SetViewsVisible(false);
        root?.SetActive(false);
    }

    public void RefreshDiagnostics(DiagnosticsPresentation? snapshot)
    {
        cachedDiagnostics = snapshot;
        if (selectedTab == StatisticsPanelTab.Diagnostics && IsUsable && boundViews.ContainsKey(selectedTab)) diagnosticsView?.Refresh(snapshot);
    }

    private void SetViewsVisible(bool visible)
    {
        overviewContentView?.SetActive(visible && selectedTab == StatisticsPanelTab.Overview);
        runsView?.SetVisible(visible && selectedTab == StatisticsPanelTab.Runs);
        recordsView?.SetVisible(visible && selectedTab == StatisticsPanelTab.Records);
        combatView?.SetVisible(visible && selectedTab == StatisticsPanelTab.Combat);
        equipmentView?.SetVisible(visible && selectedTab == StatisticsPanelTab.Equipment);
        economyView?.SetVisible(visible && selectedTab == StatisticsPanelTab.Economy);
        craftingView?.SetVisible(visible && selectedTab == StatisticsPanelTab.Crafting);
        itemUseView?.SetVisible(visible && selectedTab == StatisticsPanelTab.ItemUse);
        diagnosticsView?.SetVisible(visible && selectedTab == StatisticsPanelTab.Diagnostics);
        aboutView?.SetVisible(visible && selectedTab == StatisticsPanelTab.About);
    }

    private void ApplyViewVisibility()
    {
        foreach (var control in tabControls) control.VisualState.Apply(selectedTab);
        SetViewsVisible(true);
        UpdateLoadingLabel();
    }

    private void UpdateLoadingLabel()
    {
        var pending = selectedTab != StatisticsPanelTab.About
            && (!boundViews.TryGetValue(selectedTab, out var version) || version != contentVersion);
        if (pending) loadingPendingSince ??= Time.unscaledTime;
        else loadingPendingSince = null;
        // Fast loads should go straight to their content. Use unscaled time because
        // the native pause menu can stop gameplay time while the UI remains active.
        var visible = loadingPendingSince is { } since && Time.unscaledTime - since >= .25f;
        foreach (var label in loadingLabels)
        {
            var show = visible && label.transform.parent != shellRoot;
            if (show)
            {
                var text = UiText.Get(boundViews.ContainsKey(selectedTab) ? "ui.refreshing" : "ui.overview_highlights_loading");
                if (label.text != text) label.text = text;
                label.rectTransform.SetAsLastSibling();
            }
            if (label.gameObject.activeSelf != show) label.gameObject.SetActive(show);
        }
    }

    private void AttachLoadingLabels(RectTransform[] containers)
    {
        loadingLayoutUnit = float.NaN;
        while (loadingLabels.Count < containers.Length)
        {
            var index = loadingLabels.Count;
            var go = new GameObject(index == 0 ? "UdsViewLoading" : "UdsViewLoading" + index, typeof(RectTransform));
            go.transform.SetParent(shellRoot, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = overviewTypography!.Font;
            text.fontSharedMaterial = tabLabelMaterial!.Instance;
            text.raycastTarget = false;
            text.color = new Color(.7f, .7f, .7f, 1);
            text.enableWordWrapping = true;
            loadingLabels.Add(text);
        }
        for (var i = 0; i < loadingLabels.Count; i++)
        {
            var label = loadingLabels[i];
            label.gameObject.SetActive(false);
            label.rectTransform.SetParent(i < containers.Length ? containers[i] : shellRoot, false);
        }
        UpdateLoadingLabel();
    }

    private void LayoutLoadingLabels(float scale)
    {
        var unit = selectedTab == StatisticsPanelTab.Overview ? scale : 1;
        var hasContent = boundViews.ContainsKey(selectedTab);
        if (loadingLayoutUnit == unit && loadingLayoutHasContent == hasContent) return;
        loadingLayoutUnit = unit; loadingLayoutHasContent = hasContent;
        foreach (var label in loadingLabels)
        {
            var rect = label.rectTransform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(24 * unit, 24 * unit);
            rect.offsetMax = new Vector2(-24 * unit, -24 * unit);
            label.fontSize = 20 * unit;
            label.alignment = hasContent ? TextAlignmentOptions.BottomRight : TextAlignmentOptions.Center;
        }
    }

    private void BindSelectedView()
    {
        // Allow a frame to paint the shell/selection before any data binding.
        if (!IsUsable || Time.frameCount <= contentAfterFrame || cachedProjection == null
            || !projectionAvailable || boundViews.TryGetValue(selectedTab, out var version) && version == contentVersion) return;
#if UDS_PERFORMANCE_DIAGNOSTICS
        using var timing = UltimateDuckovStatistics.Adapters.NativeHotPathDiagnostics.Measure(
            UltimateDuckovStatistics.Adapters.NativeHotPathArea.PanelViewBind);
#endif
        var p = cachedProjection;
        lastAppliedVisualLayout = null;
        switch (selectedTab)
        {
            case StatisticsPanelTab.Overview:
                RebuildOverview(p, cachedGeneration);
                break;
            case StatisticsPanelTab.Runs:
                RefreshRuns(p, cachedGeneration);
                if (pendingRunRoute is { } route)
                {
                    pendingRunRoute = null;
                    if (route.Generation == cachedGeneration) runsView!.Route(route.Generation, route.Id);
                }
                break;
            case StatisticsPanelTab.Records:
                recordsView!.Refresh(RecordsPresentationFactory.Create(p, cachedGeneration));
                break;
            case StatisticsPanelTab.Combat:
                combatView!.Refresh(CombatPresentationFactory.Create(p, cachedGeneration, isThrowable: NativeThrowableIdentity.IsThrowable));
                break;
            case StatisticsPanelTab.Equipment:
                equipmentView!.Refresh(EquipmentPresentationFactory.Create(p, cachedGeneration));
                break;
            case StatisticsPanelTab.Economy:
                economyView!.Refresh(EconomyPresentationFactory.Create(p, cachedGeneration));
                break;
            case StatisticsPanelTab.Crafting:
                craftingView!.Refresh(CraftingPresentationFactory.Create(p, cachedGeneration));
                break;
            case StatisticsPanelTab.ItemUse:
                itemUseView!.Refresh(ItemUsePresentationFactory.Create(p, cachedGeneration));
                break;
            case StatisticsPanelTab.Diagnostics:
                diagnosticsView!.Refresh(cachedDiagnostics);
                EnsureModal();
                break;
            case StatisticsPanelTab.About:
                aboutView!.RefreshText();
                break;
        }
        boundViews[selectedTab] = contentVersion;
        ApplyViewVisibility();
    }

    private void ResetCachedContent()
    {
        cachedProjection = null;
        cachedGeneration = string.Empty;
        cachedDiagnostics = null;
        cachedOperations = null;
        changeHotkeyAction = cancelHotkeyAction = null;
        copyExportAction = copyDataAction = null;
        pendingRunRoute = null;
        loadingLabels.Clear();
        loadingLayoutUnit = float.NaN;
        loadingPendingSince = null;
        frameCreationError = null;
        boundViews.Clear();
        contentVersion = 0;
    }
}
