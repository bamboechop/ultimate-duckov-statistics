using System.Diagnostics;
using System.Text.Json;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Tests;

public sealed partial class RouteLifecycleTests
{
    private readonly Xunit.Abstractions.ITestOutputHelper output;
    public RouteLifecycleTests(Xunit.Abstractions.ITestOutputHelper output) => this.output = output;

    [Theory]
    [InlineData(0)]
    [InlineData(200)]
    [Trait("Category", "M18")]
    public void Mixed144000EventsPersistExactlyAcrossSixSegmentsAndCurrentFormatReopen(int priorRunCount)
    {
        using var directory = new TemporaryDirectory();
        var clock = Now.AddDays(-1);
        var repository = new ProfileRepository(directory.Path, () => clock, () => "generation-1");
        var identity = new SaveIdentitySnapshot { Slot = 1, GameVersion = "2.3.30", SaveFilePresent = true, ContentSha256 = new string('a', 64) };
        repository.Open(identity);
        repository.EnableDeferredItemPersistence();
        repository.SetEconomyCapabilities(SupportedEconomyCapabilities());
        repository.SetCraftingCapabilities(CraftingNativeContractPolicy.Supported("delivery", "recipe", "items", "money"));
        repository.SetWorldTimeCapabilities(WorldTimeNativeContractPolicy.Supported("clock", "sleep"));
        repository.BeginEconomyActivation("test-route-lifecycle");
        // Real reducers build an isolated, sequential prior history. Nothing is
        // injected into a user's profile or attributed to actual native play.
        for (var prior = 0; prior < priorRunCount; prior++)
        {
            var history = new RunLifecycleTracker(() => "history-" + prior);
            var start = Now.AddDays(-1).AddSeconds(prior * 2);
            var initialized = Event(RunLifecycleEventKind.RaidInitialized, 0, nativeRaidId: "1"); initialized.TimestampUtc = start;
            var ready = Event(RunLifecycleEventKind.ControlReady, 0, context: Context("A", "1")); ready.TimestampUtc = start;
            var terminal = Event(RunLifecycleEventKind.Extracted, 1); terminal.TimestampUtc = start.AddSeconds(1);
            history.Apply(initialized); history.Apply(ready);
            Assert.True(RunReducer.Apply(repository.Current.Statistics, history.Apply(terminal).Completed!));
        }
        clock = Now;
        repository.Flush();
        var tracker = Start("A");
        repository.SaveActiveRun(tracker.CreateCheckpoint(clock, 0)!);
        var windows = new List<object>();
        var mapNames = new[] { "A", "B", "A", "C", "B", "C" };
        long failures = 0;
        const int eventCount = 144_000, chunkSize = 6000;
        for (var chunk = 0; chunk < eventCount / chunkSize; chunk++)
        {
            if (chunk > 0 && chunk % 4 == 0)
                Transition(tracker, chunk * 10 - 1, chunk * 10, mapNames[chunk / 4]);
            var allocationStart = GC.GetAllocatedBytesForCurrentThread();
            var timer = Stopwatch.StartNew();
            for (var offset = 0; offset < chunkSize; offset++)
            {
                var index = chunk * chunkSize + offset;
                var seconds = chunk * 10 + offset / 1000d;
                clock = Now.AddSeconds(seconds);
                var map = mapNames[chunk / 4];
                var segment = tracker.ActiveSegmentId!;
                var id = "mixed:" + index;
                switch (index % 8)
                {
                    case 0:
                        var item = Item(id, tracker, map); item.TimestampUtc = clock;
                        if (!repository.RecordDeferred(item) || !tracker.RecordItemUse(item)) failures++;
                        break;
                    case 1:
                        var shot = Shot(id, tracker); shot.TimestampUtc = clock;
                        if (!tracker.RecordShot(shot)) failures++;
                        break;
                    case 2:
                        var combat = Combat(id, tracker, segment, map, segment, map); combat.TimestampUtc = clock;
                        if (!tracker.RecordCombat(combat)) failures++;
                        break;
                    case 3:
                        var healing = Healing(id, tracker, segment, map, segment, map); healing.TimestampUtc = clock;
                        if (!repository.RecordDeferred(healing) || !tracker.RecordHealing(healing)) failures++;
                        break;
                    case 4:
                        var currency = Currency(id, tracker, map, CurrencyKind.Money, CurrencyFlowDirection.Inflow, 3);
                        currency.TimestampUtc = clock;
                        if (!repository.RecordDeferred(currency) || !tracker.RecordCurrencyFlow(currency)) failures++;
                        break;
                    case 5:
                        if (!repository.RecordWorldTimeDeferred(new WorldTimeMutation(0, 10, 0, 0))) failures++;
                        break;
                    case 6:
                        if (!repository.RecordCraftingDeferred(new CraftingMutation("generation-1", clock,
                            [new CraftingMutationRow("131", "Output", "1026", 1, 1, new() { ["1"] = 1 },
                                resources: [new CraftingResourceMutation("764", "Parts", 1, 2)])]))) failures++;
                        break;
                    case 7:
                        tracker.ObserveEquipment(Snapshot(), clock, seconds);
                        break;
                }
            }
            timer.Stop();
            var allocated = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
            var persistence = Stopwatch.StartNew();
            repository.SaveActiveRun(tracker.CreateCheckpoint(clock, chunk * 10 + 6)!);
            repository.SaveSnapshot(repository.CapturePersistenceSnapshot());
            persistence.Stop();
            windows.Add(new
            {
                chunk,
                events = chunkSize,
                processingMilliseconds = timer.Elapsed.TotalMilliseconds,
                allocatedBytes = allocated,
                retainedBytes = GC.GetTotalMemory(forceFullCollection: true),
                persistenceMilliseconds = persistence.Elapsed.TotalMilliseconds,
                profileBytes = new FileInfo(repository.CurrentProfilePath!).Length
            });
        }
        Assert.Equal(0, failures);
        var run = tracker.Apply(Event(RunLifecycleEventKind.Extracted, 240)).Completed!;
        clock = Now.AddSeconds(240);
        Assert.True(repository.CompleteRun(run));
        Assert.Equal(72_000, run.SegmentEventAssociations.Sum(value => value.Count));
        Assert.Equal(6, run.Segments.Count);
        Assert.Equal(24, run.SegmentEventAssociations.Count);
        repository.CloseClean();
        var reopened = new ProfileRepository(directory.Path, () => clock.AddSeconds(300), () => "unexpected-generation", output.WriteLine);
        var result = reopened.Open(identity);
        Assert.False(result.CreatedNew, string.Join("\n", result.LoadFailures));
        Assert.False(result.RecoveredSnapshot);
        Assert.Empty(result.LoadFailures);
        Assert.Equal("generation-1", reopened.CurrentGenerationId);
        var statistics = reopened.Current.Statistics;
        Assert.Equal(priorRunCount + 1, statistics.Runs.Count);
        Assert.Equal(18_000, statistics.Overall.ActivationCount);
        Assert.Equal(126_000, statistics.Overall.ActualHealthRestored);
        Assert.Equal(18_000, statistics.RunTotals.WeaponStatistics.Totals.FiringActions);
        Assert.Equal(162_000, statistics.RunTotals.CombatStatistics.Totals.DamageDealt);
        Assert.Equal(54_000, statistics.Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.Equal(54_000, statistics.RunTotals.Economy.Currencies["Money"].Totals.GrossInflow);
        Assert.Equal(180_000, statistics.WorldTime.ObservedGameTimeTicks);
        Assert.Equal(18_000, statistics.Crafting.CompletionActions);
        Assert.Equal(36_000, statistics.Crafting.Resources["764"].ConsumedQuantity);
        Assert.Equal(3, statistics.RunTotals.RouteMaps.Count);
        Assert.All(statistics.RunTotals.RouteMaps.Values, map => Assert.Equal(6000, map.WeaponStatistics.Totals.FiringActions));
        Assert.Null(ProfileFormat.ValidateRecoveryCandidate(reopened.Current));
        reopened.CloseClean();
        output.WriteLine("M18_SYNTHETIC_WORKLOAD " + JsonSerializer.Serialize(new
        {
            eventCount,
            priorRunCount,
            segments = 6,
            maps = 3,
            exactReopen = true,
            measurementScope = "Managed event construction, repository/tracker/reducer fan-out; persistence and full GC outside processing windows. No native frame-time claim.",
            windows
        }));
    }
}
