using System.Diagnostics;
using Newtonsoft.Json.Linq;
using UltimateDuckovStatistics.Core.Encounters;

namespace UltimateDuckovStatistics.Encounters;

internal sealed class EncounterCapturePipeline
{
    private sealed class Evidence
    {
        internal string Generation = "", Run = "", Map = "", Segment = "", Kind = "";
        internal double Time;
        internal object Data = null!;
    }
    private sealed class BatchResult
    {
        internal readonly List<(string Generation, EncounterRecord Record)> Records = new();
        internal Evidence[] Uncommitted = Array.Empty<Evidence>();
        internal string? Error;
    }
    private readonly List<Evidence> queued = new();
    private readonly Queue<(string Generation, EncounterRecord Record)> ready = new();
    private readonly Queue<(string Generation, EncounterRecord Record)> coverage = new();
    private readonly HashSet<(string Generation, string Run, EncounterCaptureIssue Issue)> reported = new();
    private readonly Func<double> monotonic;
    private Task<BatchResult>? pending;
    private Evidence[] inFlight = Array.Empty<Evidence>();
    private EncounterEvidenceProjection? projection;
    private string workerRun = "", workerGeneration = "";
    private readonly string session = Guid.NewGuid().ToString("N");
    private int epoch;
    private double retryAfter;
    private EncounterCaptureIssue stoppedIssue;
    public string? Failure { get; private set; }
    public string? PublicationFailure { get; private set; }
    public bool HasPending => queued.Count != 0 || pending != null || ready.Count != 0 || coverage.Count != 0;

    internal EncounterCapturePipeline(Func<double>? monotonic = null) =>
        this.monotonic = monotonic ?? (() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency);

    // Called even after capture stops: subsequent runs must not look like complete,
    // empty recordings while this host remains disabled. No fake visit is needed.
    internal void ObserveRun(string generation, string run, double time)
    {
        if (Failure != null) ReportCoverage(generation, run, time, stoppedIssue);
    }

    internal void ReportCoverage(string generation, string run, double time, EncounterCaptureIssue issue)
    {
        if (generation.Length == 0 || run.Length == 0 || !reported.Add((generation, run, issue))) return;
        var record = new EncounterRecord
        {
            RunId = run, Id = session + "/coverage/" + (int)issue, Kind = EncounterRecordKind.Coverage,
            Coverage = new EncounterCoverage { Issue = issue, ObservedSeconds = double.IsFinite(time) ? Math.Max(0, time) : 0,
                CaptureStopped = issue <= EncounterCaptureIssue.NativeCaptureFailed }
        };
        coverage.Enqueue((generation, record));
    }

    internal void StopCapture(string generation, string run, double time, EncounterCaptureIssue issue, string detail)
    {
        if (Failure == null) { Failure = detail; stoppedIssue = issue; }
        ReportCoverage(generation, run, time, issue);
    }

    public void Record(string generation, string run, string map, string segment, double time, string kind, object payload)
    {
        if (Failure != null) { ObserveRun(generation, run, time); return; }
        if (generation.Length == 0 || run.Length == 0 || !EncounterEvidenceProjection.Accepts(kind)) return;
        if (queued.Count >= 8192)
        {
            StopCapture(generation, run, time, EncounterCaptureIssue.QueueLimit, "Encounter observation queue limit reached; capture stopped.");
            return;
        }
        queued.Add(new Evidence { Generation = generation, Run = run, Map = map, Segment = segment, Time = time, Kind = kind, Data = payload });
    }

    // A capture failure is terminal for this host, not for the surrounding profile.
    // Valid completed batches and coverage still drain. Storage rejection/exception
    // retains the exact front record and retries, independently of capture health.
    public bool Pump(Func<string, EncounterRecord, bool> publish, bool flush = false, bool start = true)
    {
        while (true)
        {
            if (pending != null && (flush || pending.IsCompleted))
            {
                var task = pending;
                var batch = inFlight;
                pending = null; inFlight = Array.Empty<Evidence>(); // Never retry a faulted Task forever.
                BatchResult result;
                try { result = task.GetAwaiter().GetResult(); }
                catch (Exception exception)
                { result = new BatchResult { Error = exception.GetType().Name + ": " + exception.Message, Uncommitted = batch }; }
                foreach (var record in result.Records) ready.Enqueue(record);
                if (result.Error != null)
                {
                    foreach (var item in result.Uncommitted.Concat(queued))
                        StopCapture(item.Generation, item.Run, item.Time, EncounterCaptureIssue.ReductionFailed, result.Error);
                    queued.Clear(); // This uncommitted tail cannot safely resume the failed reducer.
                }
            }
            if (!PublishQueue(ready, publish)) return false;
            if (pending == null && queued.Count > 0 && (flush || start))
            {
                var batch = queued.ToArray();
                try { pending = Task.Run(() => Reduce(batch)); }
                catch (Exception exception)
                {
                    foreach (var item in batch)
                        StopCapture(item.Generation, item.Run, item.Time, EncounterCaptureIssue.ReductionFailed,
                            exception.GetType().Name + ": " + exception.Message);
                }
                queued.Clear();
                inFlight = pending == null ? Array.Empty<Evidence>() : batch;
                if (flush) continue;
            }
            // Failure metadata belongs to its original generation and is never skipped
            // to force a transition through an actual storage outage.
            if (!PublishQueue(coverage, publish)) return false;
            return !HasPending;
        }
    }

    private bool PublishQueue(Queue<(string Generation, EncounterRecord Record)> queue, Func<string, EncounterRecord, bool> publish)
    {
        if (queue.Count > 0 && monotonic() < retryAfter) return false;
        while (queue.Count > 0)
        {
            var next = queue.Peek();
            try
            {
                if (!publish(next.Generation, next.Record))
                { PublicationFailure = "Encounter publication remains pending."; retryAfter = monotonic() + 0.5; return false; }
            }
            catch (Exception exception)
            { PublicationFailure = exception.GetType().Name + ": " + exception.Message; retryAfter = monotonic() + 0.5; return false; }
            queue.Dequeue(); PublicationFailure = null; retryAfter = 0;
        }
        return true;
    }

    private BatchResult Reduce(Evidence[] batch)
    {
        var result = new BatchResult();
        var uncommittedStart = 0;
        try
        {
            for (var i = 0; i < batch.Length; i++)
            {
                var item = batch[i];
                if (workerRun != item.Run || workerGeneration != item.Generation)
                {
                    if (projection != null) result.Records.AddRange(projection.Flush().Select(record => (workerGeneration, record)));
                    uncommittedStart = i;
                    projection = new EncounterEvidenceProjection(session + "/" + ++epoch);
                    workerRun = item.Run; workerGeneration = item.Generation;
                }
                projection!.Apply(item.Run, item.Map, item.Segment, item.Time, item.Kind, JObject.FromObject(item.Data));
            }
            if (projection != null) result.Records.AddRange(projection.Flush().Select(record => (workerGeneration, record)));
        }
        catch (Exception exception)
        {
            result.Error = exception.GetType().Name + ": " + exception.Message;
            result.Uncommitted = batch.Skip(uncommittedStart).ToArray();
            projection = null; workerRun = workerGeneration = "";
        }
        return result;
    }
}
