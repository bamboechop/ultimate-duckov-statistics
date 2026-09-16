using System.Globalization;
using UltimateDuckovStatistics.Core.Encounters;
using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Sqlite;

public sealed partial class SqliteProfileStorage : IEncounterHistorySource
{
    int IEncounterHistorySource.Count
    {
        get { using var reader = OpenEncounterReader(); return checked((int)reader.ScalarLong("SELECT count(*) FROM records WHERE kind=33")); }
    }

    EncounterRecord? IEncounterHistorySource.Find(string runId, EncounterRecordKind kind, string id)
    {
        using var reader = OpenEncounterReader();
        return ReadEncounter(reader, runId, kind, id);
    }

    IEnumerable<EncounterRecord> IEncounterHistorySource.Read(string? runId, EncounterRecordKind? kind)
    {
        using var reader = OpenEncounterReader();
        var sql = "SELECT k1,k2,k3,payload,payload_sha FROM records WHERE kind=33";
        var parameters = new List<object>();
        if (runId != null) { sql += " AND k1=?"; parameters.Add(runId); }
        if (kind.HasValue) { sql += " AND k2=?"; parameters.Add(((int)kind.Value).ToString(CultureInfo.InvariantCulture)); }
        foreach (var row in reader.EnumerateRows(sql + " ORDER BY ordinal", parameters.ToArray()))
            yield return DecodeEncounter(row);
    }

    private SqliteStore OpenEncounterReader()
    {
        lock (gate)
        {
            if (disposed) throw new ObjectDisposedException(nameof(SqliteProfileStorage));
            var reader = new SqliteStore(Path, readOnly: true);
            try
            {
                reader.Exec("BEGIN");
                var generation = reader.ScalarText("SELECT generation FROM profile_state WHERE id=1");
                if (historyGeneration != null && generation != historyGeneration)
                    throw new IOException("The encounter reader's profile generation changed.");
                return reader;
            }
            catch { reader.Dispose(); throw; }
        }
    }

    private EncounterRecord? ReadEncounter(SqliteStore db, string runId, EncounterRecordKind kind, string id)
    {
        var rows = db.Rows("SELECT k1,k2,k3,payload,payload_sha FROM records WHERE kind=33 AND k1=? AND k2=? AND k3=?",
            runId, ((int)kind).ToString(CultureInfo.InvariantCulture), id);
        return rows.Count == 0 ? null : DecodeEncounter(rows[0]);
    }

    private EncounterRecord DecodeEncounter(object[] row)
    {
        try
        {
            var result = ProfileRecordCodec.Decode<EncounterRecord>(DecodePayload((byte[])row[3], (byte[])row[4]));
            ValidateEncounterAddress(result, new ProfileRecordAddress(ProfileRecordKind.Encounter, (string)row[0], (string)row[1], (string)row[2]));
            return result;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or ArgumentException or System.Runtime.Serialization.SerializationException)
        {
            readFailure = true;
            try
            {
                lock (gate)
                {
                    // An abandoned view may finish after profile rotation. It must not mark
                    // the replacement generation at the same pathname as damaged.
                    if (!disposed)
                    {
                        using var marker = new FileStream(Path + ".read-failure", FileMode.CreateNew, FileAccess.Write, FileShare.Read);
                        var bytes = System.Text.Encoding.UTF8.GetBytes("Encounter record read failed: " + exception.GetType().Name);
                        marker.Write(bytes, 0, bytes.Length); marker.Flush(true);
                    }
                }
            }
            catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { }
            throw;
        }
    }

    private static void ValidateEncounterAddress(EncounterRecord record, ProfileRecordAddress address)
    {
        EncounterRecordValidation.Validate(record);
        if (!ProfileChangeJournal.EncounterAddress(record).Equals(address)) throw new InvalidDataException("Encounter address disagrees with its payload.");
    }

    private void PrepareEncounterRecord(SqliteStore db, ProfileRecordChange record)
    {
        if (record.Address.Kind != ProfileRecordKind.Encounter) return;
        if (record.Bytes == null) throw new InvalidDataException("Encounter history cannot be deleted by a routine mutation.");
        var next = ProfileRecordCodec.Decode<EncounterRecord>(record.Bytes);
        ValidateEncounterAddress(next, record.Address);
        var previous = ReadEncounter(db, next.RunId, next.Kind, next.Id);
        if (previous != null) EncounterRecordValidation.ValidateReplacement(previous, next);
    }

    private void ValidateEncounterTransaction(SqliteStore db, IncrementalProfileWrite write)
    {
        // Read only parents of changed rows. Unrelated encounters and path chunks
        // are never decoded to validate an ordinary write.
        foreach (var changed in write.Records.Where(record => record.Address.Kind == ProfileRecordKind.Encounter))
        {
            var record = ProfileRecordCodec.Decode<EncounterRecord>(changed.Bytes!);
            if (record.Kind == EncounterRecordKind.Coverage) continue;
            if (ReadEncounter(db, record.RunId, EncounterRecordKind.Visit, record.VisitId) == null)
                throw new InvalidDataException("Encounter map visit is missing.");
            if (record.Encounter?.OutcomeVisitId is { } outcomeVisit && ReadEncounter(db, record.RunId, EncounterRecordKind.Visit, outcomeVisit) == null)
                throw new InvalidDataException("Encounter outcome visit is missing.");
            if (record.EncounterId != null && ReadEncounter(db, record.RunId, EncounterRecordKind.Encounter, record.EncounterId)?.VisitId != record.VisitId)
                throw new InvalidDataException("Encounter child ownership is invalid.");
        }
    }
}
