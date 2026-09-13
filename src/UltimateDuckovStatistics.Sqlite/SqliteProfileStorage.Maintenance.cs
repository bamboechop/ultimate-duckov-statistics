using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Sqlite;

public sealed partial class SqliteProfileStorage
{
    private Task<ProfileMaintenanceResult>? maintenanceTask;
    private readonly CancellationTokenSource maintenanceCancellation = new();
    private const long FallbackWalBytes = 16 * 1024 * 1024;

    public Task<ProfileMaintenanceResult> RequestMaintenance(ProfileMaintenanceReason reason)
    {
        lock (gate)
        {
            if (disposed) throw new ObjectDisposedException(nameof(SqliteProfileStorage));
            if (maintenanceTask is { IsCompleted: false }) return maintenanceTask;
            maintenanceTask = Task.Run(() =>
            {
                var wal = Path + "-wal";
                if (maintenanceCancellation.IsCancellationRequested || !File.Exists(wal)
                    || reason == ProfileMaintenanceReason.LongSession && new FileInfo(wal).Length < FallbackWalBytes)
                    return new ProfileMaintenanceResult();
                // A separate connection/worker never enters the durability tail.
                // PASSIVE does not wait for readers/writers and does not remove
                // frames still needed by a pinned export snapshot.
                using var maintenance = new SqliteStore(Path);
                maintenance.DisableAutomaticCheckpoint();
                maintenance.Exec("PRAGMA synchronous=FULL");
                if (maintenanceCancellation.IsCancellationRequested) return new ProfileMaintenanceResult();
                var row = maintenance.Rows("PRAGMA wal_checkpoint(PASSIVE)").Single();
                return new ProfileMaintenanceResult
                {
                    Attempted = true,
                    Busy = (long)row[0] != 0,
                    WalFrames = (long)row[1],
                    CheckpointedFrames = (long)row[2]
                };
            });
            return maintenanceTask;
        }
    }

    private void StopMaintenance()
    {
        maintenanceCancellation.Cancel();
        try { maintenanceTask?.GetAwaiter().GetResult(); }
        catch (Exception) { /* The requester observes maintenance failure; it cannot claim or undo a durable write. */ }
        maintenanceCancellation.Dispose();
    }
}
