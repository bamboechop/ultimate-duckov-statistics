using System.Collections;
using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Core.Encounters;

internal interface IEncounterHistorySource
{
    int Count { get; }
    EncounterRecord? Find(string runId, EncounterRecordKind kind, string id);
    IEnumerable<EncounterRecord> Read(string? runId, EncounterRecordKind? kind);
}

internal sealed class MemoryEncounterHistorySource(IList<EncounterRecord> records) : IEncounterHistorySource
{
    public int Count => records.Count;
    public EncounterRecord? Find(string runId, EncounterRecordKind kind, string id) =>
        records.FirstOrDefault(record => record.RunId == runId && record.Kind == kind && record.Id == id);
    public IEnumerable<EncounterRecord> Read(string? runId, EncounterRecordKind? kind) =>
        records.Where(record => (runId == null || record.RunId == runId) && (!kind.HasValue || record.Kind == kind));
}

// Unacknowledged records and a bounded recent cache stay resident. A selected view explicitly loads
// its run/family; opening a profile never decodes historical route/loot payloads.
internal sealed class EncounterHistory : IList<EncounterRecord>
{
    private readonly object gate = new();
    private readonly Dictionary<(string Run, EncounterRecordKind Kind, string Id), byte[]> pending = new();
    private readonly Dictionary<(string Run, EncounterRecordKind Kind, string Id), byte[]> recent = new();
    private IEncounterHistorySource? source;
    internal EncounterHistory(IEncounterHistorySource? source = null) => this.source = source;
    internal void RestoreSource(IEncounterHistorySource restored) { lock (gate) source = restored; }
    internal void Put(EncounterRecord record, byte[] bytes)
    {
        lock (gate)
        {
            var key = EncounterRecordValidation.Key(record);
            pending[key] = bytes;
            Remember(key, bytes);
        }
    }
    internal void Acknowledge(string runId, EncounterRecordKind kind, string id, byte[] bytes)
    {
        lock (gate)
            if (source != null && pending.TryGetValue((runId, kind, id), out var current) && ReferenceEquals(current, bytes))
                pending.Remove((runId, kind, id));
    }
    internal EncounterRecord? Find(string runId, EncounterRecordKind kind, string id)
    {
        IEncounterHistorySource? reader;
        lock (gate)
        {
            if (pending.TryGetValue((runId, kind, id), out var bytes)) return ProfileRecordCodec.Decode<EncounterRecord>(bytes);
            if (recent.TryGetValue((runId, kind, id), out bytes)) return ProfileRecordCodec.Decode<EncounterRecord>(bytes);
            reader = source;
        }
        var result = reader?.Find(runId, kind, id);
        if (result != null) lock (gate) Remember((runId, kind, id), new ProfileRecordCodec().Encode(result));
        return result;
    }
    internal IEnumerable<EncounterRecord> Read(string? runId = null, EncounterRecordKind? kind = null)
    {
        Dictionary<(string Run, EncounterRecordKind Kind, string Id), byte[]> captured;
        IEncounterHistorySource? reader;
        lock (gate)
        {
            captured = pending.Where(entry => (runId == null || entry.Key.Run == runId) && (!kind.HasValue || entry.Key.Kind == kind))
                .ToDictionary(entry => entry.Key, entry => entry.Value);
            reader = source;
        }
        foreach (var record in reader?.Read(runId, kind) ?? Enumerable.Empty<EncounterRecord>())
        {
            var key = EncounterRecordValidation.Key(record);
            if (captured.TryGetValue(key, out var bytes))
            { captured.Remove(key); yield return ProfileRecordCodec.Decode<EncounterRecord>(bytes); }
            else yield return record;
        }
        foreach (var bytes in captured.Values) yield return ProfileRecordCodec.Decode<EncounterRecord>(bytes);
    }
    public int Count
    {
        get
        {
            lock (gate) return checked((source?.Count ?? 0) + pending.Keys.Count(key => source?.Find(key.Run, key.Kind, key.Id) == null));
        }
    }
    public bool IsReadOnly => true;
    private void Remember((string Run, EncounterRecordKind Kind, string Id) key, byte[] bytes)
    {
        if (recent.Count >= 128 && !recent.ContainsKey(key)) recent.Clear();
        recent[key] = bytes;
    }
    public EncounterRecord this[int index] { get => Read().ElementAt(index); set => throw new NotSupportedException(); }
    public IEnumerator<EncounterRecord> GetEnumerator() => Read().GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    public int IndexOf(EncounterRecord item)
    { var index = 0; foreach (var row in this) { if (EncounterRecordValidation.Key(row) == EncounterRecordValidation.Key(item)) return index; index++; } return -1; }
    public bool Contains(EncounterRecord item) => Find(item.RunId, item.Kind, item.Id) != null;
    public void CopyTo(EncounterRecord[] array, int arrayIndex) { foreach (var row in this) array[arrayIndex++] = row; }
    public void Add(EncounterRecord item) => throw new NotSupportedException();
    public void Clear() => throw new NotSupportedException();
    public void Insert(int index, EncounterRecord item) => throw new NotSupportedException();
    public bool Remove(EncounterRecord item) => throw new NotSupportedException();
    public void RemoveAt(int index) => throw new NotSupportedException();
}
