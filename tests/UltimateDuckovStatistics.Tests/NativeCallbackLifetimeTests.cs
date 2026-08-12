using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Compatibility;

namespace UltimateDuckovStatistics.Tests;

public sealed class NativeCallbackLifetimeTests
{
    [Fact]
    [Trait("Category", "Equipment")]
    public void ThreeArgumentGuardStopsInventoryCallbacksAfterDisposalStarts()
    {
        var lifetime = new NativeCallbackLifetime();
        var calls = 0;
        var guarded = lifetime.Guard<int, string, bool>((_, _, _) => calls++);
        Assert.True(lifetime.Activate(Array.Empty<SubscriptionBinding>()));

        guarded(1, "inventory", true);
        Assert.Equal(1, calls);

        Assert.True(lifetime.TryCleanup(() => true, out var failure));
        Assert.Null(failure);
        guarded(2, "inventory", false);
        Assert.Equal(1, calls);
    }

    [Fact]
    [Trait("Category", "Run")]
    [Trait("Category", "Weapon")]
    [Trait("Category", "Lifecycle")]
    public void FailedLevelCallbackRemovalCannotReacquireAnInstanceHandlerAfterDisposal()
    {
        Action? levelInitialized = null;
        Action? positionChanged = null;
        var failLevelCleanup = true;
        var lifetime = new NativeCallbackLifetime();
        Action positionHandler = () => { };
        var guardedLevelHandler = lifetime.Guard(() => positionChanged += positionHandler);
        var binding = new SubscriptionBinding(
            () => levelInitialized += guardedLevelHandler,
            () =>
            {
                if (failLevelCleanup)
                {
                    throw new InvalidOperationException("injected level callback cleanup failure");
                }

                levelInitialized -= guardedLevelHandler;
            });

        Assert.True(lifetime.Activate(new[] { binding }));
        levelInitialized?.Invoke();
        Assert.Single(positionChanged!.GetInvocationList());

        Assert.False(lifetime.TryCleanup(DetachPositionHandler, out var cleanupFailure));
        Assert.IsType<AggregateException>(cleanupFailure);
        Assert.Null(positionChanged);
        Assert.Single(levelInitialized!.GetInvocationList());

        levelInitialized.Invoke();
        Assert.Null(positionChanged);

        failLevelCleanup = false;
        Assert.True(lifetime.TryCleanup(DetachPositionHandler, out cleanupFailure));
        Assert.Null(cleanupFailure);
        Assert.Null(levelInitialized);
        Assert.Null(positionChanged);
        Assert.False(lifetime.CanHandleCallbacks);

        bool DetachPositionHandler()
        {
            positionChanged -= positionHandler;
            return positionChanged == null;
        }
    }
}
