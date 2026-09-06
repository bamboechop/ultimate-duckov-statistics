namespace UltimateDuckovStatistics.UI;

internal sealed class EquipmentSelection
{
    private static readonly string[] SelectorIds = { "0", "1", "2", "3" };
    public EquipmentPresentation? Snapshot { get; private set; }
    public EquipmentPanelSection Page { get; private set; }
    public string? InspectedId { get; private set; }
    private readonly HashSet<string> expanded = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> offsets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> focus = new(StringComparer.Ordinal);
    public void Refresh(EquipmentPresentation? next)
    {
        if (next == null || Snapshot?.GenerationId != next.GenerationId)
        { Page = EquipmentPanelSection.Loadouts; InspectedId = null; expanded.Clear(); offsets.Clear(); focus.Clear(); }
        Snapshot = next;
        if (next == null) return;
        expanded.IntersectWith(next.ExpansionIds);
        if (InspectedId != null && !next.InspectableSlots.ContainsKey(InspectedId)) InspectedId = null;
        var valid = new HashSet<string>(next.ExpansionIds.Concat(next.Recent.Select(r => "route:" + r.RunId))
            .Concat(next.InspectableSlots.Keys).Concat(SelectorIds), StringComparer.Ordinal);
        foreach (var key in focus.Where(p => !valid.Contains(p.Value)).Select(p => p.Key).ToArray()) focus.Remove(key);
    }
    public bool SelectPage(EquipmentPanelSection page)
    { if (Snapshot == null || !Enum.IsDefined(typeof(EquipmentPanelSection), page)) return false; Page = page; return true; }
    public bool Toggle(string generation, string id)
    {
        if (Snapshot?.GenerationId != generation || !Snapshot.ExpansionIds.Contains(id)) return false;
        if (!expanded.Add(id)) expanded.Remove(id); return true;
    }
    public bool Expanded(string id) => expanded.Contains(id);
    public bool Inspect(string generation, string id)
    {
        if (Snapshot?.GenerationId != generation || !Snapshot.InspectableSlots.ContainsKey(id)) return false;
        InspectedId = InspectedId == id ? null : id; return true;
    }
    private string Key(string region) => region == "selector" ? region : Page + ":" + region;
    public void Capture(string region, float offset)
    { if (Snapshot != null && !float.IsNaN(offset) && !float.IsInfinity(offset)) offsets[Key(region)] = Math.Max(0, offset); }
    public float Offset(string region, float viewport, float content)
    {
        offsets.TryGetValue(Key(region), out var offset);
        return offsets[Key(region)] = Math.Clamp(offset, 0, Math.Max(0, content - viewport));
    }
    public void Focus(string region, string id) { if (Snapshot != null) focus[Key(region)] = id; }
    public string? FocusId(string region) => focus.TryGetValue(Key(region), out var id) ? id : null;
}
internal enum EquipmentRowKind { Heading, Notice, Item, Selector, Slot, Route }
internal sealed class EquipmentRenderRow
{
    public string Id { get; set; } = "";
    public EquipmentRowKind Kind { get; set; }
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
    public string Caption { get; set; } = "";
    public string IconId { get; set; } = "";
    public bool Actionable { get; set; }
    public bool Selected { get; set; }
    public bool Expandable { get; set; }
    public RunSlotPresentation? Slot { get; set; }
    public float X { get; set; }
    public float Y { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float NameWidth { get; set; }
    public float NameHeight { get; set; }
    public float ValueHeight { get; set; }
    public float CaptionTop { get; set; }
}

internal static class EquipmentLayoutPolicy
{
    public static bool TwoColumns(EquipmentPanelSection page) => page != EquipmentPanelSection.Weapons;
    public static int Split(int count) => (count + 1) / 2;
    public static float BoundedHeight(bool stacked, float available, float content) => stacked ? Math.Max(1, Math.Min(content, Math.Max(320, available * .85f))) : available;
    public static int Move(int index, int count, int delta) => Math.Clamp(index + delta, -1, count);
    public static string Duration(double seconds) => RetainedRunDurationFormatter.TryFormat(seconds, out var result) ? result : UiText.Get("ui.unavailable");
}

// Rows are measured once per publication/reflow/expansion. Sorted vertical intervals allow bounded visible lookup.
internal sealed class EquipmentDocument
{
    public List<EquipmentRenderRow> Rows { get; } = new();
    public float Height { get; private set; } = 60;
    private float[] ends = Array.Empty<float>();
    private bool stacked;
    private readonly Func<string, float, float, float> measure;
    private readonly Func<string, string> text;
    private readonly Func<string, float, float> measureWidth;
    public EquipmentDocument(Func<string, float, float, float> measure, Func<string, string>? text = null, Func<string, float, float>? measureWidth = null)
    { this.measure = measure; this.text = text ?? UiText.Get; this.measureWidth = measureWidth ?? ((value, size) => value.Length * size * .5f); }
    public float Add(EquipmentRenderRow r, float x, float y, float width)
    {
        r.X = x; r.Y = y; r.Width = Math.Max(1, width);
        var size = r.Kind == EquipmentRowKind.Heading ? 40 : r.Kind == EquipmentRowKind.Notice ? 22 : r.Kind == EquipmentRowKind.Selector ? 32 : 28;
        var inset = r.Kind == EquipmentRowKind.Item ? 30 + (r.IconId.Length > 0 ? 72 : 0) + (r.Expandable ? 28 : 0) : 30;
        var inner = Math.Max(1, width - inset);
        r.NameWidth = r.Value.Length > 0 ? Math.Max(1, inner * .64f - 10) : inner;
        r.NameHeight = measure(r.Name, r.NameWidth, size);
        r.ValueHeight = r.Value.Length == 0 ? 0 : measure(r.Value, Math.Max(1, inner * .36f), 28);
        r.CaptionTop = Math.Max(r.NameHeight, r.ValueHeight) + 16;
        r.Height = r.Kind == EquipmentRowKind.Slot ? width : Math.Max(r.IconId.Length > 0 ? 80 : r.Kind == EquipmentRowKind.Selector ? 64 : 36,
            r.CaptionTop + (r.Caption.Length == 0 ? 0 : measure(r.Caption, inner, 22) + 6) + 12);
        Rows.Add(r); Height = Math.Max(Height, y + r.Height + 30); return r.Height + 10;
    }
    public void Seal()
    {
        Rows.Sort((a, b) => { var y = a.Y.CompareTo(b.Y); return y == 0 ? a.X.CompareTo(b.X) : y; });
        ends = new float[Rows.Count]; float end = 0;
        for (var i = 0; i < Rows.Count; i++) ends[i] = end = Math.Max(end, Rows[i].Y + Rows[i].Height);
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
    private float Notice(string message, float x, float y, float w) => message.Length == 0 ? 0
        : Add(new EquipmentRenderRow { Kind = EquipmentRowKind.Notice, Name = message }, x, y, w);
    private float Heading(string title, float x, float y, float w) => title.Length == 0 ? 0
        : Add(new EquipmentRenderRow { Kind = EquipmentRowKind.Heading, Name = title }, x, y, w);
    private float Entry(EquipmentEntry entry, float x, float y, float w, EquipmentSelection? selection = null, bool value = true)
    {
        var start = y; var expanded = selection?.Expanded(entry.Id) == true;
        y += Add(new EquipmentRenderRow { Id = entry.Id, Kind = EquipmentRowKind.Item, Name = entry.Name,
            IconId = entry.ItemId, Value = value ? EquipmentLayoutPolicy.Duration(entry.Duration) : "", Caption = entry.Caption,
            Actionable = selection != null && entry.Expandable, Expandable = selection != null && entry.Expandable, Selected = expanded }, x, y, w);
        y += Notice(entry.Notice, x, y, w);
        if (expanded)
        {
            var groups = entry.Groups;
            // First group is the exact per-character-slot evidence. Remaining groups have independent two-column layout.
            if (groups.Count > 0) y += Group(groups[0], x + 10, y, w - 20);
            if (groups.Count > 1)
            {
                var stack = stacked || w < 850; var split = EquipmentLayoutPolicy.Split(groups.Count - 1); var cw = stack ? w - 20 : (w - 40) / 2;
                var left = y; var right = y;
                for (var i = 1; i < groups.Count; i++)
                {
                    if (i <= split) left += Group(groups[i], x + 10, left, cw);
                    else { if (stack && i == split + 1) right = left; right += Group(groups[i], stack ? x + 10 : x + cw + 30, right, cw); }
                }
                y = Math.Max(left, right);
            }
        }
        return y - start;
    }
    private float Group(EquipmentGroup g, float x, float y, float w, EquipmentSelection? selection = null)
    {
        var start = y; y += Heading(g.Name, x, y, w); y += Notice(g.Notice, x, y, w);
        foreach (var row in g.Rows) y += Entry(row, x, y, w, selection);
        return y - start + 10;
    }
    private float Loadout(EquipmentEntry entry, float x, float y, float width, EquipmentSelection selection)
    {
        var start = y;
        if (entry.RunId.Length > 0)
        {
            var label = text("ui.view_run"); var buttonWidth = Math.Min(width * .4f, measureWidth(label, 28) + 30);
            var titleHeight = Heading(entry.Name, x, y, width - buttonWidth - 10);
            var buttonHeight = Add(new EquipmentRenderRow { Id = "route:" + entry.RunId, Kind = EquipmentRowKind.Route,
                Name = label, Actionable = true }, x + width - buttonWidth, y, buttonWidth);
            y += Math.Max(titleHeight, buttonHeight);
        }
        else y += Heading(entry.Name, x, y, width);
        y += Notice(entry.Caption, x, y, width);
        var size = RunsViewStyle.SlotSize(width);
        for (var i = 0; i < entry.Slots.Count; i++)
            Add(new EquipmentRenderRow { Id = EquipmentPresentation.InspectionId(entry, entry.Slots[i]), Kind = EquipmentRowKind.Slot,
                    Slot = entry.Slots[i], IconId = entry.Slots[i].ItemId, Actionable = entry.Slots[i].CanOpenDetails,
                    Selected = selection.InspectedId == EquipmentPresentation.InspectionId(entry, entry.Slots[i]) },
                x + 10 + i % 5 * (size + 10), y + i / 5 * (size + 10), size);
        y += (entry.Slots.Count + 4) / 5 * (size + 10);
        var inspected = entry.Slots.FirstOrDefault(slot => EquipmentPresentation.InspectionId(entry, slot) == selection.InspectedId);
        if (inspected != null)
        {
            foreach (var evidence in inspected.Evidence)
                y += Entry(new EquipmentEntry("", evidence.ItemName, evidence.ItemId, 0, evidence.SlotName), x, y, width, value: false);
            if (!inspected.NestedComplete) y += Notice(text("ui.runs_nested_partial"), x, y, width);
        }
        y += Notice(entry.Notice, x, y, width);
        y += Notice(entry.Notice == text("ui.unavailable") ? text("ui.unavailable") : EquipmentLayoutPolicy.Duration(entry.Duration) + " " + text("ui.equipment_active_time"), x, y, width);
        return y - start + 20;
    }
    public void Page(EquipmentSelection selection, bool right, float width, bool stacked = false)
    {
        this.stacked = stacked;
        var p = selection.Snapshot!; float y = 30; var w = Math.Max(1, width - 60); const float x = 30;
        void Section(string title, string key) { y += Heading(text(title), x, y, w); y += Notice(p.Notices[key], x, y, w); }
        void Empty(string key = "ui.equipment_no_observation") { y += Notice(text(key), x, y, w); }
        switch (selection.Page)
        {
            case EquipmentPanelSection.Loadouts:
                if (!right)
                {
                    Section("ui.equipment_most_used", "loadouts");
                    if (p.MostUsed == null) Empty("ui.equipment_no_recurring"); else y += Loadout(p.MostUsed, x, y, w, selection);
                    Section("ui.equipment_selected", "selected");
                    foreach (var row in p.SelectedWeapons) y += Entry(row, x, y, w);
                    if (p.SelectedWeapons.Count == 0) Empty();
                }
                else
                {
                    y += Heading(text("ui.equipment_recent"), x, y, w);
                    foreach (var row in p.Recent) y += Loadout(row, x, y, w, selection);
                    if (p.Recent.Count == 0) Empty();
                }
                break;
            case EquipmentPanelSection.Weapons:
                y += Notice(p.Notices["weapons"], x, y, w);
                foreach (var row in p.Weapons) y += Entry(row, x, y, w, selection);
                if (p.Weapons.Count == 0) Empty();
                break;
            case EquipmentPanelSection.ArmorAndGear:
                y += Notice(p.Notices["armor"], x, y, w);
                var split = EquipmentLayoutPolicy.Split(p.Armor.Count);
                foreach (var group in right ? p.Armor.Skip(split) : p.Armor.Take(split)) y += Group(group, x, y, w, selection);
                if (p.Armor.Count == 0) Empty();
                break;
            case EquipmentPanelSection.Totems:
                if (!right)
                {
                    Section("ui.equipment_direct", "direct");
                    foreach (var row in p.DirectTotems) y += Entry(row, x, y, w, selection);
                    if (p.DirectTotems.Count == 0) Empty();
                    Section("ui.equipment_empty_slots", "empty");
                    foreach (var row in p.EmptySlots) y += Entry(row, x, y, w);
                    if (p.EmptySlots.Count == 0) Empty("ui.unavailable");
                }
                else
                {
                    Section("ui.equipment_sets", "sets");
                    foreach (var set in p.ActiveSets)
                    {
                        foreach (var member in set.Groups.SelectMany(g => g.Rows)) y += Entry(member, x, y, w, value: false);
                        y += Notice(set.Notice, x, y, w); y += Notice(EquipmentLayoutPolicy.Duration(set.Duration) + " " + set.Caption, x, y, w);
                    }
                    if (p.ActiveSets.Count == 0) Empty();
                    Section("ui.equipment_tote", "tote"); y += Notice(text("ui.equipment_tote_unknown"), x, y, w);
                    foreach (var row in p.ToteTotems) y += Entry(row, x, y, w);
                    if (p.ToteTotems.Count == 0) Empty();
                }
                break;
        }
        Seal();
    }
}
