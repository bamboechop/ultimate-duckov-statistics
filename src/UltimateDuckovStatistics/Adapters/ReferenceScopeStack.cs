namespace UltimateDuckovStatistics.Adapters;

internal sealed class ReferenceScopeStack<T>
    where T : class
{
    private readonly List<T> values = new();

    public int Count => values.Count;

    public T? Current => values.Count == 0 ? null : values[^1];

    public T Push(T value)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));
        values.Add(value);
        return value;
    }

    public void Pop(T? value)
    {
        if (value == null || values.Count == 0) return;
        var last = values.Count - 1;
        if (ReferenceEquals(values[last], value))
        {
            values.RemoveAt(last);
            return;
        }

        var index = values.FindLastIndex(candidate => ReferenceEquals(candidate, value));
        if (index >= 0) values.RemoveRange(index, values.Count - index);
    }

    public void Clear() => values.Clear();
}
