using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace UltimateDuckovStatistics.UI;

// Own the wheel event once. Native ScrollRect reverses Y, then maps it to X,
// which moves horizontal content right when the wheel points down.
internal sealed class RetainedTabsScrollRect : ScrollRect
{
    public Action<float>? ScrollBy;
    public override void OnScroll(PointerEventData data)
    {
        if (data.used || !isActiveAndEnabled) return;
        var delta = data.scrollDelta;
        ScrollBy?.Invoke((Math.Abs(delta.x) > Math.Abs(delta.y) ? delta.x : -delta.y) * scrollSensitivity);
        data.Use();
    }
}

internal sealed class RetainedTabWheelForwarder : MonoBehaviour, IScrollHandler
{
    public Action<PointerEventData>? Forward;
    public void OnScroll(PointerEventData data) => Forward?.Invoke(data);
}

internal sealed partial class RetainedStatisticsShell
{
    private ScrollRect? tabScroll;
    private RectTransform? tabViewport;
    private RectTransform? tabContent;
    private Button? earlierTabs, laterTabs;
    private TMPro.TextMeshProUGUI? earlierTabsLabel, laterTabsLabel;
    private float[]? measuredTabWidths;

    private void CreateScrollableTabs(RectTransform parent)
    {
        tabViewport = (RectTransform)new GameObject("HorizontalTabViewport", typeof(RectTransform)).transform;
        tabViewport.SetParent(parent, false);
        tabViewport.anchorMin = tabViewport.anchorMax = tabViewport.pivot = new Vector2(0, 1);
        tabViewport.gameObject.AddComponent<RectMask2D>();
        // Stencil clipping also clips the ProceduralImage shader and TMP effects.
        // Mask supplies a raycast filter, so cropped button portions cannot receive clicks.
        var hit = tabViewport.gameObject.AddComponent<Image>(); hit.color = Color.white; hit.raycastTarget = true;
        tabViewport.gameObject.AddComponent<Mask>().showMaskGraphic = false;
        tabContent = (RectTransform)new GameObject("HorizontalTabs", typeof(RectTransform)).transform;
        tabContent.SetParent(tabViewport, false);
        tabContent.anchorMin = tabContent.anchorMax = tabContent.pivot = new Vector2(0, 1);
        var wheel = tabViewport.gameObject.AddComponent<RetainedTabsScrollRect>();
        wheel.ScrollBy = amount => SetTabOffset(-tabContent.anchoredPosition.x + amount);
        tabScroll = wheel;
        tabScroll.viewport = tabViewport; tabScroll.content = tabContent;
        tabScroll.horizontal = true; tabScroll.vertical = false;
        tabScroll.movementType = ScrollRect.MovementType.Clamped; tabScroll.scrollSensitivity = 40;
        tabScroll.inertia = false;
        tabScroll.onValueChanged.AddListener(_ => UpdateTabScrollControls());
        tabListenerLease!.Register(tabScroll.onValueChanged.RemoveAllListeners);
        tabListenerLease.Register(() => wheel.ScrollBy = null);
        earlierTabs = CreateTabScrollControl(parent, "EarlierTabs", "‹", -1, out earlierTabsLabel);
        laterTabs = CreateTabScrollControl(parent, "LaterTabs", "›", 1, out laterTabsLabel);
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

    private Button CreateTabScrollControl(RectTransform parent, string name, string glyph, int direction, out TMPro.TextMeshProUGUI label)
    {
        var rect = (RectTransform)new GameObject(name, typeof(RectTransform)).transform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
        var hit = rect.gameObject.AddComponent<Image>();
        hit.color = new Color(.06f, .22f, .3f, 1f); hit.raycastTarget = true;
        var button = rect.gameObject.AddComponent<Button>(); button.targetGraphic = hit;
        button.navigation = new Navigation { mode = Navigation.Mode.None };
        button.onClick.AddListener(() => RevealObscuredTab(direction));
        tabListenerLease!.Register(button.onClick.RemoveAllListeners);
        var wheel = rect.gameObject.AddComponent<RetainedTabWheelForwarder>();
        wheel.Forward = tabScroll!.OnScroll;
        tabListenerLease.Register(() => wheel.Forward = null);
        CreateStatisticsRowText(rect, name + "Label", glyph, overviewTypography!, tabLabelMaterial!.Instance, out label);
        label.alignment = TMPro.TextAlignmentOptions.Center;
        label.raycastTarget = false;
        return button;
    }

    private void LayoutScrollableTabs(RetainedVisualCanvasLayout layout)
    {
        if (tabViewport == null || tabContent == null) return;
        var first = layout.TabStrip.Tabs[0];
        var last = layout.TabStrip.Tabs[layout.TabStrip.Tabs.Count - 1];
        var inset = first.Left - layout.Header.Left;
        var available = Math.Max(1, layout.Header.Width - 2 * inset);
        var contentWidth = last.Left + last.Width - first.Left;
        var overflow = contentWidth > available;
        var gutter = overflow ? layout.ReferenceTransform.CanvasLength(48) : 0;
        // End precisely at the underline's top, not the taller button rectangle.
        var height = layout.HeaderBottomBar.Top - first.Top;
        tabViewport.anchoredPosition = new Vector2(first.Left + gutter, -first.Top);
        tabViewport.sizeDelta = new Vector2(Math.Max(1, available - 2 * gutter), height);
        tabContent.sizeDelta = new Vector2(Math.Max(tabViewport.rect.width, last.Left + last.Width - first.Left), first.Height);
        for (var i = 0; i < tabControls.Count; i++)
        {
            var tab = layout.TabStrip.Tabs[i];
            tabControls[i].Rect.anchoredPosition = new Vector2(tab.Left - first.Left, -(tab.Top - first.Top));
        }
        foreach (var button in new[] { earlierTabs!, laterTabs! })
        {
            button.gameObject.SetActive(overflow);
            var rect = (RectTransform)button.transform;
            rect.anchoredPosition = new Vector2(button == earlierTabs ? first.Left : first.Left + available - gutter, -first.Top);
            rect.sizeDelta = new Vector2(gutter, height);
            var label = button == earlierTabs ? earlierTabsLabel! : laterTabsLabel!;
            Stretch(label.rectTransform); label.fontSize = first.FontSize;
        }
        tabScroll!.scrollSensitivity = layout.ReferenceTransform.CanvasLength(80);
        // Measurement/refresh is not a request to reveal the selection. Keep manual browsing.
        SetTabOffset(-tabContent.anchoredPosition.x);
    }

    private void SetTabOffset(float offset)
    {
        if (tabViewport == null || tabContent == null) return;
        tabScroll?.StopMovement();
        offset = Math.Clamp(offset, 0, Math.Max(0, tabContent.rect.width - tabViewport.rect.width));
        tabContent.anchoredPosition = new Vector2(-offset, 0);
        UpdateTabScrollControls();
    }

    private void UpdateTabScrollControls()
    {
        if (tabViewport == null || tabContent == null || earlierTabs == null || laterTabs == null) return;
        var offset = -tabContent.anchoredPosition.x;
        earlierTabs.interactable = offset > .01f;
        laterTabs.interactable = offset < tabContent.rect.width - tabViewport.rect.width - .01f;
        earlierTabsLabel!.color = earlierTabs.interactable ? Color.white : new Color(1, 1, 1, .3f);
        laterTabsLabel!.color = laterTabs.interactable ? Color.white : new Color(1, 1, 1, .3f);
    }

    private void RevealObscuredTab(int direction)
    {
        if (tabViewport == null || tabContent == null) return;
        var offset = -tabContent.anchoredPosition.x;
        var width = tabViewport.rect.width;
        var controls = direction > 0 ? tabControls.AsEnumerable() : tabControls.AsEnumerable().Reverse();
        foreach (var control in controls)
        {
            var left = control.Rect.anchoredPosition.x;
            var right = left + control.Rect.rect.width;
            if (direction > 0 ? right <= offset + width + .01f : left >= offset - .01f) continue;
            // Oversized translations advance a viewport; ordinary tabs are revealed in full.
            var fits = control.Rect.rect.width <= width;
            SetTabOffset(direction > 0 ? (fits ? right - width : Math.Min(right - width, offset + width))
                : (fits ? left : Math.Max(left, offset - width)));
            return;
        }
    }

    private void EnsureSelectedTabVisible() => EnsureTabVisible(tabControls.FindIndex(control => control.Specification.Tab == selectedTab));
    private void EnsureTabVisible(int index)
    {
        if (tabViewport == null || tabContent == null || index < 0 || index >= tabControls.Count) return;
        var control = tabControls[index];
        if (!RuntimeTabStripScrollPolicy.TryEnsureVisible(tabViewport.rect.width, tabContent.rect.width,
                control.Rect.anchoredPosition.x, control.Rect.rect.width, -tabContent.anchoredPosition.x, out var offset)) return;
        SetTabOffset(offset);
    }
}
