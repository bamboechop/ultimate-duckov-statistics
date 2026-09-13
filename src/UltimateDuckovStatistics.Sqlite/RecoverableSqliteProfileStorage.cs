using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Sqlite;

/// <summary>Independent incremental recovery copy; acknowledgement requires both durable stores.</summary>
public sealed class RecoverableSqliteProfileStorage : IIncrementalProfileStorage, IProfileExportSource, IProfileStorageMaintenance
{
    private readonly object gate = new();
    private readonly ProfileRecordCodec codec;
    private readonly FileStream ownership;
    private readonly string? exportRoot;
    private SqliteProfileStorage primary;
    private SqliteProfileStorage replica;
    private Task tail = Task.CompletedTask;
    private bool disposed;
    public string Path { get; }
    public string RecoveryPath => Path + ".recovery";

    public RecoverableSqliteProfileStorage(string path, ProfileRecordCodec codec, string? exportRoot = null)
    {
        Path = System.IO.Path.GetFullPath(path); this.codec = codec ?? throw new ArgumentNullException(nameof(codec));
        this.exportRoot = exportRoot;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        ownership = new FileStream(Path + ".pair-owner", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        SqliteProfileStorage? created = null;
        try { primary = created = new SqliteProfileStorage(Path, codec, exportRoot); replica = new SqliteProfileStorage(RecoveryPath, codec, exportRoot); }
        catch { created?.Dispose(); ownership.Dispose(); throw; }
    }

    public IncrementalProfileState? Load() => Queue(() =>
    {
        IncrementalProfileState? current = null; Exception? primaryFailure = null;
        try { current = primary.Load(); }
        catch (Exception exception) when (IsRecoverableReadFailure(exception)) { primaryFailure = exception; }
        if (current == null)
        {
            if (!File.Exists(RecoveryPath))
            { if (primaryFailure != null) throw new InvalidDataException("Primary SQLite profile failed and no recovery database exists.", primaryFailure); return null; }
            // Promotion validates the entire recovery candidate, independently
            // of the lazy history index, before replacing any primary evidence.
            var stage = Path + ".recover-" + Guid.NewGuid().ToString("N");
            replica.CreateValidatedCopy(stage);
            CloseIgnoringObservedFailure(primary);
            PreserveEvidence(Path);
            File.Move(stage, Path);
            primary = new SqliteProfileStorage(Path, codec, exportRoot);
            current = primary.Load() ?? throw new InvalidDataException("Recovered SQLite profile is empty.");
        }
        IncrementalProfileState? recovery = null;
        try { recovery = replica.Load(); }
        catch (Exception exception) when (IsRecoverableReadFailure(exception)) { /* Preserve and rebuild below from the valid primary. */ }
        if (recovery == null || !primary.HasSameCommittedBoundary(replica))
        {
            // A replica ahead of, or from another generation than, the valid
            // primary is ambiguous. Never silently choose a generation here.
            if (recovery != null && (recovery.Profile.GenerationId != current.Profile.GenerationId || recovery.Profile.Revision > current.Profile.Revision))
                throw new InvalidDataException("SQLite primary and recovery ownership diverged.");
            RebuildReplica();
        }
        return current;
    }).GetAwaiter().GetResult();

    public void Import(ProfileDocument profile, SessionCheckpoint? session, ActiveRunCheckpoint? checkpoint, bool sessionEvidencePresent = false) => Queue(() =>
    {
        if (File.Exists(RecoveryPath)) throw new IOException("Import cannot overwrite an existing SQLite recovery database.");
        primary.Import(profile, session, checkpoint, sessionEvidencePresent);
        RebuildReplica(); return true;
    }).GetAwaiter().GetResult();

    public Task Commit(IncrementalProfileWrite write) => Queue(() =>
    {
        // Each command carries its full unacknowledged changed-entry union.
        // If primary succeeded but replica failed, a retry safely covers both.
        primary.Commit(write).GetAwaiter().GetResult();
        replica.Commit(write).GetAwaiter().GetResult();
        return true;
    });
    public Task Drain() { lock (gate) return tail; }

    public Task<ProfileExportSnapshot> CaptureExport(string generation, long revision) =>
        Queue(() => primary.CaptureExport(generation, revision)).Unwrap();

    public Task<ProfileMaintenanceResult> RequestMaintenance(ProfileMaintenanceReason reason)
    {
        lock (gate)
        {
            if (disposed) throw new ObjectDisposedException(nameof(RecoverableSqliteProfileStorage));
            // Starting both workers is cheap and independent of the save queue.
            // Each store coalesces duplicate requests and retains its own lease.
            return Task.WhenAll(primary.RequestMaintenance(reason), replica.RequestMaintenance(reason)).ContinueWith(completed =>
            {
                var results = completed.GetAwaiter().GetResult();
                return new ProfileMaintenanceResult
                {
                    Attempted = results.Any(value => value.Attempted),
                    Busy = results.Any(value => value.Busy),
                    WalFrames = results.Sum(value => value.WalFrames),
                    CheckpointedFrames = results.Sum(value => value.CheckpointedFrames)
                };
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
        }
    }

    private void RebuildReplica()
    {
        var stage = RecoveryPath + ".rebuild-" + Guid.NewGuid().ToString("N");
        primary.CreateValidatedCopy(stage);
        CloseIgnoringObservedFailure(replica);
        PreserveEvidence(RecoveryPath);
        File.Move(stage, RecoveryPath);
        replica = new SqliteProfileStorage(RecoveryPath, codec, exportRoot);
    }

    private static bool IsRecoverableReadFailure(Exception exception) => exception is IOException or InvalidDataException
        or ArgumentException or System.Runtime.Serialization.SerializationException;

    private static void PreserveEvidence(string database)
    {
        var directory = database + ".preserved-" + Guid.NewGuid().ToString("N");
        var paths = new[] { database, database + "-wal", database + "-shm", database + ".read-failure" }.Where(File.Exists).ToArray();
        if (paths.Length == 0) return;
        Directory.CreateDirectory(directory);
        foreach (var path in paths) File.Move(path, System.IO.Path.Combine(directory, System.IO.Path.GetFileName(path)));
    }
    private static void CloseIgnoringObservedFailure(SqliteProfileStorage storage)
    { try { storage.Dispose(); } catch (Exception exception) when (IsRecoverableReadFailure(exception)) { /* Dispose releases handles even when the previous read failed. */ } }

    private Task<T> Queue<T>(Func<T> action)
    {
        lock (gate)
        {
            if (disposed) throw new ObjectDisposedException(nameof(RecoverableSqliteProfileStorage));
            var next = tail.ContinueWith(_ => action(), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            tail = next; return next;
        }
    }
    public void Dispose()
    {
        Task pending;
        lock (gate) { if (disposed) return; disposed = true; pending = tail; }
        try { pending.GetAwaiter().GetResult(); }
        finally { try { primary.Dispose(); } finally { try { replica.Dispose(); } finally { ownership.Dispose(); } } }
    }
}
