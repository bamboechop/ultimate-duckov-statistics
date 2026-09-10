using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Diagnostics;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;

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
        var now = 0d;
        var boundary = new NativeProfileTransitionBoundary(() => now);
        var diagnostics = new DiagnosticStore(Path.Combine(directory.Path, "diagnostics.json"), 100, () => TestTime);
        var failedAttempts = 0;
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
                failedAttempts++;
                repository.SetEconomyCapabilities(SupportedEconomyCapabilities());
            });

        Assert.False(boundary.Retry(() => true, message => diagnostics.Add(message)));
        var rotatedGeneration = repository.CurrentGenerationId;
        Assert.Equal("generation-two", rotatedGeneration);
        Assert.Equal(1, profileChangedCalls);
        Assert.True(boundary.HasPendingTransition);

        // ModBehaviour.Update and cleanup drains must not repeatedly hit the failed writer.
        for (var frame = 0; frame < 10_000; frame++)
        {
            Assert.False(boundary.Retry(() => true, message => diagnostics.Add(message)));
            Assert.False(boundary.Drain(() => true, message => diagnostics.Add(message)));
        }
        Assert.Equal(1, failedAttempts);
        Assert.Single(diagnostics.Entries);
        foreach (var retryTime in new[] { 1d, 3d, 7d, 15d, 31d, 63d, 123d })
        {
            now = retryTime;
            Assert.False(boundary.Retry(() => true, message => diagnostics.Add(message)));
        }
        Assert.Equal(8, failedAttempts);
        Assert.Equal(3, diagnostics.Entries.Count);
        Assert.Equal(1, profileChangedCalls);
        Directory.Delete(blockedTemporaryPath!);
        now = 183;
        Assert.True(boundary.Retry(() => true, _ => { }));

        Assert.False(boundary.HasPendingTransition);
        Assert.Equal(1, profileChangedCalls);
        Assert.Equal(rotatedGeneration, repository.CurrentGenerationId);
        Assert.Equal("generation-two", repository.Current.GenerationId);
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "M13")]
    [Trait("Category", "Persistence")]
    public void CraftCompletedWhileSaveTransitionIsDeferredCommitsOnlyToTargetGeneration()
    {
        using var directory = new TemporaryDirectory();
        var identities = new Queue<string>(
            ["generation-old", "session-old", "generation-target", "session-target", "session-reopen"]);
        var repository = new ProfileRepository(directory.Path, () => TestTime, identities.Dequeue);
        repository.Open(Identity(100, slot: 1));
        repository.SetCraftingCapabilities(CraftingNativeContractPolicy.Supported("completion", "formula"));
        var oldGeneration = repository.CurrentGenerationId;

        const long transitionId = 41;
        var now = 0d;
        var transition = new NativeProfileTransitionBoundary(() => now);
        var handoff = new CraftingProfileHandoffBoundary();
        handoff.Begin(transitionId);
        transition.Enqueue(
            "Save-slot transition",
            () => repository.Open(Identity(200, slot: 2)),
            () => repository.SetCraftingCapabilities(CraftingNativeContractPolicy.Supported("completion", "formula")),
            () => Assert.True(handoff.Complete(transitionId, repository.CurrentGenerationId)),
            () => Assert.True(handoff.TryFlushCompleted(repository.RecordCraftingDeferred)),
            repository.Flush);

        var acceptBoundary = false;
        Assert.False(transition.Retry(() => acceptBoundary, _ => { }));
        Assert.Equal(oldGeneration, repository.CurrentGenerationId);

        var completion = new CraftingCompletionBoundary();
        var token = completion.Begin(new CraftingCompletionEvidence("595", "Standard-Muni (S)", "2301", 30));
        Assert.True(completion.TryComplete(
            token,
            CraftingProfileHandoffBoundary.StagedGenerationId,
            TestTime.AddMinutes(1),
            out var staged));
        Assert.True(handoff.Stage(transitionId, staged));
        Assert.True(completion.FinishPublication(token));
        Assert.Equal(0, repository.Current.Statistics.Crafting.CompletionActions);

        acceptBoundary = true;
        now = 1;
        Assert.True(transition.Retry(() => acceptBoundary, _ => { }));
        var targetGeneration = repository.CurrentGenerationId;
        Assert.NotEqual(oldGeneration, targetGeneration);
        Assert.Equal(1, repository.Current.Statistics.Crafting.CompletionActions);
        Assert.Equal(30, repository.Current.Statistics.Crafting.ProducedQuantity);
        repository.CloseClean();

        repository.Open(Identity(100, slot: 1));
        Assert.Equal(oldGeneration, repository.CurrentGenerationId);
        Assert.Equal(0, repository.Current.Statistics.Crafting.CompletionActions);
        Assert.Equal(0, repository.Current.Statistics.Crafting.ProducedQuantity);
        repository.CloseClean();
    }

    [Fact]
    [Trait("Category", "M13")]
    [Trait("Category", "Persistence")]
    public void CraftCompletedWhileUserResetIsDeferredCommitsOnlyToResetGeneration()
    {
        using var directory = new TemporaryDirectory();
        var identities = new Queue<string>(
            ["generation-old", "session-old", "generation-reset", "session-reset"]);
        var repository = new ProfileRepository(directory.Path, () => TestTime, identities.Dequeue);
        var identity = Identity(100);
        repository.Open(identity);
        repository.SetCraftingCapabilities(CraftingNativeContractPolicy.Supported("completion", "formula"));
        var oldGeneration = repository.CurrentGenerationId;

        const long transitionId = 42;
        var now = 0d;
        var transition = new NativeProfileTransitionBoundary(() => now);
        var handoff = new CraftingProfileHandoffBoundary();
        var profileWriterAvailable = false;
        NativeProfileResetTransition.Queue(
            transitionId,
            craftingProfileChangeStarted: handoff.Begin,
            enqueueTransition: (description, steps) => transition.Enqueue(description, steps),
            profileChanging: () => { },
            waitRunCheckpoint: () => { },
            drainProfileWriter: () =>
            {
                if (!profileWriterAvailable)
                    throw new IOException("Deferred profile writer remains pending.");
            },
            refreshIdentity: () => repository.RefreshIdentity(identity),
            rotateRepository: () => repository.Rotate(identity, "UserReset"),
            openDiagnostics: () => { },
            worldTimeProfileChanged: () => { },
            craftingProfileChangeCompleted: completedTransitionId =>
            {
                Assert.True(handoff.Complete(completedTransitionId, repository.CurrentGenerationId));
                Assert.True(handoff.TryFlushCompleted(repository.RecordCraftingDeferred));
            },
            profileChanged: () => { },
            applyCurrentMetricCapabilities: () => repository.SetCraftingCapabilities(
                CraftingNativeContractPolicy.Supported("completion", "formula")),
            writeDiagnostic: repository.Flush);

        Assert.False(transition.Retry(() => true, _ => { }));
        Assert.Equal(oldGeneration, repository.CurrentGenerationId);

        var completion = new CraftingCompletionBoundary();
        var token = completion.Begin(new CraftingCompletionEvidence("100", "Tierfalle", "1005", 1));
        Assert.True(completion.TryComplete(
            token,
            CraftingProfileHandoffBoundary.StagedGenerationId,
            TestTime.AddMinutes(1),
            out var staged));
        Assert.True(handoff.Stage(transitionId, staged));
        Assert.True(completion.FinishPublication(token));
        Assert.Equal(0, repository.Current.Statistics.Crafting.CompletionActions);

        profileWriterAvailable = true;
        now = 1;
        Assert.True(transition.Retry(() => true, _ => { }));
        Assert.NotEqual(oldGeneration, repository.CurrentGenerationId);
        Assert.Equal(1, repository.Current.Statistics.Crafting.CompletionActions);
        Assert.Equal(1, repository.Current.Statistics.Crafting.ProducedQuantity);
        repository.CloseClean();

        var archive = Assert.Single(Directory.EnumerateDirectories(Path.Combine(
            directory.Path,
            "profiles",
            "slot-01",
            "archives")));
        var archivedProfilePath = Path.Combine(archive, "profile.json");
        Assert.True((File.GetAttributes(archivedProfilePath) & FileAttributes.ReadOnly) != 0);
        var archived = new AtomicJsonStore<ProfileDocument>().Load(archivedProfilePath).Value!;
        Assert.Equal(oldGeneration, archived.GenerationId);
        Assert.Equal(0, archived.Statistics.Crafting.CompletionActions);
        Assert.Equal(0, archived.Statistics.Crafting.ProducedQuantity);
    }

    [Fact]
    public void DeferredObserverIsNotReenteredUntilDueAndNextTransitionHasItsOwnBudget()
    {
        var now = 0d;
        var boundary = new NativeProfileTransitionBoundary(() => now);
        var observations = 0;
        var steps = 0;
        var diagnostics = new List<string>();
        boundary.Enqueue("first", () => steps++);
        boundary.Enqueue("second", () => steps++);
        bool Observe() { observations++; return now >= 1; }
        Assert.False(boundary.Retry(Observe, diagnostics.Add));
        for (var frame = 0; frame < 10_000; frame++)
            Assert.False(boundary.Drain(Observe, diagnostics.Add));
        Assert.Equal(1, observations);
        Assert.Equal(0, steps);
        Assert.Single(diagnostics);
        now = 1;
        Assert.True(boundary.Drain(Observe, diagnostics.Add));
        Assert.Equal(3, observations);
        Assert.Equal(2, steps);
    }

    private static SaveIdentitySnapshot Identity(long creationTicks, int slot = 1) => new()
    {
        Slot = slot,
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
            RouteAttribution = Supported()
        };
    }
}
