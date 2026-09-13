using System.Collections;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Sqlite;

// Scalars are resident; detail is loaded by identity. The bounded cache is a
// view cache, not retention: eviction never deletes a persisted run.
internal sealed class StoredRunHistory : IIndexedRunHistory, ICommittedRunHistory, ICorrectableRunHistory
{
    private readonly object gate = new();
    private const int CacheCapacity = 24;
    private readonly List<RunOverview> overview;
    private readonly Dictionary<string, RunOverview> identities;
    private readonly Dictionary<string, RunSummary> cache = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WeakReference<RunSummary>> observed = new(StringComparer.Ordinal);
    private readonly LinkedList<string> recent = new();
    private readonly Dictionary<string, RunSummary> pending = new(StringComparer.Ordinal);
    private Func<string, RunSummary> load;

    internal StoredRunHistory(IEnumerable<RunOverview> overview, Func<string, RunSummary> load)
    {
        this.overview = overview.ToList();
        identities = this.overview.ToDictionary(run => run.RunId, StringComparer.Ordinal);
        this.load = load;
    }
    public IReadOnlyList<RunOverview> Overview => overview.AsReadOnly();
    public int Count => overview.Count;
    public bool IsReadOnly => false;
    public RunSummary this[int index] { get => GetById(overview[index].RunId); set => throw new NotSupportedException("History correction requires an explicit repository operation."); }
    public RunSummary GetById(string runId)
    {
        lock (gate) return GetOwnedDetail(runId);
    }
    private RunSummary GetOwnedDetail(string runId)
    {
        if (!identities.ContainsKey(runId)) throw new KeyNotFoundException("Run identity is not in this generation.");
        if (pending.TryGetValue(runId, out var accepted)) return accepted;
        if (cache.TryGetValue(runId, out var cached)) { recent.Remove(runId); recent.AddLast(runId); return cached; }
        var run = observed.TryGetValue(runId, out var weak) && weak.TryGetTarget(out var retained) ? retained : load(runId);
        observed[runId] = new WeakReference<RunSummary>(run);
        cache.Add(runId, run); recent.AddLast(runId);
        if (recent.Count > CacheCapacity) { cache.Remove(recent.First!.Value); recent.RemoveFirst(); }
        return run;
    }
    public void Add(RunSummary item)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));
        var row = RunOverview.From(item);
        lock (gate) { identities.Add(row.RunId, row); overview.Add(row); pending.Add(row.RunId, item); }
    }
    public void ReleaseCommittedDetail(string runId)
    {
        lock (gate) Committed(runId);
    }
    public void RestoreDetailSource(IList<RunSummary> source)
    {
        if (source is not StoredRunHistory restored) throw new ArgumentException("History requires its storage reader.", nameof(source));
        // Keep pending and observed objects owned by this generation. Only the
        // cold detail reader changes after its database handles were reopened.
        lock (gate) load = restored.load;
    }
    public void AcceptCorrection(RunSummary replacement)
    {
        lock (gate)
        {
            var index = overview.FindIndex(row => row.RunId == replacement.RunId);
            if (index < 0 || overview[index].GenerationId != replacement.SaveGenerationId)
                throw new InvalidOperationException("Correction does not identify an owned history record.");
            var row = RunOverview.From(replacement);
            overview[index] = row; identities[row.RunId] = row;
            pending[row.RunId] = replacement;
            cache.Remove(row.RunId); recent.Remove(row.RunId); observed.Remove(row.RunId);
        }
    }
    private void Committed(string runId)
    {
        if (!pending.TryGetValue(runId, out var value)) return;
        pending.Remove(runId); cache[runId] = value; recent.Remove(runId); recent.AddLast(runId);
        observed[runId] = new WeakReference<RunSummary>(value);
        if (recent.Count > CacheCapacity) { cache.Remove(recent.First!.Value); recent.RemoveFirst(); }
    }
    public int IndexOf(RunSummary item)
    {
        if (item == null) return -1;
        lock (gate)
        {
            var index = overview.FindIndex(row => row.RunId == item.RunId);
            return index >= 0 && ReferenceEquals(GetOwnedDetail(item.RunId), item) ? index : -1;
        }
    }
    public bool Contains(RunSummary item) => IndexOf(item) >= 0;
    public void CopyTo(RunSummary[] array, int arrayIndex) { foreach (var run in this) array[arrayIndex++] = run; }
    public IEnumerator<RunSummary> GetEnumerator() { foreach (var row in overview) yield return GetById(row.RunId); }
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public void Clear() => throw new NotSupportedException("History reset requires a new generation.");
    public void Insert(int index, RunSummary item) => throw new NotSupportedException("Runs are appended in their retained order.");
    public bool Remove(RunSummary item) => throw new NotSupportedException("Persisted history cannot be silently removed.");
    public void RemoveAt(int index) => throw new NotSupportedException("Persisted history cannot be silently removed.");
}
