#if UDS_ENCOUNTER_DIAGNOSTICS
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace UltimateDuckovStatistics.Encounters.Diagnostics;

// A bounded, development-only evidence stream. Producers never serialize or wait for IO.
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable",
    Justification = "Stop ends producer access; the background worker disposes its queue and streams before Completion finishes.")]
internal sealed class EncounterDiagnosticWriter
{
    private readonly object gate = new();
    private readonly BlockingCollection<object> pending;
    private readonly Func<object, string> serialize;
    private readonly string path;
    private readonly long maximumBytes;
    private long accepted;
    private long dropped;
    private long written;
    private int stopped;
    private int limitReached;
    private string? failure;

    internal EncounterDiagnosticWriter(string path, Func<object, string> serialize, int capacity = 4096, long maximumBytes = 128 * 1024 * 1024)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        if (maximumBytes < 2048) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        this.maximumBytes = maximumBytes;
        this.path = path ?? throw new ArgumentNullException(nameof(path));
        this.serialize = serialize ?? throw new ArgumentNullException(nameof(serialize));
        pending = new BlockingCollection<object>(capacity);
        Completion = Task.Run(Write);
    }

    internal Task Completion { get; }
    internal long Accepted => Interlocked.Read(ref accepted);
    internal long Dropped => Interlocked.Read(ref dropped);
    internal long Written => Interlocked.Read(ref written);
    internal string? Failure => Volatile.Read(ref failure);
    internal bool LimitReached => Volatile.Read(ref limitReached) != 0;

    internal bool TryRecord(object value)
    {
        if (value == null) throw new ArgumentNullException(nameof(value));
        lock (gate)
        {
            if (stopped != 0 || Failure != null) return false;
            if (pending.TryAdd(value)) { Interlocked.Increment(ref accepted); return true; }
            Interlocked.Increment(ref dropped);
            return false;
        }
    }

    internal void Stop()
    {
        lock (gate)
        {
            if (stopped != 0) return;
            stopped = 1;
            pending.CompleteAdding();
        }
    }

    private void Write()
    {
        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            using var writer = new StreamWriter(stream, new UTF8Encoding(false));
            writer.NewLine = "\n";
            var lastFlush = Environment.TickCount;
            long bytes = 0;
            foreach (var value in pending.GetConsumingEnumerable())
            {
                var json = serialize(value);
                var size = Encoding.UTF8.GetByteCount(json) + 1L;
                if (size > maximumBytes - 1024 - bytes)
                {
                    Volatile.Write(ref limitReached, 1);
                    Stop();
                    Interlocked.Increment(ref dropped);
                    while (pending.TryTake(out _)) Interlocked.Increment(ref dropped);
                    break;
                }
                writer.WriteLine(json);
                bytes += size;
                Interlocked.Increment(ref written);
                if (unchecked(Environment.TickCount - lastFlush) >= 1000)
                {
                    writer.Flush();
                    lastFlush = Environment.TickCount;
                }
            }
            writer.WriteLine(serialize(new { Kind = "capture-footer", Written, Dropped, Complete = Dropped == 0 && !LimitReached, LimitReached }));
            writer.Flush();
        }
        catch (Exception exception)
        {
            Volatile.Write(ref failure, exception.GetType().Name + ": " + exception.Message);
            Stop();
        }
        finally
        {
            // No references to native objects are permitted in this queue.
            lock (gate)
            {
                stopped = 1;
                while (pending.TryTake(out _)) { }
                pending.Dispose();
            }
        }
    }
}
#endif
