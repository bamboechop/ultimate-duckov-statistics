using UltimateDuckovStatistics.Core.Encounters;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;
using UltimateDuckovStatistics.Sqlite;

namespace UltimateDuckovStatistics.Tests;

public sealed partial class EncounterPersistenceTests
{
    [Fact]
    public async Task KillDistanceHighlightsReadPersistedEncountersAfterReopeningWithoutChangingData()
    {
        var profile = NativeProfileJsonWriterTests.CreateProfile();
        profile.EncounterHistory = Records();
        var tracker = IncrementalCheckpointProtocolTests.Started(profile.GenerationId, runId: "run-1");
        var run = tracker.Apply(new RunLifecycleEvent
        { Kind = RunLifecycleEventKind.Extracted, TimestampUtc = Now.AddSeconds(20), MonotonicSeconds = 20 }).Completed!;
        Assert.True(RunReducer.Apply(profile.Statistics, run));
        using (var storage = new SqliteProfileStorage(Database, codec)) storage.Import(profile, null, null);
        using var reopened = new SqliteProfileStorage(Database, codec);
        var loaded = reopened.Load()!.Profile;
        var before = codec.Encode(loaded);
        using var query = new KillDistanceHighlightsQuery();
        query.Refresh(loaded);
        for (var i = 0; query.Loading && i < 500; i++) { await Task.Delay(10); query.Refresh(loaded); }
        Assert.False(query.Loading); Assert.False(query.Failed);
        Assert.Equal(24, query.Value!.Longest!.Meters);
        Assert.Equal(24, query.Value.Shortest!.Meters);
        Assert.Equal(before, codec.Encode(reopened.Load()!.Profile));
    }

}
