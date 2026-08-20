using UltimateDuckovStatistics.Core.Compatibility;

namespace UltimateDuckovStatistics.Tests;

public sealed class WorldTimeLifecycleTests
{
    [Fact]
    public void ProfileDependencySurvivesUntilRunAndWorldTimeCleanupBothSucceed()
    {
        var disposals = 0;
        var gate = new CleanupCompletionGate(2, () => disposals++);
        var worldTimeResource = new WorldTimeCleanupResource { FailCleanup = true };
        var runResource = new RunCleanupResource();
        var worldTimeOwner = new ProcessLifetimeCleanupOwner<WorldTimeCleanupResource>();
        var runOwner = new ProcessLifetimeCleanupOwner<RunCleanupResource>();
        worldTimeOwner.Assign(worldTimeResource);
        runOwner.Assign(runResource);

        Assert.False(worldTimeOwner.TryCleanupOwned(gate.Signal));
        Assert.True(runOwner.TryCleanupOwned(gate.Signal));
        gate.Signal();

        Assert.Equal(1, gate.Remaining);
        Assert.Equal(0, disposals);

        worldTimeResource.FailCleanup = false;
        Assert.True(worldTimeOwner.TryCleanupPending());

        Assert.Equal(0, gate.Remaining);
        Assert.Equal(1, disposals);
        gate.Signal();
        Assert.Equal(1, disposals);
    }

    private sealed class WorldTimeCleanupResource : IRetryableCleanup
    {
        public bool FailCleanup { get; set; }

        public bool TryCleanup() => !FailCleanup;
    }

    private sealed class RunCleanupResource : IRetryableCleanup
    {
        public bool TryCleanup() => true;
    }
}
