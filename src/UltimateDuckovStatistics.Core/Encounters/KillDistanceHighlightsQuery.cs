using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Encounters;

// Owned and polled by the open panel on its main thread. Only the worker reads history.
// No persisted derived fields, historical run-detail decoding, or route/loot scans.
public sealed class KillDistanceHighlightsQuery : IDisposable
{
    private ProfileDocument? profile;
    private long profileRevision = -1;
    private Request? requested, running, completed;
    private Task<KillDistanceHighlights>? pending;
    private CancellationTokenSource? cancellation;
    public KillDistanceHighlights? Value { get; private set; }
    public bool Failed { get; private set; }
    public bool Loading => requested != null && completed != requested;

    public bool Refresh(ProfileDocument current)
    {
        var changed = false;
        var history = current.EncounterHistory;
        var revision = history is EncounterHistory indexed ? indexed.DistanceRevision : current.Revision;
        if (!ReferenceEquals(profile, current) || profileRevision != current.Revision || requested?.Revision != revision
            || !ReferenceEquals(requested?.History, history) || requested?.Generation != current.GenerationId)
        {
            var eligible = new HashSet<string>(RunHistory.Overview(current.Statistics.Runs)
                .Where(run => run.RecordEligible && run.GenerationId == current.GenerationId)
                .Select(run => run.RunId), StringComparer.Ordinal);
            if (!ReferenceEquals(profile, current) || requested == null || requested.Revision != revision
                || !ReferenceEquals(requested.History, history) || requested.Generation != current.GenerationId
                || !requested.Eligible.SetEquals(eligible))
            {
                requested = new Request(history, revision, current.GenerationId, eligible);
                Value = null; Failed = false; changed = true;
                cancellation?.Cancel();
            }
            profile = current; profileRevision = current.Revision;
        }
        if (pending?.IsCompleted == true)
        {
            try
            {
                var result = pending.GetAwaiter().GetResult();
                if (running == requested) { Value = result; completed = running; changed = true; }
            }
            catch (Exception)
            {
                // Disposed/replaced generation, cancellation, or a read failure must never
                // publish a partial maximum/minimum. A reopen can retry a failed read.
                if (running == requested) { Failed = true; completed = running; changed = true; }
            }
            pending = null; running = null;
            cancellation?.Dispose(); cancellation = null;
        }
        if (pending == null && requested != null && completed != requested)
        {
            if (requested.History == null || requested.Eligible.Count == 0)
            {
                Value = new KillDistanceHighlights(); completed = requested;
                return true;
            }
            running = requested;
            cancellation = new CancellationTokenSource();
            var token = cancellation.Token;
            var request = requested;
            pending = Task.Run(() => Read(request, token), token);
        }
        return changed;
    }

    public void RetryFailed()
    {
        if (!Failed) return;
        completed = null; Failed = false;
    }

    public void Reset()
    {
        cancellation?.Cancel();
        profile = null; profileRevision = -1; requested = completed = null;
        Value = null; Failed = false;
        // Keep the cancelled task until it is observed: at most one reader per panel.
    }

    public void Dispose()
    {
        Reset();
        if (pending != null)
            _ = pending.ContinueWith(task => { _ = task.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
        cancellation?.Dispose(); cancellation = null;
    }

    private static KillDistanceHighlights Read(Request request, CancellationToken token)
    {
        if (request.History == null || request.Eligible.Count == 0) return new KillDistanceHighlights();
        IEnumerable<EncounterRecord> ReadKind(EncounterRecordKind kind) => request.History is EncounterHistory history
            ? history.Read(kind: kind) : request.History.Where(record => record.Kind == kind);
        return KillDistanceHighlights.Read(ReadKind(EncounterRecordKind.Visit), ReadKind(EncounterRecordKind.Encounter), request.Eligible, token);
    }

    private sealed class Request(IList<EncounterRecord>? history, long revision, string generation, HashSet<string> eligible)
    {
        public IList<EncounterRecord>? History { get; } = history;
        public long Revision { get; } = revision;
        public string Generation { get; } = generation;
        public HashSet<string> Eligible { get; } = eligible;
    }
}
