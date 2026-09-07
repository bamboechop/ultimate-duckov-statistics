using UltimateDuckovStatistics.UI;

namespace UltimateDuckovStatistics.Tests;

public sealed class OverviewRefreshTests
{
    [Theory]
    [InlineData(1280, 720)]
    [InlineData(1680, 1050)]
    [InlineData(2560, 1440)]
    [InlineData(1024, 768)]
    public void HighlightEllipsisHasRoomForNativeLineHeightAndPreservesCenter(float width, float height)
    {
        var layout = RetainedVisualLayoutPolicy.Create(width, height, 1);
        for (var i = 0; i < layout.OverviewHighlightRows.Count; i++)
        {
            var row = layout.OverviewHighlightRows[i];
            var entry = layout.OverviewHighlightEntries[i];
            Assert.Equal(row.Height, entry.LabelHeight);
            Assert.Equal(row.Height, entry.ValueHeight);
            Assert.True(entry.LabelHeight > entry.FontSize * 1.5f);
            Assert.Equal(row.ContentTop + row.ContentHeight / 2, entry.LabelTop + entry.LabelHeight / 2, 4);
            Assert.Equal(row.ContentTop + row.ContentHeight / 2, entry.ValueTop + entry.ValueHeight / 2, 4);
            Assert.Equal(row.ContentLeft, entry.LabelLeft);
            Assert.Equal(row.ContentLeft + row.ContentWidth, entry.ValueLeft + entry.ValueWidth, 4);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RebuiltOverviewMeasuresCurrentLabelsInActiveHierarchyAndRestoresVisibility(bool initiallyActive)
    {
        var active = initiallyActive;
        var measured = new List<string>();
        foreach (var label in new[] { "Died", "Extracted", "Unknown", "View run", "Lauf ansehen" })
        {
            var result = RetainedActiveMeasurementPolicy.Measure(active, value => active = value, () =>
            {
                Assert.True(active);
                measured.Add(label);
                return label;
            });
            Assert.Equal(label, result);
            Assert.Equal(initiallyActive, active);
        }
        Assert.Equal(5, measured.Count);
    }

    [Fact]
    public void MeasurementFailureRestoresHiddenOverview()
    {
        var active = false;
        Assert.Throws<InvalidOperationException>(() => RetainedActiveMeasurementPolicy.Measure<int>(active,
            value => active = value, () => throw new InvalidOperationException()));
        Assert.False(active);
    }

    [Fact]
    public void OverviewCompositionMeasuresInsideActivationScopeAndBoundsEveryHighlight()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "UltimateDuckovStatistics.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        var source = File.ReadAllText(Path.Combine(directory.FullName, "src", "UltimateDuckovStatistics", "UI", "RetainedStatisticsShell.cs"));
        var start = source.IndexOf("var layout = RetainedActiveMeasurementPolicy.Measure(", StringComparison.Ordinal);
        var end = source.IndexOf("var leftPanelRect =", start, StringComparison.Ordinal);
        var measurement = source[start..end];
        Assert.Contains("overviewContentView!.activeSelf, overviewContentView.SetActive", measurement);
        Assert.Contains("MeasureRunBadgeReferenceWidth(", measurement);
        Assert.Contains("MeasureLatestRunViewRunReferenceWidth(", measurement);
        var rowStart = source.IndexOf("private static RetainedOverviewHighlightRowControl CreateOverviewHighlightRow(", StringComparison.Ordinal);
        var rowEnd = source.IndexOf("private static RectTransform CreateOverviewLatestRunHeading(", rowStart, StringComparison.Ordinal);
        var row = source[rowStart..rowEnd];
        Assert.Contains("label.overflowMode = value.overflowMode = TextOverflowModes.Ellipsis", row);
        Assert.Contains("row.AddComponent<TooltipsProvider>()", row);
        Assert.Contains("presentation.Label + \": \" + presentation.Value", row);
    }
}
