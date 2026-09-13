using UltimateDuckovStatistics.Core;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Sqlite;

public sealed partial class SqliteProfileStorage
{
    private string? historyGeneration;
    private static void CreateHistoryIndex(SqliteStore db) => db.Exec(
        "CREATE TABLE history_index(run_id TEXT PRIMARY KEY,ordinal INTEGER NOT NULL,overview BLOB NOT NULL,overview_sha BLOB NOT NULL,record_sha BLOB NOT NULL)");

    private void PutHistoryIndex(SqliteStore db, ProfileRecordChange record)
    {
        if (record.Bytes == null) { db.Exec("DELETE FROM history_index WHERE run_id=?", record.Address.First); return; }
        var run = ProfileRecordCodec.Decode<RunSummary>(record.Bytes);
        var overview = codec.Encode(RunOverview.From(run));
        var ordinal = db.ScalarLong("SELECT ordinal FROM records WHERE kind=17 AND k1=?", run.RunId);
        db.Exec("INSERT INTO history_index VALUES(?,?,?,?,?) ON CONFLICT(run_id) DO UPDATE SET overview=excluded.overview,overview_sha=excluded.overview_sha,record_sha=excluded.record_sha",
            run.RunId, ordinal, overview, HashBytes(overview), HashBytes(record.Bytes));
    }

    private IncrementalProfileState ReadIndexed(SqliteStore db, bool includeCheckpoint = true)
    {
        foreach (var required in new[] { 1, 2, 4, 5, 6, 8, 9, 15, 16 })
            if (db.ScalarLong("SELECT count(*) FROM records WHERE kind=? AND k1='' AND k2='' AND k3=''", required) != 1)
                throw new InvalidDataException("An authoritative profile root is missing.");
        if (db.ScalarLong("SELECT count(*) FROM records WHERE kind<1 OR kind>32") != 0)
            throw new InvalidDataException("Profile contains an unknown record kind.");
        var state = ProfileRecordReconstruction.Read(ReadRecords(db, includeHistory: false, includeCheckpoint));
        var profile = state.Profile;
        if (!ProfileFormat.IsCurrent(profile) || profile.GenerationId != db.ScalarText("SELECT generation FROM profile_state WHERE id=1")
            || profile.Revision != db.ScalarLong("SELECT revision FROM profile_state WHERE id=1"))
            throw new InvalidDataException("Current profile metadata and transaction state disagree.");
        var overview = new List<RunOverview>();
        foreach (var row in db.EnumerateRows("SELECT h.run_id,h.overview,h.overview_sha,h.record_sha,r.payload_sha FROM history_index h JOIN records r ON r.kind=17 AND r.k1=h.run_id ORDER BY h.ordinal"))
        {
            VerifyHash((byte[])row[1], (byte[])row[2]);
            if (!((byte[])row[3]).SequenceEqual((byte[])row[4])) throw new InvalidDataException("A history index is stale.");
            var item = ProfileRecordCodec.Decode<RunOverview>((byte[])row[1]);
            if (item.RunId != (string)row[0] || item.GenerationId != profile.GenerationId)
                throw new InvalidDataException("History index ownership is invalid.");
            overview.Add(item);
        }
        if (overview.Count != db.ScalarLong("SELECT count(*) FROM records WHERE kind=17")
            || overview.Count != db.ScalarLong("SELECT count(*) FROM history_index")
            || overview.Count != profile.Statistics.RunTotals.TotalRuns)
            throw new InvalidDataException("History count and retained totals disagree.");
        // These rows were fully validated on import/commit. Read checksums here;
        // detail validation happens before any selected run reaches a consumer.
        historyGeneration = profile.GenerationId;
        profile.Statistics.Runs = new StoredRunHistory(overview, LoadRun);
        BaseMovementStatistics.Validate(profile.Statistics.BaseMovement);
        CraftingStatisticsReducer.Validate(profile.Statistics.Crafting);
        if (state.Checkpoint != null) ProfileRepository.ValidateActiveCheckpointForStorage(state.Checkpoint, profile.GenerationId);
        return new IncrementalProfileState(profile, state.Session, state.Checkpoint, db.ScalarLong("SELECT session_present FROM profile_state WHERE id=1") == 1);
    }

    private RunSummary LoadRun(string runId)
    {
        lock (gate) if (disposed) throw new ObjectDisposedException(nameof(SqliteProfileStorage));
        try { return ReadRun(runId); }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException or System.Runtime.Serialization.SerializationException)
        {
            readFailure = true;
            // Keep query failure separate from an empty run. The marker also
            // prevents reopening this generation as healthy after a restart.
            try
            {
                using var marker = new FileStream(Path + ".read-failure", FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                var bytes = System.Text.Encoding.UTF8.GetBytes("Retained record read failed: " + exception.GetType().Name);
                marker.Write(bytes, 0, bytes.Length); marker.Flush(true);
            }
            catch (Exception markerFailure) when (markerFailure is IOException or UnauthorizedAccessException)
            { /* The in-memory failure guard still prevents acknowledgement. */ }
            throw;
        }
    }

    private RunSummary ReadRun(string runId)
    {
        using var reader = new SqliteStore(Path, readOnly: true);
        var rows = reader.Rows("SELECT payload,payload_sha FROM records WHERE kind=17 AND k1=?", runId);
        if (rows.Count != 1) throw new InvalidDataException("A retained run is missing.");
        var bytes = DecodePayload((byte[])rows[0][0], (byte[])rows[0][1]);
        var run = ProfileRecordCodec.Decode<RunSummary>(bytes);
        if (run.RunId != runId || run.SaveGenerationId != historyGeneration || run.SchemaVersion != ProductInfo.SchemaVersion)
            throw new InvalidDataException("Retained run identity or schema is invalid.");
        ProfileFormat.ValidateRecordMembers(run);
        RunReducer.Validate(run);
        return run;
    }
}
