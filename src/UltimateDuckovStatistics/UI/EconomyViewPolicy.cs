using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.UI;

internal sealed class EconomySelection
{
    public EconomyPresentation? Snapshot { get; private set; }
    public string? ExpandedRunId { get; private set; }
    private readonly Dictionary<string, float> offsets = new(StringComparer.Ordinal);
    public void Refresh(EconomyPresentation? next)
    {
        if (next == null || Snapshot?.GenerationId != next.GenerationId)
        { ExpandedRunId = next != null && next.RecentRuns.Count > 0 ? next.RecentRuns[0].RunId : null; offsets.Clear(); }
        else if (!next.RecentRuns.Any(r => r.RunId == ExpandedRunId)) ExpandedRunId = null;
        Snapshot = next;
    }
    public bool Toggle(string generation, string id)
    {
        if (Snapshot?.CanRoute(generation, id) != true) return false;
        ExpandedRunId = ExpandedRunId == id ? null : id; return true;
    }
    public void Capture(string region, float offset)
    { if (Snapshot != null && !float.IsNaN(offset) && !float.IsInfinity(offset)) offsets[region] = Math.Max(0, offset); }
    public float Offset(string region, float viewport, float content)
    {
        offsets.TryGetValue(region, out var value);
        return offsets[region] = Math.Clamp(value, 0, Math.Max(0, content - viewport));
    }
}

internal static class EconomyLayoutPolicy
{
    public const float Padding = 30, Gap = 40, RowGap = 10;
    public static (float Primary, float Recent) Widths(float width, bool stacked) => stacked
        ? (Math.Max(1, width), Math.Max(1, width)) : (Math.Max(1, (width - Gap) * 2 / 3), Math.Max(1, (width - Gap) / 3));
    public static float BoundedHeight(bool stacked, float available, float content) => stacked
        ? Math.Max(1, Math.Min(content, Math.Max(320, available * .9f))) : Math.Max(1, available);
    public static float[] TableColumns(float width, IReadOnlyList<string> headers, IEnumerable<EconomyFlowRow> rows,
        Func<string, float, float> measure)
    {
        var source = rows.ToArray(); var values = new float[3];
        for (var i = 0; i < values.Length; i++)
        {
            var column = i;
            values[i] = Math.Max(measure(headers[i], 20), source.Select(row => measure(Cell(row, column), 28)).DefaultIfEmpty(0).Max()) + 15;
        }
        var label = width - values.Sum() - 30;
        return label < 180 ? Array.Empty<float>() : new[] { label, values[0] + 10, values[1] + 10, values[2] + 10 };
    }
    public static string Cell(EconomyFlowRow row, int column) => column switch
    {
        0 => EconomyPresentationFactory.Number(row.Inflow, true),
        1 => EconomyPresentationFactory.Number(row.Outflow.HasValue ? -row.Outflow.Value : null),
        _ => EconomyPresentationFactory.Number(row.Net, true)
    };
}

internal enum EconomyElementKind { Text, RunToggle, Route, Badge, MoneyIcon, CashIcon, Chevron }
internal enum EconomyTextAlignment { Left, Center, Right, MiddleLeft }

