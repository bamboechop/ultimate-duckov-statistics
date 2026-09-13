using System.Runtime.Serialization;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Core.Statistics;

/// <summary>Recorded scalar history fields; absence of detail never masquerades as an empty run.</summary>
[DataContract]
public sealed class RunOverview
{
    [DataMember(Order = 1)] public string RunId { get; private set; } = "";
    [DataMember(Order = 2)] public string GenerationId { get; private set; } = "";
    [DataMember(Order = 3)] public DateTime StartedUtc { get; private set; }
    [DataMember(Order = 4)] public DateTime EndedUtc { get; private set; }
    [DataMember(Order = 5)] public double ActiveDurationSeconds { get; private set; }
    [DataMember(Order = 6)] public double PhysicalDistance { get; private set; }
    [DataMember(Order = 7)] public double TeleportDistance { get; private set; }
    [DataMember(Order = 8)] public RunOutcome Outcome { get; private set; }
    [DataMember(Order = 9)] public bool RecordEligible { get; private set; }
    [DataMember(Order = 10)] public IntegrityTags IntegrityTags { get; private set; }
    [DataMember(Order = 11)] public AdapterCapabilityState MovementCapability { get; private set; }
    [DataMember(Order = 12)] public AdapterCapabilityState LifecycleCapability { get; private set; }
    [DataMember(Order = 13)] public string StartingMapId { get; private set; } = "";
    [DataMember(Order = 14)] public string StartingMapDisplayName { get; private set; } = "";
    [DataMember(Order = 15)] public bool StartingMapKnown { get; private set; }
    [DataMember(Order = 16)] public string EndingMapId { get; private set; } = "";
    [DataMember(Order = 17)] public string EndingMapDisplayName { get; private set; } = "";
    [DataMember(Order = 18)] public bool EndingMapKnown { get; private set; }
    [DataMember(Order = 19)] public int SegmentCount { get; private set; }
    [DataMember(Order = 20)] public double TransitionExcludedDistance { get; private set; }
    [DataMember(Order = 21)] public bool ReplayCompactionPending { get; private set; }
    [DataMember(Order = 22)] public bool RouteExact { get; private set; }
    [DataMember(Order = 23)] public bool RouteMapsKnown { get; private set; }
    [DataMember(Order = 24)] public int DistinctMapCount { get; private set; }
    [DataMember(Order = 25)] public string FirstMapId { get; private set; } = "";
    [DataMember(Order = 26)] public string FirstMapDisplayName { get; private set; } = "";
    [DataMember(Order = 27)] public bool FirstMapKnown { get; private set; }
    [DataMember(Order = 28)] public string LastMapId { get; private set; } = "";
    [DataMember(Order = 29)] public string LastMapDisplayName { get; private set; } = "";
    [DataMember(Order = 30)] public bool LastMapKnown { get; private set; }
    internal static RunOverview From(RunSummary run) => new()
    {
        RunId = run.RunId,
        GenerationId = run.SaveGenerationId,
        StartedUtc = run.StartedUtc,
        EndedUtc = run.EndedUtc,
        ActiveDurationSeconds = run.ActiveDurationSeconds,
        PhysicalDistance = run.PhysicalDistance,
        TeleportDistance = run.TeleportDistance,
        Outcome = run.Outcome,
        RecordEligible = run.RecordEligible,
        IntegrityTags = run.IntegrityTags,
        MovementCapability = run.MovementCapability,
        LifecycleCapability = run.LifecycleCapability,
        StartingMapId = run.StartingMapId,
        StartingMapDisplayName = run.StartingMapDisplayName,
        StartingMapKnown = run.StartingMapKnown,
        EndingMapId = run.EndingMapId,
        EndingMapDisplayName = run.EndingMapDisplayName,
        EndingMapKnown = run.EndingMapKnown,
        SegmentCount = run.Segments.Count,
        TransitionExcludedDistance = run.TransitionExcludedDistance,
        ReplayCompactionPending = NeedsReplayCompaction(run.Economy) || run.Segments.Any(segment => NeedsReplayCompaction(segment.Economy)),
        RouteExact = !run.RouteWasRepairedFromInvalidState && run.RouteCapabilities.OrderedRoute.State == AdapterCapabilityState.Supported && run.RouteCapabilities.Segments.State == AdapterCapabilityState.Supported,
        RouteMapsKnown = run.Segments.Count > 0 && run.Segments.All(segment => segment.MapKnown),
        DistinctMapCount = run.Segments.Select(segment => segment.MapId).Distinct(StringComparer.Ordinal).Count(),
        FirstMapId = run.Segments.FirstOrDefault()?.MapId ?? run.StartingMapId,
        FirstMapDisplayName = run.Segments.FirstOrDefault()?.MapDisplayName ?? run.StartingMapDisplayName,
        FirstMapKnown = run.Segments.FirstOrDefault()?.MapKnown ?? run.StartingMapKnown,
        LastMapId = run.Segments.LastOrDefault()?.MapId ?? run.StartingMapId,
        LastMapDisplayName = run.Segments.LastOrDefault()?.MapDisplayName ?? run.StartingMapDisplayName,
        LastMapKnown = run.Segments.LastOrDefault()?.MapKnown ?? run.StartingMapKnown
    };
    private static bool NeedsReplayCompaction(EconomyStatisticsAggregate economy) =>
        !string.IsNullOrEmpty(economy.ReplayCursor?.ActivationId) || economy.ReplayCursor?.ClosedThroughSequence != 0;
}

