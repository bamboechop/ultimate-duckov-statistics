using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Tests;

public sealed class PersistenceBackoffTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FrameDrivenCompletionRetainsSummaryAndBoundsAttemptsAndDiagnostics(bool throws)
    {
        var now = 0d;
        var boundary = new NativeRunCompletionBoundary(() => now);
        var summary = new RunSummary { RunId = "pending", Outcome = RunOutcome.Extracted };
        boundary.Begin(summary, "extraction", true);
        var attempts = new List<double>();
        var diagnostics = new List<string>();
        bool Fail(RunSummary observed)
        {
            Assert.Same(summary, observed);
            attempts.Add(now);
            if (throws) throw new IOException("persistence unavailable");
            return false;
        }
        for (var frame = 0; frame < 60 * 180; frame++)
        {
            now = frame / 60d;
            Assert.False(boundary.Retry(Fail, diagnostics.Add));
        }
        Assert.Equal(new double[] { 0, 1, 3, 7, 15, 31, 63, 123 }, attempts);
        Assert.Equal(3, diagnostics.Count);
        Assert.Same(summary, boundary.PendingSummary);
        Assert.Throws<InvalidOperationException>(() => boundary.Begin(new RunSummary(), null, false));
        now = 183;
        Assert.True(boundary.Retry(observed => ReferenceEquals(observed, summary), diagnostics.Add));
        Assert.False(boundary.HasPendingCompletion);
        boundary.Begin(new RunSummary(), null, false);
        Assert.True(boundary.Retry(_ => true, diagnostics.Add));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeferredFailureBudgetIsSharedByTicksFlushesAndNewDirtyNotifications(bool captureFails)
    {
        var now = 0d;
        var unavailable = true;
        var captures = 0;
        var writes = 0;
        var writer = new DeferredSnapshotWriter<string>(
            () =>
            {
                captures++;
                if (captureFails && unavailable) throw new InvalidOperationException("invalid snapshot");
                return "retained data";
            },
            _ => { writes++; if (unavailable) throw new IOException("store unavailable"); },
            () => now);
        writer.MarkDirty();
        Assert.Equal(DeferredWriteState.Failed, writer.Flush().State);
        var initialCaptures = captures;
        var initialWrites = writes;
        for (var frame = 0; frame < 10000; frame++)
        {
            writer.MarkDirty();
            Assert.Equal(DeferredWriteState.Failed, writer.Tick().State);
            Assert.Equal(DeferredWriteState.Failed, writer.Flush().State);
        }
        Assert.Equal(initialCaptures, captures);
        Assert.Equal(initialWrites, writes);
        Assert.True(writer.IsDirty);
        now = 1;
        Assert.Equal(DeferredWriteState.Failed, writer.Flush().State);
        unavailable = false;
        now = 2.999;
        Assert.Equal(DeferredWriteState.Failed, writer.Flush().State);
        now = 3;
        Assert.Equal(DeferredWriteState.Succeeded, writer.Flush().State);
        Assert.False(writer.IsDirty);
        writer.MarkDirty();
        Assert.Equal(DeferredWriteState.Succeeded, writer.Flush().State);
    }
}
