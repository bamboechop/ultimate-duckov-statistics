using System.Globalization;
using UltimateDuckovStatistics.Adapters;

namespace UltimateDuckovStatistics.Tests;

[Collection(NativeHotPathDiagnosticsTestGroup.CollectionName)]
public sealed class NativeHotPathTimingTests
{
    [Fact]
    public void NestedAndExceptionalScopesStaySeparateAndCloseWithoutSwallowingExceptions()
    {
        NativeHotPathDiagnostics.Reset();
        var expected = new InvalidOperationException("native callback failure");
        Assert.Same(expected, Assert.Throws<InvalidOperationException>((Action)(() =>
        {
            using var parent = NativeHotPathDiagnostics.Measure(NativeHotPathArea.Update);
            using var child = NativeHotPathDiagnostics.Measure(NativeHotPathArea.Equipment);
            throw expected;
        })));
        var summary = Summary();
        var parentRow = Row(summary, "Update");
        var childRow = Row(summary, "Equipment");
        Assert.Equal(1, parentRow[0]);
        Assert.Equal(1, childRow[0]);
        Assert.True(parentRow[1] >= childRow[1]);
        Assert.Equal(parentRow[1], parentRow[2]);
        Assert.Equal(childRow[1], childRow[2]);
        Assert.Equal(0, Row(summary, "CombatEffectUpdate")[0]);
    }

    [Fact]
    public void ResetAndSummaryExcludeScopesCrossingTheIntervalBoundary()
    {
        NativeHotPathDiagnostics.Reset();
        var beforeReset = NativeHotPathDiagnostics.Measure(NativeHotPathArea.Equipment);
        NativeHotPathDiagnostics.Reset();
        beforeReset.Dispose();
        using (NativeHotPathDiagnostics.Measure(NativeHotPathArea.Equipment)) { }
        var acrossSummary = NativeHotPathDiagnostics.Measure(NativeHotPathArea.Update);
        var summary = Summary();
        acrossSummary.Dispose();
        using (NativeHotPathDiagnostics.Measure(NativeHotPathArea.Equipment)) { }
        Assert.Equal(1, Row(summary, "Equipment")[0]);
        Assert.Equal(0, Row(summary, "Update")[0]);
        var repeated = new List<string>();
        NativeHotPathDiagnostics.WriteSummary(repeated.Add);
        Assert.Empty(repeated);
        NativeHotPathDiagnostics.Reset();
        Assert.Equal(0, Row(Summary(), "Equipment")[0]);
    }

    [Fact]
    public void OtherThreadsAreReportedButDoNotMixTheirTimesIntoUnityThreadMeasurements()
    {
        NativeHotPathDiagnostics.Reset();
        var thread = new Thread(() =>
        {
            using var timing = NativeHotPathDiagnostics.Measure(NativeHotPathArea.GameClockCallback);
        });
        thread.Start();
        thread.Join();
        using (NativeHotPathDiagnostics.Measure(NativeHotPathArea.GameClockCallback)) { }
        var summary = Summary();
        Assert.Contains("otherThreadCalls=1", summary, StringComparison.Ordinal);
        Assert.Equal(1, Row(summary, "GameClockCallback")[0]);
    }

    [Fact]
    public void ResetLoggingIsOutsideTheControlledGcInterval()
    {
        NativeHotPathDiagnostics.HandleControl(true, false, _ => GC.Collect(2, GCCollectionMode.Forced, blocking: true));
        var baseline = GC.CollectionCount(2);
        var summary = Summary();
        var gen2 = long.Parse(summary.Split(' ').Single(part => part.StartsWith("gc2=", StringComparison.Ordinal))[4..], CultureInfo.InvariantCulture);
        Assert.Equal(GC.CollectionCount(2) - baseline, gen2);
    }

    [Fact]
    public void SteadyTimingDoesNotAllocatePerCallback()
    {
        NativeHotPathDiagnostics.Reset();
        for (var i = 0; i < 100; i++)
        {
            using var timing = NativeHotPathDiagnostics.Measure(NativeHotPathArea.CombatEffectTick);
        }
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10_000; i++)
        {
            using var timing = NativeHotPathDiagnostics.Measure(NativeHotPathArea.CombatEffectTick);
        }
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.Equal(10_100, Row(Summary(), "CombatEffectTick")[0]);
    }

    private static string Summary()
    {
        var messages = new List<string>();
        NativeHotPathDiagnostics.WriteSummary(messages.Add);
        return Assert.Single(messages);
    }

    private static long[] Row(string summary, string area) => summary.Split(' ')
        .Single(part => part.StartsWith(area + "=", StringComparison.Ordinal))[(area.Length + 1)..]
        .Split(',').Select(value => long.Parse(value, CultureInfo.InvariantCulture)).ToArray();
}
