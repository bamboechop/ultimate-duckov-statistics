using TMPro;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.UI;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Xunit;

namespace UltimateDuckovStatistics.Shell.Tests;

public sealed partial class ShellAccessTests
{
    [Theory]
    [InlineData(2559, 1439, false)]
    [InlineData(2559, 1439, true)]
    [InlineData(1280, 720, true)]
    [InlineData(720, 480, true)]
    public void OverviewReflowsMeasuredEnglishGermanAndLongNamesWithoutLosingPadding(int width, int height, bool populated)
    {
        ((RectTransform)canvas.transform).sizeDelta = new Vector2(width, height);
        if (populated)
            coordinator.Current.Statistics.Runs.Add(new RunSummary
            {
                RunId = "layout-run",
                SaveGenerationId = coordinator.CurrentGenerationId,
                StartingMapId = "duckov:map:Scene_Zero",
                StartingMapKnown = true,
                StartingMapDisplayName = string.Join(" ", Enumerable.Repeat("Very long map name ÄÖÜ", 16)),
                Outcome = RunOutcome.Extracted
            });
        var original = System.Text.Json.JsonSerializer.Serialize(coordinator.Current);
        using var panel = new NativeStatisticsPanel(coordinator);
        Press(panel, KeyCode.F8);
        foreach (var language in new[] { SystemLanguage.English, SystemLanguage.German, SystemLanguage.English })
        {
            SodaCraft.Localizations.LocalizationManager.SetLanguage(language); panel.Tick();
            var scale = width / 2560f;
            foreach (var spec in RetainedProfileSummaryRowsPolicy.Specifications)
                CheckRow(spec.LabelName, spec.ValueName, scale, width < 1100);
            foreach (var spec in RetainedOverviewHighlightsRowsPolicy.Specifications)
                CheckRow(spec.LabelName, spec.ValueName, scale, width < 1100);
            var summary = Find("OverviewSummaryScroll").GetComponent<ScrollRect>();
            var heading = Find(RetainedOverviewProfileSummaryHeadingPolicy.Name).GetComponent<RectTransform>();
            var content = (RectTransform)heading.parent!;
            Assert.True(-content.anchoredPosition.y - heading.anchoredPosition.y >= 10 * scale);
            var highlights = Find(RetainedOverviewHighlightsHeadingPolicy.Name).GetComponent<RectTransform>();
            Assert.Equal(heading.anchoredPosition.y, highlights.anchoredPosition.y, 3);
            Assert.Equal(0, summary.content.anchoredPosition.y);
            var last = Find(RetainedOverviewHighlightsRowsPolicy.Specifications.Last().RowName).GetComponent<RectTransform>();
            var latestHeading = Find(RetainedOverviewLatestRunHeadingPolicy.Name).GetComponent<RectTransform>();
            Assert.True(-latestHeading.anchoredPosition.y >= -last.anchoredPosition.y + last.rect.height);
            Assert.Equal(language == SystemLanguage.German ? "Rekorde" : "Records", UiText.Get("ui.records"));
            Assert.Equal(language == SystemLanguage.German ? "Itemnutzung" : "Item Use", UiText.Get("ui.item_use"));
            Assert.Equal(language == SystemLanguage.German ? "Gesamte Bewegungsdistanz" : "Total movement distance",
                Find("OverviewTotalDistanceTravelledRowLabel").GetComponent<TextMeshProUGUI>().text);
            if (populated)
            {
                var map = Find(RetainedOverviewLatestRunMapNamePolicy.Name).GetComponent<TextMeshProUGUI>();
                Assert.True(map.rectTransform.rect.height >= map.GetPreferredValues(map.rectTransform.rect.width, float.PositiveInfinity).y);
            }
            var measurements = TextMeshProUGUI.Measurements;
            for (var i = 0; i < 10; i++) panel.Tick();
            Assert.Equal(measurements, TextMeshProUGUI.Measurements);
        }
        ((RectTransform)canvas.transform).sizeDelta = new Vector2(960, 540); panel.Tick();
        CheckRow("OverviewFastestExtractionLabel", "OverviewFastestExtractionValue", 960f / 2560, true);
        Assert.Equal(original, System.Text.Json.JsonSerializer.Serialize(coordinator.Current));
    }

