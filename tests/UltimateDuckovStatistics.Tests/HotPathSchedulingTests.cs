using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Tests;

public sealed class HotPathSchedulingTests
{
    [Fact]
    [Trait("Category", "Performance")]
    public void PatchInspectionCoversEachRegistrationOncePerCycleWithoutBursting()
    {
        var scheduler = new IncrementalPatchInspectionScheduler(TimeSpan.FromSeconds(2));
        var origin = new DateTime(2026, 8, 14, 0, 0, 0, DateTimeKind.Utc);
        scheduler.Reset(origin, 7);
        var observed = new List<int>();

        for (var step = 1; step <= 7; step++)
        {
            var due = origin.AddTicks(TimeSpan.FromSeconds(2).Ticks * step / 7 + step);
            Assert.True(scheduler.TryTake(due, 7, out var index));
            observed.Add(index);
            Assert.False(scheduler.TryTake(due, 7, out _));
        }

        Assert.Equal(Enumerable.Range(0, 7), observed);
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void PatchInspectionDoesNotCatchUpMultipleRegistrationsAfterAStall()
    {
        var scheduler = new IncrementalPatchInspectionScheduler(TimeSpan.FromSeconds(2));
        var origin = new DateTime(2026, 8, 14, 0, 0, 0, DateTimeKind.Utc);
        scheduler.Reset(origin, 3);

        Assert.True(scheduler.TryTake(origin.AddSeconds(30), 3, out var index));
        Assert.Equal(0, index);
        Assert.False(scheduler.TryTake(origin.AddSeconds(30), 3, out _));
    }

    [Fact]
    [Trait("Category", "Performance")]
    public void ReferenceScopeStackUsesIdentityAndUnwindsNestedScopesSafely()
    {
        var stack = new ReferenceScopeStack<object>();
        var outer = new object();
        var middle = new object();
        var inner = new object();

        Assert.Same(outer, stack.Push(outer));
        stack.Push(middle);
        stack.Push(inner);
        Assert.Same(inner, stack.Current);

        stack.Pop(middle);

        Assert.Same(outer, stack.Current);
        Assert.Equal(1, stack.Count);
        stack.Pop(outer);
        Assert.Null(stack.Current);
    }

    [Fact]
    [Trait("Category", "Performance")]
    [Trait("Category", "Persistence")]
    public void DeferredCheckpointWriterIsSingleFlightAndFlushesTheExactSubmittedValue()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var written = new List<string>();
        var writer = new DeferredCheckpointWriter<string>(value =>
        {
            entered.Set();
            release.Wait();
            written.Add(value);
        });

        Assert.True(writer.TrySubmit("checkpoint-1"));
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(DeferredWriteState.Pending, writer.Poll().State);
        Assert.False(writer.TrySubmit("checkpoint-2"));
        release.Set();

        Assert.Equal(DeferredWriteState.Succeeded, writer.Flush().State);
        Assert.Equal(["checkpoint-1"], written);
        Assert.Equal(DeferredWriteState.None, writer.Poll().State);
    }

    [Fact]
    [Trait("Category", "Performance")]
    [Trait("Category", "Persistence")]
    public void DeferredCheckpointWriterSurfacesFailureAndAcceptsTheBoundedRetry()
    {
        var attempts = 0;
        var writer = new DeferredCheckpointWriter<string>(_ =>
        {
            attempts++;
            if (attempts == 1) throw new IOException("injected");
        });

        Assert.True(writer.TrySubmit("checkpoint"));
        var failed = writer.Flush();
        Assert.Equal(DeferredWriteState.Failed, failed.State);
        Assert.IsType<IOException>(failed.Exception);
        Assert.True(writer.TrySubmit("checkpoint"));
        Assert.Equal(DeferredWriteState.Succeeded, writer.Flush().State);
        Assert.Equal(2, attempts);
    }

