using UltimateDuckovStatistics.Core.Encounters;
using UltimateDuckovStatistics.Core.Export;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Tracking;
using UltimateDuckovStatistics.Encounters;

namespace UltimateDuckovStatistics.Tests;

public sealed partial class EncounterPersistenceTests
{
    private static readonly string[] FatalIdOrder = ["b", "a", "c"];
    private static readonly long?[] FatalSequenceOrder = [2, 10, 1];
    private static readonly int[] FatalMarkerOrder = [1, 2, 3];

    [Fact]
    public async Task FatalOrderingSurvivesNativeAndPortableCodecsSqliteReopenAndZipRestore()
    {
        var records = Records().Where(record => record.Visit != null).ToList();
        foreach (var (id, time, sequence) in new[] { ("a", 8, 10L), ("b", 8, 2L), ("c", 9, 1L) })
        {
            var actor = Records().Single(record => record.Encounter != null);
            actor.Id = id; actor.Encounter!.ActorId = id;
            actor.Encounter.EndedSeconds = time; actor.Encounter.FatalSequence = sequence;
            records.Add(actor);
        }
        foreach (var writer in new[] { new ProfileRecordCodec(), codec })
            AssertFatalOrder(records.Select(record => ProfileRecordCodec.Decode<EncounterRecord>(writer.Encode(record))));

        using (var source = Repository())
        {
            source.Open(Identity());
            var tracker = IncrementalCheckpointProtocolTests.Started(source.CurrentGenerationId, route: true, runId: "run-1");
            source.CompleteRun(tracker.Apply(new RunLifecycleEvent { Kind = RunLifecycleEventKind.Extracted,
                TimestampUtc = Now.AddMinutes(1), MonotonicSeconds = 20 }).Completed!);
            foreach (var record in records) source.RecordEncounterDeferred(source.CurrentGenerationId, record);
            source.CloseClean();
        }
        using var reopened = Repository(); reopened.Open(Identity());
        AssertFatalOrder(reopened.ReadEncounters("run-1"));
        using var snapshot = await reopened.CaptureExportSnapshotAsync();
        reopened.CloseClean();
        var export = ProfileExportWriter.WriteToRoot(snapshot, Path.Combine(directory.Path, "exports"), Now);
        var preview = StatisticsRestoreReader.Read(Assert.Single(export.Files), 1);
        using (var restored = Repository("restored"))
        {
            restored.Open(Identity());
            restored.RestoreStatistics(Identity(), preview, restored.CurrentGenerationId);
            AssertFatalOrder(restored.ReadEncounters("run-1"));
            restored.CloseClean();
        }
        using var restoredReopen = Repository("restored"); restoredReopen.Open(Identity());
        AssertFatalOrder(restoredReopen.ReadEncounters("run-1"));
        restoredReopen.CloseClean();
    }

    private static void AssertFatalOrder(IEnumerable<EncounterRecord> records)
    {
        var run = StoredEncounterRun.Build(records.Reverse().ToArray());
        // Numeric fatal order breaks ties; a different event time still takes priority.
        Assert.Equal(FatalIdOrder, run.Events.Select(record => record.Id));
        Assert.Equal(FatalSequenceOrder, run.Events.Select(record => record.Encounter!.FatalSequence));
        Assert.Equal(FatalMarkerOrder, Assert.Single(run.Visits).Events.Select(record => run.EventIndex(record.Id) + 1));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(-1, true)]
    [InlineData(1, false)]
    public void InvalidFatalSequenceCannotEnterTheRepository(long sequence, bool hasOutcome)
    {
        using var repository = Repository(); repository.Open(Identity());
        var records = Records();
        repository.RecordEncounterDeferred(repository.CurrentGenerationId, records[0]);
        var actor = records.Single(record => record.Encounter != null);
        actor.Encounter!.FatalSequence = sequence;
        if (!hasOutcome) { actor.Encounter.Outcome = null; actor.Encounter.EndedSeconds = null; }
        Assert.Throws<ArgumentException>(() => repository.RecordEncounterDeferred(repository.CurrentGenerationId, actor));
        Assert.Empty(repository.ReadEncounters("run-1", EncounterRecordKind.Encounter));
        repository.CloseClean();
    }

    [Theory]
    [InlineData(1L, 2L)]
    [InlineData(1L, null)]
    [InlineData(null, 1L)]
    public void PublishedFatalOrderCannotBeChangedOrInvented(long? original, long? replacement)
    {
        var actor = Records().Single(record => record.Encounter != null);
        actor.Encounter!.FatalSequence = original;
        var changed = ProfileRecordCodec.Decode<EncounterRecord>(codec.Encode(actor));
        changed.Encounter!.FatalSequence = replacement;
        Assert.Throws<ArgumentException>(() => EncounterRecordValidation.ValidateReplacement(actor, changed));
    }

    [Fact]
    public void MissingFatalSequenceRemainsUnknownThroughBothCodecs()
    {
        var actor = Records().Single(record => record.Encounter != null);
        foreach (var writer in new[] { new ProfileRecordCodec(), codec })
        {
            var read = ProfileRecordCodec.Decode<EncounterRecord>(writer.Encode(actor));
            EncounterRecordValidation.Validate(read);
            Assert.Null(read.Encounter!.FatalSequence);
        }
    }
}
