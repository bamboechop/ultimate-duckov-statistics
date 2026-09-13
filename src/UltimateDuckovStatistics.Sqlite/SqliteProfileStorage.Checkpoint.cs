using System.Globalization;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Sqlite;

public sealed partial class SqliteProfileStorage
{
    private static void CreateCheckpointCounts(SqliteStore db)
    {
        db.Exec("CREATE TABLE checkpoint_counts(scope TEXT NOT NULL,kind TEXT NOT NULL,entries INTEGER NOT NULL CHECK(entries>=0),PRIMARY KEY(scope,kind))");
        db.Exec("CREATE TRIGGER checkpoint_insert AFTER INSERT ON records WHEN new.kind=27 BEGIN INSERT INTO checkpoint_counts VALUES(new.k1,new.k2,1) ON CONFLICT(scope,kind) DO UPDATE SET entries=entries+1; END");
        db.Exec("CREATE TRIGGER checkpoint_delete AFTER DELETE ON records WHEN old.kind=27 BEGIN UPDATE checkpoint_counts SET entries=entries-1 WHERE scope=old.k1 AND kind=old.k2; END");
    }

    private static void ValidateCheckpointTransaction(SqliteStore db, IncrementalProfileWrite write)
    {
        if (!write.Records.Any(record => record.Address.Kind >= ProfileRecordKind.ActiveCheckpoint || record.Address.Kind == ProfileRecordKind.CompletedRun)) return;
        var bytes = ReadRootPayload(db, 24);
        if (bytes == null)
        {
            if (db.ScalarLong("SELECT count(*) FROM records WHERE kind BETWEEN 25 AND 27") != 0)
                throw new InvalidDataException("Checkpoint entries have no owner root.");
            return;
        }
        var root = ProfileRecordCodec.Decode<CheckpointRootRecord>(bytes);
        if (root.Header.SaveGenerationId != write.GenerationId || string.IsNullOrWhiteSpace(root.Header.RunId)
            || db.ScalarLong("SELECT count(*) FROM records WHERE kind=17 AND k1=?", root.Header.RunId) != 0
            || db.ScalarLong("SELECT count(*) FROM records WHERE kind=23") != 0)
            throw new InvalidDataException("Checkpoint owner conflicts with committed history or another representation.");
        if (root.SegmentCount < 0 || root.SegmentCount > Core.Statistics.RouteStatisticsReducer.MaximumSegmentsPerRun
            || root.SegmentCount != db.ScalarLong("SELECT count(*) FROM records WHERE kind=25")
            || 30L * (root.SegmentCount + 1) + 2 != db.ScalarLong("SELECT count(*) FROM records WHERE kind=26"))
            throw new InvalidDataException("Checkpoint scope completeness is inconsistent.");
        foreach (var record in write.Records.Where(record => record.Address.Kind == ProfileRecordKind.CheckpointCollection))
        {
            var address = record.Address; var state = ProfileRecordCodec.Decode<CheckpointCollectionState>(record.Bytes!);
            var count = db.ScalarLong("SELECT COALESCE((SELECT entries FROM checkpoint_counts WHERE scope=? AND kind=?),0)", address.First, address.Second);
            if (count != state.Count || address.First.Length > 0 && db.ScalarLong("SELECT count(*) FROM records WHERE kind=25 AND k1=?", address.First) != 1)
                throw new InvalidDataException("Checkpoint changed collection did not commit its complete entry set.");
            var kind = CheckpointRecordChanges.Kind(address.Second);
            if (kind == CheckpointEntryKind.RouteAssociations && (address.First.Length != 0 || count != root.AssociationCount)
                || kind == CheckpointEntryKind.ContainerIdentities && (address.First.Length != 0 || count != root.ContainerIdentityCount))
                throw new InvalidDataException("Checkpoint retained identity count disagrees with its owner root.");
        }
        foreach (var record in write.Records.Where(record => record.Address.Kind == ProfileRecordKind.CheckpointEntry))
            if (db.ScalarLong("SELECT count(*) FROM records WHERE kind=26 AND k1=? AND k2=?", record.Address.First, record.Address.Second) != 1)
                throw new InvalidDataException("Checkpoint changed entry lacks completeness metadata.");
    }

    private static void PrepareCheckpointRecord(SqliteStore db, ProfileRecordChange record)
    {
        var address = record.Address;
        if (address.Kind == ProfileRecordKind.ActiveCheckpoint)
        {
            db.Exec("DELETE FROM records WHERE kind BETWEEN 24 AND 27");
            db.Exec("DELETE FROM checkpoint_counts");
        }
        if (address.Kind == ProfileRecordKind.CheckpointRoot)
        {
            if (record.Bytes == null) throw new InvalidDataException("Incremental checkpoint root cannot be removed independently.");
            var next = ProfileRecordCodec.Decode<CheckpointRootRecord>(record.Bytes);
            var oldBytes = ReadRootPayload(db, 24);
            if (oldBytes != null)
            {
                var previous = ProfileRecordCodec.Decode<CheckpointRootRecord>(oldBytes);
                if (previous.Header.RunId != next.Header.RunId || previous.Header.SaveGenerationId != next.Header.SaveGenerationId)
                    throw new InvalidDataException("A pending checkpoint cannot be replaced by another run owner.");
            }
            var legacyBytes = ReadRootPayload(db, 23);
            if (legacyBytes != null)
            {
                var previous = ProfileRecordCodec.Decode<ActiveRunCheckpoint>(legacyBytes);
                if (previous.RunId != next.Header.RunId || previous.SaveGenerationId != next.Header.SaveGenerationId)
                    throw new InvalidDataException("A pending checkpoint cannot be replaced by another run owner.");
                db.Exec("DELETE FROM records WHERE kind=23");
            }
        }
        if (address.Kind != ProfileRecordKind.CheckpointCollection) return;
        if (record.Bytes == null) throw new InvalidDataException("Checkpoint completeness metadata cannot be removed independently.");
        var kind = CheckpointRecordChanges.Kind(address.Second);
        var state = ProfileRecordCodec.Decode<CheckpointCollectionState>(record.Bytes);
        if (state.Count < 0 || state.MinimumSequence < 0
            || state.MinimumSequence.HasValue && kind != CheckpointEntryKind.EquipmentTransitions)
            throw new InvalidDataException("Checkpoint completeness metadata is invalid.");
        if (state.Replace) db.Exec("DELETE FROM records WHERE kind=27 AND k1=? AND k2=?", address.First, address.Second);
        if (state.MinimumSequence.HasValue)
        {
            // Only the bounded transition window uses numeric entry keys. Do
            // not apply SQLite numeric coercion to arbitrary dictionary keys.
            foreach (var row in db.Rows("SELECT k3 FROM records WHERE kind=27 AND k1=? AND k2=?", address.First, address.Second))
            {
                if (!long.TryParse((string)row[0], NumberStyles.None, CultureInfo.InvariantCulture, out var sequence))
                    throw new InvalidDataException("Retained checkpoint transition sequence is invalid.");
                if (sequence < state.MinimumSequence.Value)
                    db.Exec("DELETE FROM records WHERE kind=27 AND k1=? AND k2=? AND k3=?", address.First, address.Second, (string)row[0]);
            }
        }
    }
}
