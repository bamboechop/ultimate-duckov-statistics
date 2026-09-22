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
    private TextMeshProUGUI? loadingText;
    private float? loadingPendingSince;
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
        ApplyViewVisibility();
        RefreshStaticText();
        RefreshVisualLayout(force: false);
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
        if (selectedTab == StatisticsPanelTab.Diagnostics && IsUsable) diagnosticsView?.Refresh(snapshot);
    }

    private void SetViewsVisible(bool visible)
    {
        overviewContentView?.SetActive(visible && selectedTab == StatisticsPanelTab.Overview && projectionAvailable);
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
        if (loadingText == null) return;
        var pending = !boundViews.TryGetValue(selectedTab, out var version) || version != contentVersion;
        if (pending) loadingPendingSince ??= Time.unscaledTime;
        else loadingPendingSince = null;
        // Fast loads should go straight to their content. Use unscaled time because
        // the native pause menu can stop gameplay time while the UI remains active.
        var visible = loadingPendingSince is { } since && Time.unscaledTime - since >= .25f;
        if (visible)
        {
            var text = UiText.Get(boundViews.ContainsKey(selectedTab) ? "ui.refreshing" : "ui.overview_highlights_loading");
            if (loadingText.text != text) loadingText.text = text;
        }
        if (loadingText.gameObject.activeSelf != visible) loadingText.gameObject.SetActive(visible);
    }

    private void CreateLoadingLabel()
    {
        var go = new GameObject("UdsViewLoading", typeof(RectTransform));
        go.transform.SetParent(shellRoot, false);
        loadingText = go.AddComponent<TextMeshProUGUI>();
        loadingText.font = overviewTypography!.Font;
        loadingText.fontSharedMaterial = overviewTypography.Material;
        loadingText.raycastTarget = false;
        loadingText.color = new Color(.7f, .7f, .7f, 1);
        UpdateLoadingLabel();
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
        var parent = shellRoot!;
        var typography = overviewTypography!;
        var material = tabLabelMaterial!.Instance;
        lastAppliedVisualLayout = null;
        switch (selectedTab)
        {
            case StatisticsPanelTab.Overview:
                RebuildOverview(p, cachedGeneration);
                break;
            case StatisticsPanelTab.Runs:
                runsView ??= new RunsView(parent, typography, material, FocusSelectedTab);
                RefreshRuns(p, cachedGeneration);
                if (pendingRunRoute is { } route)
                {
                    pendingRunRoute = null;
                    if (route.Generation == cachedGeneration) runsView.Route(route.Generation, route.Id);
                }
                break;
            case StatisticsPanelTab.Records:
                recordsView ??= new RecordsView(parent, typography, material, RouteToRun, FocusSelectedTab);
                recordsView.Refresh(RecordsPresentationFactory.Create(p, cachedGeneration));
                break;
            case StatisticsPanelTab.Combat:
                combatView ??= new CombatView(parent, typography, material, FocusSelectedTab);
                combatView.Refresh(CombatPresentationFactory.Create(p, cachedGeneration, isThrowable: NativeThrowableIdentity.IsThrowable));
                break;
            case StatisticsPanelTab.Equipment:
                equipmentView ??= new EquipmentView(parent, typography, material, RouteToRun, FocusSelectedTab);
                equipmentView.Refresh(EquipmentPresentationFactory.Create(p, cachedGeneration));
                break;
            case StatisticsPanelTab.Economy:
                economyView ??= new EconomyView(parent, typography, material, RouteToRun, FocusSelectedTab);
                economyView.Refresh(EconomyPresentationFactory.Create(p, cachedGeneration));
                break;
            case StatisticsPanelTab.Crafting:
                craftingView ??= new CraftingView(parent, typography, material, FocusSelectedTab);
                craftingView.Refresh(CraftingPresentationFactory.Create(p, cachedGeneration));
                break;
            case StatisticsPanelTab.ItemUse:
                itemUseView ??= new ItemUseView(parent, typography, material, RouteToRun, FocusSelectedTab);
                itemUseView.Refresh(ItemUsePresentationFactory.Create(p, cachedGeneration));
                break;
            case StatisticsPanelTab.Diagnostics:
                diagnosticsView ??= new DiagnosticsView(parent, typography, material, cachedOperations!,
                    changeHotkeyAction!, copyExportAction!, copyDataAction!, FocusSelectedTab);
                diagnosticsView.Refresh(cachedDiagnostics);
                EnsureModal();
                break;
            case StatisticsPanelTab.About:
                aboutView ??= new AboutView(parent, typography, material, FocusSelectedTab);
                aboutView.RefreshText();
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
        loadingText = null;
        loadingPendingSince = null;
        boundViews.Clear();
        contentVersion = 0;
    }
}
