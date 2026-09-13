using UltimateDuckovStatistics.Adapters;

namespace UltimateDuckovStatistics.Tests;

public sealed class DeferredSnapshotBoundaryTests
{
    [Fact]
    public async Task BoundaryJoinsSharedOldSnapshotBeforePreparingAndCapturingLatest()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var prepared = new ManualResetEventSlim();
        var current = "old";
        var writes = new List<string>();
        var writer = new DeferredSnapshotWriter<string>(() => current, value =>
        {
            if (value == "old")
            {
                entered.Set();
                if (!release.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException();
            }
            writes.Add(value);
        });
        writer.MarkDirty();
        Assert.Equal(DeferredWriteState.Pending, writer.Tick().State);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        writer.MarkDirty();
        var boundary = Task.Run(() => writer.FlushForBoundary(() =>
        {
            Assert.Equal(["old"], writes);
            current = "latest";
            prepared.Set();
            return true;
        }));
        try { Assert.False(prepared.Wait(TimeSpan.FromMilliseconds(50))); }
        finally { release.Set(); }
        Assert.Equal(DeferredWriteState.Succeeded, (await boundary).State);
        Assert.Equal(["old", "latest"], writes);
        Assert.False(writer.IsDirty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreparationRunsOnceAcrossOldOrLatestStoreFailure(bool oldPending)
    {
        var current = "old";
        var attempts = new List<string>();
        var preparations = 0;
        var writer = new DeferredSnapshotWriter<string>(() => current, value =>
        {
            attempts.Add(value);
            if (attempts.Count == 1) throw new IOException("first attempt");
        });
        writer.MarkDirty();
        if (oldPending) Assert.Equal(DeferredWriteState.Pending, writer.Tick().State);
        Assert.Equal(DeferredWriteState.Succeeded, writer.FlushForBoundary(() =>
        {
            preparations++;
            current = "latest";
            writer.MarkDirty();
            return true;
        }).State);
        Assert.Equal(1, preparations);
        Assert.Equal(2, attempts.Count);
        Assert.Equal(oldPending ? "old" : "latest", attempts[0]);
        Assert.Equal("latest", attempts[1]);
        Assert.False(writer.HasFailure);
        Assert.False(writer.IsDirty);
    }

    [Fact]
    public void FailedBoundarySharesBackoffWithTickAndLaterBoundaries()
    {
        var now = 0d;
        var writes = 0;
        var preparations = 0;
        var fail = true;
        var writer = new DeferredSnapshotWriter<string>(() => "retained intent", _ =>
        {
            writes++;
            if (fail) throw new IOException("blocked storage");
        }, () => now);
        bool Prepare() { preparations++; writer.MarkDirty(); return true; }
        Assert.Equal(DeferredWriteState.Failed, writer.FlushForBoundary(Prepare).State);
        for (var i = 0; i < 100; i++)
        {
            Assert.Equal(DeferredWriteState.Failed, writer.Tick().State);
            Assert.Equal(DeferredWriteState.Failed, writer.FlushForBoundary(Prepare).State);
        }
        Assert.Equal(1, preparations);
        Assert.Equal(2, writes);
        Assert.True(writer.IsDirty);
        fail = false;
        now = 1;
        Assert.Equal(DeferredWriteState.Succeeded, writer.FlushForBoundary(Prepare).State);
        Assert.Equal(2, preparations);
        Assert.Equal(3, writes);
        Assert.False(writer.IsDirty);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CheckpointRefusalRetainsDirtyDataAndAnOlderWriteFailure(bool oldWriteFails)
    {
        var now = 0d;
        var attempts = 0;
        var writer = new DeferredSnapshotWriter<string>(() => "snapshot", _ =>
        {
            attempts++;
            if (oldWriteFails && attempts == 1) throw new IOException("old snapshot");
        }, () => now);
        writer.MarkDirty();
        Assert.Equal(DeferredWriteState.Pending, writer.Tick().State);
        var blocked = writer.FlushForBoundary(() => false);
        Assert.Equal(oldWriteFails ? DeferredWriteState.Failed : DeferredWriteState.Pending, blocked.State);
        Assert.Equal(1, attempts);
        Assert.True(writer.IsDirty);
        now = 1;
        Assert.Equal(DeferredWriteState.Succeeded, writer.FlushForBoundary(() => true).State);
        Assert.Equal(2, attempts);
        Assert.False(writer.IsDirty);
    }

    [Fact]
    public void ReentrantFlushAndTickCannotAcknowledgeOrCaptureUnpreparedState()
    {
        var captures = 0;
        var preparations = 0;
        var writer = new DeferredSnapshotWriter<string>(() => { captures++; return "snapshot"; }, _ => { });
        var result = writer.FlushForBoundary(() =>
        {
            preparations++;
            writer.MarkDirty();
            Assert.Equal(DeferredWriteState.Pending, writer.Flush().State);
            Assert.Equal(DeferredWriteState.Pending, writer.Tick().State);
            Assert.Equal(DeferredWriteState.Pending, writer.FlushForBoundary(() => throw new InvalidOperationException()).State);
            Assert.Equal(0, captures);
            return true;
        });
        Assert.Equal(DeferredWriteState.Succeeded, result.State);
        Assert.Equal(1, preparations);
        Assert.Equal(1, captures);
    }
}
