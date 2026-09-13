using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;
using UltimateDuckovStatistics.Sqlite;

namespace UltimateDuckovStatistics.Tests;

public sealed class IncrementalCompletedRunTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "uds-completed-records-" + Guid.NewGuid().ToString("N"));
    private readonly ProfileRecordCodec codec = new(NativeProfileJsonWriter.WriteRecord);
    private static readonly DateTime Origin = new(2026, 8, 10, 10, 0, 0, DateTimeKind.Utc);
    public IncrementalCompletedRunTests()
    { Directory.CreateDirectory(directory); SqliteLibrary.Initialize(Path.Combine(AppContext.BaseDirectory, "sqlite3.dll")); }

    [Fact]
    public async Task CompletingDistinctWeaponsWritesOnlyNewRunAndAffectedLifetimeEntries()
    {
        var profile = NativeProfileJsonWriterTests.CreateProfile();
        using var storage = new SqliteProfileStorage(Path.Combine(directory, "profile.sqlite"), codec);
        storage.Import(profile, null, null); var journal = new ProfileChangeJournal(profile.GenerationId, codec);
        for (var index = 0; index < 6; index++)
        {
            var tracker = IncrementalCheckpointProtocolTests.Started(profile.GenerationId, route: true, runId: "run-" + index);
            var shot = WeaponStatisticsTests.Shot("shot-" + index, "weapon-" + index, "Weapon", "ammo", "Ammo", 2);
            shot.SaveGenerationId = profile.GenerationId; shot.RunId = tracker.ActiveRunId!; shot.SegmentId = tracker.ActiveSegmentId;
            Assert.True(tracker.RecordShot(shot));
            var summary = tracker.Apply(new RunLifecycleEvent
            {
                Kind = RunLifecycleEventKind.Extracted,
                TimestampUtc = Origin.AddSeconds(10),
                MonotonicSeconds = 10
            }).Completed!;
            Assert.True(RunReducer.Apply(profile.Statistics, summary)); profile.Revision++;
            journal.CompletedRun(profile, summary); var command = journal.Capture(profile);
            Assert.Single(command.Records, record => record.Address.Kind == ProfileRecordKind.CompletedRun);
            foreach (var record in command.Records.Where(record => record.Address.Kind == ProfileRecordKind.RunMetricEntry && record.Address.Second == "1"))
                Assert.Equal("weapon-" + index, record.Address.Third);
            Assert.DoesNotContain(command.Records, record => record.Address.Kind == ProfileRecordKind.CompletedRun && record.Address.First != summary.RunId);
            await storage.Commit(command); journal.Acknowledge(command);
            var reopened = storage.Load()!.Profile;
            Assert.Null(ProfileFormat.ValidateRecoveryCandidate(reopened));
            Assert.Equal(codec.Encode(profile), codec.Encode(reopened));
            Assert.Equal(index + 1, reopened.Statistics.RunTotals.WeaponStatistics.Weapons.Count);
        }
    }

    public void Dispose() { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
}
