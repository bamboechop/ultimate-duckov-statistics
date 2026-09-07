namespace UltimateDuckovStatistics.UI;

internal sealed class CombatTableSort
{
    private readonly bool enemies;
    public CombatTableSort(bool enemies = false) { this.enemies = enemies; Reset(); }
    public int Column { get; private set; }
    public bool Descending { get; private set; } = true;
    public void Reset() { Column = enemies ? 2 : 1; Descending = true; }
    public bool Toggle(int column)
    {
        if (column < 0 || column > 3) return false;
        Descending = column != Column || !Descending; Column = column; return true;
    }
    public string Header(int column, string label) => column == Column ? (Descending ? "↓ " : "↑ ") + label : label;
    public IEnumerable<CombatTableRow> Apply(IEnumerable<CombatTableRow> rows) => rows.OrderBy(r => r, Comparer<CombatTableRow>.Create(Compare));
    private int Compare(CombatTableRow a, CombatTableRow b)
    {
        var result = Column switch
        {
            0 => (Descending ? -1 : 1) * StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name),
            1 => CompareValue(a.SortDamage, b.SortDamage),
            2 => enemies ? CompareValue(a.SortKills, b.SortKills) : CompareValue(a.SortShare, b.SortShare),
            _ => enemies ? CompareValue(a.SortWorld, b.SortWorld) : CompareValue(a.SortDeaths, b.SortDeaths)
        };
        if (result != 0) return result;
        result = StringComparer.Ordinal.Compare(a.Name, b.Name);
        return result != 0 ? result : StringComparer.Ordinal.Compare(a.Id, b.Id);
    }
    private int CompareValue<T>(T? a, T? b) where T : struct, IComparable<T>
    {
        // Missing evidence stays last in either direction. Counts retain Int64 precision.
        if (!a.HasValue) return b.HasValue ? 1 : 0;
        if (!b.HasValue) return -1;
        return Descending ? b.Value.CompareTo(a.Value) : a.Value.CompareTo(b.Value);
    }
}
