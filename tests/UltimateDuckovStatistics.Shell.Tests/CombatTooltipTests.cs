using TMPro;
using UltimateDuckovStatistics.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Xunit;

namespace UltimateDuckovStatistics.Shell.Tests;

public sealed class CombatTooltipTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HoverRendersInsideStatisticsWithoutGameplayTooltipAndNeverInterceptsPointer(bool initiallyVisible)
    {
        var root = Root();
        root.gameObject.SetActive(initiallyVisible);
        using var tooltip = new CombatTooltip(root, new TMP_FontAsset(), new Material());
        var row = Target(root, tooltip, "Effects / damage-over-time kills");
        var panel = root.GetComponentsInChildren<TextMeshProUGUI>(true).Single().transform.parent.gameObject;
        Assert.False(panel.activeSelf);
        root.gameObject.SetActive(true);
        ((IPointerEnterHandler)row).OnPointerEnter(new PointerEventData { position = new Vector2(780, -570) });
        Assert.True(panel.activeInHierarchy);
        Assert.Same(root, panel.transform.parent);
        Assert.Equal(root.childCount - 1, panel.transform.GetSiblingIndex());
        Assert.Equal(row.Text, panel.GetComponentsInChildren<TextMeshProUGUI>().Single().text);
        Assert.All(panel.GetComponentsInChildren<Graphic>(), graphic => Assert.False(graphic.raycastTarget));
        var bounds = (RectTransform)panel.transform;
        Assert.InRange(bounds.anchoredPosition.x, 0, root.rect.width - bounds.rect.width);
        Assert.InRange(-bounds.anchoredPosition.y, 0, root.rect.height - bounds.rect.height);
        ((IPointerExitHandler)row).OnPointerExit(new PointerEventData());
        Assert.False(panel.activeSelf);
        tooltip.Dispose(); UnityEngine.Object.Destroy(root.gameObject);
    }

    [Fact]
    public void RebindingHideAndDisposalCannotLeaveAStaleTooltip()
    {
        var root = Root();
        var tooltip = new CombatTooltip(root, new TMP_FontAsset(), new Material());
        var first = Target(root, tooltip, "First"); var second = Target(root, tooltip, "Second");
        var panel = root.GetComponentsInChildren<TextMeshProUGUI>(true).Single().transform.parent.gameObject;
        first.OnPointerEnter(new PointerEventData()); second.OnPointerEnter(new PointerEventData());
        first.OnPointerExit(new PointerEventData());
        Assert.True(panel.activeSelf); // A recycled old row cannot hide the current row's popup.
        second.Bind(tooltip, "Second"); Assert.True(panel.activeSelf); // A normal publication must not dismiss unchanged hover text.
        second.Bind(tooltip, "Replacement"); Assert.False(panel.activeSelf);
        second.OnPointerEnter(new PointerEventData()); Assert.True(panel.activeSelf);
        second.OnDisable(); Assert.False(panel.activeSelf);
        second.OnPointerEnter(new PointerEventData()); tooltip.Dismiss(); Assert.False(panel.activeSelf);
        second.Bind(null, ""); second.OnPointerEnter(new PointerEventData()); Assert.False(panel.activeSelf);
        first.OnPointerEnter(new PointerEventData()); tooltip.Dispose(); tooltip.Dispose();
        Assert.True(panel.Destroyed);
        first.OnPointerEnter(new PointerEventData()); Assert.False(panel.activeSelf);
        UnityEngine.Object.Destroy(root.gameObject);
    }

    private static RectTransform Root()
    {
        var root = (RectTransform)new GameObject("Statistics menu canvas", typeof(Canvas)).transform;
        root.pivot = new Vector2(0, 1); root.sizeDelta = new Vector2(800, 600);
        return root;
    }
    private static CombatTooltipTrigger Target(RectTransform parent, CombatTooltip tooltip, string text)
    {
        var row = new GameObject("Read-only metric"); row.transform.SetParent(parent);
        row.AddComponent<Image>().raycastTarget = true;
        var target = row.AddComponent<CombatTooltipTrigger>(); target.Bind(tooltip, text);
        return target;
    }
}
