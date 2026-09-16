using UltimateDuckovStatistics.Core.Encounters;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Core.Export;

internal sealed class ExportEncounterHistory(IList<EncounterRecord> source, HashSet<string> completedRuns) : IEncounterHistorySource
{
    public int Count => Read(null, null).Count();
    public EncounterRecord? Find(string runId, EncounterRecordKind kind, string id) => Read(runId, kind).FirstOrDefault(record => record.Id == id);
    public IEnumerable<EncounterRecord> Read(string? runId, EncounterRecordKind? kind) => source
        .Where(record => completedRuns.Contains(record.RunId) && (runId == null || record.RunId == runId) && (!kind.HasValue || record.Kind == kind));

    internal static IList<EncounterRecord>? Create(ProfileDocument profile, bool stream)
    {
        if (profile.EncounterHistory == null) return null;
        // Active-run checkpoint data is not part of a statistics export. Apply
        // the same boundary to encounters; never restore an orphan active run.
        var runs = new HashSet<string>(RunHistory.Overview(profile.Statistics.Runs).Select(run => run.RunId), StringComparer.Ordinal);
        var history = new EncounterHistory(new ExportEncounterHistory(profile.EncounterHistory, runs));
        if (!history.Read().Any()) return null;
        return stream ? history : history.Select(record => ProfileRecordCodec.Decode<EncounterRecord>(new ProfileRecordCodec().Encode(record))).ToList();
    }
}
