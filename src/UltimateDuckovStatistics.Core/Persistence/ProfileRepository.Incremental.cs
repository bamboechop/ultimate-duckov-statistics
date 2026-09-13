using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Core.Persistence;

public sealed partial class ProfileRepository
{
    private readonly Func<string, IIncrementalProfileStorage>? createIncrementalStorage;
    private readonly ProfileRecordCodec recordCodec;
    private IIncrementalProfileStorage? incrementalStorage;
    private ProfileChangeJournal? changes;
    private SessionCheckpoint? recoveredSession;
    private bool recoveredSessionEvidence;
    private ActiveRunCheckpoint? recoveredCheckpoint;

    public bool UsesIncrementalStorage => createIncrementalStorage != null;

    // Resource release does not claim a clean durable shutdown. CloseClean owns
    // that acknowledgement; a failed/abandoned owner leaves recovery markers.
    public void Dispose() => CloseIncrementalStorage();

    private AtomicJsonLoadResult<ProfileDocument> LoadProfile(string profilePath)
    {
        var databasePath = Path.Combine(Path.GetDirectoryName(profilePath)!, "profile.sqlite");
        if (createIncrementalStorage == null || !File.Exists(databasePath) && !File.Exists(databasePath + ".recovery"))
            return profileStore.Load(profilePath, ProfileFormat.ValidateRecoveryCandidate, repairPrimary: !UsesIncrementalStorage);
        incrementalStorage = createIncrementalStorage(databasePath);
        var state = incrementalStorage.Load() ?? throw new InvalidDataException("An existing SQLite profile has no committed state.");
        recoveredSession = state.Session;
        recoveredSessionEvidence = state.SessionEvidencePresent;
        recoveredCheckpoint = state.Checkpoint;
        changes = new ProfileChangeJournal(state.Profile.GenerationId, recordCodec);
        return new AtomicJsonLoadResult<ProfileDocument>(state.Profile, AtomicJsonLoadSource.Primary, Array.Empty<string>(), false);
    }

    private void EnsureIncrementalStorage()
    {
        if (incrementalStorage != null) return;
        if (createIncrementalStorage == null || currentDirectory == null) throw new InvalidOperationException("Incremental storage is unavailable.");
        var storage = createIncrementalStorage(Path.Combine(currentDirectory, "profile.sqlite"));
        try
        {
            if (File.Exists(storage.Path) || File.Exists(storage.Path + ".recovery"))
            {
                var restored = storage.Load() ?? throw new InvalidDataException("Restored SQLite generation is empty.");
                if (restored.Profile.GenerationId != Current.GenerationId) throw new InvalidDataException("Restored SQLite generation does not match its owner.");
                recoveredSession = restored.Session; recoveredCheckpoint = restored.Checkpoint;
                recoveredSessionEvidence = restored.SessionEvidencePresent;
                if (Current.Statistics.Runs is ICommittedRunHistory retainedHistory)
                    retainedHistory.RestoreDetailSource(restored.Profile.Statistics.Runs);
                // A suspended reset retains its journal, including publications
                // accepted while a failed owner restoration awaits retry.
                changes ??= new ProfileChangeJournal(Current.GenerationId, recordCodec);
                incrementalStorage = storage;
                return;
            }
            // Import is a one-time recovery boundary. The current reader retains
            // its primary/backup/temporary selection and repair semantics.
            var session = sessionStore.Load(GetSessionPath(currentDirectory), null, repairPrimary: false);
            var checkpoint = activeRunStore.Load(GetActiveRunPath(currentDirectory), ValidateActiveRunCheckpointForRecovery, repairPrimary: false);
            var sessionPath = GetSessionPath(currentDirectory);
            var sessionEvidence = File.Exists(sessionPath) || File.Exists(AtomicJsonPaths.GetBackupPath(sessionPath)) || File.Exists(AtomicJsonPaths.GetTemporaryPath(sessionPath));
            storage.Import(Current, session.Value, checkpoint.Value, sessionEvidence);
            recoveredSession = session.Value;
            recoveredSessionEvidence = sessionEvidence;
            recoveredCheckpoint = checkpoint.Value;
            changes = new ProfileChangeJournal(Current.GenerationId, recordCodec);
            incrementalStorage = storage;
        }
        catch { storage.Dispose(); throw; }
    }

