using UltimateDuckovStatistics.Core.Compatibility;

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
}
