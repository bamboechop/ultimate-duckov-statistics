using System.Collections.ObjectModel;
using System.Globalization;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.UI;

internal sealed class RecordsCard
{
    public string Id { get; }
    public string Heading { get; }
    public string? RunId { get; }
    public string Notice { get; }
    public IReadOnlyList<KeyValuePair<string, string>> Rows { get; }
    public RecordsCard(string id, string heading, IEnumerable<KeyValuePair<string, string>> rows,
        string? runId = null, string notice = "")
    {
        Id = id; Heading = heading; RunId = runId; Notice = notice;
        Rows = new ReadOnlyCollection<KeyValuePair<string, string>>(rows.ToArray());
    }
}

internal sealed class RecordsPresentation
{
    public string GenerationId { get; }
    public IReadOnlyList<RecordsCard> Overall { get; }
    public IReadOnlyList<RecordsCard> Maps { get; }
    public bool ShowMaps { get; }
    public RecordsPresentation(string generation, IEnumerable<RecordsCard> overall, IEnumerable<RecordsCard> maps, bool hasRecordedRuns = false)
    {
        GenerationId = generation;
        Overall = Array.AsReadOnly(overall.ToArray()); Maps = Array.AsReadOnly(maps.ToArray());
        ShowMaps = hasRecordedRuns || Maps.Count > 0;
    }
}

internal static class RecordsPresentationFactory
{
    public static RecordsPresentation? Create(StatisticsPanelProjection projection, string generation,
        Func<string, string>? text = null, Func<DateTime, DateTime>? toLocal = null)
    {
        if (!StatisticsPanelProjectionFactory.HasProvableGeneration(projection.Profile, generation)) return null;
        var model = projection.Runs;
        var statistics = projection.Profile.Statistics;
        var profileRuns = new HashSet<RunSummary>(statistics.Runs);
        // These are the exact objects composed by the shared projection factory. Never combine
        // current generation labels with a detached/stale view model from another publication.
        if (!ReferenceEquals(model.Records, statistics.RunRecords)
            || model.Runs.Count != statistics.Runs.Count
            || model.Runs.Any(run => !profileRuns.Contains(run) || run.SaveGenerationId != generation || string.IsNullOrWhiteSpace(run.RunId))
            || model.Runs.Select(run => run.RunId).Distinct(StringComparer.Ordinal).Count() != model.Runs.Count
            || model.Maps.Count != statistics.RunTotals.Maps.Count
            || model.Maps.Any(map => string.IsNullOrWhiteSpace(map.MapId))
            || model.Maps.Select(map => map.MapId).Distinct(StringComparer.Ordinal).Count() != model.Maps.Count
            || model.Maps.Any(map => !statistics.RunTotals.Maps.TryGetValue(map.MapId, out var source) || !ReferenceEquals(map, source))) return null;
        var t = text ?? UiText.Get;
        var local = toLocal ?? (value => value.ToLocalTime());
        var runs = model.Runs.ToDictionary(run => run.RunId, StringComparer.Ordinal);
        var startingRuns = model.Runs.ToLookup(run => run.StartingMapId, StringComparer.Ordinal);
        var overall = new List<RecordsCard>();
        AddOverall(model.Records.Extraction, RunOutcome.Extracted, "extraction", overall, runs, t, local, projection.Names);
        AddOverall(model.Records.Death, RunOutcome.Died, "death", overall, runs, t, local, projection.Names);
        var maps = model.Maps.ToDictionary(map => map.MapId, StringComparer.Ordinal);
        // Orphan record-map entries remain visible as inconsistent aggregate composition.
        var ids = maps.Keys.Union(model.Records.Maps.Keys, StringComparer.Ordinal)
            .OrderBy(id => maps.TryGetValue(id, out var map) && map.IsKnown ? 0 : 1)
            .ThenBy(id => maps.TryGetValue(id, out var map) ? MapName(map.MapId, map.DisplayName, map.IsKnown, t, projection.Names) : id, StringComparer.Ordinal)
            .ThenBy(id => id, StringComparer.Ordinal);
        var cards = new List<RecordsCard>();
        foreach (var id in ids)
        {
            maps.TryGetValue(id, out var map);
            var hasMapRecords = model.Records.Maps.TryGetValue(id, out var records);
            var validMapRecords = !hasMapRecords || records != null && records.MapId == id;
            var rows = new List<KeyValuePair<string, string>>();
            void Add(string key, string value) => rows.Add(new(t(key), value));
            string Count(RunOutcome outcome) => map == null ? t("ui.unavailable") : Number(map.Outcomes.TryGetValue(outcome.ToString(), out var n) ? n : 0, t);
            Add("ui.runs", map == null ? t("ui.unavailable") : Number(map.TotalRuns, t));
            Add("ui.records_extracted", Count(RunOutcome.Extracted)); Add("ui.records_died", Count(RunOutcome.Died));
            if (map?.Outcomes.TryGetValue(RunOutcome.Interrupted.ToString(), out var interrupted) == true && interrupted != 0)
                Add("ui.records_interrupted", Number(interrupted, t));
            var history = startingRuns[id].ToArray();
            var movementExact = map != null && model.MovementSupported && history.LongLength == map.TotalRuns
                && history.All(run => run.MovementCapability == AdapterCapabilityState.Supported);
            Add("ui.overview_total_distance_travelled", Distance(map?.PhysicalDistance, movementExact, t));
            Add("ui.records_teleport", Distance(map?.TeleportDistance, movementExact, t));
            AddMapPair(records?.Extraction, RunOutcome.Extracted, "extraction", id,
                validMapRecords && (records == null || records.Extraction != null), rows, runs, t);
            if (!validMapRecords || records != null && (records.Death == null || records.Death.Shortest != null || records.Death.Longest != null))
                AddMapPair(records?.Death, RunOutcome.Died, "death", id,
                    validMapRecords && (records == null || records.Death != null), rows, runs, t);
            cards.Add(new RecordsCard(id, map == null ? MapName(id, records?.DisplayName ?? "", false, t, projection.Names)
                : MapName(id, map.DisplayName, map.IsKnown, t, projection.Names), rows,
                notice: map == null || !validMapRecords ? t("ui.records_inconsistent") : ""));
        }
        return new RecordsPresentation(generation, overall, cards, runs.Count > 0 || model.TotalRuns > 0);
    }

