using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Tests;

public sealed class WorldTimePersistenceTests
{
    private static readonly DateTime Now = new(2026, 8, 20, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    [Trait("Category", "Persistence")]
    public void SchemaElevenMigrationPreservesAllPriorDataAndMarksM12HistoryUnavailable()
    {
        var profile = Document("generation-1");
        profile.SchemaVersion = 11;
        profile.Statistics.SchemaVersion = 11;
        profile.Statistics.Overall.ActivationCount = 42;
        profile.Statistics.WorldTime = null!;

        Assert.True(ProfileMigrator.Migrate(profile));

        Assert.Equal(13, profile.SchemaVersion);
        Assert.Equal(13, profile.Statistics.SchemaVersion);
        Assert.Equal(42, profile.Statistics.Overall.ActivationCount);
        Assert.True(profile.Statistics.WorldTime.HistoricalUnavailable);
        Assert.Contains("predates M12", profile.Statistics.WorldTime.HistoricalProvenance, StringComparison.Ordinal);
        Assert.Equal(0, profile.Statistics.WorldTime.ObservedGameTimeTicks);
        Assert.Equal(0, profile.Statistics.WorldTime.CompletedSleepSessions);
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void DeferredWorldTimeSurvivesCleanRestartAndInterruptedSessionRecovery()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var identity = Identity(1, 100);
        var first = Repository(temporaryDirectory.Path, "generation-1", "session-1");
        first.Open(identity);
        first.SetWorldTimeCapabilities(WorldTimeNativeContractPolicy.Supported("clock", "sleep"));
        Assert.True(first.RecordWorldTimeDeferred(new WorldTimeMutation(1, 9000, 1, 3600)));
        first.Flush();

        var interrupted = Repository(temporaryDirectory.Path, "unused-generation", "session-2");
        var open = interrupted.Open(identity);
        Assert.True(open.InterruptedSessionRecovered);
        AssertWorldTime(interrupted.Current.Statistics.WorldTime, 1, 9000, 1, 3600);
        interrupted.CloseClean();

        var clean = Repository(temporaryDirectory.Path, "unused-generation-2", "session-3");
        clean.Open(identity);
        AssertWorldTime(clean.Current.Statistics.WorldTime, 1, 9000, 1, 3600);
        clean.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void SaveSlotAndGenerationReplacementKeepWorldTimeTotalsIndependent()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var ids = new Queue<string>([
            "generation-slot-1", "session-slot-1",
            "generation-slot-2", "session-slot-2",
            "session-slot-1-reopen", "generation-slot-1-new", "session-slot-1-new"]);
        var repository = Repository(temporaryDirectory.Path, ids);
        repository.Open(Identity(1, 100));
        repository.RecordWorldTimeDeferred(new WorldTimeMutation(1, 100, 0, 0));
        repository.Flush();

        repository.Open(Identity(2, 200));
        Assert.Equal(0, repository.Current.Statistics.WorldTime.CalendarDaysAdvanced);
        repository.RecordWorldTimeDeferred(new WorldTimeMutation(2, 200, 1, 50));
        repository.Flush();

        repository.Open(Identity(1, 100));
        AssertWorldTime(repository.Current.Statistics.WorldTime, 1, 100, 0, 0);
        repository.Rotate(Identity(1, 300), "DuckovNewGame");
        AssertWorldTime(repository.Current.Statistics.WorldTime, 0, 0, 0, 0);
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "Persistence")]
    public void PrimaryBackupAndTemporaryRecoveryRetainExactWorldTimeFields()
    {
        using var temporaryDirectory = new TemporaryDirectory();
        var store = new AtomicJsonStore<ProfileDocument>();
        var path = Path.Combine(temporaryDirectory.Path, "profile.json");
        var first = Document("generation-1");
        first.Statistics.WorldTime = Aggregate(1, 100, 1, 50);
        var second = Document("generation-1");
        second.Revision = 2;
        second.Statistics.WorldTime = Aggregate(2, 200, 2, 100);
        store.Save(path, first);
        AssertWorldTime(store.Load(path, ProfileMigrator.ValidateRecoveryCandidate).Value!.Statistics.WorldTime, 1, 100, 1, 50);
        store.Save(path, second);
        File.WriteAllText(path, "{ corrupt");

        var backup = store.Load(path, ProfileMigrator.ValidateRecoveryCandidate);
        Assert.Equal(AtomicJsonLoadSource.Backup, backup.Source);
        AssertWorldTime(backup.Value!.Statistics.WorldTime, 1, 100, 1, 50);

        var tempPath = Path.Combine(temporaryDirectory.Path, "temporary-profile.json");
        store.Save(tempPath, second);
        File.Move(tempPath, AtomicJsonPaths.GetTemporaryPath(tempPath));
        var temporary = store.Load(tempPath, ProfileMigrator.ValidateRecoveryCandidate);
        Assert.Equal(AtomicJsonLoadSource.Temporary, temporary.Source);
        AssertWorldTime(temporary.Value!.Statistics.WorldTime, 2, 200, 2, 100);
    }

    private static ProfileRepository Repository(string root, params string[] ids) =>
        Repository(root, new Queue<string>(ids));

    private static ProfileRepository Repository(string root, Queue<string> ids) =>
        new(root, () => Now, () => ids.Dequeue());

    private static SaveIdentitySnapshot Identity(int slot, long creationTicks) => new()
    {
        Slot = slot,
        SaveFilePresent = true,
        SaveFileCreationUtcTicks = creationTicks,
        ObservedWriteUtcTicks = creationTicks,
        ObservedLength = 10,
        GameVersion = "2.3.30",
        ContentSha256 = creationTicks.ToString("x", System.Globalization.CultureInfo.InvariantCulture).PadLeft(64, '0')
    };

    private static ProfileDocument Document(string generationId) => new()
    {
        GenerationId = generationId,
        Slot = 1,
        GenerationReason = "test",
        CreatedUtc = Now,
        UpdatedUtc = Now,
        Identity = Identity(1, 100),
        Statistics = new ProfileStatistics
        {
            SaveGenerationId = generationId,
            CreatedUtc = Now,
            UpdatedUtc = Now
        }
    };

    private static WorldTimeStatisticsAggregate Aggregate(long days, long elapsed, long sleeps, long sleepElapsed)
    {
        var value = new WorldTimeStatisticsAggregate
        {
            CalendarDaysAdvanced = days,
            ObservedGameTimeTicks = elapsed,
            CompletedSleepSessions = sleeps,
            SleepAdvancedTimeTicks = sleepElapsed
        };
        WorldTimeStatisticsReducer.InitializeOrRestrictCapabilities(
            value,
            WorldTimeNativeContractPolicy.Supported("clock", "sleep"));
        return value;
    }

    private static void AssertWorldTime(
        WorldTimeStatisticsAggregate value,
        long days,
        long elapsed,
        long sleeps,
        long sleepElapsed)
    {
        Assert.Equal(days, value.CalendarDaysAdvanced);
        Assert.Equal(elapsed, value.ObservedGameTimeTicks);
        Assert.Equal(sleeps, value.CompletedSleepSessions);
        Assert.Equal(sleepElapsed, value.SleepAdvancedTimeTicks);
    }
}
