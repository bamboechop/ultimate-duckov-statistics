using UltimateDuckovStatistics.Core.Compatibility;

namespace UltimateDuckovStatistics.Tests;

public sealed class IdempotentSubscriptionSetTests
{
    [Fact]
    [Trait("Category", "Run")]
    [Trait("Category", "Lifecycle")]
    public void RepeatedActivationAndDeactivationDoNotDuplicateSubscriptionsOrSamplers()
    {
        var subscriptions = 0;
        var samplerStarts = 0;
        var set = new IdempotentSubscriptionSet();
        var bindings = new[]
        {
            new SubscriptionBinding(() => subscriptions++, () => subscriptions--),
            new SubscriptionBinding(() => samplerStarts++, () => samplerStarts--)
        };

        Assert.True(set.Activate(bindings));
        Assert.False(set.Activate(bindings));
        Assert.Equal(1, subscriptions);
        Assert.Equal(1, samplerStarts);
        Assert.True(set.Deactivate());
        Assert.False(set.Deactivate());
        Assert.Equal(0, subscriptions);
        Assert.Equal(0, samplerStarts);
    }

    [Fact]
    [Trait("Category", "Run")]
    [Trait("Category", "Lifecycle")]
    public void PartialActivationFailureRollsBackEarlierSubscriptions()
    {
        var subscriptions = 0;
        var set = new IdempotentSubscriptionSet();
        var bindings = new[]
        {
            new SubscriptionBinding(() => subscriptions++, () => subscriptions--),
            new SubscriptionBinding(() => throw new InvalidOperationException("injected"), () => { })
        };

        Assert.Throws<InvalidOperationException>(() => set.Activate(bindings));
        Assert.Equal(0, subscriptions);
        Assert.False(set.IsActive);
        Assert.False(set.HasPendingCleanup);
    }

    [Fact]
    [Trait("Category", "Run")]
    [Trait("Category", "Lifecycle")]
    public void FailedDeactivationRemainsRetryableAndCannotReactivateOverLeftovers()
    {
        var subscribed = false;
        var failCleanup = true;
        var set = new IdempotentSubscriptionSet();
        var bindings = new[]
        {
            new SubscriptionBinding(
                () => subscribed = true,
                () =>
                {
                    if (failCleanup)
                    {
                        throw new InvalidOperationException("injected cleanup failure");
                    }

                    subscribed = false;
                })
        };

        set.Activate(bindings);
        Assert.Throws<AggregateException>(() => set.Deactivate());
        Assert.False(set.IsActive);
        Assert.True(set.HasPendingCleanup);
        Assert.True(subscribed);
        Assert.Throws<InvalidOperationException>(() => set.Activate(bindings));

        failCleanup = false;
        Assert.True(set.Deactivate());
        Assert.False(subscribed);
        Assert.False(set.HasPendingCleanup);
        Assert.True(set.Activate(bindings));
        Assert.True(set.Deactivate());
    }
}