    private static void AddOverall(DurationRecordPair? pair, RunOutcome outcome, string category,
        List<RecordsCard> cards, IReadOnlyDictionary<string, RunSummary> runs, Func<string, string> t, Func<DateTime, DateTime> local, EntityDisplayNames names)
    {
        if (pair != null && pair.Shortest == null && pair.Longest == null)
        {
            cards.Add(new RecordsCard(category, t("ui.records_" + category), Array.Empty<KeyValuePair<string, string>>(),
                notice: t("ui.records_no_" + category))); return;
        }
        for (var i = 0; i < 2; i++)
        {
            var reference = i == 0 ? pair?.Shortest : pair?.Longest;
            var run = Resolve(reference, outcome, null, runs);
            var valid = ValidReference(reference, outcome, null, runs);
            var rows = new List<KeyValuePair<string, string>>
            {
                new(t("ui.records_time"), valid ? Duration(reference!, t) : t("ui.unavailable")),
                new(t("ui.records_starting_map"), run == null ? t("ui.unavailable") : MapName(run.StartingMapId, run.StartingMapDisplayName, run.StartingMapKnown, t, names)),
                new(t("ui.records_date"), Timestamp(reference?.StartedUtc ?? default, local, t))
            };
            var route = Route(run, t, names);
            if (route != null) rows.Add(new(t("ui.runs_route"), route));
            cards.Add(new RecordsCard(category + i, Heading(category, i, t), rows,
                runId: valid ? run?.RunId : null, notice: run == null || !valid ? t("ui.records_run_unavailable") : ""));
        }
    }

    private static void AddMapPair(DurationRecordPair? pair, RunOutcome outcome, string category, string map,
        bool composition, List<KeyValuePair<string, string>> rows, IReadOnlyDictionary<string, RunSummary> runs, Func<string, string> t)
    {
        for (var i = 0; i < 2; i++)
        {
            var reference = i == 0 ? pair?.Shortest : pair?.Longest;
            var empty = pair == null || pair.Shortest == null && pair.Longest == null;
            var value = !composition ? t("ui.unavailable") : empty ? t("ui.records_empty_" + category)
                : ValidReference(reference, outcome, map, runs) ? Duration(reference!, t) : t("ui.unavailable");
            rows.Add(new(Heading(category, i, t), value));
        }
    }

