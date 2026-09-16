using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Encounters;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.UI;

namespace UltimateDuckovStatistics.Tests;

public sealed class KillDistanceHighlightsTests
{
    [Fact]
    public void UsesFatalPlayerPositionsForEligiblePlayerKillsIncludingEffectsAndRealZero()
    {
        var records = EncounterPersistenceTests.Records();
        var first = records.Single(record => record.Encounter != null);
        first.Encounter!.HasGaps = true; // A damage gap does not invalidate observed fatal positions.
        first.Encounter.SourcePosition = new() { X = 9000 };
        first.Encounter.PlayerPosition!.Y = 8000; // Horizontal distance, consistent with Map & Kills.
        var zero = Kill("zero", 0); records.Add(zero);
        var other = Kill("other", 999); other.Encounter!.Outcome = EncounterOutcome.OtherDeath; records.Add(other);
        var death = Kill("death", 999); death.Encounter!.Outcome = EncounterOutcome.PlayerDeath; records.Add(death);
        var unknown = Kill("unknown", 999); unknown.Encounter!.FinalSource!.Credit = EncounterCredit.Unknown; records.Add(unknown);
        var result = Calculate(records);
        Assert.Equal(24, result.Longest!.Meters);
        Assert.Equal("actor", result.Longest.EncounterId);
        Assert.Equal(0, result.Shortest!.Meters);
        Assert.Equal("zero", result.Shortest.EncounterId);
        Assert.Null(Calculate(records, new()).Longest);
    }

    [Fact]
    public void MissingNonfiniteAndWrongMapPositionsCannotBecomeRecords()
    {
        var records = EncounterPersistenceTests.Records();
        records.RemoveAll(record => record.Encounter != null);
        var missing = Kill("missing", 30); missing.Encounter!.PlayerPosition = null; records.Add(missing);
        var invalid = Kill("invalid", 40); invalid.Encounter!.EnemyPosition!.X = float.NaN; records.Add(invalid);
        var crossMap = Kill("cross", 50); crossMap.Encounter!.EnemyPosition!.MapId = "Cellar"; records.Add(crossMap);
        var noVisit = Kill("no-visit", 60); noVisit.Encounter!.OutcomeVisitId = "absent"; records.Add(noVisit);
        Assert.Null(Calculate(records).Longest);
        var valid = Kill("valid", 5);
        valid.Encounter!.PlayerPosition!.MapId = "duckov:map:GroundZero";
        valid.Encounter.EnemyPosition!.MapId = "GroundZero";
        valid.Encounter.OutcomeVisitId = "visit"; valid.VisitId = "earlier-visit";
        records.Add(valid);
        Assert.Equal(5, Calculate(records).Longest!.Meters);
    }

    [Fact]
    public void TiesAreStableRegardlessOfStorageEnumerationOrder()
    {
        var records = EncounterPersistenceTests.Records();
        records.RemoveAll(record => record.Encounter != null);
        records.Add(Kill("b", 5)); records.Add(Kill("a", 5));
        Assert.Equal("a", Calculate(records).Shortest!.EncounterId);
        records.Reverse();
        Assert.Equal("a", Calculate(records).Longest!.EncounterId);
    }

    [Fact]
    public async Task IndexedQueryAvoidsRouteLootAndUnrelatedRefreshesButTracksCorrectedKills()
    {
        var source = new Source();
        var history = new EncounterHistory(source);
        var profile = Profile(history);
        using var query = new KillDistanceHighlightsQuery();
        await Complete(query, profile);
        Assert.Equal(24, query.Value!.Longest!.Meters);
        Assert.Equal(new[] { EncounterRecordKind.Visit, EncounterRecordKind.Encounter }, source.Reads);
        var same = query.Value;
        profile.Revision++;
        Assert.False(query.Refresh(profile));
        Assert.Same(same, query.Value);
        var loot = source.Records.Single(record => record.Loot != null);
        history.Put(loot, new ProfileRecordCodec().Encode(loot)); profile.Revision++;
        Assert.False(query.Refresh(profile));
        var corrected = Kill("actor", 35.29f);
        history.Put(corrected, new ProfileRecordCodec().Encode(corrected)); profile.Revision++;
        await Complete(query, profile);
        Assert.Equal(35.29, query.Value!.Longest!.Meters, 2);
        profile.Statistics.Runs[0].RecordEligible = false; profile.Revision++;
        await Complete(query, profile);
        Assert.Null(query.Value!.Longest);
    }

