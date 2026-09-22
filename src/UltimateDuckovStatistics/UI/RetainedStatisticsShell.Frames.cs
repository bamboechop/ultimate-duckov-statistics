using UnityEngine;

namespace UltimateDuckovStatistics.UI;

internal sealed partial class RetainedStatisticsShell
{
    // Construct the selected view's persistent frame before projecting or binding data.
    // The normal Layout methods also handle this initial, unbound state.
    private void EnsureSelectedViewCreated()
    {
        var parent = shellRoot!;
        var typography = overviewTypography!;
        var material = tabLabelMaterial!.Instance;
        RectTransform[] containers;
        switch (selectedTab)
        {
            case StatisticsPanelTab.Overview:
                EnsureOverviewFrame();
                containers = new[] { overviewLeftPanelRect!, overviewRightPanelRect! };
                break;
            case StatisticsPanelTab.Runs:
                runsView ??= new RunsView(parent, typography, material, FocusSelectedTab);
                containers = runsView.LoadingContainers;
                break;
            case StatisticsPanelTab.Records:
                recordsView ??= new RecordsView(parent, typography, material, RouteToRun, FocusSelectedTab);
                containers = recordsView.LoadingContainers;
                break;
            case StatisticsPanelTab.Combat:
                combatView ??= new CombatView(parent, typography, material, FocusSelectedTab);
                containers = combatView.LoadingContainers;
                break;
            case StatisticsPanelTab.Equipment:
                equipmentView ??= new EquipmentView(parent, typography, material, RouteToRun, FocusSelectedTab);
                containers = equipmentView.LoadingContainers;
                break;
            case StatisticsPanelTab.Economy:
                economyView ??= new EconomyView(parent, typography, material, RouteToRun, FocusSelectedTab);
                containers = economyView.LoadingContainers;
                break;
            case StatisticsPanelTab.Crafting:
                craftingView ??= new CraftingView(parent, typography, material, FocusSelectedTab);
                containers = craftingView.LoadingContainers;
                break;
            case StatisticsPanelTab.ItemUse:
                itemUseView ??= new ItemUseView(parent, typography, material, RouteToRun, FocusSelectedTab);
                containers = itemUseView.LoadingContainers;
                break;
            case StatisticsPanelTab.Diagnostics:
                diagnosticsView ??= new DiagnosticsView(parent, typography, material, cachedOperations!,
                    changeHotkeyAction!, copyExportAction!, copyDataAction!, FocusSelectedTab);
                containers = diagnosticsView.LoadingContainers;
                break;
            case StatisticsPanelTab.About:
                aboutView ??= new AboutView(parent, typography, material, FocusSelectedTab);
                containers = Array.Empty<RectTransform>(); // Static content needs no data load.
                break;
            default: throw new ArgumentOutOfRangeException(nameof(selectedTab));
        }
        AttachLoadingLabels(containers);
    }

    private void EnsureOverviewFrame()
    {
        if (overviewContentView != null) return;
        overviewContentView = new GameObject(RetainedOverviewLeftPanelPolicy.ViewName, typeof(RectTransform));
        var rect = (RectTransform)overviewContentView.transform;
        rect.SetParent(shellRoot, false);
        Stretch(rect);
        overviewLeftPanelRect = CreateOverviewPanel(rect, RetainedOverviewLeftPanelPolicy.BackgroundName, out var left);
        overviewLeftPanelModifier = left;
        overviewRightPanelRect = CreateOverviewPanel(rect, RetainedOverviewRightPanelPolicy.BackgroundName, out var right);
        overviewRightPanelModifier = right;
        rect.SetAsFirstSibling();
    }

    private void LayoutSelectedView(RetainedVisualCanvasLayout layout)
    {
        var height = shellRoot!.rect.height;
        switch (selectedTab)
        {
            case StatisticsPanelTab.Runs: runsView?.Layout(layout, lastViewportPixelWidth, height); break;
            case StatisticsPanelTab.Records: recordsView?.Layout(layout, height); break;
            case StatisticsPanelTab.Combat: combatView?.Layout(layout, lastViewportPixelWidth, height); break;
            case StatisticsPanelTab.Equipment: equipmentView?.Layout(layout, lastViewportPixelWidth, height); break;
            case StatisticsPanelTab.Economy: economyView?.Layout(layout, lastViewportPixelWidth, height); break;
            case StatisticsPanelTab.Crafting: craftingView?.Layout(layout, lastViewportPixelWidth, height); break;
            case StatisticsPanelTab.ItemUse: itemUseView?.Layout(layout, lastViewportPixelWidth, height); break;
            case StatisticsPanelTab.Diagnostics: diagnosticsView?.Layout(layout, lastViewportPixelWidth, height); break;
            case StatisticsPanelTab.About: aboutView?.Layout(layout, height); break;
        }
        LayoutLoadingLabels(layout.ReferenceTransform.CanvasLength(1));
    }

    private static void LayoutPendingColumns(ScrollRegion outer, RectTransform first, RectTransform second,
        float width, float height, bool stacked, float firstWidth, float secondWidth, float firstHeight = 0)
    {
        var h = stacked ? (firstHeight > 0 ? firstHeight : Math.Max(160, (height - 40) / 2)) : height;
        var y = stacked ? h + 40 : 0;
        PlaceFrame(first, 0, 0, firstWidth, h);
        PlaceFrame(second, stacked ? 0 : firstWidth + 40, y, secondWidth, stacked ? Math.Max(160, height - y) : height);
        outer.Size(0, 0, width, height, Math.Max(h, y + second.rect.height));
        outer.Scroll.vertical = stacked;
    }

    private static void PlaceFrame(RectTransform rect, float x, float y, float width, float height)
    {
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(Math.Max(1, width), Math.Max(1, height));
    }
}