    private static string Heading(string category, int index, Func<string, string> t) => t(category == "extraction"
        ? index == 0 ? "ui.overview_fastest_extraction" : "ui.overview_longest_successful_raid"
        : index == 0 ? "ui.records_shortest_death" : "ui.records_longest_death");

    private static bool ValidReference(DurationRecordReference? reference, RunOutcome outcome, string? map, IReadOnlyDictionary<string, RunSummary> runs)
    {
        if (reference == null || string.IsNullOrWhiteSpace(reference.RunId)
            || !RetainedRunDurationFormatter.TryFormat(reference.ActiveDurationSeconds, out _)) return false;
        if (!runs.TryGetValue(reference.RunId, out var run)) return map == null || reference.MapId == map;
        return Eligible(run, outcome) && (map == null || run.StartingMapId == map);
    }
    private static bool Eligible(RunSummary run, RunOutcome outcome) => run.RecordEligible && run.Outcome == outcome
        && run.IntegrityTags == IntegrityTags.Normal && run.LifecycleCapability == AdapterCapabilityState.Supported;
    private static RunSummary? Resolve(DurationRecordReference? reference, RunOutcome outcome, string? map, IReadOnlyDictionary<string, RunSummary> runs) =>
        reference != null && runs.TryGetValue(reference.RunId, out var run) && Eligible(run, outcome)
        && run.StartedUtc == reference.StartedUtc && run.ActiveDurationSeconds == reference.ActiveDurationSeconds
        && (map == null || run.StartingMapId == map) ? run : null;

    internal static string MapName(string id, string name, bool known, Func<string, string> t, EntityDisplayNames? names = null) =>
        known && !string.IsNullOrWhiteSpace(name) ? (names ?? EntityDisplayNames.Recorded).Get(id, name)
        : !string.IsNullOrWhiteSpace(id) && id != MapIdentity.UnknownId ? t("ui.overview_latest_run_unknown_map") + " (" + id + ")" : t("ui.overview_latest_run_unknown_map");
    private static string? Route(RunSummary? run, Func<string, string> t, EntityDisplayNames names)
    {
        if (run == null) return t("ui.unavailable");
        var exact = !run.RouteWasRepairedFromInvalidState
            && run.RouteCapabilities.OrderedRoute.State == AdapterCapabilityState.Supported
            && run.RouteCapabilities.Segments.State == AdapterCapabilityState.Supported
            && run.Segments.Count > 0 && run.Segments.All(segment => !segment.WasRepairedFromInvalidState);
        if (exact && run.Segments.Count == 1) return null;
        if (!exact) return t("ui.unavailable") + " (" + t("ui.runs_partial") + ")";
        return string.Join(" - ", run.Segments.Select(segment => MapName(segment.MapId, segment.MapDisplayName, segment.MapKnown, t, names)));
    }
    private static string Timestamp(DateTime utc, Func<DateTime, DateTime> local, Func<string, string> t)
    {
        if (utc == default) return t("ui.unavailable");
        try { return local(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToString(RunDateStyle.Format, CultureInfo.InvariantCulture); }
        catch (ArgumentException) { return t("ui.unavailable"); }
    }
    private static string Duration(DurationRecordReference reference, Func<string, string> t) =>
        RetainedRunDurationFormatter.TryFormat(reference.ActiveDurationSeconds, out var value) ? value : t("ui.unavailable");
    private static string Number(long n, Func<string, string> t) => n < 0 ? t("ui.unavailable") : n.ToString("N0", CultureInfo.InvariantCulture);
    private static string Distance(double? n, bool exact, Func<string, string> t) =>
        !n.HasValue || double.IsNaN(n.Value) || double.IsInfinity(n.Value) || n < 0 ? t("ui.unavailable")
        : exact ? n == 0 ? "0" : n.Value.ToString("N2", CultureInfo.InvariantCulture) + " m"
        : n > 0 ? n.Value.ToString("N2", CultureInfo.InvariantCulture) + " m (" + t("ui.runs_partial") + ")" : t("ui.unavailable");
}
