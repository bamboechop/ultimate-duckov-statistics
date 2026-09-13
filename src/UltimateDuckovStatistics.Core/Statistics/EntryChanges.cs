using System.Runtime.CompilerServices;

namespace UltimateDuckovStatistics.Core.Statistics;

// Reducers mark the exact entries they mutate. Tracking is attached only to
// owned incremental aggregates; ordinary projections/clones have no ledger.
// The weak key does not retain a discarded raid or profile generation.
internal static class EntryChanges
{
    private static readonly ConditionalWeakTable<object, EntryChangeLedger> ledgers = new();
    private static readonly ConditionalWeakTable<object, ScopeNotification> scopes = new();
    internal static void WatchScope(object owner, Action changed) => scopes.GetValue(owner, _ => new ScopeNotification(changed));
    internal static void ScopeChanged(object owner) { if (scopes.TryGetValue(owner, out var scope)) scope.Changed(); }
    private sealed class ScopeNotification(Action changed) { internal Action Changed { get; } = changed; }
    internal static EntryChangeLedger Track<T>(Dictionary<string, T> entries, Action changed) =>
        Track(entries, entries.Keys, changed);
    internal static EntryChangeLedger Track(object entries, IEnumerable<string> keys, Action changed) =>
        ledgers.GetValue(entries, _ => new EntryChangeLedger(keys, changed));
    internal static void Mark<T>(Dictionary<string, T> entries, string key)
        => Mark((object)entries, key);
    internal static void Mark(object entries, string key)
    { if (key != null && ledgers.TryGetValue(entries, out var ledger)) ledger.Mark(key); }
    internal static void TransferUnchangedEntries<T>(Dictionary<string, T> previous, Dictionary<string, T> next) where T : class
    {
        if (!ledgers.TryGetValue(previous, out var ledger) || previous.Count != next.Count
            || previous.Any(entry => !next.TryGetValue(entry.Key, out var value) || !ReferenceEquals(entry.Value, value))) return;
        ledgers.GetValue(next, _ => ledger);
    }
}

internal sealed class EntryChangeLedger
{
    private readonly object gate = new();
    private readonly Dictionary<string, long> dirty = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> insertionOrder = new(StringComparer.Ordinal);
    private readonly Action changed;
    private long version;
    private bool acknowledgedInitial;
    internal EntryChangeLedger(IEnumerable<string> keys, Action changed)
    { this.changed = changed; foreach (var key in keys) { dirty[key] = checked(++version); insertionOrder[key] = version; } }
    internal void Mark(string key)
    {
        lock (gate)
        {
            dirty[key] = checked(++version);
            insertionOrder.TryAdd(key, version);
        }
        changed();
    }
    internal EntryChangeCapture Capture()
    {
        lock (gate) return new EntryChangeCapture(this, !acknowledgedInitial,
            dirty.OrderBy(entry => insertionOrder[entry.Key]).ToArray());
    }
    internal void Acknowledge(EntryChangeCapture capture)
    {
        if (!ReferenceEquals(capture.Owner, this)) throw new InvalidOperationException("Entry receipt belongs to another aggregate.");
        lock (gate)
        {
            foreach (var entry in capture.Entries)
                if (dirty.TryGetValue(entry.Key, out var current) && current <= entry.Value) dirty.Remove(entry.Key);
            if (capture.ReplacesCollection) acknowledgedInitial = true;
        }
    }
}

internal sealed class EntryChangeCapture
{
    internal EntryChangeCapture(EntryChangeLedger owner, bool replacesCollection, KeyValuePair<string, long>[] entries)
    { Owner = owner; ReplacesCollection = replacesCollection; Entries = entries; }
    internal EntryChangeLedger Owner { get; }
    internal bool ReplacesCollection { get; }
    internal IReadOnlyList<KeyValuePair<string, long>> Entries { get; }
}