    [Fact]
    [Trait("Category", "Performance")]
    [Trait("Category", "Persistence")]
    public void DeferredCheckpointWriterWaitsWithoutConsumingTheCompletedWrite()
    {
        var written = new List<string>();
        var writer = new DeferredCheckpointWriter<string>(written.Add);

        Assert.True(writer.TrySubmit("checkpoint"));
        Assert.Equal(DeferredWriteState.Succeeded, writer.Wait().State);
        Assert.False(writer.TrySubmit("replacement"));
        Assert.Equal(DeferredWriteState.Succeeded, writer.Poll().State);
        Assert.Equal(["checkpoint"], written);
        Assert.Equal(DeferredWriteState.None, writer.Poll().State);
    }

    [Fact]
    [Trait("Category", "Performance")]
    [Trait("Category", "Persistence")]
    public void DeferredSnapshotWriterCoalescesSameFrameMutationsIntoOneCapturedValue()
    {
        var captures = 0;
        var written = new List<string>();
        var writer = new DeferredSnapshotWriter<string>(
            () => $"snapshot-{++captures}",
            written.Add);

        writer.MarkDirty();
        writer.MarkDirty();

        Assert.Equal(DeferredWriteState.Pending, writer.Tick().State);
        Assert.Equal(DeferredWriteState.Succeeded, writer.Flush().State);
        Assert.Equal(1, captures);
        Assert.Equal(["snapshot-1"], written);
        Assert.False(writer.IsDirty);
    }

    [Fact]
    [Trait("Category", "Performance")]
    [Trait("Category", "Persistence")]
    public void DeferredSnapshotWriterRetainsDirtyStateWhileSubmissionGateIsClosed()
    {
        var captures = 0;
        var written = new List<string>();
        var writer = new DeferredSnapshotWriter<string>(
            () => $"snapshot-{++captures}",
            written.Add);

        writer.MarkDirty();

        Assert.Equal(DeferredWriteState.None, writer.Tick(allowSubmit: false).State);
        Assert.True(writer.IsDirty);
        Assert.Equal(0, captures);
        Assert.Equal(DeferredWriteState.Pending, writer.Tick(allowSubmit: true).State);
        Assert.Equal(DeferredWriteState.Succeeded, writer.Flush().State);
        Assert.Equal(["snapshot-1"], written);
        Assert.False(writer.IsDirty);
    }

    [Fact]
    [Trait("Category", "Performance")]
    [Trait("Category", "Persistence")]
    public void DeferredSnapshotWriterFlushesAnewerMutationAfterThePendingSnapshot()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var captures = 0;
        var written = new List<string>();
        var writer = new DeferredSnapshotWriter<string>(
            () => $"snapshot-{++captures}",
            value =>
            {
                if (value == "snapshot-1")
                {
                    entered.Set();
                    release.Wait();
                }

                written.Add(value);
            });

        writer.MarkDirty();
        Assert.Equal(DeferredWriteState.Pending, writer.Tick().State);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
        writer.MarkDirty();
        Assert.Equal(DeferredWriteState.Pending, writer.Tick().State);
        release.Set();

