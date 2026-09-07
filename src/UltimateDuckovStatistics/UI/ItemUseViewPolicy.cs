using System.Globalization;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.UI;

internal sealed class ItemUseSelection
{
    public ItemUsePresentation? Snapshot { get; private set; }
    public CanonicalItemGroup? Filter { get; private set; }
    private readonly HashSet<string> expanded = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> offsets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> focused = new(StringComparer.Ordinal);
    public void Refresh(ItemUsePresentation? next)
    {
        var reset = next == null || Snapshot?.GenerationId != next.GenerationId;
        if (reset) { Filter = null; expanded.Clear(); offsets.Clear(); focused.Clear(); }
        Snapshot = next;
        if (next == null) return;
        expanded.IntersectWith(next.ExpansionIds);
        if (reset)
        {
            if (next.Items.Count > 0) expanded.Add("item:" + next.Items[0].ItemId);
            if (next.RecentRuns.Count > 0) expanded.Add("run:" + next.RecentRuns[0].RunId);
        }
    }
    public bool Toggle(string generation, string id)
    {
        if (Snapshot?.GenerationId != generation || !Snapshot.ExpansionIds.Contains(id)) return false;
        if (!expanded.Add(id)) expanded.Remove(id); return true;
    }
    public bool SelectFilter(string generation, CanonicalItemGroup? group)
    {
        if (Snapshot?.GenerationId != generation || group.HasValue && !Enum.IsDefined(typeof(CanonicalItemGroup), group.Value)) return false;
        Filter = group; return true;
    }
    public bool Expanded(string id) => expanded.Contains(id);
    public IEnumerable<ItemUseEntry> VisibleItems => Snapshot?.Items.Where(item => !Filter.HasValue || item.Matches(Filter.Value))
        ?? Enumerable.Empty<ItemUseEntry>();
    private string Key(string region) => region == "left" ? region + ":" + (Filter?.ToString() ?? "all") : region;
    public void Capture(string region, float offset)
    { if (Snapshot != null && !float.IsNaN(offset) && !float.IsInfinity(offset)) offsets[Key(region)] = Math.Max(0, offset); }
    public float Offset(string region, float viewport, float content)
    {
        offsets.TryGetValue(Key(region), out var offset);
        return offsets[Key(region)] = Math.Clamp(offset, 0, Math.Max(0, content - viewport));
    }
    public void Focus(string region, string id) { if (Snapshot != null) focused[Key(region)] = id; }
    public string? FocusId(string region) => focused.TryGetValue(Key(region), out var id) ? id : null;
}

