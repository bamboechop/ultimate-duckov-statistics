using TMPro;
using UnityEngine;

namespace UltimateDuckovStatistics.UI;

internal sealed partial class RetainedStatisticsShell
{
    // Runs only when text/projection or viewport dimensions change. Measure the
    // active native TMP controls at their final widths, including long names.
    private void ReflowOverview(RetainedVisualCanvasLayout layout, float viewportWidth)
    {
        RetainedActiveMeasurementPolicy.Measure(overviewContentView!.activeSelf, overviewContentView.SetActive, () =>
        {
            var scale = layout.ReferenceTransform;
            var rowGap = scale.CanvasLength(10);
            var stacked = viewportWidth < 1100;
            var summaryBottom = 0f;
            var growth = 0f;
            for (var i = 0; i < overviewProfileSummaryRows.Count; i++)
            {
                var control = overviewProfileSummaryRows[i];
                var baseline = layout.OverviewProfileSummaryRows[i].Surface;
                var top = baseline.Top - layout.OverviewLeftPanel.ContentTop + growth;
                control.Rect.anchoredPosition = new Vector2(control.Rect.anchoredPosition.x, -top);
                // The row owns horizontal padding; text uses the inner content rect.
                var inset = baseline.ContentLeft - baseline.Left;
                control.ContentRect.anchoredPosition = new Vector2(inset, 0);
                var height = ReflowOverviewRow(control.Text.Label, control.Text.Value, control.Text.SecondaryValue,
                    baseline.ContentWidth, baseline.Height, scale, stacked);
                control.ContentRect.sizeDelta = new Vector2(baseline.ContentWidth, height);
                control.Rect.sizeDelta = new Vector2(baseline.Width, height);
                growth += height - baseline.Height;
                summaryBottom = top + height;
            }

            growth = 0;
            foreach (var pair in overviewHighlightRows.Select((control, index) => (control, index)))
            {
                var control = pair.control;
                var baseline = layout.OverviewHighlightRows[pair.index];
                var top = baseline.Top - layout.OverviewRightPanel.ContentTop + growth;
                var inset = baseline.ContentLeft - baseline.Left;
                var height = ReflowOverviewRow(control.Label, control.Value, null,
                    baseline.ContentWidth, baseline.Height, scale, stacked, inset);
                control.Rect.anchoredPosition = new Vector2(baseline.Left - layout.OverviewRightPanel.ContentLeft, -top);
                control.Rect.sizeDelta = new Vector2(baseline.Width, height);
                growth += height - baseline.Height;
            }
            // These sections are siblings of the measured highlight rows.
            foreach (var rect in new[] { overviewLatestRunHeadingRect!, overviewLatestRunCardRect!,
                         overviewWorldTimeHeadingRect!, overviewWorldTimeCardRect! })
                rect.anchoredPosition = new Vector2(rect.anchoredPosition.x, rect.anchoredPosition.y - growth);

            if (overviewLatestRunMapName!.Presentation.IsVisible)
            {
                var map = overviewLatestRunMapName.Label;
                var mapGrowth = Math.Max(0, MeasureWrapped(map, map.rectTransform.rect.width) - map.rectTransform.rect.height);
                map.rectTransform.sizeDelta = new Vector2(map.rectTransform.rect.width, map.rectTransform.rect.height + mapGrowth);
                var stats = overviewLatestRunStatistics!.Label;
                var statsGrowth = Math.Max(0, MeasureWrapped(stats, stats.rectTransform.rect.width) - stats.rectTransform.rect.height);
                stats.rectTransform.anchoredPosition = new Vector2(stats.rectTransform.anchoredPosition.x, stats.rectTransform.anchoredPosition.y - mapGrowth);
                stats.rectTransform.sizeDelta = new Vector2(stats.rectTransform.rect.width, stats.rectTransform.rect.height + statsGrowth);
                var button = overviewLatestRunViewRun!.Rect;
                button.anchoredPosition = new Vector2(button.anchoredPosition.x, button.anchoredPosition.y - mapGrowth - statsGrowth);
                overviewLatestRunCardRect!.sizeDelta = new Vector2(overviewLatestRunCardRect.rect.width,
                    overviewLatestRunCardRect.rect.height + mapGrowth + statsGrowth);
            }

            var rightBottom = Math.Max(-overviewLatestRunCardRect!.anchoredPosition.y + overviewLatestRunCardRect.rect.height,
                -overviewWorldTimeCardRect!.anchoredPosition.y + overviewWorldTimeCardRect.rect.height);
            SizeOverviewScroll(overviewSummaryScroll!, overviewLeftPanelContentRect!, layout.OverviewLeftPanel, summaryBottom + rowGap);
            SizeOverviewScroll(overviewHighlightsScroll!, overviewRightPanelContentRect!, layout.OverviewRightPanel, rightBottom + rowGap);
            return true;
        });
    }