        Assert.Equal(DeferredWriteState.Succeeded, writer.Flush().State);
        Assert.Equal(2, captures);
        Assert.Equal(["snapshot-1", "snapshot-2"], written);
        Assert.False(writer.IsDirty);
    }

    [Fact]
    [Trait("Category", "Performance")]
    [Trait("Category", "Persistence")]
    public void DeferredSnapshotWriterRetriesOneFreshSnapshotAtAFlushBoundary()
    {
        var captures = 0;
        var attempts = 0;
        var writer = new DeferredSnapshotWriter<string>(
            () => $"snapshot-{++captures}",
            _ =>
            {
                attempts++;
                if (attempts == 1) throw new IOException("injected");
            });

        writer.MarkDirty();

        Assert.Equal(DeferredWriteState.Succeeded, writer.Flush().State);
        Assert.Equal(2, captures);
        Assert.Equal(2, attempts);
        Assert.False(writer.IsDirty);
    }

    [Fact]
    [Trait("Category", "Performance")]
    [Trait("Category", "Persistence")]
    public void DeferredSnapshotWriterRetainsDirtyStateAfterTheBoundedRetryAlsoFails()
    {
        var captures = 0;
        var attempts = 0;
        var writer = new DeferredSnapshotWriter<string>(
            () => $"snapshot-{++captures}",
            _ =>
            {
                attempts++;
                if (attempts <= 2) throw new IOException($"injected-{attempts}");
            });

        writer.MarkDirty();

        var failed = writer.Flush();
        Assert.Equal(DeferredWriteState.Failed, failed.State);
        var aggregate = Assert.IsType<AggregateException>(failed.Exception);
        Assert.Equal(2, aggregate.InnerExceptions.Count);
        Assert.Equal(2, captures);
        Assert.Equal(2, attempts);
        Assert.True(writer.IsDirty);

        Assert.Equal(DeferredWriteState.Succeeded, writer.Flush().State);
        Assert.Equal(3, captures);
        Assert.Equal(3, attempts);
        Assert.False(writer.IsDirty);
    }

    [Fact]
    [Trait("Category", "Performance")]
    [Trait("Category", "Persistence")]
    public void DeferredSnapshotWriterRetainsDirtyStateWhenSnapshotCaptureFails()
    {
        var captures = 0;
        var written = new List<string>();
        var writer = new DeferredSnapshotWriter<string>(
            () =>
            {
                captures++;
                if (captures == 1) throw new InvalidOperationException("injected");
                return $"snapshot-{captures}";
            },
            written.Add);

        writer.MarkDirty();

        var failed = writer.Tick();
        Assert.Equal(DeferredWriteState.Failed, failed.State);
        Assert.IsType<InvalidOperationException>(failed.Exception);
        Assert.True(writer.IsDirty);

        Assert.Equal(DeferredWriteState.Pending, writer.Tick().State);
        Assert.Equal(DeferredWriteState.Succeeded, writer.Flush().State);
        Assert.Equal(2, captures);
        Assert.Equal(["snapshot-2"], written);
        Assert.False(writer.IsDirty);
    }

    [Fact]
    [Trait("Category", "Performance")]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M9")]
    public void EconomyProfileSnapshotFailureRetriesWithTheExactBoundedReplayCursor()
    {
        using var directory = new TemporaryDirectory();
        var path = Path.Combine(directory.Path, "profile.json");
        var aggregate = new EconomyStatisticsAggregate();
        Assert.True(EconomyStatisticsReducer.Record(aggregate, "generation", new CurrencyFlowRecorded
        {
            EventId = "economy:retry-activation:1",
            TimestampUtc = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc),
            SaveGenerationId = "generation",
            MapId = MapIdentity.UnknownId,
            Currency = CurrencyKind.Money,
            Direction = CurrencyFlowDirection.Inflow,
            Amount = 17,
            Source = CurrencySourceCategory.UnknownAdjustment,
            GameplayContext = GameplayContext.Base,
            ProducerActivationId = "retry-activation",
            ProducerSequence = 1
        }));
        var attempts = 0;
        var store = new AtomicJsonStore<ProfileDocument>();
        var writer = new DeferredSnapshotWriter<ProfileDocument>(
            () => new ProfileDocument
            {
                GenerationId = "generation",
                Slot = 1,
                CreatedUtc = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc),
                UpdatedUtc = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc),
                Identity = new SaveIdentitySnapshot { Slot = 1 },
                Statistics = new ProfileStatistics
                {
                    SaveGenerationId = "generation",
                    CreatedUtc = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc),
                    UpdatedUtc = new DateTime(2026, 8, 15, 12, 0, 0, DateTimeKind.Utc),
                    Economy = EconomyStatisticsReducer.Clone(aggregate)
                }
            },
            snapshot =>
            {
                attempts++;
                if (attempts == 1) throw new IOException("injected profile failure");
                store.Save(path, snapshot);
            });

        writer.MarkDirty();
        Assert.Equal(DeferredWriteState.Succeeded, writer.Flush().State);
        Assert.Equal(2, attempts);
        var persisted = store.Load(path).Value!.Statistics.Economy;
        Assert.Equal(17, persisted.Currencies["Money"].Totals.GrossInflow);
        Assert.Equal("retry-activation", persisted.ReplayCursor!.ActivationId);
        Assert.Equal(1, persisted.ReplayCursor.ClosedThroughSequence);
        Assert.Empty(persisted.RecentEventIds);
    }
}