internal sealed class EconomyElement
{
    public string Id { get; set; } = "";
    public EconomyElementKind Kind { get; set; }
    public string Text { get; set; } = "";
    public float Size { get; set; } = 28;
    public bool Muted { get; set; }
    public bool Selected { get; set; }
    public EconomyTextAlignment Alignment { get; set; }
    public RetainedRunBadgeState Outcome { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public bool Actionable => Kind is EconomyElementKind.RunToggle or EconomyElementKind.Route;
}

internal readonly struct EconomySurface
{
    public float X { get; }
    public float Y { get; }
    public float Width { get; }
    public float Height { get; }
    public EconomySurface(float x, float y, float width, float height) { X = x; Y = y; Width = width; Height = height; }
}

// A bounded Economy publication has at most twelve run cards and enum-keyed flow rows.
// Measure all native labels on reflow; rendering introduces neither profile I/O nor visual gates.
internal sealed class EconomyDocument
{
    public List<EconomyElement> Elements { get; } = new();
    public List<EconomySurface> Surfaces { get; } = new();
    public float Height { get; private set; } = 60;
    private readonly Func<string, float, float, float> measure;
    private readonly Func<string, float, float> measureWidth;
    private readonly Func<string, string> text;
    public EconomyDocument(Func<string, float, float, float> measure, Func<string, float, float> measureWidth, Func<string, string>? text = null)
    { this.measure = measure; this.measureWidth = measureWidth; this.text = text ?? UiText.Get; }
    private float Label(string value, float x, float y, float width, float size = 28, bool muted = false,
        EconomyTextAlignment alignment = EconomyTextAlignment.Left, string id = "")
    {
        if (value.Length == 0) return 0;
        var h = Math.Max(1, measure(value, Math.Max(1, width), size));
        Add(new EconomyElement { Id = id, Text = value, Size = size, Muted = muted, Alignment = alignment }, x, y, width, h);
        return h;
    }
    private void Add(EconomyElement element, float x, float y, float width, float height)
    {
        element.X = x; element.Y = y; element.Width = Math.Max(1, width); element.Height = Math.Max(1, height);
        Elements.Add(element); Height = Math.Max(Height, y + height + EconomyLayoutPolicy.Padding);
    }
    private float Notice(string value, float x, float y, float width) => value.Length == 0 ? 0 : Label(value, x, y, width, 22, true) + 10;
    public void Primary(EconomyPresentation snapshot, float width, bool stacked)
    {
        var inner = Math.Max(1, width - 60); float y = 30;
        y += Label(text("ui.economy_current_holdings"), 30, y, inner, 46.3f);
        y += Label(text("ui.economy_holdings_caption"), 30, y, inner, 22, true) + 20;
        var holdingWidth = (inner - 20) / 3;
        var holdingHeights = snapshot.Holdings.Select(holding => MetricHeight(holding.Name,
            Value(holding.Value), holding.Caption, holdingWidth)).ToArray();
        var hh = holdingHeights.Max();
        for (var i = 0; i < snapshot.Holdings.Count; i++)
        {
            var holding = snapshot.Holdings[i];
            Metric(holding.Name, Value(holding.Value), holding.Caption, 30 + i * (holdingWidth + 10), y, holdingWidth, hh, "holding:" + i);
        }
        y += hh + 40;
        var flowWidth = stacked ? inner : (inner - 40) / 2;
        var left = Flow(snapshot.Money, 30, y, flowWidth);
        var right = Flow(snapshot.Cash, stacked ? 30 : 30 + flowWidth + 40, stacked ? left + 40 : y, flowWidth);
        Height = Math.Max(left, right) + 30;
    }
    private float MetricHeight(string name, string value, string caption, float width) => 28 + measure(RunsViewStyle.Uppercase(name), Math.Max(1, width - 30), 28)
        + 6 + measure(value, Math.Max(1, width - 30), 42) + (caption.Length == 0 ? 0 : 10 + measure(caption, Math.Max(1, width - 30), 22));
    private void Metric(string name, string value, string caption, float x, float y, float width, float height, string id)
    {
        Surfaces.Add(new EconomySurface(x, y, width, height));
        var top = y + 14;
        top += Label(value, x + 15, top, width - 30, 42, alignment: EconomyTextAlignment.Center, id: id + ":value") + 6;
        top += Label(RunsViewStyle.Uppercase(name), x + 15, top, width - 30, 28, true, alignment: EconomyTextAlignment.Center, id: id + ":label");
        if (caption.Length > 0) Label(caption, x + 15, top + 10, width - 30, 22, true, id: id + ":caption");
    }
    private string Value(long? value, bool signed = false) => value.HasValue ? EconomyPresentationFactory.Number(value, signed) : text("ui.unavailable");
    private float Flow(EconomyFlow flow, float x, float y, float width)
    {
        var cash = flow.Currency == CurrencyKind.Cash;
        Add(new EconomyElement { Kind = cash ? EconomyElementKind.CashIcon : EconomyElementKind.MoneyIcon }, x, y, 66, 66);
        y += Math.Max(66, Label(text(cash ? "ui.economy_cash_flow" : "ui.economy_money_flow"), x + 86, y, width - 86, 50)) + 10;
        var labels = new[] { text("ui.economy_inflow"), text("ui.economy_outflow"), text("ui.net_flow") };
        var values = new[] { Value(flow.Totals.Inflow, true), Value(flow.Totals.Outflow.HasValue ? -flow.Totals.Outflow : null), Value(flow.Totals.Net, true) };
        var mw = (width - 20) / 3;
        var height = labels.Select((label, i) => MetricHeight(label, values[i], "", mw)).Max();
        for (var i = 0; i < labels.Length; i++) Metric(labels[i], values[i], "", x + i * (mw + 10), y, mw, height, flow.Currency + ":total:" + i);
        y += height + 20;
        y += Notice(flow.Notice, x, y, width);
        y = Table(text("ui.sources"), flow.Sources, flow.SourceNotice, x, y + 10, width, flow.Currency + ":sources");
        if (flow.Sources.Any(row => row.Id == nameof(CurrencySourceCategory.UnknownAdjustment)))
            y += Notice(text("ui.economy_unknown_explanation"), x, y, width);
        y = Table(text("ui.contexts"), flow.Contexts, flow.ContextNotice, x, y + 20, width, flow.Currency + ":contexts", cash ? flow : null);
        return y;
    }
    private float Table(string title, IReadOnlyList<EconomyFlowRow> rows, string notice, float x, float y, float width, string id, EconomyFlow? cash = null)
    {
        y += Label(title, x, y, width, 34) + 10;
        y += Notice(notice, x, y, width);
        if (rows.Count == 0) return y + (notice.Length == 0 ? Label(text("ui.economy_no_flows"), x, y, width, 24, true, id: id + ":empty") + 10 : 0);
        var headers = new[] { text("ui.economy_in"), text("ui.economy_out"), text("ui.economy_net") };
        var columns = EconomyLayoutPolicy.TableColumns(width, headers, rows, measureWidth);
        if (columns.Length > 0)
        {
            float tx = x + columns[0], hh = 0;
            for (var i = 0; i < 3; i++) { hh = Math.Max(hh, Label(headers[i], tx, y, columns[i + 1], 20, true, EconomyTextAlignment.Right)); tx += columns[i + 1]; }
            y += hh + 3;
        }
        foreach (var row in rows)
        {
            var start = y;
            if (columns.Length > 0)
            {
                var rh = Label(row.Name, x, y, columns[0] - 10, id: id + ":" + row.Id + ":label");
                var tx = x + columns[0];
                for (var i = 0; i < 3; i++)
                { rh = Math.Max(rh, Label(EconomyLayoutPolicy.Cell(row, i), tx, y, columns[i + 1], alignment: EconomyTextAlignment.Right, id: id + ":" + row.Id + ":" + i)); tx += columns[i + 1]; }
                y += rh + 3;
            }
            else
            {
                y += Label(row.Name, x, y, width, id: id + ":" + row.Id + ":label") + 3;
                var cellWidth = (width - 20) / 3;
                var rh = headers.Select((header, i) => Label(header + "\n" + EconomyLayoutPolicy.Cell(row, i), x + i * (cellWidth + 10), y,
                    cellWidth, 24, alignment: EconomyTextAlignment.Right, id: id + ":" + row.Id + ":" + i)).Max();
                y += rh + 10;
            }
            if (cash != null && row.Id == nameof(GameplayContext.Raid))
            {
                // This has only an inflow cell: it is neither a net nor terminal secured profit.
                var labelWidth = columns.Length > 0 ? columns[0] - 25 : width * .7f;
                var amountX = columns.Length > 0 ? x + columns[0] : x + width * .7f + 10;
                var amountWidth = columns.Length > 0 ? columns[1] : width * .3f - 10;
                var rh = Label(text("ui.economy_of_which_acquired"), x + 20, y, labelWidth, 22, id: id + ":acquired:label");
                rh = Math.Max(rh, Label(Value(cash.ProvenRaidAcquired, true), amountX, y, amountWidth, 22, alignment: EconomyTextAlignment.Right, id: id + ":acquired:value"));
                y += rh + 6;
                y += Notice(cash.AcquisitionNotice, x + 20, y, width - 20);
            }
            Height = Math.Max(Height, Math.Max(y, start) + 30);
        }
        return y;
    }
    public void Recent(EconomySelection selection, float width)
    {
        var snapshot = selection.Snapshot;
        if (snapshot == null) return;
        var inner = Math.Max(1, width - 60); float y = 30;
        y += Label(text("ui.economy_recent_runs"), 30, y, inner, 46.3f) + 20;
        foreach (var run in snapshot.RecentRuns)
        {
            var start = y; var selected = selection.ExpandedRunId == run.RunId;
            var badgeText = text(RetainedRunBadgePolicy.ResolveSpecification(run.Outcome).TextKey);
            var badgeWidth = Math.Min(inner - 65, measureWidth(badgeText, RetainedRunBadgePolicy.ReferenceFontSize)
                + RetainedRunBadgePolicy.FixedHorizontalContentPixels);
            var badgeHeight = Math.Max(32, measure(badgeText, Math.Max(1, badgeWidth - RetainedRunBadgePolicy.FixedHorizontalContentPixels), RetainedRunBadgePolicy.ReferenceFontSize) + 6);
            var routeText = text(RetainedOverviewLatestRunViewRunPolicy.TextKey);
            var routeWidth = Math.Min(inner - 40, measureWidth(routeText, RetainedOverviewLatestRunViewRunPolicy.ReferenceFontSize)
                + 2 * RetainedOverviewLatestRunViewRunPolicy.HorizontalLabelPaddingPixels);
            var routeHeight = Math.Max(RetainedOverviewLatestRunViewRunPolicy.HeightPixels,
                measure(routeText, Math.Max(1, routeWidth - 2 * RetainedOverviewLatestRunViewRunPolicy.HorizontalLabelPaddingPixels), RetainedOverviewLatestRunViewRunPolicy.ReferenceFontSize) + 12);
            var top = y + 12;
            var titleLeft = 77 + badgeWidth + 12;
            var titleWidth = Math.Max(1, 30 + inner - titleLeft - 20);
            var titleHeight = measure(run.Title, titleWidth, 34);
            var bandHeight = Math.Max(badgeHeight, titleHeight);
            var metaTop = top + bandHeight + 8;
            var metaHeight = measure(run.Metadata, Math.Max(1, inner - 40), 22);
            var headerHeight = Math.Max(96, metaTop + metaHeight - y + 12);
            Add(new EconomyElement { Kind = EconomyElementKind.RunToggle, Id = run.RunId, Selected = selected }, 30, y, inner, headerHeight);
            Add(new EconomyElement { Kind = EconomyElementKind.Chevron, Text = "›", Selected = selected },
                45, top, 24, bandHeight);
            Add(new EconomyElement { Kind = EconomyElementKind.Badge, Outcome = run.Outcome, Text = badgeText }, 77, top + (bandHeight - badgeHeight) / 2, badgeWidth, badgeHeight);
            Add(new EconomyElement { Text = run.Title, Size = 34, Alignment = EconomyTextAlignment.MiddleLeft, Id = "run:" + run.RunId + ":title" }, titleLeft, top, titleWidth, bandHeight);
            Label(run.Metadata, 50, metaTop, inner - 40, 22, !selected, id: "run:" + run.RunId + ":metadata");
            y += headerHeight;
            if (selected)
            {
                y += 20;
                var mw = (inner - 50) / 2;
                var moneyName = text("ui.economy_money_net_label"); var cashName = text("ui.economy_cash_net_label");
                var mh = Math.Max(MetricHeight(moneyName, Value(run.Money.Totals.Net, true), run.Money.Notice, mw),
                    MetricHeight(cashName, Value(run.Cash.Totals.Net, true), run.Cash.Notice, mw));
                Metric(moneyName, Value(run.Money.Totals.Net, true), run.Money.Notice, 50, y, mw, mh, "run:" + run.RunId + ":money");
                Metric(cashName, Value(run.Cash.Totals.Net, true), run.Cash.Notice, 50 + mw + 10, y, mw, mh, "run:" + run.RunId + ":cash");
                y += mh + 20;
                Add(new EconomyElement { Kind = EconomyElementKind.Route, Id = run.RunId, Text = routeText }, 30 + inner - routeWidth - 20, y, routeWidth, routeHeight);
                y += routeHeight + 20;
                Surfaces.Add(new EconomySurface(30, start, inner, y - start));
            }
            y += EconomyLayoutPolicy.RowGap;
        }
        if (snapshot.RecentRuns.Count == 0) y += Label(text("ui.runs_empty"), 30, y, inner, 28, true);
        Height = y + 30;
    }
}
