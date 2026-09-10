namespace UltimateDuckovStatistics.UI;

internal sealed class CombatSelection
{
    public CombatPresentation? Snapshot { get; private set; }
    public CombatPanelSection Page { get; private set; }
    public string? EnemyId { get; private set; }
    public string? WeaponId { get; private set; }
    private readonly HashSet<string> weaponDetails = new(StringComparer.Ordinal);
    public bool WeaponDetailsExpanded => WeaponId != null && weaponDetails.Contains(WeaponId);
    public CombatTableSort IncomingSort { get; } = new();
    public CombatTableSort EnemySort { get; } = new(enemies: true);
    private readonly Dictionary<string, float> offsets = new(StringComparer.Ordinal);
    private readonly Dictionary<CombatPanelSection, string> focus = new();
    public void Refresh(CombatPresentation? next)
    {
        if (next == null || Snapshot?.GenerationId != next.GenerationId)
        { Page = CombatPanelSection.Summary; EnemyId = WeaponId = null; weaponDetails.Clear(); offsets.Clear(); focus.Clear(); IncomingSort.Reset(); EnemySort.Reset(); }
        Snapshot = next;
        if (next == null) return;
        weaponDetails.IntersectWith(next.Weapons.Select(w => w.Row.Id));
        if (!next.Enemies.Any(r => r.Id == EnemyId && r.CanExpand)) EnemyId = null;
        if (!next.Weapons.Any(r => r.Row.Id == WeaponId)) WeaponId = next.Weapons.Count == 0 ? null : next.Weapons[0].Row.Id;
    }
    public bool SelectPage(CombatPanelSection page)
    { if (Snapshot == null || !Enum.IsDefined(typeof(CombatPanelSection), page)) return false; Page = page; return true; }
    public bool ToggleEnemy(string generation, string id)
    {
        if (Snapshot?.GenerationId != generation || !Snapshot.Enemies.Any(r => r.Id == id && r.CanExpand)) return false;
        EnemyId = EnemyId == id ? null : id; return true;
    }
    public bool SelectWeapon(string generation, string id)
    {
        if (Snapshot?.GenerationId != generation || !Snapshot.Weapons.Any(r => r.Row.Id == id)) return false;
        WeaponId = id; return true;
    }
    public CombatWeapon? Weapon => Snapshot?.Weapons.FirstOrDefault(w => w.Row.Id == WeaponId);
    public bool ToggleWeaponDetails(string generation, string id)
    {
        if (Snapshot?.GenerationId != generation || WeaponId != id || Weapon == null) return false;
        if (!weaponDetails.Add(id)) weaponDetails.Remove(id);
        return true;
    }
    public bool SortIncoming(string generation, int column) => Snapshot?.GenerationId == generation && IncomingSort.Toggle(column);
    public bool SortEnemy(string generation, int column) => Snapshot?.GenerationId == generation && EnemySort.Toggle(column);
    private string Key(string region) => region == "selector" ? region : Page + ":" + region + (region == "ammo" ? ":" + WeaponId : "");
    public void Capture(string region, float offset)
    { if (Snapshot != null && !float.IsNaN(offset) && !float.IsInfinity(offset)) offsets[Key(region)] = Math.Max(0, offset); }
    public float Offset(string region, float viewport, float content)
    {
        offsets.TryGetValue(Key(region), out var offset);
        var clamped = Math.Clamp(offset, 0, Math.Max(0, content - viewport));
        if (Snapshot != null) offsets[Key(region)] = clamped;
        return clamped;
    }
    public void Focus(string id) { if (Snapshot != null) focus[Page] = id; }
    public string? FocusId => focus.TryGetValue(Page, out var id) ? id : null;
}

