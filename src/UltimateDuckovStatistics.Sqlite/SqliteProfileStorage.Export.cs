using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Sqlite;

public sealed partial class SqliteProfileStorage
{
    private readonly CancellationTokenSource exportCancellation = new();
    private Task<string>? exportCopy;
    private Task<ProfileExportSnapshot>? exportResult;

    public Task<ProfileExportSnapshot> CaptureExport(string generation, long revision)
    {
        lock (gate)
        {
            if (exportResult is { IsCompleted: false }) throw new InvalidOperationException("An export snapshot is already being prepared.");
            var reservation = Queue(() =>
            {
                var reader = new SqliteStore(Path, readOnly: true);
                try
                {
                    reader.DisableAutomaticCheckpoint();
                    reader.Exec("BEGIN");
                    // The first read pins the WAL boundary. A later write cannot
                    // change the export, even while its copy runs separately.
                    var state = reader.Rows("SELECT generation,revision FROM profile_state WHERE id=1");
                    if (state.Count != 1 || (string)state[0][0] != generation || (long)state[0][1] != revision)
                        throw new InvalidOperationException("Export revision no longer matches the requested boundary.");
                    return reader;
                }
                catch { reader.Dispose(); throw; }
            });
            exportCopy = reservation.ContinueWith(completed =>
            {
                using var reader = completed.GetAwaiter().GetResult();
                var directory = System.IO.Path.Combine(exportRoot, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                var database = System.IO.Path.Combine(directory, "snapshot.sqlite");
                try
                {
                    exportCancellation.Token.ThrowIfCancellationRequested();
                    reader.BackupTo(database, exportCancellation.Token);
                    reader.Exec("ROLLBACK");
                    exportCancellation.Token.ThrowIfCancellationRequested();
                    return directory;
                }
                catch { RemoveExportCopy(directory, database); throw; }
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            // Decoding the live aggregates and scalar history index no longer
            // holds a reader on the active database or delays its disposal.
            exportResult = exportCopy.ContinueWith(completed =>
            {
                var directory = completed.GetAwaiter().GetResult();
                var database = System.IO.Path.Combine(directory, "snapshot.sqlite");
                SqliteProfileStorage? detached = null;
                try
                {
                    detached = new SqliteProfileStorage(database, codec);
                    using var copiedReader = new SqliteStore(database, readOnly: true);
                    var document = detached.ReadIndexed(copiedReader, includeCheckpoint: false).Profile;
                    var owner = detached;
                    return new ProfileExportSnapshot(document, Path, () =>
                    {
                        try { owner.Dispose(); }
                        finally { RemoveExportCopy(directory, database); }
                    });
                }
                catch
                {
                    detached?.Dispose();
                    RemoveExportCopy(directory, database);
                    throw;
                }
            }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
            return exportResult;
        }
    }

    private void StopExportCopy()
    {
        exportCancellation.Cancel();
        try { exportCopy?.GetAwaiter().GetResult(); }
        catch (Exception) { /* The export caller owns its failure; release the generation's reader before closing. */ }
        exportCancellation.Dispose();
    }

    private static void RemoveExportCopy(string directory, string database)
    {
        // Delete only names created by this operation, never a recursive path or
        // a profile/recovery file. A failed export remains harmless staging data.
        foreach (var suffix in new[] { "", "-wal", "-shm", "-journal", ".owner", ".read-failure" })
            try { File.Delete(database + suffix); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        try { Directory.Delete(directory); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
