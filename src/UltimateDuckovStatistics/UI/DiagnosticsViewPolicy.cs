namespace UltimateDuckovStatistics.UI;

internal sealed class DiagnosticsSelection
{
    private static readonly string[] TechnicalIds = { "technical", "recovery", "log" };
    private readonly HashSet<string> expanded = new(StringComparer.Ordinal);
    private readonly Dictionary<string, float> offsets = new(StringComparer.Ordinal);
    public DiagnosticsPresentation? Snapshot { get; private set; }
    public DiagnosticsLogFilter LogFilter { get; private set; }
    public bool Expanded(string id) => expanded.Contains(id);
    public void Refresh(DiagnosticsPresentation? next)
    {
        if (next == null || Snapshot?.GenerationId != next.GenerationId)
        {
            expanded.Clear(); offsets.Clear(); LogFilter = DiagnosticsLogFilter.All;
            if (next != null)
            {
                expanded.Add("technical");
            }
        }
        Snapshot = next;
        if (next == null) return;
        var valid = new HashSet<string>(next.Systems.SelectMany(s => new[] { "system:" + s.Id, "contracts:" + s.Id })
            .Concat(next.Issues.Select(i => "issue:" + i.Id)).Concat(TechnicalIds), StringComparer.Ordinal);
        expanded.IntersectWith(valid);
    }
    public bool Toggle(string generation, string id)
    {
        if (Snapshot?.GenerationId != generation) return false;
        if (!expanded.Add(id)) expanded.Remove(id); return true;
    }
    public bool Filter(string generation, DiagnosticsLogFilter filter)
    {
        if (Snapshot?.GenerationId != generation || !Enum.IsDefined(typeof(DiagnosticsLogFilter), filter)) return false;
        LogFilter = filter; return true;
    }
    public IEnumerable<DiagnosticsLogEntry> VisibleLog => Snapshot?.Log.Where(e => LogFilter == DiagnosticsLogFilter.All
        || e.Severity.Equals(LogFilter == DiagnosticsLogFilter.Warnings ? "Warning" : "Error", StringComparison.OrdinalIgnoreCase))
        ?? Enumerable.Empty<DiagnosticsLogEntry>();
    public void Capture(string region, float offset)
    { if (Snapshot != null && !float.IsNaN(offset) && !float.IsInfinity(offset)) offsets[region] = Math.Max(0, offset); }
    public float Offset(string region, float viewport, float content)
    { offsets.TryGetValue(region, out var offset); return offsets[region] = Math.Clamp(offset, 0, Math.Max(0, content - viewport)); }
}

internal static class DiagnosticsLayoutPolicy
{
    public const float Gap = 40, Padding = 30, RowGap = 10;
    public static bool Stack(float viewportPixels) => viewportPixels < 1180;
    public static float ColumnWidth(float width, bool stacked) => Math.Max(1, stacked ? width : (width - Gap) / 2);
    public static (float Label, float Value, bool Stacked) Columns(float width, float preferredLabel, float preferredValue)
    {
        width = Math.Max(1, width);
        var label = Math.Min(preferredLabel, width * .48f);
        var value = Math.Max(1, width - label - 20);
        var stacked = width < 500 || label < Math.Min(preferredLabel, 180) || value < Math.Min(preferredValue, 240);
        return stacked ? (width, width, true) : (label, value, false);
    }
    public static float ColumnViewport(bool stacked, float available, float documentHeight)
        => stacked ? Math.Max(1, Math.Min(documentHeight, Math.Max(320, available * .9f))) : Math.Max(1, available);
    public static (float Width, float Height) Modal(float width, float height) => (Math.Max(1, Math.Min(660, width - 60)), Math.Max(1, height - 60));
}

internal static class PanelHotkeyPolicy
{
    public static bool IsAllowed(string name) => !string.IsNullOrWhiteSpace(name)
        && name != "None" && name != "Escape" && name != "Return" && name != "KeypadEnter" && name != "Tab"
        && name != "LeftArrow" && name != "RightArrow" && name != "UpArrow" && name != "DownArrow" && name != "Space"
        && name != "LeftShift" && name != "RightShift" && name != "LeftControl" && name != "RightControl"
        && name != "LeftAlt" && name != "RightAlt" && name != "LeftCommand" && name != "RightCommand"
        && name != "LeftWindows" && name != "RightWindows" && name != "AltGr"
        && !name.StartsWith("Mouse", StringComparison.Ordinal) && !name.StartsWith("Joystick", StringComparison.Ordinal);
}