    private static float ReflowOverviewRow(TextMeshProUGUI label, TextMeshProUGUI value, TextMeshProUGUI? secondary,
        float width, float minimumHeight, RetainedReferenceTransform scale, bool stacked, float left = 0)
    {
        // 24 reference pixels leave at least 20 clear pixels plus room for the subtle text effect.
        var gap = scale.CanvasLength(24);
        var verticalPadding = scale.CanvasLength(10);
        var labelWidth = stacked ? width : (width - gap) * .44f;
        var valueWidth = stacked ? width : width - labelWidth - gap;
        var labelHeight = MeasureWrapped(label, labelWidth);
        var valueHeight = MeasureWrapped(value, valueWidth);
        var secondaryHeight = secondary == null ? 0 : MeasureWrapped(secondary, valueWidth) + verticalPadding;
        var valueTop = stacked ? verticalPadding + labelHeight + verticalPadding : verticalPadding;
        var valueLeft = stacked ? left : left + labelWidth + gap;
        PlaceOverviewText(label, left, verticalPadding, labelWidth, labelHeight);
        PlaceOverviewText(value, valueLeft, valueTop, valueWidth, valueHeight);
        if (secondary != null)
            PlaceOverviewText(secondary, valueLeft, valueTop + valueHeight + verticalPadding, valueWidth, secondaryHeight - verticalPadding);
        return Math.Max(minimumHeight, Math.Max(verticalPadding + labelHeight, valueTop + valueHeight + secondaryHeight) + verticalPadding);
    }

    private static float MeasureWrapped(TextMeshProUGUI text, float width)
    {
        // TMP's first activation restores native defaults. Apply our row policy after activation.
        text.enableWordWrapping = true;
        text.enableAutoSizing = false;
        text.overflowMode = TextOverflowModes.Overflow;
        text.alignment = TextAlignmentOptions.TopLeft;
        var height = text.GetPreferredValues(width, float.PositiveInfinity).y;
        return float.IsNaN(height) || float.IsInfinity(height) ? text.fontSize * 1.5f : Math.Max(text.fontSize, height);
    }

    private static void PlaceOverviewText(TextMeshProUGUI text, float left, float top, float width, float height)
    {
        text.rectTransform.anchoredPosition = new Vector2(left, -top);
        text.rectTransform.sizeDelta = new Vector2(width, height);
    }

    private static void SizeOverviewScroll(ScrollRegion scroll, RectTransform content,
        RetainedOverviewPanelCanvasLayout panel, float contentHeight)
    {
        var topPadding = panel.ContentTop - panel.Top;
        // The heading's optical offset extends above ContentTop. Include the panel's
        // existing padding inside the clip instead of placing the mask at the glyphs.
        scroll.Size(0, 0, panel.Width, panel.Height, contentHeight + topPadding * 2);
        content.anchoredPosition = new Vector2(panel.ContentLeft - panel.Left, -topPadding);
        content.sizeDelta = new Vector2(panel.ContentWidth, Math.Max(panel.ContentHeight, contentHeight));
    }
}
