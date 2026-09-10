using System.Globalization;

namespace UltimateDuckovStatistics.UI;

internal static class RunDateStyle
{
    public const string Format = "yyyy-MM-dd - HH:mm";
}

internal static class RunsEvidenceLayout
{
    public static (float Height, float HeaderHeight, float ContentTop, float ContentHeight) Measure(
        float availableHeight, float titleHeight, float contentHeight)
    {
        var headerHeight = Math.Max(64, titleHeight);
        var contentTop = 16 + headerHeight + 16;
        // Fit the measured attachment rows instead of imposing a fixed-height scrolling panel.
        // Exceptional long/localized evidence still has a bounded viewport on small screens.
        var height = Math.Min(Math.Max(1, availableHeight), contentTop + contentHeight + 16);
        return (height, headerHeight, contentTop, Math.Max(1, height - contentTop - 16));
    }
}

internal static class RunsEvidenceIdentity
{
    public static bool Matches(string generation, string runId, RunSlotPresentation captured,
        string? nextGeneration, string? nextRunId, RunSlotPresentation? next) =>
        generation == nextGeneration && runId == nextRunId && next?.CanOpenDetails == true
        && captured.SlotId == next.SlotId && captured.State == next.State && captured.ItemId == next.ItemId
        && captured.Text == next.Text && captured.NestedComplete == next.NestedComplete
        && captured.Attachments.SequenceEqual(next.Attachments)
        && captured.Evidence.Select(row => (row.State, row.ItemId, row.ItemName, row.SlotName))
            .SequenceEqual(next.Evidence.Select(row => (row.State, row.ItemId, row.ItemName, row.SlotName)));
}

internal static class RunsViewStyle
{
    public const byte Muted = 177;
    public const float SummaryLabelSize = 20;
    public const float SummaryValueSize = 22;
    public const float SegmentTitleSize = 32;
    public const float SegmentDetailSize = 20;
    public const float SlotRadius = 16;
    public const float SlotBorder = 2;
    public const byte BorderRed = 146, BorderGreen = 152, BorderBlue = 164;
    public const int SlotColumns = 5;
    public static string Uppercase(string text) => text.ToUpper(CultureInfo.CurrentCulture);
    public static float SlotSize(float width) => Math.Max(1, Math.Min(90, (width - 20 - 4 * 10) / SlotColumns));
    public static (float X, float Y, float Size) AttachmentDot(float slotSize, int count, int index)
    {
        var columns = Math.Max(1, Math.Max((int)((slotSize - 12) / 12), (int)Math.Ceiling(Math.Sqrt(count))));
        var step = Math.Min(12, (slotSize - 12) / columns);
        return (6 + index % columns * step, 2 + index / columns * step, Math.Min(7, step * .7f));
    }
}

internal static class RetainedRefreshPolicy
{
    // Same-profile data updates must not disable/reselect existing controls: doing so
    // restarts native highlight transitions and replays keyboard selection feedback.
    public static bool RequiresInvalidation(string? currentGeneration, string? nextGeneration) =>
        string.IsNullOrEmpty(nextGeneration) || !string.Equals(currentGeneration, nextGeneration, StringComparison.Ordinal);
}

// Each click must still refer to the binding that received pointer-down. Wheel scrolling and
// recycling cancel that gesture; keyboard submit uses the currently focused, rebound identity.
internal sealed class RunsRowBinding
{
    public string Generation { get; private set; } = string.Empty;
    public string Id { get; private set; } = string.Empty;
    private (string Generation, string Id)? pressed;
    public void Bind(string generation, string id)
    {
        if (Generation != generation || Id != id) CancelPointer();
        Generation = generation; Id = id;
    }
    public void Press() => pressed = (Generation, Id);
    public void CancelPointer() => pressed = null;
    public bool Release(bool dragging)
    {
        var accept = !dragging && pressed == (Generation, Id) && Id.Length > 0;
        CancelPointer(); return accept;
    }
    public bool Activate(RunsSelection selection) => selection.Snapshot?.GenerationId == Generation && selection.Select(Id);
}

internal static class NativeItemTypeIdPolicy
{
    // Native Unarmed is invisible to players; retain its identity but use the empty-slot glyph.
    public static bool UseEmptyIcon(string? id) => id != null && TryParse(id, out var typeId) && typeId == 356;

    public static bool TryParse(string id, out int typeId)
    {
        typeId = 0;
        if (string.IsNullOrWhiteSpace(id)) return false;
        var parts = id.Split(':');
        return parts.Length == 3 && parts[0] == "duckov" && parts[1] is "item" or "weapon" or "totem" or "ammo"
            && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out typeId);
    }
    public static T? Resolve<T>(string id, Func<int, (int Id, T? Icon)> metadata, T? fallback) where T : class
    {
        if (!TryParse(id, out var typeId) || typeId == 356) return null;
        try
        {
            var result = metadata(typeId);
            return result.Id == typeId && !ReferenceEquals(result.Icon, fallback) ? result.Icon : null;
        }
        catch { return null; }
    }
}

internal static class RunsFlowLayout
{
    // Separate measured controls keep native metrics and move to the next line only when necessary.
    public static IReadOnlyList<(float X, float Y, float Width, float Height)> Arrange(float width,
        IReadOnlyList<(float Width, float Height)> items, float gap = 12)
    {
        var result = new List<(float, float, float, float)>();
        float x = 0, y = 0, lineHeight = 0;
        foreach (var item in items)
        {
            var w = Math.Min(width, item.Width);
            if (x > 0 && x + w > width) { x = 0; y += lineHeight + 6; lineHeight = 0; }
            result.Add((x, y, w, item.Height));
            x += w + gap; lineHeight = Math.Max(lineHeight, item.Height);
        }
        return result;
    }
}

internal static class RunsRoundedEdgePolicy
{
    public static IReadOnlyList<(float X, float Y)> Points(float width, float height, float radius, bool top)
    {
        var r = Math.Max(0, Math.Min(radius, Math.Min(width, height) / 2));
        var points = new List<(float, float)>();
        for (var corner = 0; corner < 2; corner++)
            for (var step = 0; step <= 8; step++)
            {
                var angle = Math.PI + (corner * 90 + step * 90d / 8) * Math.PI / 180;
                var x = (corner == 0 ? r : width - r) + r * (float)Math.Cos(angle);
                var y = r + r * (float)Math.Sin(angle);
                points.Add((x, top ? y : height - y));
            }
        return points;
    }
}

internal static class RunsLowerLayout
{
    public static float RouteHeight(bool stacked, float panelHeight, float routeTop) =>
        stacked ? 440 : Math.Max(1, panelHeight - 60 - routeTop);
    public static float EquipmentTop(bool stacked, float lowerTop, float routeTop, float routeHeight) =>
        stacked ? routeTop + routeHeight + 30 : lowerTop;
    public static float EquipmentHeight(bool stacked, float panelHeight, float top, float contentHeight) =>
        stacked ? Math.Min(520, contentHeight) : Math.Max(1, panelHeight - 60 - top);
}
