namespace UltimateDuckovStatistics.UI;

internal static class RecordsControlPool
{
    public static void Synchronize<T>(List<T> controls, int count, Func<int, T> create, Action<T> release)
    {
        while (controls.Count > count)
        {
            var last = controls.Count - 1;
            release(controls[last]); controls.RemoveAt(last);
        }
        while (controls.Count < count) controls.Add(create(controls.Count));
    }
}

internal sealed class RecordsScrollState
{
    private string? generation;
    public float Offset { get; private set; }
    public bool Available { get; private set; }
    public void Capture(float offset) { if (Available) Offset = Math.Max(0, offset); }
    public void Refresh(string? next)
    {
        Available = next != null;
        if (next == null) return;
        if (generation != next) Offset = 0;
        generation = next;
    }
}

internal static class RecordsLayoutPolicy
{
    public const float Padding = 30, SectionGap = 40, CardGap = 20, Bottom = 30;
    public static (float LabelWidth, float ValueLeft, float ValueWidth, bool Stacked) Columns(float width, float preferredLabel)
    {
        width = Math.Max(1, width);
        var label = Math.Min(preferredLabel, width * .45f);
        var stack = width - label - 20 < 240;
        return stack ? (width, 0, width, true) : (label, label + 20, width - label - 20, false);
    }
    public static float RowHeight(float label, float value, bool stacked) =>
        (stacked ? label + 6 + value : Math.Max(label, value)) + 12;
    public static float DocumentHeight(float overall, float maps) => overall + (maps > 0 ? SectionGap + maps : 0) + Bottom;
}
