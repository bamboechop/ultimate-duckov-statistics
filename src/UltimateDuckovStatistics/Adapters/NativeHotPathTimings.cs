#if UDS_PERFORMANCE_DIAGNOSTICS
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;

namespace UltimateDuckovStatistics.Adapters;

internal enum NativeHotPathArea
{
    Update,
    ProfileReadiness,
    RunLifecycle,
    Equipment,
    ItemUse,
    Economy,
    Holdings,
    Healing,
    Combat,
    Containers,
    WorldTime,
    Crafting,
    ProfilePersistence,
    Panel,
    GameClockCallback,
    CombatEffectUpdate,
    CombatEffectTick,
    CombatEffectOther,
    CombatEffectFinalizer,
    HealingEffectPrefix,
    HealingEffectFinalizer,
    MovementSample,
    Count
}

internal static partial class NativeHotPathDiagnostics
{
    // Fixed storage, no per-call objects/delegates. Only the thread that resets the
    // interval is measured: these Unity Update/native callback paths run there.
    private static readonly long[] timingCalls = new long[(int)NativeHotPathArea.Count];
    private static readonly long[] timingTotals = new long[(int)NativeHotPathArea.Count];
    private static readonly long[] timingMaximums = new long[(int)NativeHotPathArea.Count];
    private static readonly int[] timingCollections = new int[3];
    private static int timingThread;
    private static int timingEpoch;
    private static volatile bool timingActive;
    private static long timingStarted;
    private static long timingOtherThreadCalls;

    private static void ResetTimings()
    {
        timingActive = false;
        timingEpoch++;
        timingThread = Environment.CurrentManagedThreadId;
        Array.Clear(timingCalls, 0, timingCalls.Length);
        Array.Clear(timingTotals, 0, timingTotals.Length);
        Array.Clear(timingMaximums, 0, timingMaximums.Length);
        for (var i = 0; i < timingCollections.Length; i++) timingCollections[i] = GC.CollectionCount(i);
        Interlocked.Exchange(ref timingOtherThreadCalls, 0);
        timingStarted = Stopwatch.GetTimestamp();
        timingActive = true;
    }

    public static NativeHotPathMeasurement Measure(NativeHotPathArea area)
    {
        if (!timingActive) return default;
        if (Environment.CurrentManagedThreadId != timingThread)
        {
            Interlocked.Increment(ref timingOtherThreadCalls);
            return default;
        }
        return new NativeHotPathMeasurement(area, timingEpoch, Stopwatch.GetTimestamp());
    }

    internal static void RecordTiming(NativeHotPathArea area, int epoch, long started)
    {
        if (!timingActive || epoch != timingEpoch) return;
        if (Environment.CurrentManagedThreadId != timingThread)
        {
            Interlocked.Increment(ref timingOtherThreadCalls);
            return;
        }
        var elapsed = Stopwatch.GetTimestamp() - started;
        var index = (int)area;
        timingCalls[index]++;
        timingTotals[index] += elapsed;
        if (elapsed > timingMaximums[index]) timingMaximums[index] = elapsed;
    }

    private static string FinishTimingSummary()
    {
        timingActive = false;
        var elapsed = Stopwatch.GetTimestamp() - timingStarted;
        var result = new StringBuilder(" M18Timing frequency=");
        result.Append(Stopwatch.Frequency.ToString(CultureInfo.InvariantCulture));
        result.Append(" elapsedTicks=").Append(elapsed.ToString(CultureInfo.InvariantCulture));
        result.Append(" otherThreadCalls=").Append(Interlocked.Read(ref timingOtherThreadCalls).ToString(CultureInfo.InvariantCulture));
        for (var i = 0; i < timingCollections.Length; i++)
            result.Append(" gc").Append(i).Append('=').Append(GC.CollectionCount(i) - timingCollections[i]);
        // Parent measurements include child work and instrumentation. These rows
        // must not be summed or treated as an ordinary-release frame-time result.
        result.Append(" units=stopwatchTicks fields=calls,total,max nested=inclusive");
        for (var i = 0; i < timingCalls.Length; i++)
        {
            result.Append(' ').Append((NativeHotPathArea)i).Append('=');
            result.Append(timingCalls[i].ToString(CultureInfo.InvariantCulture)).Append(',');
            result.Append(timingTotals[i].ToString(CultureInfo.InvariantCulture)).Append(',');
            result.Append(timingMaximums[i].ToString(CultureInfo.InvariantCulture));
        }
        return result.ToString();
    }
}

internal readonly struct NativeHotPathMeasurement : IDisposable
{
    private readonly NativeHotPathArea area;
    private readonly int epoch;
    private readonly long started;
    private readonly bool measured;

    internal NativeHotPathMeasurement(NativeHotPathArea area, int epoch, long started)
    {
        this.area = area;
        this.epoch = epoch;
        this.started = started;
        measured = true;
    }

    public void Dispose()
    {
        if (measured) NativeHotPathDiagnostics.RecordTiming(area, epoch, started);
    }
}
#endif