internal static class CombatLayoutPolicy
{
    public const float TableHeaderSize = 22;
    public static bool HasBackground(CombatRowKind kind) => kind is not (CombatRowKind.Heading or CombatRowKind.Notice or CombatRowKind.TableHeader);
    public static float AlignGlyphBottom(float titleY, float titleBottom, float suffixBottom) => titleY + titleBottom - suffixBottom;
    public static bool Muted(CombatRenderRow row, int cell) => row.Kind == CombatRowKind.Card && cell == 1
        || row.Kind == CombatRowKind.Notice || row.Kind == CombatRowKind.Heading && cell > 0
        || row.Kind == CombatRowKind.Item && cell > 0 && !row.Selected;
    public static float[] TableColumns(float width, IReadOnlyList<string> headers, Func<string, float, float> measure)
    {
        if (StackTable(width)) return Array.Empty<float>();
        var minimum = headers.Select(h => Math.Max(measure("↓ " + h, TableHeaderSize), measure("↑ " + h, TableHeaderSize)) + 30).ToArray();
        var spare = width - 30 - minimum.Sum();
        if (spare < 0) return Array.Empty<float>();
        var weights = new[] { .4f, .27f, .12f, .21f };
        return minimum.Select((m, i) => m + spare * weights[i]).ToArray();
    }
    public const float Padding = 30, Gap = 40, RowGap = 10, Radius = 20, Bottom = 30;
    public static (float Left, float Top, float Width, float Height, float Scale) Frame(RetainedVisualCanvasLayout shell, float canvasHeight)
    {
        var scale = shell.ReferenceTransform.CanvasLength(1);
        var top = shell.Header.Top + shell.Header.Height + Gap * scale;
        return (shell.Header.Left, top, shell.Header.Width / scale, Math.Max(1, (canvasHeight - top) / scale - Bottom), scale);
    }
    public static bool Stack(float pixels) => pixels < 1180;
    public static float OuterViewport(bool stacked, float height, float footerHeight) => Math.Max(1, stacked ? height - footerHeight : height);
    public static (float Selector, float Page) Widths(float width, bool stacked) =>
        stacked ? (width, width) : ((width - Gap) / 3, (width - Gap) * 2 / 3);
    public static int CardColumns(float width, bool stacked) => stacked || width < 1000 ? width < 500 ? 1 : 2 : 4;
    public static bool StackTable(float width) => width < 900;
    public static float Clamp(float offset, float viewport, float content) => Math.Clamp(offset, 0, Math.Max(0, content - viewport));
    public static int Move(int index, int count, int delta) => Math.Clamp(index + delta, -1, count);
    public static bool ReturnFromScroll(bool up, float offset) => up && offset <= .5f;
    public static int FocusSlot(IReadOnlyList<CombatRenderRow> rows, IReadOnlyList<int> visible, string id)
    {
        for (var i = 0; i < visible.Count; i++) if (rows[visible[i]].Actionable && rows[visible[i]].Id == id) return i;
        return -1;
    }
    public static IReadOnlyList<int> Visible(IReadOnlyList<CombatRenderRow> rows, float offset, float height)
    {
        var result = new List<int>();
        for (var i = 0; i < rows.Count; i++)
            if (rows[i].Y + rows[i].Height >= offset - 100 && rows[i].Y <= offset + height + 100) result.Add(i);
        return result;
    }
}

internal enum CombatRowKind { Heading, Notice, Metric, Card, Table, TableHeader, TableHeaderBackground, Item, Selector }

internal static class CombatItemIconPolicy
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1845", Justification = "The Unity netstandard2.1 target lacks the span-based string.Concat overload available to the linked net8 tests.")]
    public static T? Resolve<T>(string capturedId, Func<string, T?> resolve) where T : class
    {
        // NativeWeaponFireAdapter captures the ammunition's Item.TypeID under duckov:ammo:.
        // Resolve the same native metadata ID through the established item-icon namespace;
        // the original ammunition identity remains unchanged in presentation and selection.
        var iconId = capturedId.StartsWith("duckov:ammo:", StringComparison.Ordinal)
            ? "duckov:item:" + capturedId.Substring("duckov:ammo:".Length) : capturedId;
        try { return resolve(iconId); }
        catch { return null; }
    }
}

internal sealed class CombatControlPool<T> : IDisposable where T : IDisposable
{
    public List<T> Items { get; } = new();
    private readonly Func<T> create;
    private bool disposed;
    public CombatControlPool(Func<T> create) => this.create = create;
    public void Ensure(int visibleCount)
    {
        if (disposed) return;
        while (Items.Count < visibleCount) Items.Add(create());
    }
    public void Dispose()
    {
        if (disposed) return; disposed = true;
        foreach (var item in Items) item.Dispose(); Items.Clear();
    }
}