internal enum ItemUseRowKind { Heading, Notice, Statistic, Filter, Item, Group, Run, Route }
internal readonly struct ItemUseSurface
{
    public float X { get; }
    public float Y { get; }
    public float Width { get; }
    public float Height { get; }
    public float Radius { get; }
    public ItemUseSurface(float x, float y, float width, float height, float radius)
    { X = x; Y = y; Width = width; Height = height; Radius = radius; }
}
internal sealed class ItemUseRenderRow
{
    public string Id { get; set; } = "";
    public ItemUseRowKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public string Caption { get; set; } = "";
    public string IconId { get; set; } = "";
    public bool Actionable { get; set; }
    public bool Selected { get; set; }
    public bool Expandable { get; set; }
    public RetainedRunBadgeState? Outcome { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float NameLeft { get; set; }
    public float NameTop { get; set; }
    public bool ValueFirst { get; set; }
    public float NameWidth { get; set; }
    public float NameHeight { get; set; }
    public float ValueTop { get; set; }
    public float ValueLeft { get; set; }
    public float ValueWidth { get; set; }
    public float ValueHeight { get; set; }
    public float CaptionTop { get; set; }
    public float CaptionHeight { get; set; }
    public float BadgeWidth { get; set; }
    public float BadgeHeight { get; set; }
    public float BadgeTop { get; set; }
    public float RouteWidth { get; set; }
    public float Size => Kind == ItemUseRowKind.Heading ? 46.3f : Kind is ItemUseRowKind.Notice or ItemUseRowKind.Filter or ItemUseRowKind.Statistic ? 20 : 32;
    public bool HasIcon => IconId.Length > 0;
    public bool EmptyIcon => NativeItemTypeIdPolicy.UseEmptyIcon(IconId);
    public string IconFallback => EmptyIcon ? "—" : "?";
}

internal static class ItemUseLayoutPolicy
{
    public const float Padding = 30, Gap = 40, RowGap = 10;
    public static float ColumnWidth(float width, bool stacked) => Math.Max(1, stacked ? width : (width - Gap) / 2);
    public static float BoundedHeight(bool stacked, float available, float content) => stacked
        ? Math.Max(1, Math.Min(content, Math.Max(320, available * .85f))) : Math.Max(1, available);
    public static int StatisticColumns(float width, int count) => Math.Max(1, Math.Min(count, (int)((width + 20) / 250)));
}

// The measured document retains strings and boxes only. Native controls are materialized
// for visible intervals, so large exact-identity inventories do not create hidden objects.
internal sealed class ItemUseDocument
{
    public List<ItemUseRenderRow> Rows { get; } = new();
    public List<ItemUseSurface> Surfaces { get; } = new();
    public float Height { get; private set; } = 1;
    private float[] ends = Array.Empty<float>();
    private readonly Func<string, float, float, float> measure;
    private readonly Func<string, float, float> measureWidth;
    private readonly Func<string, string> text;
    public ItemUseDocument(Func<string, float, float, float> measure, Func<string, float, float> measureWidth, Func<string, string>? text = null)
    { this.measure = measure; this.measureWidth = measureWidth; this.text = text ?? UiText.Get; }
    public float Add(ItemUseRenderRow row, float x, float y, float width)
    {
        row.X = x; row.Y = y; row.Width = Math.Max(1, width);
        var plain = row.Kind is ItemUseRowKind.Heading or ItemUseRowKind.Notice or ItemUseRowKind.Group;
        row.NameLeft = plain ? 0 : row.Kind == ItemUseRowKind.Filter ? 12 : 20 + (row.Expandable ? 30 : 0) + (row.HasIcon ? 80 : 0);
        row.NameTop = plain ? 0 : row.Kind == ItemUseRowKind.Filter ? 8 : 12;
        var right = plain ? 0 : row.Kind == ItemUseRowKind.Filter ? 12 : 20;
        var inner = Math.Max(1, width - row.NameLeft - right);
        if (row.Kind == ItemUseRowKind.Run && row.Outcome.HasValue)
        {
            var spec = RetainedRunBadgePolicy.ResolveSpecification(row.Outcome.Value);
            row.BadgeWidth = Math.Min(inner * .3f, measureWidth(text(spec.TextKey), RetainedRunBadgePolicy.ReferenceFontSize)
                + RetainedRunBadgePolicy.FixedHorizontalContentPixels);
            row.BadgeHeight = Math.Max(RetainedRunBadgePolicy.HeightPixels, measure(text(spec.TextKey),
                Math.Max(1, row.BadgeWidth - RetainedRunBadgePolicy.FixedHorizontalContentPixels), RetainedRunBadgePolicy.ReferenceFontSize) + 6);
            row.NameLeft += row.BadgeWidth + 14;
            inner = Math.Max(1, width - row.NameLeft - right - row.RouteWidth - 20);
        }
        row.NameWidth = row.Value.Length > 0 && row.Kind != ItemUseRowKind.Statistic ? Math.Max(1, inner * .66f - 10) : inner;
        row.NameHeight = measure(row.Name, row.NameWidth, row.Size);
        row.ValueLeft = row.NameLeft + row.NameWidth + 10;
        row.ValueWidth = Math.Max(1, width - row.ValueLeft - right);
        row.ValueTop = row.NameTop;
        if (row.Kind == ItemUseRowKind.Statistic)
        { row.NameLeft = row.ValueLeft = 20; row.NameWidth = row.ValueWidth = Math.Max(1, width - 40); row.NameHeight = measure(row.Name, row.NameWidth, 20); row.ValueTop = 12 + row.NameHeight + 2; }
        row.ValueHeight = row.Value.Length == 0 ? 0 : measure(row.Value, row.ValueWidth, 32);
        if (row.Kind == ItemUseRowKind.Statistic && row.ValueFirst)
        { row.ValueTop = 12; row.NameTop = row.ValueTop + row.ValueHeight + 2; }
        var bodyBottom = Math.Max(row.NameTop + row.NameHeight, row.ValueTop + row.ValueHeight);
        if (row.Kind == ItemUseRowKind.Run) bodyBottom = Math.Max(bodyBottom, row.NameTop + row.BadgeHeight);
        row.BadgeTop = row.NameTop + (bodyBottom - row.NameTop - row.BadgeHeight) / 2;
        row.CaptionTop = bodyBottom + 8;
        row.CaptionHeight = row.Caption.Length == 0 ? 0 : measure(row.Caption, Math.Max(1, width - (row.Kind == ItemUseRowKind.Item ? row.NameLeft + 20 : 40)), 20);
        row.Height = (row.Caption.Length == 0 ? bodyBottom : row.CaptionTop + row.CaptionHeight) + (plain ? 0 : row.Kind == ItemUseRowKind.Filter ? 8 : 12);
        if (row.HasIcon) row.Height = Math.Max(100, row.Height);
        if (row.Kind == ItemUseRowKind.Item && row.Caption.Length == 0)
            row.NameTop = row.ValueTop = (row.Height - Math.Max(row.NameHeight, row.ValueHeight)) / 2;
        if (row.Kind == ItemUseRowKind.Filter) row.Height = Math.Max(36, row.Height);
        if (row.Kind == ItemUseRowKind.Route) row.Height = Math.Max(RetainedOverviewLatestRunViewRunPolicy.HeightPixels,
            measure(row.Name, Math.Max(1, width - 2 * RetainedOverviewLatestRunViewRunPolicy.HorizontalLabelPaddingPixels), RetainedOverviewLatestRunViewRunPolicy.ReferenceFontSize) + 12);
        Rows.Add(row); Height = Math.Max(Height, y + row.Height); return row.Height;
    }
    private float Heading(string key, float x, float y, float width) => Add(new ItemUseRenderRow { Kind = ItemUseRowKind.Heading, Name = text(key) }, x, y, width);
    private float Notice(string message, float x, float y, float width) => message.Length == 0 ? 0
        : Add(new ItemUseRenderRow { Kind = ItemUseRowKind.Notice, Name = message }, x, y, width) + 10;
    private float Statistics(IReadOnlyList<KeyValuePair<string, string>> values, float x, float y, float width)
    {
        var columns = ItemUseLayoutPolicy.StatisticColumns(width, Math.Min(values.Count, values.Count == 4 ? 2 : 3));
        var cellWidth = (width - (columns - 1) * 20) / columns;
        var cursor = y;
        for (var i = 0; i < values.Count; i += columns)
        {
            var line = new List<ItemUseRenderRow>();
            for (var column = 0; column < columns && i + column < values.Count; column++)
            {
                var pair = values[i + column]; var row = new ItemUseRenderRow { Kind = ItemUseRowKind.Statistic, Name = RunsViewStyle.Uppercase(pair.Key), Value = pair.Value, ValueFirst = values.Count == 4 };
                Add(row, x + column * (cellWidth + 20), cursor, cellWidth); line.Add(row);
            }
            var rowHeight = line.Max(row => row.Height);
            foreach (var row in line) row.Height = rowHeight;
            cursor += rowHeight + 20;
        }
        Height = Math.Max(Height, cursor - 20); return cursor - y;
    }
    public void Left(ItemUseSelection selection, float width)
    {
        var snapshot = selection.Snapshot!;
        var y = Notice(text("ui.item_use_scope"), 0, 0, width);
        y += Notice(snapshot.Notice, 0, y, width);
        y += Statistics(new[] { new KeyValuePair<string, string>(text("ui.item_use_uses"), snapshot.Uses.Text),
            new KeyValuePair<string, string>(text("ui.item_use_different"), snapshot.DifferentItems.Text),
            new KeyValuePair<string, string>(text("ui.runs_hp"), snapshot.Health.Text) }, 0, y, width) + 20;
        var start = y; y += 30; var inner = Math.Max(1, width - 60);
        y += Heading("ui.item_use_most", 30, y, inner) + 10;
        var filters = new[] { (CanonicalItemGroup?)null }.Concat(Enum.GetValues(typeof(CanonicalItemGroup)).Cast<CanonicalItemGroup>().Select(group => (CanonicalItemGroup?)group)).ToArray();
        float fx = 30, filterHeight = 0;
        foreach (var group in filters)
        {
            var label = RunsViewStyle.Uppercase(group.HasValue ? ItemUsePresentationFactory.GroupName(group.Value, text) : text("ui.item_use_all"));
            var fw = Math.Min(inner, measureWidth(label, 20) + 24);
            if (fx > 30 && fx + fw > width - 30) { y += filterHeight + 10; fx = 30; filterHeight = 0; }
            var row = new ItemUseRenderRow { Id = "filter:" + (group.HasValue ? ((int)group.Value).ToString(CultureInfo.InvariantCulture) : "all"),
                Kind = ItemUseRowKind.Filter, Name = label, Actionable = true, Selected = selection.Filter == group };
            Add(row, fx, y, fw); filterHeight = Math.Max(filterHeight, row.Height); fx += fw + 10;
        }
        y += filterHeight + 20;
        var items = selection.VisibleItems.ToArray();
        if (items.Length == 0) y += Notice(text("ui.item_use_filter_empty"), 30, y, inner);
        foreach (var item in items)
        {
            var cardTop = y; var id = "item:" + item.ItemId; var expanded = selection.Expanded(id);
            y += Add(new ItemUseRenderRow { Id = id, Kind = ItemUseRowKind.Item, Name = item.Name, IconId = item.ItemId,
                Value = Uses(item.Uses, item.Count), Expandable = true, Actionable = true, Selected = expanded }, 30, y, inner) + 10;
            if (expanded)
            {
                y += Statistics(new[] { new KeyValuePair<string, string>(text("ui.item_use_amount"), item.Amount.Text),
                    new KeyValuePair<string, string>(text("ui.runs_hp"), item.Health.Text),
                    new KeyValuePair<string, string>(text("ui.item_use_primary"), item.GroupName),
                    new KeyValuePair<string, string>(text("ui.effects"), item.Effects) }, 50, y, inner - 40);
                Surfaces.Add(new ItemUseSurface(30, cardTop, inner, y - cardTop, 10)); y += 10;
            }
        }
        Surfaces.Add(new ItemUseSurface(0, start, width, y - start + 20, 20)); Height = y + 20; Seal();
    }
    public void Right(ItemUseSelection selection, float width)
    {
        var snapshot = selection.Snapshot!; var inner = Math.Max(1, width - 60); float y = 30;
        y += Heading("ui.item_use_groups", 30, y, inner) + 20;
        foreach (var group in snapshot.Groups)
            y += Add(new ItemUseRenderRow { Kind = ItemUseRowKind.Group, Name = group.Name, Value = Uses(group.Uses, group.Count) }, 30, y, inner) + 6;
        Surfaces.Add(new ItemUseSurface(0, 0, width, y + 24, 20)); y += 64;
        var start = y; y += 30;
        y += Heading("ui.recent_runs", 30, y, inner) + 10;
        if (snapshot.RecentRuns.Count == 0) y += Notice(text("ui.runs_empty"), 30, y, inner);
        foreach (var run in snapshot.RecentRuns)
        {
            var cardTop = y; var id = "run:" + run.RunId; var expanded = selection.Expanded(id);
            var routeLabel = text(RetainedOverviewLatestRunViewRunPolicy.TextKey);
            var routeWidth = Math.Min(inner * .27f, measureWidth(routeLabel, RetainedOverviewLatestRunViewRunPolicy.ReferenceFontSize)
                + 2 * RetainedOverviewLatestRunViewRunPolicy.HorizontalLabelPaddingPixels);
            var header = new ItemUseRenderRow { Id = id, Kind = ItemUseRowKind.Run, Name = run.Title, Caption = run.Caption,
                Expandable = true, Actionable = true, Selected = expanded, Outcome = run.Outcome, RouteWidth = routeWidth };
            Add(header, 30, y, inner);
            var route = new ItemUseRenderRow { Id = "route:" + run.RunId, Kind = ItemUseRowKind.Route, Name = routeLabel, Actionable = true };
            Add(route, width - 45 - routeWidth, y + 12, routeWidth);
            // The styled route control shares the title band, with a real right inset.
            var headerBand = Math.Max(header.BadgeHeight, Math.Max(header.NameHeight, route.Height));
            header.NameTop = 12 + (headerBand - header.NameHeight) / 2;
            header.BadgeTop = 12 + (headerBand - header.BadgeHeight) / 2;
            route.Y = y + 12 + (headerBand - route.Height) / 2;
            header.CaptionTop = 12 + headerBand + 8;
            header.Height = header.CaptionTop + header.CaptionHeight + 12;
            y += header.Height + 10;
            if (expanded)
            {
                if (run.Items.Count == 0) y += Notice(run.EmptyText, 50, y, inner - 40);
                foreach (var item in run.Items)
                    y += Add(new ItemUseRenderRow { Kind = ItemUseRowKind.Item, Name = item.Name, IconId = item.ItemId, Value = Uses(item.Uses, item.Count),
                        Caption = item.Health.Evidence == ItemUseEvidence.Supported && item.Health.Text == "0" ? ""
                            : ItemUsePresentationFactory.Format(text("ui.item_use_hp_value"), item.Health.Text) }, 50, y, inner - 40) + 10;
                Surfaces.Add(new ItemUseSurface(30, cardTop, inner, y - cardTop, 10)); y += 10;
            }
        }
        Surfaces.Add(new ItemUseSurface(0, start, width, y - start + 20, 20)); Height = y + 20; Seal();
    }
    private string Uses(ItemUseValue value, long count) => ItemUsePresentationFactory.Format(text(count == 1 ? "ui.item_use_use_value" : "ui.item_use_uses_value"), value.Text);
    public void Seal()
    {
        Rows.Sort((a, b) => { var result = a.Y.CompareTo(b.Y); return result == 0 ? a.X.CompareTo(b.X) : result; });
        // Parent cards paint first; child expansion surfaces remain above their parent.
        Surfaces.Sort((a, b) => { var radius = b.Radius.CompareTo(a.Radius); return radius == 0 ? a.Y.CompareTo(b.Y) : radius; });
        ends = new float[Rows.Count]; float last = 0;
        for (var i = 0; i < Rows.Count; i++) ends[i] = last = Math.Max(last, Rows[i].Y + Rows[i].Height);
    }
    public IReadOnlyList<int> Visible(float offset, float height)
    {
        var lo = 0; var hi = ends.Length;
        while (lo < hi) { var mid = lo + (hi - lo) / 2; if (ends[mid] < offset - 100) lo = mid + 1; else hi = mid; }
        var result = new List<int>();
        for (var i = lo; i < Rows.Count && Rows[i].Y <= offset + height + 100; i++)
            if (Rows[i].Y + Rows[i].Height >= offset - 100) result.Add(i);
        return result;
    }
    public IEnumerable<ItemUseSurface> VisibleSurfaces(float offset, float height) =>
        Surfaces.Where(surface => surface.Y + surface.Height >= offset - 100 && surface.Y <= offset + height + 100);
}
