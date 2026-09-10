using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UltimateDuckovStatistics.UI;

internal sealed partial class RetainedStatisticsShell
{
    private ScrollRect? tabScroll;
    private RectTransform? tabViewport;
    private RectTransform? tabContent;

    private void CreateScrollableTabs(RectTransform parent)
    {
        tabViewport = (RectTransform)new GameObject("HorizontalTabViewport", typeof(RectTransform)).transform;
        tabViewport.SetParent(parent, false);
        tabViewport.anchorMin = tabViewport.anchorMax = tabViewport.pivot = new Vector2(0, 1);
        tabViewport.gameObject.AddComponent<RectMask2D>();
        var hit = tabViewport.gameObject.AddComponent<Image>(); hit.color = Color.clear;
        tabContent = (RectTransform)new GameObject("HorizontalTabs", typeof(RectTransform)).transform;
        tabContent.SetParent(tabViewport, false);
        tabContent.anchorMin = tabContent.anchorMax = tabContent.pivot = new Vector2(0, 1);
        tabScroll = tabViewport.gameObject.AddComponent<ScrollRect>();
        tabScroll.viewport = tabViewport; tabScroll.content = tabContent;
        tabScroll.horizontal = true; tabScroll.vertical = false;
        tabScroll.movementType = ScrollRect.MovementType.Clamped; tabScroll.scrollSensitivity = 40;
        for (var index = 0; index < tabControls.Count; index++)
        {
            var control = tabControls[index]; var tabIndex = index;
            control.Rect.SetParent(tabContent, false);
            control.Button.navigation = new Navigation { mode = Navigation.Mode.None };
            var focus = control.Button.gameObject.AddComponent<RunsFocusHandler>();
            focus.Selected = () => EnsureTabVisible(tabIndex);
            focus.Move = direction =>
            {
                if (direction == MoveDirection.Down)
                {
                    if (selectedTab == StatisticsPanelTab.Runs) runsView?.FocusHistory();
                    else if (selectedTab == StatisticsPanelTab.Records) recordsView?.FocusPage();
                    else if (selectedTab == StatisticsPanelTab.Combat) combatView?.FocusSelector();
                    else if (selectedTab == StatisticsPanelTab.Equipment) equipmentView?.FocusSelector();
                    else if (selectedTab == StatisticsPanelTab.Economy) economyView?.FocusPage();
                    else if (selectedTab == StatisticsPanelTab.Crafting) craftingView?.FocusFirst();
                    else if (selectedTab == StatisticsPanelTab.ItemUse) itemUseView?.FocusPage();
                    else if (selectedTab == StatisticsPanelTab.About) aboutView?.FocusFirst();
                    else if (selectedTab == StatisticsPanelTab.Diagnostics) diagnosticsView?.FocusFirst();
                    else if (selectedTab == StatisticsPanelTab.Overview && overviewSummaryScroll != null)
                        GameManager.EventSystem?.SetSelectedGameObject(overviewSummaryScroll.Rect.gameObject);
                    return;
                }
                if (direction != MoveDirection.Left && direction != MoveDirection.Right) return;
                var next = Math.Clamp(tabIndex + (direction == MoveDirection.Left ? -1 : 1), 0, tabControls.Count - 1);
                GameManager.EventSystem?.SetSelectedGameObject(tabControls[next].Button.gameObject);
            };
            control.Button.gameObject.AddComponent<RunsButtonFeedback>();
        }
    }

    private void LayoutScrollableTabs(RetainedVisualCanvasLayout layout)
    {
        if (tabViewport == null || tabContent == null) return;
        var first = layout.TabStrip.Tabs[0];
        var last = layout.TabStrip.Tabs[layout.TabStrip.Tabs.Count - 1];
        tabViewport.anchoredPosition = new Vector2(first.Left, -first.Top);
        tabViewport.sizeDelta = new Vector2(Math.Max(1, layout.Header.Left + layout.Header.Width - first.Left), first.Height);
        tabContent.sizeDelta = new Vector2(Math.Max(tabViewport.rect.width, last.Left + last.Width - first.Left), first.Height);
        for (var i = 0; i < tabControls.Count; i++)
        {
            var tab = layout.TabStrip.Tabs[i];
            tabControls[i].Rect.anchoredPosition = new Vector2(tab.Left - first.Left, -(tab.Top - first.Top));
        }
        var focused = GameManager.EventSystem?.currentSelectedGameObject;
        var focusedIndex = tabControls.FindIndex(control => control.Button.gameObject == focused);
        if (focusedIndex >= 0) EnsureTabVisible(focusedIndex); else EnsureSelectedTabVisible();
    }

    private void EnsureSelectedTabVisible() => EnsureTabVisible(tabControls.FindIndex(control => control.Specification.Tab == selectedTab));
    private void EnsureTabVisible(int index)
    {
        if (tabViewport == null || tabContent == null || index < 0 || index >= tabControls.Count) return;
        var control = tabControls[index];
        if (!RuntimeTabStripScrollPolicy.TryEnsureVisible(tabViewport.rect.width, tabContent.rect.width,
                control.Rect.anchoredPosition.x, control.Rect.rect.width, -tabContent.anchoredPosition.x, out var offset)) return;
        tabScroll?.StopMovement(); tabContent.anchoredPosition = new Vector2(-offset, 0);
    }
}
