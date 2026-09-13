using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Sqlite;

public sealed partial class SqliteProfileStorage
{
    // Startup/import/recovery only. The production save queue must never enqueue
    // this discretionary whole-database work ahead of a native boundary.
    internal void CreateValidatedCopy(string newPath) => Queue(() =>
    {
        var db = Open();
        if (db.ScalarLong("PRAGMA user_version") != StorageVersion || db.ScalarText("PRAGMA integrity_check") != "ok")
            throw new InvalidDataException("Recovery source schema or integrity is invalid.");
        db.BackupTo(newPath);
        using var copied = new SqliteStore(newPath);
        copied.Configure("DELETE");
        var state = Read(copied);
        var failure = ProfileFormat.ValidateRecoveryCandidate(state.Profile);
        if (failure != null) throw new InvalidDataException("SQLite recovery candidate failed profile validation: " + failure);
        if (state.Checkpoint != null) ProfileRepository.ValidateActiveCheckpointForStorage(state.Checkpoint, state.Profile.GenerationId);
        if (copied.ScalarLong("SELECT complete FROM profile_state WHERE id=1") != 1
            || copied.ScalarText("SELECT generation FROM profile_state WHERE id=1") != state.Profile.GenerationId
            || copied.ScalarLong("SELECT revision FROM profile_state WHERE id=1") != state.Profile.Revision)
            throw new InvalidDataException("Recovery source transaction metadata disagrees.");
        if (copied.ScalarText("PRAGMA integrity_check") != "ok") throw new InvalidDataException("Recovery copy integrity failed.");
        return true;
    }).GetAwaiter().GetResult();

    internal bool HasSameCommittedBoundary(SqliteProfileStorage other)
    {
        var left = Queue(() => Boundary(Open())).GetAwaiter().GetResult();
        var right = other.Queue(() => Boundary(other.Open())).GetAwaiter().GetResult();
        return left.SequenceEqual(right);
    }
    private static byte[] Boundary(SqliteStore db)
    {
        using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream);
        writer.Write(db.ScalarText("SELECT generation FROM profile_state WHERE id=1"));
        writer.Write(db.ScalarLong("SELECT revision FROM profile_state WHERE id=1"));
        var receipt = db.Rows("SELECT owner,sequence,through_version,digest FROM receipt WHERE id=1");
        if (receipt.Count != 0) { writer.Write((string)receipt[0][0]); writer.Write((long)receipt[0][1]); writer.Write((long)receipt[0][2]); writer.Write((byte[])receipt[0][3]); }
        writer.Flush(); return stream.ToArray();
    }
}