    private ProfilePersistenceSnapshot CaptureIncremental(SessionCheckpoint? session = null, bool sessionChanged = false,
        ActiveRunCheckpoint? checkpoint = null, bool checkpointChanged = false)
    {
        pendingUserResetRollback?.Restore();
        EnsureIncrementalStorage();
        var journal = changes!;
        var capturedHistory = Current.Statistics.Runs as Statistics.ICommittedRunHistory;
        var command = journal.Capture(Current, session, sessionChanged, checkpoint, checkpointChanged);
        // Enqueue while the caller still owns the capture boundary. The outer
        // deferred writers may wait later; they cannot reorder checkpoint and
        // matching lifetime state by scheduling different worker tasks.
        var committed = incrementalStorage!.Commit(command);
        var acknowledged = committed.ContinueWith(task =>
        {
            task.GetAwaiter().GetResult();
            journal.Acknowledge(command);
            if (command.ReplaceRunStatistics)
            {
                var pending = System.Threading.Volatile.Read(ref pendingRunCorrection);
                if (pending != null && ReferenceEquals(pending.Owner, journal))
                    Interlocked.CompareExchange(ref pendingRunCorrection, null, pending);
            }
            if (capturedHistory != null)
                foreach (var record in command.Records.Where(record => record.Address.Kind == ProfileRecordKind.CompletedRun))
                    capturedHistory.ReleaseCommittedDetail(record.Address.First);
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        return new ProfilePersistenceSnapshot(incrementalStorage.Path, command.GenerationId, command.Revision, acknowledged);
    }

    public ProfilePersistenceSnapshot CaptureActiveRunPersistence(ActiveRunCheckpoint checkpoint)
    {
        if (!UsesIncrementalStorage) throw new InvalidOperationException("Incremental checkpoint capture is unavailable.");
        ValidateActiveRunForSave(checkpoint);
        return CaptureIncremental(checkpoint: checkpoint, checkpointChanged: true);
    }

    public ProfilePersistenceSnapshot CaptureActiveRunPersistence(RunLifecycleTracker tracker, DateTime timestampUtc,
        double monotonicSeconds, RunOutcome? pendingTerminalOutcome = null)
    {
        if (!UsesIncrementalStorage || currentDirectory == null) throw new InvalidOperationException("Incremental checkpoint capture is unavailable.");
        if (tracker == null) throw new ArgumentNullException(nameof(tracker));
        pendingUserResetRollback?.Restore();
        EnsureIncrementalStorage();
        var captured = tracker.CaptureIncrementalCheckpoint(timestampUtc, monotonicSeconds, pendingTerminalOutcome, recordCodec)
            ?? throw new InvalidOperationException("The tracker has no active checkpoint owner.");
        if (captured.Generation != Current.GenerationId) throw new InvalidOperationException("Checkpoint capture belongs to another profile generation.");
        changes!.Checkpoint(captured);
        return CaptureIncremental();
    }

    private void CloseIncrementalStorage()
    {
        try { incrementalStorage?.Dispose(); }
        finally
        {
            incrementalStorage = null; changes = null; recoveredSession = null; recoveredCheckpoint = null; recoveredSessionEvidence = false;
            pendingRunCorrection = null;
        }
    }

    private void SuspendIncrementalStorageForReset()
    {
        // Renaming requires closed handles. Retain mutation ownership until
        // promotion succeeds or the original generation is fully reconnected.
        try { incrementalStorage?.Dispose(); }
        finally { incrementalStorage = null; }
    }

    private void SaveSession(SessionCheckpoint? session)
    {
        if (!UsesIncrementalStorage)
        {
            if (session == null) sessionStore.Delete(GetSessionPath(currentDirectory!));
            else sessionStore.Save(GetSessionPath(currentDirectory!), session);
            return;
        }
        SaveSnapshot(CaptureIncremental(session, sessionChanged: true));
        recoveredSession = session;
        recoveredSessionEvidence = session != null;
    }

    private void PrepareResetStorage(string directory, ProfileDocument next, SessionCheckpoint session)
    {
        if (UsesIncrementalStorage)
        {
            using var prepared = createIncrementalStorage!(Path.Combine(directory, "profile.sqlite"));
            prepared.Import(next, session, null);
        }
        else
        {
            profileStore.Save(GetProfilePath(directory), next);
            sessionStore.Save(GetSessionPath(directory), session);
        }
    }

    private bool CompactReplayOnOpen()
    {
        if (Current.Statistics.Runs is not IIndexedRunHistory indexed)
        {
            if (!ProfileFormat.CompactEconomyReplayEvidenceAfterRecovery(Current)) return false;
            changes?.Import(Current);
            return true;
        }
        var changed = ProfileFormat.CompactTrustedLiveReplay(Current);
        foreach (var row in indexed.Overview.Where(row => row.ReplayCompactionPending))
        {
            var run = indexed.GetById(row.RunId);
            var runChanged = EconomyStatisticsReducer.ClearReplayCursor(run.Economy);
            foreach (var segment in run.Segments) runChanged |= EconomyStatisticsReducer.ClearReplayCursor(segment.Economy);
            if (!runChanged) continue;
            changes?.CompletedReplayCompacted(run.RunId);
            changed = true;
        }
        if (changed) changes?.Import(Current, includeHistory: false);
        return changed;
    }
}
