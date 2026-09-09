using System.Globalization;
using Microsoft.VisualBasic.FileIO;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Export;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;
using UltimateDuckovStatistics.UI;

namespace UltimateDuckovStatistics.Tests;

public sealed partial class RouteLifecycleTests
{
    private static readonly string[] LargeHistoryRecordIds = ["history-0000", "history-0001", "history-0008", "history-0003"];

    [Fact]
    [Trait("Category", "M18")]
    public void ThousandDiverseRunsRemainCompleteAcrossReopenPresentationAndExport()
    {
        // This is an explicit workload, not a retention maximum. Shipping run
        // history has no count cap. All data stays in an isolated test directory.
        const int runCount = 1000;
        using var directory = new TemporaryDirectory();
        var clock = Now;
        var repository = new ProfileRepository(directory.Path, () => clock, () => "generation-1");
        var identity = new SaveIdentitySnapshot
        {
            Slot = 1,
            GameVersion = "2.3.30",
            SaveFilePresent = true,
            ContentSha256 = new string('a', 64)
        };
        repository.Open(identity);
        repository.SetEconomyCapabilities(SupportedEconomyCapabilities());
        repository.BeginEconomyActivation("test-route-lifecycle");
        var expectedRuns = new Dictionary<string, (string Route, RunOutcome Outcome, int Visits, double Duration)>();
        var expectedSegments = new Dictionary<string, (string Run, string Map, double Duration, double Healing, long Money)>();
        var expectedMaps = new Dictionary<string, (long Visits, double Duration, double Healing, long Money)>();
        long expectedMoney = 0;
        double expectedHealing = 0;

        for (var index = 0; index < runCount; index++)
        {
            var runId = "history-" + index.ToString("D4", CultureInfo.InvariantCulture);
            var start = Now.AddMinutes(index);
            var tracker = new RunLifecycleTracker(() => runId);
            var firstMap = ((char)('A' + index % 3)).ToString();
            var secondMap = ((char)('A' + (index + 1) % 3)).ToString();
            var maps = index % 2 == 0 ? new[] { firstMap } : new[] { firstMap, secondMap, firstMap };
            RunLifecycleEvent At(RunLifecycleEventKind kind, double seconds, RunStartContext? context = null, MapIdentity? map = null)
            {
                var value = Event(kind, seconds, context, nativeRaidId: runId, map);
                value.TimestampUtc = start.AddSeconds(seconds);
                return value;
            }
            tracker.Apply(At(RunLifecycleEventKind.RaidInitialized, 0));
            tracker.Apply(At(RunLifecycleEventKind.ControlReady, 0, Context(firstMap, runId)));

            for (var visit = 0; visit < maps.Length; visit++)
            {
                var map = maps[visit];
                if (visit > 0)
                {
                    tracker.Apply(At(RunLifecycleEventKind.MapTransitionStarted, visit * 10 - 2));
                    tracker.Apply(At(RunLifecycleEventKind.LoadingEnded, visit * 10 - .1));
                    tracker.Apply(At(RunLifecycleEventKind.DestinationControlReady, visit * 10, map: Map(map)));
                }
                var segment = tracker.ActiveSegmentId!;
                var eventId = runId + ":" + visit.ToString(CultureInfo.InvariantCulture);
                clock = start.AddSeconds(visit * 10 + 1);
                var item = Item(eventId + ":item", tracker, map); item.TimestampUtc = clock;
                var healing = Healing(eventId + ":heal", tracker, segment, map, segment, map);
                healing.TimestampUtc = clock;
                healing.IntegrityTags = IntegrityTags.Normal;
                healing.ActualHealthRestored = index % 7 + 1;
                var currency = Currency(eventId + ":money", tracker, map, CurrencyKind.Money, CurrencyFlowDirection.Inflow, index + 1);
                currency.TimestampUtc = clock;
                var shot = Shot(eventId + ":shot", tracker); shot.TimestampUtc = clock;
                shot.IntegrityTags = IntegrityTags.Normal;
                var combat = Combat(eventId + ":combat", tracker, segment, map, segment, map); combat.TimestampUtc = clock;
                combat.IntegrityTags = IntegrityTags.Normal;

                Assert.True(ItemUseReducer.Apply(repository.Current.Statistics, item));
                Assert.True(tracker.RecordItemUse(item));
                Assert.True(HealingReducer.Apply(repository.Current.Statistics, healing));
                Assert.True(tracker.RecordHealing(healing));
                Assert.True(EconomyStatisticsReducer.Record(repository.Current.Statistics.Economy, "generation-1", currency));
                Assert.True(tracker.RecordCurrencyFlow(currency));
                Assert.True(tracker.RecordShot(shot));
                Assert.True(tracker.RecordCombat(combat));

                var duration = visit == maps.Length - 1 ? 9d : 8d;
                var mapId = "duckov:map:" + map;
                expectedSegments.Add(segment, (runId, mapId, duration, healing.ActualHealthRestored, index + 1));
                expectedMaps.TryGetValue(mapId, out var expected);
                expectedMaps[mapId] = (expected.Visits + 1, expected.Duration + duration,
                    expected.Healing + healing.ActualHealthRestored, expected.Money + index + 1);
                expectedMoney += index + 1;
                expectedHealing += healing.ActualHealthRestored;
            }

            var kind = (index % 5) switch
            {
                3 => RunLifecycleEventKind.Died,
                4 => RunLifecycleEventKind.Interrupted,
                _ => RunLifecycleEventKind.Extracted
            };
            clock = start.AddSeconds((maps.Length - 1) * 10 + 9);
            var run = tracker.Apply(At(kind, (maps.Length - 1) * 10 + 9)).Completed!;
            expectedRuns.Add(runId, (string.Join(">", maps.Select(map => "duckov:map:" + map)),
                kind == RunLifecycleEventKind.Died ? RunOutcome.Died : kind == RunLifecycleEventKind.Interrupted
                    ? RunOutcome.Interrupted : RunOutcome.Extracted,
                maps.Length, (maps.Length - 1) * 8 + 9));
            // Build prior history through the shipping reducer, then exercise
            // repository completion at the large-history boundary itself.
            Assert.True(index == runCount - 1 ? repository.CompleteRun(run) : RunReducer.Apply(repository.Current.Statistics, run));
        }
        Assert.Null(ProfileFormat.ValidateRecoveryCandidate(repository.Current));
        repository.CloseClean();
        var reopened = new ProfileRepository(directory.Path, () => clock.AddMinutes(1), () => "unexpected-generation");
        var open = reopened.Open(identity);
        Assert.False(open.CreatedNew, string.Join(" | ", open.LoadFailures));
        Assert.False(open.RotatedGeneration);
        Assert.False(open.RecoveredSnapshot);
        Assert.Empty(open.LoadFailures);
        Assert.Equal("generation-1", reopened.CurrentGenerationId);
        Assert.Null(ProfileFormat.ValidateRecoveryCandidate(reopened.Current));
        AssertHistory(reopened.Current.Statistics.Runs, reopened.Current.Statistics.RunTotals);
        Assert.Equal(expectedSegments.Count, reopened.Current.Statistics.Overall.ActivationCount);
        Assert.Equal(expectedHealing, reopened.Current.Statistics.Overall.ActualHealthRestored);
        Assert.Equal(expectedMoney, reopened.Current.Statistics.Economy.Currencies["Money"].Totals.GrossInflow);

        var projection = StatisticsPanelProjectionFactory.Create(reopened.Current, SupportedEconomyCapabilities(), new(), new());
        var presentation = Assert.IsType<RunsPresentation>(RunsPresentationFactory.Create(projection, reopened.CurrentGenerationId));
        Assert.Equal(expectedRuns.Keys.Reverse(), presentation.Runs.Select(run => run.Id));
        var selection = new RunsSelection();
        Assert.True(selection.Refresh(presentation, reopened.CurrentGenerationId));
        foreach (var id in new[] { "history-0000", "history-0501", "history-0999" })
        {
            Assert.True(selection.Route(reopened.CurrentGenerationId, id));
            Assert.Equal(id, selection.Selected!.Id);
            Assert.Equal(expectedRuns[id].Visits, selection.Selected.Segments.Count);
        }
        var records = Assert.IsType<RecordsPresentation>(RecordsPresentationFactory.Create(projection, reopened.CurrentGenerationId));
        Assert.Equal(LargeHistoryRecordIds, records.Overall.Select(card => card.RunId));
        Assert.Equal("history-0000", reopened.Current.Statistics.RunRecords.Extraction.Shortest!.RunId);
        Assert.Equal("history-0001", reopened.Current.Statistics.RunRecords.Extraction.Longest!.RunId);
        Assert.Equal("history-0008", reopened.Current.Statistics.RunRecords.Death.Shortest!.RunId);
        Assert.Equal("history-0003", reopened.Current.Statistics.RunRecords.Death.Longest!.RunId);

        var result = ProfileExportWriter.WriteToRoot(reopened.CaptureExportSnapshot(), Path.Combine(directory.Path, "exports"), clock);
        Assert.Equal(37, result.Files.Count);
        var exported = new AtomicJsonStore<StatisticsExportDocument>().Load(Path.Combine(result.Directory, "statistics.json")).Value!;
        Assert.Equal(reopened.CurrentGenerationId, exported.GenerationId);
        AssertHistory(exported.Runs, exported.RunTotals);
        Assert.Equal(expectedSegments.Count, exported.Overall.ActivationCount);
        Assert.Equal(expectedHealing, exported.Overall.ActualHealthRestored);
        Assert.Equal(expectedMoney, exported.Economy.Currencies["Money"].Totals.GrossInflow);
        var csv = result.Files.Where(path => path.EndsWith(".csv", StringComparison.Ordinal))
            .ToDictionary(path => Path.GetFileName(path), ReadHistoryCsv, StringComparer.Ordinal);
        Assert.Equal(expectedRuns.Keys, csv["runs.csv"].Select(row => row["run_id"]));
        Assert.Equal(expectedRuns.Keys, csv["routes.csv"].Select(row => row["run_id"]));
        foreach (var row in csv["routes.csv"])
        {
            var expected = expectedRuns[row["run_id"]];
            Assert.Equal(expected.Route, row["route_signature"]);
            Assert.Equal(expected.Visits, int.Parse(row["segment_count"], CultureInfo.InvariantCulture));
            Assert.Equal(expected.Visits * 4, int.Parse(row["associated_event_count"], CultureInfo.InvariantCulture));
        }
        Assert.Equal(expectedSegments.Count, csv["segments.csv"].Count);
        foreach (var row in csv["segments.csv"])
        {
            var expected = expectedSegments[row["segment_id"]];
            Assert.Equal(expected.Run, row["run_id"]);
            Assert.Equal(expected.Map, row["map_id"]);
            Assert.Equal(expected.Duration, double.Parse(row["active_duration_seconds"], CultureInfo.InvariantCulture));
            Assert.Equal(expected.Healing, double.Parse(row["actual_health_restored"], CultureInfo.InvariantCulture));
            Assert.Equal("1", row["item_activations"]);
            Assert.Equal("1", row["firing_actions"]);
            Assert.Equal("9", row["damage_dealt"]);
        }
        var economyRows = csv["economy_totals.csv"].Where(row => row["scope"] == "run" && row["currency"] == "Money").ToArray();
        Assert.Equal(expectedRuns.Keys, economyRows.Select(row => row["run_id"]));
        Assert.Equal(expectedMoney, economyRows.Sum(row => long.Parse(row["gross_inflow"], CultureInfo.InvariantCulture)));
        Assert.Equal(expectedRuns.Keys, csv["terminal_loadouts.csv"].Select(row => row["run_id"]).Distinct(StringComparer.Ordinal));
        Assert.Equal(expectedRuns.Keys, csv["segment_events.csv"].Select(row => row["run_id"]).Distinct(StringComparer.Ordinal));
        Assert.Equal(expectedSegments.Count * 4, csv["segment_events.csv"].Count);
        reopened.CloseClean();
        output.WriteLine($"M18_LARGE_HISTORY runs={runCount} segments={expectedSegments.Count} exports={result.Files.Count}; managed correctness only, no native rendering or resource claim.");

        void AssertHistory(IReadOnlyList<RunSummary> runs, RunAggregateTotals totals)
        {
            Assert.Equal(expectedRuns.Keys, runs.Select(run => run.RunId));
            Assert.Equal(runCount, totals.TotalRuns);
            Assert.Equal(600, totals.Outcomes[nameof(RunOutcome.Extracted)]);
            Assert.Equal(200, totals.Outcomes[nameof(RunOutcome.Died)]);
            Assert.Equal(200, totals.Outcomes[nameof(RunOutcome.Interrupted)]);
            Assert.Equal(expectedSegments.Count, totals.ItemStatistics.Overall.ActivationCount);
            Assert.Equal(expectedHealing, totals.ItemStatistics.Overall.ActualHealthRestored);
            Assert.Equal(expectedMoney, totals.Economy.Currencies["Money"].Totals.GrossInflow);
            Assert.Equal(expectedSegments.Count, totals.WeaponStatistics.Totals.FiringActions);
            Assert.Equal(expectedSegments.Count * 9, totals.CombatStatistics.Totals.DamageDealt);
            Assert.Equal(runCount, totals.Maps.Values.Sum(map => map.TotalRuns));
            Assert.Equal(expectedMoney, totals.Maps.Values.Sum(map => map.Economy.Currencies["Money"].Totals.GrossInflow));
            Assert.Equal(3, totals.RouteMaps.Count);
            foreach (var run in runs)
            {
                var expected = expectedRuns[run.RunId];
                Assert.Equal(expected.Route, run.RouteSignature);
                Assert.Equal(expected.Outcome, run.Outcome);
                Assert.Equal(expected.Duration, run.ActiveDurationSeconds);
                Assert.Equal(expected.Visits, run.Segments.Count);
            }
            foreach (var (id, expected) in expectedMaps)
            {
                var actual = totals.RouteMaps[id];
                Assert.Equal(expected.Visits, actual.SegmentVisits);
                Assert.Equal(expected.Duration, actual.ActiveDurationSeconds);
                Assert.Equal(expected.Visits, actual.ItemStatistics.Overall.ActivationCount);
                Assert.Equal(expected.Healing, actual.ItemStatistics.Overall.ActualHealthRestored);
                Assert.Equal(expected.Money, actual.Economy.Currencies["Money"].Totals.GrossInflow);
                Assert.Equal(expected.Visits, actual.WeaponStatistics.Totals.FiringActions);
                Assert.Equal(expected.Visits * 9, actual.CombatStatistics.Totals.DamageDealt);
            }
        }
    }

    private static List<Dictionary<string, string>> ReadHistoryCsv(string path)
    {
        using var parser = new TextFieldParser(path) { HasFieldsEnclosedInQuotes = true };
        parser.SetDelimiters(",");
        var headers = parser.ReadFields()!;
        var rows = new List<Dictionary<string, string>>();
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields()!;
            Assert.Equal(headers.Length, fields.Length);
            rows.Add(headers.Zip(fields).ToDictionary(pair => pair.First, pair => pair.Second, StringComparer.Ordinal));
        }
        return rows;
    }
}
