using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;

namespace UltimateDuckovStatistics.Tests;

public sealed class NativeProfileTransitionBoundaryTests
{
    private static readonly DateTime TestTime = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    [Trait("Category", "M9")]
    [Trait("Category", "Persistence")]
    public void FailureAfterDurableRotationRetriesTheRemainingStepWithoutRotatingAgain()
    {
        using var directory = new TemporaryDirectory();
        var identities = new Queue<string>(
            ["generation-one", "session-one", "generation-two", "session-two", "generation-three", "session-three"]);
        var repository = new ProfileRepository(directory.Path, () => TestTime, identities.Dequeue);
        repository.Open(Identity(100));
        var boundary = new NativeProfileTransitionBoundary();
        var profileChangedCalls = 0;
        string? blockedTemporaryPath = null;
        boundary.Enqueue(
            "Save-deletion rotation",
            () => repository.Rotate(Identity(200), "DuckovSaveDeleted"),
            () => profileChangedCalls++,
            () =>
            {
                if (blockedTemporaryPath == null)
                {
                    blockedTemporaryPath = AtomicJsonPaths.GetTemporaryPath(repository.CurrentProfilePath!);
                    Directory.CreateDirectory(blockedTemporaryPath);
                }
                repository.SetEconomyCapabilities(SupportedEconomyCapabilities());
            });

        Assert.False(boundary.Retry(() => true, _ => { }));
        var rotatedGeneration = repository.CurrentGenerationId;
        Assert.Equal("generation-two", rotatedGeneration);
        Assert.Equal(1, profileChangedCalls);
        Assert.True(boundary.HasPendingTransition);

        Directory.Delete(blockedTemporaryPath!);
        Assert.True(boundary.Retry(() => true, _ => { }));

        Assert.False(boundary.HasPendingTransition);
        Assert.Equal(1, profileChangedCalls);
        Assert.Equal(rotatedGeneration, repository.CurrentGenerationId);
        Assert.Equal("generation-two", repository.Current.GenerationId);
        repository.CloseClean();
    }

    private static SaveIdentitySnapshot Identity(long creationTicks) => new()
    {
        Slot = 1,
        SaveFilePresent = true,
        SaveFileCreationUtcTicks = creationTicks,
        ObservedWriteUtcTicks = creationTicks + 10,
        ObservedLength = 4096,
        GameVersion = "2.3.30",
        ContentSha256 = new string('a', 64),
        SaveTimeBinary = TestTime.AddTicks(creationTicks).ToBinary()
    };

    private static EconomyMetricCapabilities SupportedEconomyCapabilities()
    {
        MetricAvailability Supported() => new()
        {
            State = AdapterCapabilityState.Supported,
            Provenance = "test"
        };
        return new EconomyMetricCapabilities
        {
            MoneyAmountDirection = Supported(),
            MoneySourceAttribution = Supported(),
            MoneyContextAttribution = Supported(),
            CashAmountDirection = Supported(),
            CashExternalAcquisition = Supported(),
            CashContextAttribution = Supported(),
            CashTerminalOutcomes = Supported(),
            RouteAttribution = Supported()
        };
    }
}
