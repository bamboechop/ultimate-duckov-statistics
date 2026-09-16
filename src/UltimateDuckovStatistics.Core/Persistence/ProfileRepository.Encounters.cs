using UltimateDuckovStatistics.Core.Encounters;

namespace UltimateDuckovStatistics.Core.Persistence;

public sealed partial class ProfileRepository
{
    public void RecordEncounterDeferred(string generationId, EncounterRecord record)
    {
        if (generationId != Current.GenerationId) throw new ArgumentException("Encounter belongs to another profile generation.", nameof(generationId));
        EncounterRecordValidation.Validate(record);
        var history = Current.EncounterHistory as EncounterHistory;
        if (history == null)
        {
            var source = incrementalStorage as IEncounterHistorySource
                ?? (Current.EncounterHistory == null ? null : new MemoryEncounterHistorySource(Current.EncounterHistory));
            history = new EncounterHistory(source);
        }
        var previous = history.Find(record.RunId, record.Kind, record.Id);
        if (previous != null) EncounterRecordValidation.ValidateReplacement(previous, record);
        if (record.Kind is not (EncounterRecordKind.Visit or EncounterRecordKind.Coverage) && history.Find(record.RunId, EncounterRecordKind.Visit, record.VisitId) == null)
            throw new ArgumentException("Record the encounter's map visit first.", nameof(record));
        if (record.Encounter?.OutcomeVisitId is { } outcomeVisit && history.Find(record.RunId, EncounterRecordKind.Visit, outcomeVisit) == null)
            throw new ArgumentException("Record the encounter's outcome visit first.", nameof(record));
        if (record.EncounterId != null)
        {
            var owner = history.Find(record.RunId, EncounterRecordKind.Encounter, record.EncounterId);
            if (owner?.VisitId != record.VisitId) throw new ArgumentException("Record the encounter's actor first.", nameof(record));
        }
        var bytes = recordCodec.Encode(record);
        var revision = checked(Current.Revision + 1);
        history.Put(record, bytes);
        Current.EncounterHistory = history;
        changes?.Encounter(record, bytes);
        Current.Revision = revision;
        Current.UpdatedUtc = EnsureUtc(utcNow());
    }

    public IReadOnlyList<EncounterRecord> ReadEncounters(string runId, EncounterRecordKind? kind = null) =>
        (Current.EncounterHistory is EncounterHistory history ? history.Read(runId, kind)
            : (Current.EncounterHistory ?? Enumerable.Empty<EncounterRecord>()).Where(record => record.RunId == runId && (!kind.HasValue || record.Kind == kind)))
        .Select(record => ProfileRecordCodec.Decode<EncounterRecord>(recordCodec.Encode(record))).ToArray();
}