internal sealed class CombatRenderRow
{
    public string Id { get; set; } = "";
    public CombatRowKind Kind { get; set; }
    public string[] Cells { get; set; } = Array.Empty<string>();
    public string Detail { get; set; } = "";
    public string IconId { get; set; } = "";
    public bool Actionable { get; set; }
    public bool Selected { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public bool Stacked { get; set; }
    public float SuffixLeft { get; set; }
    public float SuffixTop { get; set; }
    public float[]? Columns { get; set; }
    public bool Expandable { get; set; }
    public bool RightAligned { get; set; }
    public bool Plain { get; set; }
}

// Pure measured document composition. Unity supplies native TMP measurement; tests supply
// deterministic metrics. All rows survive reflow; only visible controls are materialized.
internal sealed class CombatDocument
{
    public List<CombatRenderRow> Rows { get; } = new();
    public float Height { get; private set; }
    private readonly Func<string, float, float, float> measure;
    private readonly Func<string, string> text;
    private readonly Func<string, float, float> measureWidth;
    public CombatDocument(Func<string, float, float, float> measure, Func<string, float, float> measureWidth, Func<string, string>? text = null)
    { this.measure = measure; this.measureWidth = measureWidth; this.text = text ?? UiText.Get; }
    public float Add(CombatRenderRow row, float x, float y, float width)
    {
        row.X = x; row.Y = y; row.Width = Math.Max(1, width);
        row.Stacked = row.Kind == CombatRowKind.Table && (row.Columns == null ? CombatLayoutPolicy.StackTable(width) : row.Columns.Length == 0);
        var inner = Math.Max(1, width - 30);
        float M(string value, float w, float size) => measure(value, Math.Max(1, w), size);
        row.Height = row.Kind switch
        {
            CombatRowKind.TableHeaderBackground => row.Height,
            CombatRowKind.Heading => Math.Max(50, M(row.Cells[0], inner, 46.3f)),
            CombatRowKind.Notice => Math.Max(36, M(row.Cells[0], inner, 20)) + 12,
            CombatRowKind.Card => M(row.Cells[0], inner, 32) + M(row.Cells[1], inner, 20) + 24,
            CombatRowKind.TableHeader => M(row.Cells[0], inner, CombatLayoutPolicy.TableHeaderSize) + 24,
            CombatRowKind.Metric => Math.Max(M(row.Cells[0], inner * .6f - 10, 28), M(row.Cells[1], inner * .4f, 28)) + 20,
            CombatRowKind.Selector => Math.Max(64, M(row.Cells[0], inner - (row.Expandable ? 28 : 0), 32) + 24),
            CombatRowKind.Item => Math.Max(108, M(row.Cells[0], inner - 100, 32) + M(row.Cells[1], inner - 100, 24) + M(row.Cells[2], inner - 100, 20) + 24),
            _ => (row.Stacked ? row.Cells.Select((c, i) => M(c, inner - (i == 0 && row.Expandable ? 28 : 0), 26)).Sum() + (row.Cells.Length - 1) * 6
                : row.Cells.Select((c, i) => M(c, (row.Columns == null ? i == 0 ? inner * .48f - 20 : inner * .52f / 3 - 10 : row.Columns[i] - 30) - (i == 0 && row.Expandable ? 28 : 0), 28)).Max()) + 24
        };
        if (row.Kind == CombatRowKind.Heading && row.Cells.Length > 1)
        {
            var titleWidth = measureWidth(row.Cells[0], 46.3f);
            var suffixWidth = measureWidth(row.Cells[1], 22);
            var inline = titleWidth + suffixWidth + 12 <= inner;
            row.SuffixLeft = inline ? titleWidth + 12 : 0;
            row.SuffixTop = inline ? Math.Max(0, M(row.Cells[0], inner, 46.3f) - M(row.Cells[1], inner, 22)) : row.Height;
            row.Height = Math.Max(row.Height, row.SuffixTop + M(row.Cells[1], inner - row.SuffixLeft, 22));
        }
        if (row.Detail.Length > 0) row.Height += M(row.Detail, inner, 22) + 16;
        Rows.Add(row); Height = Math.Max(Height, y + row.Height + 30); return row.Height;
    }
    public float Notice(string notice, float x, float y, float width) => notice.Length == 0 ? 0
        : Add(new CombatRenderRow { Kind = CombatRowKind.Notice, Cells = new[] { notice } }, x, y, width) + 10;
    public float Heading(string key, float x, float y, float width, string suffix = "") =>
        Add(new CombatRenderRow { Kind = CombatRowKind.Heading, Cells = suffix.Length == 0 ? new[] { text(key) } : new[] { text(key), suffix } }, x, y, width) + 10;
    public float Metrics(IReadOnlyList<CombatMetric> metrics, float x, float y, float width)
    {
        var start = y;
        foreach (var m in metrics) y += Add(new CombatRenderRow { Kind = CombatRowKind.Metric, Cells = new[] { m.Label, m.Value.Text } }, x, y, width) + 10;
        return y - start;
    }
    public float Cards(IReadOnlyList<CombatMetric> metrics, float x, float y, float width, bool stacked)
    {
        var columns = CombatLayoutPolicy.CardColumns(width, stacked); var w = (width - (columns - 1) * 20) / columns; var start = y;
        for (var first = 0; first < metrics.Count; first += columns)
        {
            var batch = new List<CombatRenderRow>();
            for (var i = first; i < Math.Min(first + columns, metrics.Count); i++)
            {
                var row = new CombatRenderRow { Kind = CombatRowKind.Card, Cells = new[] { metrics[i].Value.Text, RunsViewStyle.Uppercase(metrics[i].Label) } };
                Add(row, x + (i - first) * (w + 20), y, w); batch.Add(row);
            }
            var h = batch.Max(r => r.Height); foreach (var row in batch) row.Height = h; y += h + 20;
        }
        Height = Math.Max(Height, y + 30); return y - start;
    }
    public void Summary(CombatPresentation p, float width, bool stacked)
    {
        var w = width - 60; float y = 30;
        y += Heading("ui.records_overall", 30, y, w); y += Cards(p.Overall, 30, y, w, stacked);
        y += Notice(text("ui.combat_accuracy_scope"), 30, y, w);
        var two = !stacked && w >= 1000; var cw = two ? (w - 30) / 2 : w;
        var left = y + Heading("ui.runs_ranged", 30, y, cw); left += Metrics(p.Ranged, 30, left, cw);
        left += 20;
        left += Heading("ui.combat_throwables", 30, left, cw); left += Metrics(p.Throwables, 30, left, cw);
        left += 20;
        left += Heading("ui.combat_other_player_kills", 30, left, cw); left += Metrics(p.OtherPlayerKills, 30, left, cw);
        var rx = two ? 60 + cw : 30; var right = two ? y : left + 20;
        right += Heading("ui.runs_melee", rx, right, cw); right += Metrics(p.Melee, rx, right, cw);
        right += 20;
        right += Heading("ui.observed_world_deaths", rx, right, cw, "(" + p.WorldTotal.Text + " " + text("ui.combat_total_suffix") + ")");
        right += Notice(text("ui.combat_world_subtitle"), rx, right, cw); right += Metrics(p.Ownership, rx, right, cw);
    }
    public void Table(IReadOnlyList<CombatTableRow> rows, string notice, float width, string? expanded, bool incoming, CombatPresentation p, bool stacked, CombatTableSort? sort = null)
    {
        var w = width - 60; float y = 30;
        if (incoming) y += Cards(p.IncomingCards, 30, y, w, stacked);
        y += Notice(notice, 30, y, w);
        if (rows.Count == 0 && (!incoming || !p.HasIncomingEvidence)) return;
        var headers = incoming ? new[] { text("ui.combat_attacker"), text("ui.combat_damage_to_you"), text("ui.combat_share"), text("ui.combat_deaths_caused") }
            : new[] { text("ui.combat_enemy"), text("ui.overview_damage_dealt"), text("ui.kills_by_you"), text("ui.combat_world_deaths") };
        var columns = CombatLayoutPolicy.TableColumns(w, headers, measureWidth);
        sort ??= new CombatTableSort(enemies: !incoming);
        var headerTop = y;
        var band = new CombatRenderRow { Kind = CombatRowKind.TableHeaderBackground, Height = 1 };
        Add(band, 30, y, w);
        float hx = 45, headerHeight = 0;
        for (var i = 0; i < headers.Length; i++)
        {
            var label = sort.Header(i, headers[i]);
            var h = Add(new CombatRenderRow
            {
                Id = "sort:" + i,
                Kind = CombatRowKind.TableHeader,
                Cells = new[] { label },
                RightAligned = columns.Length > 0 && i > 0,
                Actionable = true
            }, columns.Length == 0 ? 30 : hx - 15,
                y, columns.Length == 0 ? w : columns[i]);
            if (columns.Length == 0) y += h + 6; else { hx += columns[i]; headerHeight = Math.Max(headerHeight, h); }
        }
        band.Height = columns.Length == 0 ? y - headerTop - 6 : headerHeight;
        y += headerHeight + 10;
        foreach (var r in incoming ? new[] { p.IncomingTotal }.Concat(sort.Apply(rows)) : sort.Apply(rows))
        {
            var canExpand = !incoming && r.CanExpand;
            var cells = new[] { r.Name }.Concat(r.Values.Select((v, i) =>
                columns.Length == 0 ? headers[i + 1] + ": " + v.Text : v.Text)).ToArray();
            y += Add(new CombatRenderRow
            {
                Id = r.Id,
                Kind = CombatRowKind.Table,
                Cells = cells,
                Actionable = canExpand,
                Expandable = canExpand,
                Selected = canExpand && expanded == r.Id,
                Columns = columns
            }, 30, y, w) + 10;
            if (canExpand && expanded == r.Id) y += Metrics(r.OwnershipBreakdown!, 30, y, w);
        }
    }
    public void Items(CombatSelection selection, float width, bool ammunition)
    {
        var p = selection.Snapshot!; var w = width - 60; float y = 30;
        var weaponLabels = p.Weapons.ToDictionary(weapon => weapon.Row.Id, weapon => weapon.ActionLabel, StringComparer.Ordinal);
        y += Heading(ammunition ? selection.Weapon?.HasRangedEvidence == false ? "ui.combat_weapon_details" : "ui.ammunition" : "ui.combat_weapons", 30, y, w);
        if (ammunition)
        {
            y += Notice(selection.Weapon == null ? text("ui.combat_no_pairs") : selection.Weapon.HasRangedEvidence
                ? string.Format(System.Globalization.CultureInfo.CurrentCulture, text("ui.combat_fired_with"), selection.Weapon.Row.Name) : selection.Weapon.Row.Name, 30, y, w);
            y += Notice(selection.Weapon?.Notice ?? "", 30, y, w);
        }
        else y += Notice(p.WeaponNotice, 30, y, w);
        foreach (var item in ammunition ? selection.Weapon?.Ammunition ?? Array.Empty<CombatItemRow>() : p.Weapons.Select(weapon => weapon.Row))
        {
            y += Add(new CombatRenderRow
            {
                Id = item.Id,
                Kind = CombatRowKind.Item,
                IconId = item.Id,
                Actionable = !ammunition,
                Plain = ammunition,
                Selected = !ammunition && item.Id == selection.WeaponId,
                Cells = new[] { item.Name,
                    ammunition ? item.Actions.Text + " " + text("ui.overview_firing_actions_unit")
                        : weaponLabels[item.Id].Length == 0 ? "" : weaponLabels[item.Id] + ": " + item.Actions.Text,
                    item.PercentageBasis.Length == 0 ? "" : item.Percentage.Text + " " + item.PercentageBasis }
            }, 30, y, w) + 10;
        }
        if (ammunition && selection.Weapon is CombatWeapon weapon && weapon.Metrics.Count > 0)
        {
            y += 20;
            y += Add(new CombatRenderRow
            {
                Id = "details:" + weapon.Row.Id,
                Kind = CombatRowKind.Selector,
                Cells = new[] { text("ui.combat_weapon_details") },
                Actionable = true,
                Expandable = true,
                Selected = selection.WeaponDetailsExpanded
            }, 30, y, w) + 10;
            if (selection.WeaponDetailsExpanded) Metrics(weapon.Metrics, 30, y, w);
        }
    }
}
