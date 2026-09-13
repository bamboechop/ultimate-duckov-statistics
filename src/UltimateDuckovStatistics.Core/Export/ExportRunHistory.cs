using System.Collections;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Export;

// The detached storage owns raw records. Only the current exported run is
// cloned and capability-projected; ordering uses the resident scalar index.
internal sealed class ExportRunHistory(IList<RunSummary> source, Func<RunSummary, RunSummary> project) : IIndexedRunHistory
{
    public IReadOnlyList<RunOverview> Overview { get; } = RunHistory.Overview(source);
    public int Count => Overview.Count;
    public bool IsReadOnly => true;
    public RunSummary this[int index] { get => GetById(Overview[index].RunId); set => throw new NotSupportedException(); }
    public RunSummary GetById(string runId) => project(RunHistory.GetById(source, runId));
    public IEnumerator<RunSummary> GetEnumerator() { foreach (var row in Overview) yield return GetById(row.RunId); }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public int IndexOf(RunSummary item) { for (var index = 0; index < Count; index++) if (Overview[index].RunId == item.RunId) return index; return -1; }
    public bool Contains(RunSummary item) => IndexOf(item) >= 0;
    public void CopyTo(RunSummary[] array, int arrayIndex) { foreach (var run in this) array[arrayIndex++] = run; }
    public void Add(RunSummary item) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public void Insert(int index, RunSummary item) => throw new NotSupportedException();
    public bool Remove(RunSummary item) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
}
