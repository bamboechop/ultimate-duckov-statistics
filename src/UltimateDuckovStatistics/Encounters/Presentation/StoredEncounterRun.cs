using UltimateDuckovStatistics.Core.Encounters;

namespace UltimateDuckovStatistics.Encounters;

// One chronological feed owns the numbering. Map visits only select route/marker subsets.
internal sealed class StoredEncounterRun
{
    internal static readonly StoredEncounterRun Empty = Build(Array.Empty<EncounterRecord>());
    internal StoredEncounterMap[] Visits { get; private set; } = Array.Empty<StoredEncounterMap>();
    internal EncounterRecord[] Events { get; private set; } = Array.Empty<EncounterRecord>();
    internal EncounterRecord[] Records { get; private set; } = Array.Empty<EncounterRecord>();
    internal string? CoverageNoticeKey { get; private set; }
    private readonly Dictionary<string, int> eventIndices = new(StringComparer.Ordinal);
    private int[] eventVisits = Array.Empty<int>();

    internal static StoredEncounterRun Build(EncounterRecord[] records)
    {
        var result = new StoredEncounterRun { Records = records, Visits = StoredEncounterMap.Build(records),
            Events = OrderEvents(records).ToArray() };
        result.CoverageNoticeKey = records.Any(record => record.Coverage?.CaptureStopped == true) ? "ui.encounters_capture_stopped"
            : records.Any(record => record.Coverage != null) ? "ui.encounters_capture_incomplete" : null;
        var visits = result.Visits.Select((visit, index) => (visit.VisitId, index)).ToDictionary(pair => pair.VisitId, pair => pair.index);
        result.eventVisits = new int[result.Events.Length];
        for (var i = 0; i < result.Events.Length; i++)
        {
            var record = result.Events[i]; result.eventIndices.Add(record.Id, i);
            result.eventVisits[i] = visits.TryGetValue(record.Encounter!.OutcomeVisitId ?? record.VisitId, out var index) ? index : -1;
        }
        return result;
    }

    internal int EventIndex(string id) => eventIndices.TryGetValue(id, out var index) ? index : -1;

    internal static IOrderedEnumerable<EncounterRecord> OrderEvents(IEnumerable<EncounterRecord> records) =>
        records.Where(record => record.Encounter?.Outcome != null)
            .OrderBy(record => record.Encounter!.EndedSeconds)
            .ThenBy(record => record.Encounter!.FatalSequence ?? long.MaxValue)
            .ThenBy(record => record.Id, StringComparer.Ordinal);
    internal int VisitIndex(int eventIndex) => eventIndex >= 0 && eventIndex < eventVisits.Length ? eventVisits[eventIndex] : -1;
}

internal sealed class EncounterRunSelection
{
    internal StoredEncounterRun Run { get; }
    internal int VisitIndex { get; private set; }
    internal int EventIndex { get; private set; } = -1;
    internal StoredEncounterMap? CurrentVisit => VisitIndex >= 0 && VisitIndex < Run.Visits.Length ? Run.Visits[VisitIndex] : null;
    internal EncounterRecord? CurrentEvent => EventIndex >= 0 ? Run.Events[EventIndex] : null;

    internal EncounterRunSelection(StoredEncounterRun run) { Run = run; VisitIndex = run.Visits.Length == 0 ? -1 : 0; }
    internal void SelectVisit(int index)
    {
        if (index < 0 || index >= Run.Visits.Length) return;
        VisitIndex = index; EventIndex = -1;
    }
    internal void ToggleEvent(int index)
    {
        if (index < 0 || index >= Run.Events.Length) return;
        if (EventIndex == index) { EventIndex = -1; return; }
        EventIndex = index; VisitIndex = Run.VisitIndex(index);
    }
}