public interface IIndexedRunHistory : IList<RunSummary>
{
    IReadOnlyList<RunOverview> Overview { get; }
    RunSummary GetById(string runId);
}

internal interface ICommittedRunHistory
{
    void ReleaseCommittedDetail(string runId);
    void RestoreDetailSource(IList<RunSummary> source);
}

internal interface ICorrectableRunHistory
{
    void AcceptCorrection(RunSummary replacement);
}

public static class RunHistory
{
    public static RunHistoryView Ascending(IList<RunSummary> runs) => new(runs,
        Overview(runs).OrderBy(run => run.StartedUtc).ThenBy(run => run.RunId, StringComparer.Ordinal).ToArray());

    public static RunHistoryView ById(IList<RunSummary> runs) => new(runs,
        Overview(runs).OrderBy(run => run.RunId, StringComparer.Ordinal).ToArray());

    public static RunHistoryView Ordered(IList<RunSummary> runs, bool byEnd = false) => new(runs,
        Overview(runs).OrderByDescending(run => byEnd ? run.EndedUtc : run.StartedUtc).ThenBy(run => run.RunId, StringComparer.Ordinal).ToArray());

    public static bool Matches(IList<RunSummary> source, IReadOnlyList<RunSummary> view, string generation)
    {
        if (view is RunHistoryView indexed && source is IIndexedRunHistory) return ReferenceEquals(indexed.Source, source) && view.Count == source.Count
            && indexed.Overview.All(run => run.GenerationId == generation && !string.IsNullOrWhiteSpace(run.RunId))
            && indexed.Overview.Select(run => run.RunId).Distinct(StringComparer.Ordinal).Count() == view.Count;
        var originals = new HashSet<RunSummary>(source);
        return view.Count == source.Count && view.All(run => originals.Contains(run) && run.SaveGenerationId == generation && !string.IsNullOrWhiteSpace(run.RunId))
            && view.Select(run => run.RunId).Distinct(StringComparer.Ordinal).Count() == view.Count;
    }
    public static IReadOnlyList<RunOverview> Overview(IList<RunSummary> runs) => runs is IIndexedRunHistory indexed
        ? indexed.Overview : runs.Select(RunOverview.From).ToArray();

    public static bool ContainsId(IList<RunSummary> runs, string id) => Overview(runs).Any(run => run.RunId == id);

    public static RunSummary GetById(IList<RunSummary> runs, string id) => runs is IIndexedRunHistory indexed
        ? indexed.GetById(id) : runs.First(run => run.RunId == id);

    public static IReadOnlyList<RunSummary> Recent(IList<RunSummary> runs, int count) => Overview(runs)
        .OrderByDescending(run => run.EndedUtc).ThenBy(run => run.RunId, StringComparer.Ordinal).Take(count)
        .Select(run => GetById(runs, run.RunId)).ToArray();
}

public sealed class RunHistoryView : IReadOnlyList<RunSummary>
{
    internal RunHistoryView(IList<RunSummary> source, IReadOnlyList<RunOverview> overview) { Source = source; Overview = overview; }
    public IList<RunSummary> Source { get; }
    public IReadOnlyList<RunOverview> Overview { get; }
    public int Count => Overview.Count;
    public RunSummary this[int index] => RunHistory.GetById(Source, Overview[index].RunId);
    public IEnumerator<RunSummary> GetEnumerator() { for (var index = 0; index < Count; index++) yield return this[index]; }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}

internal sealed class ProjectedRunRows(RunHistoryView source, Func<RunSummary, RunPresentationRow> project) : IReadOnlyList<RunPresentationRow>
{
    public int Count => source.Count;
    public RunPresentationRow this[int index] => project(source[index]);
    public IEnumerator<RunPresentationRow> GetEnumerator() { for (var index = 0; index < Count; index++) yield return this[index]; }
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
