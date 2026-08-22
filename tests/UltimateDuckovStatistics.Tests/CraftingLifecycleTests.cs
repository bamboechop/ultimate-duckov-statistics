using UltimateDuckovStatistics.Core.Compatibility;
using UltimateDuckovStatistics.Core.Domain;
using UltimateDuckovStatistics.Core.Persistence;
using UltimateDuckovStatistics.Core.Statistics;
using UltimateDuckovStatistics.Core.Tracking;

namespace UltimateDuckovStatistics.Tests;

public sealed class CraftingLifecycleTests
{
    [Fact]
    [Trait("Category", "M13")]
    public void ProfileDependencySurvivesUntilCraftingWorldTimeAndRunCleanupAllSucceed()
    {
        var disposals = 0;
        var gate = new CleanupCompletionGate(3, () => disposals++);
        var craftingResource = new CraftingCleanupResource { FailCleanup = true };
        var worldTimeResource = new WorldTimeCleanupResource();
        var runResource = new RunCleanupResource();
        var craftingOwner = new ProcessLifetimeCleanupOwner<CraftingCleanupResource>();
        var worldTimeOwner = new ProcessLifetimeCleanupOwner<WorldTimeCleanupResource>();
        var runOwner = new ProcessLifetimeCleanupOwner<RunCleanupResource>();
        craftingOwner.Assign(craftingResource);
        worldTimeOwner.Assign(worldTimeResource);
        runOwner.Assign(runResource);

        Assert.False(craftingOwner.TryCleanupOwned(gate.Signal));
        Assert.True(worldTimeOwner.TryCleanupOwned(gate.Signal));
        gate.Signal();
        Assert.True(runOwner.TryCleanupOwned(gate.Signal));
        gate.Signal();

        Assert.Equal(1, gate.Remaining);
        Assert.Equal(0, disposals);

        craftingResource.FailCleanup = false;
        Assert.True(craftingOwner.TryCleanupPending());

        Assert.Equal(0, gate.Remaining);
        Assert.Equal(1, disposals);
        gate.Signal();
        Assert.Equal(1, disposals);
    }

    [Fact]
    [Trait("Category", "M13")]
    public void TerminalShutdownAbandonsIncompleteCraftFlushesProvenAggregateAndClosesSessionCleanly()
    {
        using var directory = new TemporaryDirectory();
        var ids = new Queue<string>(["generation-1", "session-1", "session-2"]);
        var identity = new SaveIdentitySnapshot
        {
            Slot = 1,
            SaveFilePresent = true,
            SaveFileCreationUtcTicks = 100,
            ObservedWriteUtcTicks = 100,
            ObservedLength = 10,
            GameVersion = "2.3.30",
            ContentSha256 = new string('0', 64)
        };
        var repository = new ProfileRepository(
            directory.Path,
            () => new DateTime(2026, 8, 22, 16, 0, 0, DateTimeKind.Utc),
            ids.Dequeue);
        repository.Open(identity);
        repository.SetCraftingCapabilities(CraftingNativeContractPolicy.Supported("completion", "formula"));

        var boundary = new CraftingCompletionBoundary();
        var pendingPublication = new CraftingPendingAccumulator();
        _ = boundary.Begin(Evidence("100", "Tierfalle", "1005", 1));
        var proven = boundary.Begin(Evidence("595", "Standard-Muni (S)", "2301", 30));
        Assert.True(boundary.TryComplete(
            proven,
            repository.CurrentGenerationId,
            new DateTime(2026, 8, 22, 16, 1, 0, DateTimeKind.Utc),
            out var provenMutation));
        Assert.True(pendingPublication.Add(provenMutation));
        Assert.True(boundary.FinishPublication(proven));

        var resource = new TerminalCraftingCleanupResource(boundary, pendingPublication, repository);
        var owner = new ProcessLifetimeCleanupOwner<TerminalCraftingCleanupResource>();
        var gate = new CleanupCompletionGate(3, repository.CloseClean);
        owner.Assign(resource);

        Assert.False(owner.TryCleanupOwned(gate.Signal));
        gate.Signal();
        gate.Signal();
        Assert.Equal(1, gate.Remaining);
        Assert.Equal(1, boundary.PendingCount);
        Assert.Equal(0, repository.Current.Statistics.Crafting.CompletionActions);

        Assert.Equal(1, boundary.AbandonUnprovenForTerminalShutdown());
        Assert.True(owner.TryCleanupPending());
        Assert.Equal(0, gate.Remaining);

        var reopened = new ProfileRepository(
            directory.Path,
            () => new DateTime(2026, 8, 22, 16, 2, 0, DateTimeKind.Utc),
            ids.Dequeue);
        var open = reopened.Open(identity);
        Assert.False(open.InterruptedSessionRecovered);
        Assert.Equal(0, reopened.Current.InterruptedSessionCount);
        Assert.Equal(1, reopened.Current.Statistics.Crafting.CompletionActions);
        Assert.Equal(30, reopened.Current.Statistics.Crafting.ProducedQuantity);
        Assert.Equal(1, reopened.Current.Statistics.Crafting.Outputs["595"].CompletionActions);
        Assert.DoesNotContain("100", reopened.Current.Statistics.Crafting.Outputs.Keys);
        reopened.CloseClean();
    }

    private sealed class CraftingCleanupResource : IRetryableCleanup
    {
        public bool FailCleanup { get; set; }

        public bool TryCleanup() => !FailCleanup;
    }

    private sealed class WorldTimeCleanupResource : IRetryableCleanup
    {
        public bool TryCleanup() => true;
    }

    private sealed class RunCleanupResource : IRetryableCleanup
    {
        public bool TryCleanup() => true;
    }

    private sealed class TerminalCraftingCleanupResource : IRetryableCleanup
    {
        private readonly CraftingCompletionBoundary boundary;
        private readonly CraftingPendingAccumulator pendingPublication;
        private readonly ProfileRepository repository;

        public TerminalCraftingCleanupResource(
            CraftingCompletionBoundary boundary,
            CraftingPendingAccumulator pendingPublication,
            ProfileRepository repository)
        {
            this.boundary = boundary;
            this.pendingPublication = pendingPublication;
            this.repository = repository;
        }

        public bool TryCleanup()
        {
            if (boundary.OutstandingCount != 0) return false;
            if (!pendingPublication.TryFlush(repository.RecordCraftingDeferred)) return false;
            repository.Flush();
            return true;
        }
    }

    private static CraftingCompletionEvidence Evidence(
        string outputItemId,
        string displayName,
        string recipeId,
        long producedQuantity) => new(outputItemId, displayName, recipeId, producedQuantity);
}