    private static void CheckRow(string labelName, string valueName, float scale, bool stacked)
    {
        var label = Find(labelName).GetComponent<TextMeshProUGUI>();
        var value = Find(valueName).GetComponent<TextMeshProUGUI>();
        foreach (var text in new[] { label, value })
        {
            Assert.True(text.enableWordWrapping);
            Assert.False(text.enableAutoSizing);
            Assert.Equal(TextOverflowModes.Overflow, text.overflowMode);
            Assert.True(text.rectTransform.rect.height + .01f >= text.GetPreferredValues(text.rectTransform.rect.width, float.PositiveInfinity).y);
            var parent = (RectTransform)text.rectTransform.parent!;
            Assert.True(text.rectTransform.anchoredPosition.x >= 0);
            Assert.True(text.rectTransform.anchoredPosition.x + text.rectTransform.rect.width <= parent.rect.width + .01f);
            Assert.True(-text.rectTransform.anchoredPosition.y + text.rectTransform.rect.height <= parent.rect.height + .01f);
        }
        if (stacked)
            Assert.True(-value.rectTransform.anchoredPosition.y >= -label.rectTransform.anchoredPosition.y + label.rectTransform.rect.height);
        else
            Assert.True(value.rectTransform.anchoredPosition.x - label.rectTransform.anchoredPosition.x - label.rectTransform.rect.width >= 20 * scale);
    }

    [Fact]
    public void LongHighlightLabelAndItemReflowPushSectionsDownAndKeepRightScrollOnRefresh()
    {
        ((RectTransform)canvas.transform).sizeDelta = new Vector2(2560, 1440);
        using var panel = new NativeStatisticsPanel(coordinator);
        var longName = string.Join(" ", Enumerable.Repeat("Localized item ÄÖÜ", 45));
        UiText.ConfigureNativeResolver(key => key == "ui.overview_most_used_consumable" ? longName : null);
        Press(panel, KeyCode.F8);
        var shell = Field<RetainedStatisticsShell>(panel, "shell");
        var projection = new StatisticsPanelProjection
        {
            Profile = coordinator.Current,
            ItemUse = new ItemUsePanelProjection
            {
                Overall = new AggregateTotals { ActivationCount = 5 },
                Items = new[] { new ItemUseRowProjection { ItemId = "item:long", DisplayName = longName,
                    Totals = new AggregateTotals { ActivationCount = 5 } } }
            }
        };
        shell.RefreshProjection(projection, coordinator.CurrentGenerationId);
        var spec = RetainedOverviewHighlightsRowsPolicy.Specifications.Last();
        CheckRow(spec.LabelName, spec.ValueName, 1, false);
        Assert.StartsWith(longName, Find(spec.ValueName).GetComponent<TextMeshProUGUI>().text);
        var row = Find(spec.RowName).GetComponent<RectTransform>();
        var heading = Find(RetainedOverviewLatestRunHeadingPolicy.Name).GetComponent<RectTransform>();
        Assert.True(-heading.anchoredPosition.y >= -row.anchoredPosition.y + row.rect.height);
        var right = Find("OverviewHighlightsScroll").GetComponent<ScrollRect>();
        Assert.True(right.content.rect.height > ((RectTransform)right.transform).rect.height);
        GameManager.EventSystem!.SetSelectedGameObject(right.gameObject);
        right.GetComponent<RunsFocusHandler>().Move!(MoveDirection.Down);
        var offset = right.content.anchoredPosition.y;
        Assert.True(offset > 0);
        shell.RefreshProjection(projection, coordinator.CurrentGenerationId);
        right = Find("OverviewHighlightsScroll").GetComponent<ScrollRect>();
        Assert.Equal(offset, right.content.anchoredPosition.y);
        Assert.Same(right.gameObject, GameManager.EventSystem.currentSelectedGameObject);
        Assert.Equal(0, Find("HorizontalTabs").GetComponent<RectTransform>().anchoredPosition.x);
    }

