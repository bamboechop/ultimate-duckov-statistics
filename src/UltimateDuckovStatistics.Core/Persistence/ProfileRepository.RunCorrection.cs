using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Persistence;

/// <summary>A validated, detached correction. Only its originating repository can accept it.</summary>
public sealed class PreparedRunCorrection
{
    internal PreparedRunCorrection(object owner, string generation, long revision, RunSummary run,
        ProfileStatistics rebuilt, ProfileRecordChange[] records)
    { Owner = owner; GenerationId = generation; ExpectedRevision = revision; Run = run; Rebuilt = rebuilt; Records = records; }
    public string GenerationId { get; }
    public long ExpectedRevision { get; }
    public string RunId => Run.RunId;
    internal object Owner { get; }
    internal RunSummary Run { get; }
    internal ProfileStatistics Rebuilt { get; }
    internal ProfileRecordChange[] Records { get; }
    internal bool Accepted { get; set; }
}

public sealed partial class ProfileRepository
{
    private PreparedRunCorrection? pendingRunCorrection;

    public Task<PreparedRunCorrection> PrepareRunCorrection(RunSummary replacement)
    {
        if (!UsesIncrementalStorage) throw new InvalidOperationException("Prepared run corrections require incremental storage.");
        if (replacement == null) throw new ArgumentNullException(nameof(replacement));
        ProfileFormat.ValidateRecordMembers(replacement); RunReducer.Validate(replacement);
        if (replacement.SaveGenerationId != Current.GenerationId || !RunHistory.ContainsId(Current.Statistics.Runs, replacement.RunId))
            throw new ArgumentException("Correction does not identify a completed run in this generation.", nameof(replacement));
        // A single replacement is frozen under caller ownership. History replay
        // and all large record encoding occur after detached capture on a worker.
        var bytes = recordCodec.Encode(replacement);
        Flush();
        var owner = changes!;
        var capture = CaptureExportSnapshotAsync();
        return capture.ContinueWith(completed =>
        {
            using var snapshot = completed.GetAwaiter().GetResult();
            var document = snapshot.Document;
            var owned = ProfileRecordCodec.Decode<RunSummary>(bytes);
            var previous = RunReducer.RebuildRunStatistics(document.GenerationId, document.Statistics.Runs);
            if (!recordCodec.Encode(previous.RunTotals).SequenceEqual(recordCodec.Encode(document.Statistics.RunTotals))
                || !recordCodec.Encode(previous.RunRecords).SequenceEqual(recordCodec.Encode(document.Statistics.RunRecords)))
                throw new InvalidOperationException("Retained runs do not exactly explain the current run-derived summaries; correction cannot replace independent or incomplete evidence.");
            var rebuilt = RunReducer.RebuildRunStatistics(document.GenerationId,
                document.Statistics.Runs.Select(run => run.RunId == owned.RunId ? owned : run));
            document.Statistics.RunTotals = rebuilt.RunTotals;
            document.Statistics.RunRecords = rebuilt.RunRecords;
            // Lifetime completeness may include evidence outside completed runs.
            // A correction cannot turn that authoritative false into true.
            document.Statistics.HealingCaptureComplete &= rebuilt.HealingCaptureComplete;
            rebuilt.HealingCaptureComplete = document.Statistics.HealingCaptureComplete;
            var journal = new ProfileChangeJournal(document.GenerationId, recordCodec);
            var records = journal.PrepareCorrectedRunRecords(document, owned);
            return new PreparedRunCorrection(owner, document.GenerationId, document.Revision, owned, rebuilt, records);
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    public void ApplyRunCorrection(PreparedRunCorrection correction)
    {
        if (correction == null) throw new ArgumentNullException(nameof(correction));
        if (!ReferenceEquals(correction.Owner, changes) || correction.GenerationId != Current.GenerationId)
            throw new InvalidOperationException("Correction belongs to a different repository generation.");
        if (correction.Accepted)
        {
            if (ReferenceEquals(pendingRunCorrection, correction)) SaveSnapshot(CaptureIncremental());
            return;
        }
        if (Current.Revision != correction.ExpectedRevision)
            throw new InvalidOperationException("Profile changed while the correction was prepared; prepare it again.");
        var nextRevision = checked(Current.Revision + 1);
        if (Current.Statistics.Runs is ICorrectableRunHistory history) history.AcceptCorrection(correction.Run);
        else
        {
            var index = RunHistory.Overview(Current.Statistics.Runs).Select((run, index) => (run, index)).Single(value => value.run.RunId == correction.RunId).index;
            Current.Statistics.Runs[index] = correction.Run;
        }
        Current.Statistics.RunTotals = correction.Rebuilt.RunTotals;
        Current.Statistics.RunRecords = correction.Rebuilt.RunRecords;
        Current.Statistics.HealingCaptureComplete = correction.Rebuilt.HealingCaptureComplete;
        Current.Revision = nextRevision; Current.UpdatedUtc = EnsureUtc(utcNow());
        changes!.ApplyPreparedRunCorrection(correction.Records);
        correction.Accepted = true;
        pendingRunCorrection = correction;
        // Failure leaves both the accepted in-memory correction and its complete
        // changed-record union owned for normal persistence retry/backoff.
        SaveSnapshot(CaptureIncremental());
    }
}
