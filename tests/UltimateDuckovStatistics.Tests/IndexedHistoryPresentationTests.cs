using System.Collections;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.UI;

namespace UltimateDuckovStatistics.Tests;

public sealed class IndexedHistoryPresentationTests
{
    private static readonly string[] LastPageIds = { "r3", "r2", "r1", "r0" };
    [Fact]
    public void EmptyReplayCursorDoesNotCauseEveryCompletedRunToBeReloadedAtStartup()
    {
        var run = new RunSummary { RunId = "r", SaveGenerationId = "g" };
        run.Segments.Add(new MapSegmentSummary());
        Assert.False(RunOverview.From(run).ReplayCompactionPending);
        run.Segments[0].Economy.ReplayCursor.ActivationId = "activation";
        run.Segments[0].Economy.ReplayCursor.ClosedThroughSequence = 10;
        Assert.True(RunOverview.From(run).ReplayCompactionPending);
        EconomyStatisticsReducer.ClearReplayCursor(run.Segments[0].Economy);
        Assert.False(RunOverview.From(run).ReplayCompactionPending);
    }
    [Fact]
    public void ThousandRunHistoryPublicationReadsNoDetailAndSelectionReadsOnlyItsIdentity()
    {
        var history = new CountingHistory(1000);
        var profile = Profile(history);
        var projection = new StatisticsPanelProjection { Profile = profile, Runs = RunStatisticsViewModelFactory.Create(profile) };
        var presentation = RunsPresentationFactory.Create(projection, "g")!;
        Assert.Equal(1000, presentation.Runs.Count);
        Assert.Empty(history.Reads);
        var selection = new RunsSelection(); selection.Refresh(presentation, "g");
        Assert.True(selection.Select("r10"));
        Assert.Equal("r10", selection.Selected!.Id);
        Assert.Equal("r10", Assert.Single(history.Reads));
        Assert.Same(selection.Selected, selection.Selected);
        Assert.Single(history.Reads);
    }

    [Fact]
    public void MissingDetailIsUnavailableAndDoesNotFallBackToAnotherRun()
    {
        var history = new CountingHistory(1000) { UnavailableId = "r10" };
        var profile = Profile(history);
        var projection = new StatisticsPanelProjection { Profile = profile, Runs = RunStatisticsViewModelFactory.Create(profile) };
        var selection = new RunsSelection(); selection.Refresh(RunsPresentationFactory.Create(projection, "g")!, "g");
        selection.Select("r10");
        Assert.Null(selection.Selected); Assert.True(selection.RequestedRunUnavailable);
        Assert.IsType<InvalidDataException>(selection.DetailFailure);
        selection.Select("r20");
        Assert.Equal("r20", selection.Selected!.Id); Assert.False(selection.RequestedRunUnavailable); Assert.Null(selection.DetailFailure);
    }

    [Fact]
    public void EquipmentPagesReachAllHistoryWithoutReadingSkippedRowsAndRejectStaleGeneration()
    {
        var history = new CountingHistory(1000);
        var profile = Profile(history);
        var projection = StatisticsPanelProjectionFactory.Create(profile, new(), new(), new());
        history.Reads.Clear();
        var presentation = EquipmentPresentationFactory.Create(projection, "g")!;
        Assert.Equal(12, presentation.Recent.Count); Assert.Equal(1000, presentation.HistoryTotal);
        Assert.Equal(12, history.Reads.Count);
        history.Reads.Clear();
        var last = presentation.LoadHistoryPage(996)!;
        Assert.Equal(996, last.HistoryOffset); Assert.Equal(4, last.Recent.Count);
        Assert.Equal(4, history.Reads.Count);
        Assert.Equal(LastPageIds, last.Recent.Select(run => run.RunId));
        var selection = new EquipmentSelection(); selection.Refresh(last);
        Assert.True(selection.MoveHistory("g", forward: false)); Assert.Equal(984, selection.Snapshot!.HistoryOffset);
        Assert.False(selection.MoveHistory("other", forward: true));
        profile.Statistics.SaveGenerationId = "other";
        Assert.Null(presentation.LoadHistoryPage(12));
    }

    [Fact]
    public void SummaryAndRecordPublicationDoNotEnumerateUnreferencedDetails()
    {
        var history = new CountingHistory(1000);
        var profile = Profile(history);
        var projection = StatisticsPanelProjectionFactory.Create(profile, new(), new(), new());
        history.Reads.Clear();
        Assert.NotNull(RecordsPresentationFactory.Create(projection, "g"));
        Assert.Empty(history.Reads);
        Assert.True(RunHistory.Matches(history, projection.Runs.Runs, "g"));
        Assert.False(RunHistory.Matches(new CountingHistory(1000), projection.Runs.Runs, "g"));
    }

    private static ProfileDocument Profile(IList<RunSummary> history) => new()
    { GenerationId = "g", Statistics = new ProfileStatistics { SaveGenerationId = "g", Runs = history } };

    private sealed class CountingHistory : IIndexedRunHistory
    {
        private readonly List<RunSummary> runs;
        private readonly Dictionary<string, RunSummary> identities;
        public List<string> Reads { get; } = new();
        public string? UnavailableId { get; init; }
        public CountingHistory(int count)
        {
            var start = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
            runs = Enumerable.Range(0, count).Select(index => new RunSummary
            {
                RunId = "r" + index,
                SaveGenerationId = "g",
                StartedUtc = start.AddMinutes(index),
                EndedUtc = start.AddMinutes(index + 1),
                Outcome = RunOutcome.Extracted,
                LifecycleCapability = AdapterCapabilityState.Supported,
                StartingMapId = "map",
                StartingMapDisplayName = "Map",
                StartingMapKnown = true
            }).ToList();
            identities = runs.ToDictionary(run => run.RunId, StringComparer.Ordinal);
            Overview = runs.Select(RunOverview.From).ToArray();
        }
        public IReadOnlyList<RunOverview> Overview { get; }
        public RunSummary GetById(string runId)
        { Reads.Add(runId); if (runId == UnavailableId) throw new InvalidDataException("Frozen detail checksum failed."); return identities[runId]; }
        public int Count => runs.Count;
        public bool IsReadOnly => true;
        public RunSummary this[int index] { get => GetById(runs[index].RunId); set => throw new NotSupportedException(); }
        public int IndexOf(RunSummary item) => runs.IndexOf(item);
        public bool Contains(RunSummary item) => runs.Contains(item);
        public IEnumerator<RunSummary> GetEnumerator() { foreach (var run in runs) yield return GetById(run.RunId); }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public void CopyTo(RunSummary[] array, int arrayIndex) => throw new NotSupportedException();
        public void Add(RunSummary item) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public void Insert(int index, RunSummary item) => throw new NotSupportedException();
        public bool Remove(RunSummary item) => throw new NotSupportedException();
        public void RemoveAt(int index) => throw new NotSupportedException();
    }
}
