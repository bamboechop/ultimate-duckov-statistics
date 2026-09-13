using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Sqlite;

public sealed partial class SqliteProfileStorage
{
    private static void CreateRunMetricCounts(SqliteStore db)
    {
        db.Exec("CREATE TABLE run_metric_counts(scope TEXT NOT NULL,kind TEXT NOT NULL,entries INTEGER NOT NULL CHECK(entries>=0),PRIMARY KEY(scope,kind))");
        db.Exec("CREATE TRIGGER run_metric_insert AFTER INSERT ON records WHEN new.kind=31 BEGIN INSERT INTO run_metric_counts VALUES(new.k1,new.k2,1) ON CONFLICT(scope,kind) DO UPDATE SET entries=entries+1; END");
        db.Exec("CREATE TRIGGER run_metric_delete AFTER DELETE ON records WHEN old.kind=31 BEGIN UPDATE run_metric_counts SET entries=entries-1 WHERE scope=old.k1 AND kind=old.k2; END");
    }
    private static void PrepareRunMetricRecord(SqliteStore db, ProfileRecordChange record)
    {
        var address = record.Address;
        if (record.Bytes == null && address.Kind is ProfileRecordKind.RunMap or ProfileRecordKind.RouteMap)
        {
            var scope = RunMetricRecords.ScopeId(address.Kind, address.First);
            db.Exec("DELETE FROM records WHERE kind IN (30,31) AND k1=?", scope);
            db.Exec("DELETE FROM run_metric_counts WHERE scope=?", scope);
        }
        if (address.Kind != ProfileRecordKind.RunMetricCollection) return;
        RunMetricRecords.ParseScope(address.First);
        var kind = CheckpointRecordChanges.Kind(address.Second);
        if ((int)kind > 29 || record.Bytes == null) throw new InvalidDataException("Maintained metric collection is invalid.");
        var state = ProfileRecordCodec.Decode<CheckpointCollectionState>(record.Bytes);
        if (state.Count < 0 || state.MinimumSequence.HasValue) throw new InvalidDataException("Maintained metric collection count is invalid.");
        if (state.Replace) db.Exec("DELETE FROM records WHERE kind=31 AND k1=? AND k2=?", address.First, address.Second);
    }
    private static void ValidateRunMetricTransaction(SqliteStore db, IncrementalProfileWrite write)
    {
        if (!write.Records.Any(record => record.Address.Kind is ProfileRecordKind.RunTotals or ProfileRecordKind.CompletedRun
            or ProfileRecordKind.RunMap or ProfileRecordKind.RouteMap or ProfileRecordKind.RunMetricCollection or ProfileRecordKind.RunMetricEntry)) return;
        var scopes = 1 + db.ScalarLong("SELECT count(*) FROM records WHERE kind IN (28,29)");
        if (scopes * 29 != db.ScalarLong("SELECT count(*) FROM records WHERE kind=30"))
            throw new InvalidDataException("Maintained metric scope completeness is inconsistent.");
        var total = ProfileRecordCodec.Decode<RunAggregateTotals>(ReadRootPayload(db, 15)
            ?? throw new InvalidDataException("Run totals are missing."));
        if (total.TotalRuns != db.ScalarLong("SELECT count(*) FROM records WHERE kind=17"))
            throw new InvalidDataException("Completed run count disagrees with its maintained total.");
        foreach (var record in write.Records.Where(record => record.Address.Kind == ProfileRecordKind.RunMetricCollection))
        {
            var address = record.Address; var owner = RunMetricRecords.ParseScope(address.First);
            var state = ProfileRecordCodec.Decode<CheckpointCollectionState>(record.Bytes!);
            if (state.Count != db.ScalarLong("SELECT COALESCE((SELECT entries FROM run_metric_counts WHERE scope=? AND kind=?),0)", address.First, address.Second)
                || db.ScalarLong("SELECT count(*) FROM records WHERE kind=? AND k1=?", (int)owner.Kind, owner.Key) != 1)
                throw new InvalidDataException("Changed maintained metric collection is incomplete or has no owner.");
        }
        foreach (var record in write.Records.Where(record => record.Address.Kind == ProfileRecordKind.RunMetricEntry))
            if (db.ScalarLong("SELECT count(*) FROM records WHERE kind=30 AND k1=? AND k2=?", record.Address.First, record.Address.Second) != 1)
                throw new InvalidDataException("Changed maintained metric entry lacks completeness metadata.");
    }
}