    [Fact]
    public void TabChevronsWheelFocusRefreshResizeAndFitUseProductionScrollPath()
    {
        using var panel = new NativeStatisticsPanel(coordinator);
        var tabKeys = RetainedTabStripPolicy.Specifications.Select(spec => spec.TextKey).ToHashSet();
        var longTabs = true;
        UiText.ConfigureNativeResolver(key => tabKeys.Contains(key) ? (longTabs ? new string('W', 38) : "Tab") : null);
        Press(panel, KeyCode.F8);
        var shell = Field<RetainedStatisticsShell>(panel, "shell");
        var scroll = Find("HorizontalTabViewport").GetComponent<RetainedTabsScrollRect>();
        var left = Find("EarlierTabs").GetComponent<Button>();
        var right = Find("LaterTabs").GetComponent<Button>();
        Assert.False(left.interactable); Assert.True(right.interactable);
        Assert.NotNull(scroll.GetComponent<Mask>());
        Assert.False(scroll.GetComponent<Mask>().showMaskGraphic);
        Assert.NotNull(scroll.GetComponent<RectMask2D>());
        var tabRects = RetainedTabStripPolicy.Specifications.Select(spec => Find(spec.BackgroundName).GetComponent<RectTransform>()).ToArray();
        var next = tabRects.First(rect => rect.anchoredPosition.x + rect.rect.width > scroll.viewport.rect.width);
        right.onClick.Invoke();
        Assert.True(-scroll.content.anchoredPosition.x <= next.anchoredPosition.x);
        Assert.True(-scroll.content.anchoredPosition.x + scroll.viewport.rect.width >= next.anchoredPosition.x + next.rect.width - .01f);
        var prior = scroll.content.anchoredPosition.x;
        shell.RefreshProjection(new StatisticsPanelProjection { Profile = coordinator.Current }, coordinator.CurrentGenerationId);
        Assert.Equal(prior, scroll.content.anchoredPosition.x);
        var wheel = new PointerEventData { scrollDelta = new Vector2(0, -1) };
        right.GetComponent<RetainedTabWheelForwarder>().OnScroll(wheel);
        Assert.True(scroll.content.anchoredPosition.x < prior);
        prior = scroll.content.anchoredPosition.x;
        scroll.OnScroll(wheel); Assert.Equal(prior, scroll.content.anchoredPosition.x); // consumed once
        scroll.OnScroll(new PointerEventData { scrollDelta = new Vector2(0, 1) });
        Assert.True(scroll.content.anchoredPosition.x > prior);
        prior = scroll.content.anchoredPosition.x;
        scroll.OnScroll(new PointerEventData { scrollDelta = new Vector2(1, 0) });
        Assert.True(scroll.content.anchoredPosition.x < prior);
        scroll.OnScroll(new PointerEventData { scrollDelta = new Vector2(-1, 0) });
        Assert.Equal(prior, scroll.content.anchoredPosition.x, 3);
        for (var i = 0; i < 30; i++) right.onClick.Invoke();
        Assert.Equal(scroll.viewport.rect.width - scroll.content.rect.width, scroll.content.anchoredPosition.x, 3);
        Assert.False(right.interactable); Assert.True(left.interactable);
        Assert.Equal(StatisticsPanelTab.Overview, shell.SelectedTab);
        for (var i = 0; i < 30; i++) left.onClick.Invoke();
        Assert.Equal(0, scroll.content.anchoredPosition.x);
        Assert.False(left.interactable);
        GameManager.EventSystem!.SetSelectedGameObject(tabRects[^1].gameObject);
        Assert.True(scroll.content.anchoredPosition.x < 0);
        Assert.Equal(StatisticsPanelTab.Overview, shell.SelectedTab);
        ((RectTransform)canvas.transform).sizeDelta = new Vector2(1280, 720); panel.Tick();
        Assert.InRange(-scroll.content.anchoredPosition.x, 0, scroll.content.rect.width - scroll.viewport.rect.width);
        var layout = (RetainedVisualCanvasLayout)typeof(RetainedStatisticsShell).GetField("lastAppliedVisualLayout", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(shell)!;
        var viewport = scroll.viewport;
        Assert.Equal(viewport.anchoredPosition.x - layout.Header.Left,
            layout.Header.Left + layout.Header.Width - viewport.anchoredPosition.x - viewport.rect.width, 3);
        Assert.Equal(layout.HeaderBottomBar.Top, -viewport.anchoredPosition.y + viewport.rect.height, 3);
        longTabs = false; shell.RefreshStaticText(); panel.Tick();
        Assert.False(left.gameObject.activeSelf); Assert.False(right.gameObject.activeSelf);
        Assert.Equal(0, scroll.content.anchoredPosition.x);
        Press(panel, KeyCode.Escape);
        Assert.Equal(0, left.onClick.ListenerCount); Assert.Equal(0, right.onClick.ListenerCount);
        Assert.Null(scroll.ScrollBy);
        Press(panel, KeyCode.F8);
        Assert.Equal(0, Find("HorizontalTabs").GetComponent<RectTransform>().anchoredPosition.x);
    }
}