    [Fact]
    public async Task ReplacedProfileCannotPublishPreviousWorkerAndReadFailureCanRetry()
    {
        using var started = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var source = new Source { BeforeRead = () => { started.Set(); release.Wait(TimeSpan.FromSeconds(5)); } };
        var first = Profile(new EncounterHistory(source));
        var second = Profile(new EncounterHistory(new Source())); second.GenerationId = "other";
        using var query = new KillDistanceHighlightsQuery();
        try
        {
            query.Refresh(first);
            Assert.True(started.Wait(TimeSpan.FromSeconds(5)));
            query.Reset(); query.Refresh(second);
            Assert.Null(query.Value);
        }
        finally { release.Set(); }
        await Complete(query, second);
        Assert.Null(query.Value!.Longest); // No run belongs to the new generation.
        source.BeforeRead = () => throw new IOException("transient read failure");
        await Complete(query, first);
        Assert.True(query.Failed); Assert.Null(query.Value);
        source.BeforeRead = null; query.RetryFailed();
        await Complete(query, first);
        Assert.False(query.Failed); Assert.Equal(24, query.Value!.Longest!.Meters);
    }

    [Fact]
    public void PresentationDistinguishesEmptyLoadingAndFailureFromZeroAndLocalizesLabels()
    {
        var projection = new StatisticsPanelProjection();
        string Value() => OverviewHighlightsPresentationFactory.Create(projection, key => UiText.EnglishFallbacks[key])
            .Single(row => row.Metric == OverviewHighlightMetric.ShortestKillDistance).Value;
        Assert.Equal("—", Value());
        projection.KillDistancesLoading = true; Assert.Equal("Loading…", Value());
        projection.KillDistancesLoading = false; projection.KillDistancesFailed = true; Assert.Equal("Unavailable", Value());
        projection.KillDistancesFailed = false;
        var records = EncounterPersistenceTests.Records(); records.Add(Kill("zero", 0));
        projection.KillDistances = Calculate(records);
        Assert.Equal(0d.ToString("0.00", System.Globalization.CultureInfo.CurrentCulture) + " m", Value());
    }

    private static KillDistanceHighlights Calculate(List<EncounterRecord> records, HashSet<string>? eligible = null) =>
        KillDistanceHighlights.Read(records.Where(record => record.Visit != null), records.Where(record => record.Encounter != null),
            eligible ?? new() { "run-1" }, CancellationToken.None);
    private static EncounterRecord Kill(string id, float meters) => new()
    {
        RunId = "run-1",
        Id = id,
        Kind = EncounterRecordKind.Encounter,
        VisitId = "visit",
        Encounter = new()
        {
            ActorId = id,
            Outcome = EncounterOutcome.PlayerKill,
            EndedSeconds = 9,
            FinalSource = new() { Credit = EncounterCredit.Player },
            PlayerPosition = new(),
            EnemyPosition = new() { X = meters }
        }
    };
    private static ProfileDocument Profile(IList<EncounterRecord> history)
    {
        var profile = NativeProfileJsonWriterTests.CreateProfile(); profile.EncounterHistory = history;
        profile.Statistics.Runs.Add(new RunSummary { RunId = "run-1", SaveGenerationId = profile.GenerationId, RecordEligible = true });
        return profile;
    }
    private static async Task Complete(KillDistanceHighlightsQuery query, ProfileDocument profile)
    {
        query.Refresh(profile);
        for (var i = 0; query.Loading && i < 500; i++) { await Task.Delay(10); query.Refresh(profile); }
        Assert.False(query.Loading);
    }
    private sealed class Source : IEncounterHistorySource
    {
        public List<EncounterRecord> Records { get; } = EncounterPersistenceTests.Records();
        public List<EncounterRecordKind> Reads { get; } = new();
        public Action? BeforeRead { get; set; }
        public int Count => throw new InvalidOperationException("No whole-history count required");
        public EncounterRecord? Find(string runId, EncounterRecordKind kind, string id) => throw new InvalidOperationException("No point reads required");
        public IEnumerable<EncounterRecord> Read(string? runId, EncounterRecordKind? kind)
        {
            Assert.Null(runId); Assert.True(kind is EncounterRecordKind.Visit or EncounterRecordKind.Encounter);
            BeforeRead?.Invoke(); Reads.Add(kind!.Value);
            return Records.Where(record => record.Kind == kind);
        }
    }
}
