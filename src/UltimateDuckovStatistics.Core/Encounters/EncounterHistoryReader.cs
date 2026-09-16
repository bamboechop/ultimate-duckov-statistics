namespace UltimateDuckovStatistics.Core.Encounters;

public static class EncounterHistoryReader
{
    // Selected-run loading must retain the storage index instead of enumerating every run.
    public static EncounterRecord[] Read(IList<EncounterRecord>? history, string runId) =>
        (history is EncounterHistory indexed ? indexed.Read(runId)
            : (history ?? Enumerable.Empty<EncounterRecord>()).Where(record => record.RunId == runId)).ToArray();
}
