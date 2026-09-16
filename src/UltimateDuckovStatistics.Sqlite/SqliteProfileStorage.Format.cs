namespace UltimateDuckovStatistics.Sqlite;

public sealed partial class SqliteProfileStorage
{
    private void PrepareFormat(SqliteStore db)
    {
        var version = db.ScalarLong("PRAGMA user_version");
        if (version == StorageVersion) return;
        if (version == 7 && db.ScalarLong("SELECT complete FROM profile_state WHERE id=1") == 1)
        {
            // New independently addressed record kinds; existing compressed
            // payloads and their hashes remain untouched. No history backfill.
            db.Exec("BEGIN IMMEDIATE");
            try { db.Exec("PRAGMA user_version=" + StorageVersion); db.Exec("COMMIT"); }
            catch { TryRollback(db); throw; }
            return;
        }
        if (version != 6 || db.ScalarLong("SELECT complete FROM profile_state WHERE id=1") != 1)
            throw new InvalidDataException("SQLite profile version or import marker is invalid.");
        if (db.ScalarText("PRAGMA quick_check") != "ok") throw new InvalidDataException("SQLite profile integrity check failed.");

        // Only the actual preceding SQLite format is converted. Before the
        // generation opens, checkpoint its old WAL and use a durable rollback
        // journal so conversion/compaction do not leave a second full-size WAL.
        db.Configure("DELETE");
        db.Exec("BEGIN IMMEDIATE");
        try
        {
            long ordinal = 0;
            while (true)
            {
                // Finish each cursor before updating; retain just one payload
                // at a time and preserve stable row IDs and all logical hashes.
                var rows = db.Rows("SELECT ordinal,payload,payload_sha FROM records WHERE ordinal>? ORDER BY ordinal LIMIT 1", ordinal);
                if (rows.Count == 0) break;
                ordinal = (long)rows[0][0];
                var original = (byte[])rows[0][1];
                VerifyHash(original, (byte[])rows[0][2]);
                var packed = SqliteRecordPayload.Encode(original);
                if (!ReferenceEquals(original, packed)) db.Exec("UPDATE records SET payload=? WHERE ordinal=?", packed, ordinal);
            }
            db.Exec("PRAGMA user_version=" + StorageVersion);
            db.Exec("COMMIT");
        }
        catch { TryRollback(db); throw; }

        // One-time space reclamation, never part of an ordinary save. Failure
        // here cannot undo the durable conversion; SQLite retains valid free
        // pages for reuse. Report it without blocking a readable generation.
        try { db.Exec("VACUUM"); }
        catch (SqliteFailure exception)
        { diagnostic?.Invoke("SQLite records were compressed; one-time space reclamation could not complete (SQLite " + exception.Code + "). Freed pages remain available for reuse."); }
    }

    internal static byte[]? ReadRootPayload(SqliteStore db, int kind)
    {
        var rows = db.Rows("SELECT payload,payload_sha FROM records WHERE kind=? AND k1='' AND k2='' AND k3=''", kind);
        return rows.Count == 0 ? null : DecodePayload((byte[])rows[0][0], (byte[])rows[0][1]);
    }

    private static byte[] DecodePayload(byte[] stored, byte[] expectedHash)
    {
        var bytes = SqliteRecordPayload.Decode(stored);
        VerifyHash(bytes, expectedHash);
        return bytes;
    }
}
