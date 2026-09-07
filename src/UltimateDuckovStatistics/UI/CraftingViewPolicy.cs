using System.Globalization;

namespace UltimateDuckovStatistics.UI;

internal sealed class CraftingSelection
{
    public CraftingPresentation? Snapshot { get; private set; }
    private readonly HashSet<string> expanded = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> offsets = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> focused = new(StringComparer.Ordinal);
    public void Refresh(CraftingPresentation? next)
    {
        if (next == null || Snapshot?.GenerationId != next.GenerationId)
        { expanded.Clear(); offsets.Clear(); focused.Clear(); }
        Snapshot = next;
        if (next == null) return;
        expanded.IntersectWith(next.ExpansionIds);
        foreach (var key in focused.Where(pair => !next.ExpansionIds.Contains(pair.Value)).Select(pair => pair.Key).ToArray()) focused.Remove(key);
    }
    public bool Toggle(string generation, string id)
    {
        if (Snapshot?.GenerationId != generation || !Snapshot.ExpansionIds.Contains(id)) return false;
        if (!expanded.Add(id)) expanded.Remove(id); return true;
    }
    public bool Expanded(string id) => expanded.Contains(id);
    public void Capture(string region, float offset)
    { if (Snapshot != null && !float.IsNaN(offset) && !float.IsInfinity(offset)) offsets[region] = Math.Max(0, offset); }
    public float Offset(string region, float viewport, float content)
    {
        offsets.TryGetValue(region, out var offset);
        var clamped = Math.Clamp(offset, 0, Math.Max(0, content - viewport));
        if (Snapshot != null) offsets[region] = clamped;
        return clamped;
    }
    public void Focus(string region, string id) { if (Snapshot?.ExpansionIds.Contains(id) == true) focused[region] = id; }
    public string? FocusId(string region) => focused.TryGetValue(region, out var id) ? id : null;
}

internal static class CraftingLayoutPolicy
{
    public static float ColumnWidth(float width, bool stacked) => Math.Max(1, stacked ? width : (width - CombatLayoutPolicy.Gap) / 2);
    public static float ColumnHeight(float height, float content, bool stacked) => stacked ? Math.Max(1, Math.Min(content, Math.Max(320, height * .85f))) : height;
}

internal static class CraftingDocument
{
    public static EquipmentDocument Create(CraftingSelection selection, bool resources, float width,
        Func<string, float, float, float> measure, Func<string, string>? text = null)
    {
        var t = text ?? UiText.Get; var document = new EquipmentDocument(measure, t);
        var p = selection.Snapshot!; const float x = 30; var w = Math.Max(1, width - 60); float y = 30;
        string Count(long? count) => count?.ToString("N0", CultureInfo.CurrentCulture) ?? t("ui.unavailable");
        string Phrase(string key, long? count) => count.HasValue ? string.Format(CultureInfo.CurrentCulture, t(key), Count(count)) : t("ui.unavailable");
        void Notice(string value)
        { if (value.Length > 0) y += document.Add(new EquipmentRenderRow { Kind = EquipmentRowKind.Notice, Name = value }, x, y, w); }
        y += document.Add(new EquipmentRenderRow { Kind = EquipmentRowKind.Heading, SectionHeading = true,
            Name = t(resources ? "ui.crafting_resources" : "ui.crafting_outputs") }, x, y, w);
        Notice(resources ? p.ResourceNotice : p.OutputNotice);
        var entries = resources ? p.Resources : p.Outputs;
        if (entries.Count == 0) Notice(resources ? p.ResourceEmpty : p.OutputEmpty);
        foreach (var entry in entries)
        {
            var start = y; var expanded = selection.Expanded(entry.Id);
            var header = new EquipmentRenderRow { Id = entry.Id, Kind = EquipmentRowKind.Item,
                Name = entry.Name, Value = resources ? Count(entry.Count) : Phrase("ui.crafting_times", entry.Count),
                IconId = CraftingIconPolicy.ResolveId(entry.ItemId), Actionable = true, Expandable = true, Selected = expanded };
            // The 10px separation belongs after the complete card, never between its header and details.
            document.Add(header, x, y, w); y += header.Height;
            if (expanded)
            {
                y += document.Add(new EquipmentRenderRow { Kind = EquipmentRowKind.Item,
                    Name = t(resources ? "ui.crafting_used_for" : "ui.crafting_resources_used"),
                    Value = resources ? "" : Phrase("ui.crafting_produced", entry.ProducedQuantity) }, x, y, w);
                foreach (var detail in entry.Details)
                {
                    var name = resources && detail.ProducedQuantity.HasValue
                        ? string.Format(CultureInfo.CurrentCulture, t("ui.crafting_named_produced"), Count(detail.ProducedQuantity), detail.Name) : detail.Name;
                    var value = resources && detail.ConsumedQuantity.HasValue
                        ? string.Format(CultureInfo.CurrentCulture, t("ui.crafting_using"), Count(detail.ConsumedQuantity), entry.Name)
                        : Phrase("ui.crafting_used", detail.ConsumedQuantity);
                    y += document.Add(new EquipmentRenderRow { Kind = EquipmentRowKind.Item, Name = name, Value = value,
                        Caption = resources && !detail.ProducedQuantity.HasValue ? t("ui.crafting_produced_unavailable") : "",
                        IconId = CraftingIconPolicy.ResolveId(detail.ItemId) }, x + 10, y, w - 20);
                }
                if (entry.DetailNotice.Length > 0)
                    y += document.Add(new EquipmentRenderRow { Kind = EquipmentRowKind.Notice, Name = entry.DetailNotice }, x, y, w);
                y += 5;
            }
            document.Surfaces.Add(new EquipmentSurface(x, start, w, y - start)); y += 10;
        }
        document.Seal(); return document;
    }
}
