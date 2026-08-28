using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;

namespace UltimateDuckovStatistics.Tests;

public sealed class EconomyHoldingsPersistenceTests
{
    private static readonly DateTime Now = new(2026, 8, 28, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    [Trait("Category", "M15")]
    [Trait("Category", "Persistence")]
    public void DeferredHoldingsSurviveRecoveryAndRestartOnlyAsLastObserved()
    {
        using var directory = new TemporaryDirectory();
        var identity = Identity(1, 100);
        var first = Repository(directory.Path, "generation-1", "session-1");
        first.Open(identity);
        first.SetEconomyHoldingsCapabilities(
            EconomyHoldingsNativeContractPolicy.Supported("money", "cash", "liquid"));
        Assert.True(first.RecordEconomyHoldingsDeferred(
            new EconomyHoldingsMutation(first.Current.GenerationId, Now, 750, 25, "native")));
        first.Flush();

        var recovered = Repository(directory.Path, "unused-generation", "session-2");
        var open = recovered.Open(identity);

        Assert.True(open.InterruptedSessionRecovered);
        AssertLastObserved(recovered.Current.Statistics.Holdings, 750, 25);
        Assert.Equal(AdapterCapabilityState.Supported, recovered.Current.Statistics.Holdings.Capabilities.Money.State);
        Assert.Equal(AdapterCapabilityState.Supported, recovered.Current.Statistics.Holdings.Capabilities.Cash.State);
        recovered.CloseClean();

        var clean = Repository(directory.Path, "unused-generation-2", "session-3");
        clean.Open(identity);
        AssertLastObserved(clean.Current.Statistics.Holdings, 750, 25);
        clean.CloseClean();
    }

    [Fact]
    [Trait("Category", "M15")]
    [Trait("Category", "Recovery")]
    public void PrimaryBackupAndTemporaryRecoveryRetainExactHoldingsAndCapabilities()
    {
        using var directory = new TemporaryDirectory();
        var store = new AtomicJsonStore<ProfileDocument>();
        var path = Path.Combine(directory.Path, "profile.json");
        var first = Document("generation-1", 100, 10);
        var second = Document("generation-1", 200, 20);
        second.Revision = 2;
        second.Statistics.Holdings.Capabilities =
            EconomyHoldingsNativeContractPolicy.MoneySupportedCashUnavailable("money", "cash unavailable");
        EconomyHoldingsReducer.MarkUnavailable(
            second.Statistics.Holdings,
            "generation-1",
            money: false,
            cash: true,
            "cash unavailable");

        store.Save(path, first);
        AssertCurrent(store.Load(path, ProfileMigrator.ValidateRecoveryCandidate).Value!.Statistics.Holdings, 100, 10);
        store.Save(path, second);
        File.WriteAllText(path, "{ corrupt");

        var backup = store.Load(path, ProfileMigrator.ValidateRecoveryCandidate);
        Assert.Equal(AtomicJsonLoadSource.Backup, backup.Source);
        AssertCurrent(backup.Value!.Statistics.Holdings, 100, 10);
        Assert.Equal(AdapterCapabilityState.Supported, backup.Value.Statistics.Holdings.Capabilities.Cash.State);

        var temporaryPath = Path.Combine(directory.Path, "temporary-profile.json");
        store.Save(temporaryPath, second);
        File.Move(temporaryPath, AtomicJsonPaths.GetTemporaryPath(temporaryPath));
        var temporary = store.Load(temporaryPath, ProfileMigrator.ValidateRecoveryCandidate);
        Assert.Equal(AtomicJsonLoadSource.Temporary, temporary.Source);
        Assert.Equal(EconomyHoldingObservationState.Current, temporary.Value!.Statistics.Holdings.Money.State);
        Assert.Equal(200, temporary.Value.Statistics.Holdings.Money.Value);
        Assert.Equal(EconomyHoldingObservationState.Unavailable, temporary.Value.Statistics.Holdings.Cash.State);
        Assert.Null(temporary.Value.Statistics.Holdings.Cash.Value);
        Assert.Equal(
            AdapterCapabilityState.DisabledIncompatible,
            temporary.Value.Statistics.Holdings.Capabilities.Cash.State);
    }

    [Fact]
    [Trait("Category", "M15")]
    [Trait("Category", "Recovery")]
    public void CurrentSchemaMissingHoldingsRootIsRejectedBeforeMigration()
    {
        using var directory = new TemporaryDirectory();
        var store = new AtomicJsonStore<ProfileDocument>();
        var path = Path.Combine(directory.Path, "profile.json");
        var document = Document("generation-1", 1, 2);
        document.Statistics.Holdings = null!;
        store.Save(path, document);
        var json = File.ReadAllText(path);
        Assert.Contains("\"Holdings\":null", json, StringComparison.Ordinal);
        File.WriteAllText(path, json.Replace(",\"Holdings\":null", string.Empty, StringComparison.Ordinal));

        var result = store.Load(path, ProfileMigrator.ValidateRecoveryCandidate);

        Assert.False(result.Found);
        Assert.Contains(
            result.Failures,
            failure => failure.Contains("Missing required data member", StringComparison.Ordinal)
                       && failure.Contains("Holdings", StringComparison.Ordinal));
    }

    [Fact]
    [Trait("Category", "M15")]
    [Trait("Category", "Persistence")]
    public void GenerationRotationStartsUnavailableAndRejectsPriorGenerationEvidence()
    {
        using var directory = new TemporaryDirectory();
        var repository = Repository(
            directory.Path,
            "generation-1", "session-1",
            "generation-2", "session-2");
        repository.Open(Identity(1, 100));
        repository.RecordEconomyHoldingsDeferred(
            new EconomyHoldingsMutation("generation-1", Now, 50, 5, "native"));
        repository.Flush();

        repository.Rotate(Identity(1, 200), "DuckovNewGame");

        Assert.Equal("generation-2", repository.Current.GenerationId);
        Assert.Equal("generation-2", repository.Current.Statistics.Holdings.SaveGenerationId);
        Assert.Equal(EconomyHoldingObservationState.Unavailable, repository.Current.Statistics.Holdings.Money.State);
        Assert.Equal(EconomyHoldingObservationState.Unavailable, repository.Current.Statistics.Holdings.Cash.State);
        Assert.Throws<InvalidOperationException>(() => repository.RecordEconomyHoldingsDeferred(
            new EconomyHoldingsMutation("generation-1", Now, 50, 5, "stale")));
        repository.CloseClean();
    }

    private static ProfileRepository Repository(string root, params string[] ids)
    {
        var queue = new Queue<string>(ids);
        return new ProfileRepository(root, () => Now, queue.Dequeue);
    }

    private static SaveIdentitySnapshot Identity(int slot, long ticks) => new()
    {
        Slot = slot,
        SaveFilePresent = true,
        SaveFileCreationUtcTicks = ticks,
        ObservedWriteUtcTicks = ticks,
        ObservedLength = 10,
        GameVersion = "2.3.30",
        ContentSha256 = ticks.ToString("x", System.Globalization.CultureInfo.InvariantCulture).PadLeft(64, '0')
    };

    private static ProfileDocument Document(string generation, long money, long cash)
    {
        var holdings = new EconomyHoldingsSnapshot
        {
            SaveGenerationId = generation,
            Capabilities = EconomyHoldingsNativeContractPolicy.Supported("money", "cash", "liquid")
        };
        EconomyHoldingsReducer.Apply(
            holdings,
            new EconomyHoldingsMutation(generation, Now, money, cash, "native"));
        return new ProfileDocument
        {
            GenerationId = generation,
            Slot = 1,
            GenerationReason = "test",
            CreatedUtc = Now,
            UpdatedUtc = Now,
            Identity = Identity(1, 100),
            Statistics = new ProfileStatistics
            {
                SaveGenerationId = generation,
                CreatedUtc = Now,
                UpdatedUtc = Now,
                Holdings = holdings
            }
        };
    }

    private static void AssertCurrent(EconomyHoldingsSnapshot holdings, long money, long cash)
    {
        Assert.Equal(EconomyHoldingObservationState.Current, holdings.Money.State);
        Assert.Equal(money, holdings.Money.Value);
        Assert.Equal(EconomyHoldingObservationState.Current, holdings.Cash.State);
        Assert.Equal(cash, holdings.Cash.Value);
    }

    private static void AssertLastObserved(EconomyHoldingsSnapshot holdings, long money, long cash)
    {
        Assert.Equal(EconomyHoldingObservationState.LastObserved, holdings.Money.State);
        Assert.Equal(money, holdings.Money.Value);
        Assert.Equal(EconomyHoldingObservationState.LastObserved, holdings.Cash.State);
        Assert.Equal(cash, holdings.Cash.Value);
        Assert.Equal(EconomyHoldingObservationState.Unavailable, EconomyHoldingsReducer.Project(holdings).LiquidWealth.State);
    }
}
