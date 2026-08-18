using UltimateDuckovStatistics.Core.Compatibility;

namespace UltimateDuckovStatistics.Tests;

public sealed class RetryableCleanupOwnerTests
{
    [Fact]
    [Trait("Category", "Run")]
    [Trait("Category", "Weapon")]
    [Trait("Category", "Lifecycle")]
    public void FailedCleanupRetainsTheSameOwnerUntilRetrySucceeds()
    {
        var resource = new TestResource { FailCleanup = true };
        var replacement = new TestResource();
        var owner = new RetryableCleanupOwner<TestResource>();
        owner.Assign(resource);

        Assert.False(owner.TryCleanup());
        Assert.Same(resource, owner.Value);
        Assert.Equal(1, resource.CleanupAttempts);
        Assert.Throws<InvalidOperationException>(() => owner.Assign(replacement));

        resource.FailCleanup = false;
        Assert.True(owner.TryCleanup());
        Assert.Null(owner.Value);
        Assert.Equal(2, resource.CleanupAttempts);
        Assert.True(owner.TryCleanup());
        Assert.Equal(2, resource.CleanupAttempts);

        owner.Assign(replacement);
        Assert.Same(replacement, owner.Value);
    }

    [Fact]
    [Trait("Category", "Run")]
    [Trait("Category", "Weapon")]
    [Trait("Category", "Lifecycle")]
    public void FailedCleanupSurvivesBehaviourDestructionAndBlocksReplacementSubscriptions()
    {
        var subscriptions = new SubscriptionCounter();
        var retained = new SubscribedResource(subscriptions) { FailCleanup = true };
        var destroyedBehaviour = new ProcessLifetimeCleanupOwner<SubscribedResource>();
        destroyedBehaviour.Assign(retained);

        Assert.False(destroyedBehaviour.TryCleanupOwned());
        Assert.True(destroyedBehaviour.HasPendingCleanup);
        Assert.Equal(1, subscriptions.Active);
        Assert.Equal(1, subscriptions.MaximumActive);

        destroyedBehaviour = null!;
        var replacementBehaviour = new ProcessLifetimeCleanupOwner<SubscribedResource>();
        var activationPermitted = !replacementBehaviour.HasValue
                                  || (replacementBehaviour.HasPendingCleanup
                                      && replacementBehaviour.TryCleanupPending());

        Assert.False(activationPermitted);
        Assert.Equal(1, subscriptions.Active);
        Assert.Equal(1, subscriptions.MaximumActive);

        retained.FailCleanup = false;
        Assert.True(replacementBehaviour.TryCleanupPending());
        Assert.False(replacementBehaviour.HasValue);
        Assert.False(replacementBehaviour.HasPendingCleanup);
        Assert.Equal(0, subscriptions.Active);

        var replacement = new SubscribedResource(subscriptions);
        replacementBehaviour.Assign(replacement);
        Assert.Same(replacement, replacementBehaviour.OwnedValue);
        Assert.Equal(1, subscriptions.Active);
        Assert.Equal(1, subscriptions.MaximumActive);
        Assert.True(replacementBehaviour.TryCleanupOwned());
        Assert.Equal(0, subscriptions.Active);
    }

    [Fact]
    [Trait("Category", "Run")]
    [Trait("Category", "Lifecycle")]
    [Trait("Category", "Persistence")]
    public void FailedProcessLifetimeCleanupKeepsItsDependencyAliveUntilSameProcessRetry()
    {
        var dependency = new CleanupDependency();
        var retained = new DependencyBoundCleanup(dependency);
        var destroyedBehaviour = new ProcessLifetimeCleanupOwner<DependencyBoundCleanup>();
        destroyedBehaviour.Assign(retained);

        Assert.False(destroyedBehaviour.TryCleanupOwned(dependency.Dispose));
        Assert.True(destroyedBehaviour.HasPendingCleanup);
        Assert.False(dependency.IsDisposed);
        Assert.Equal(1, retained.Attempts);

        destroyedBehaviour = null!;
        var replacementBehaviour = new ProcessLifetimeCleanupOwner<DependencyBoundCleanup>();
        Assert.True(replacementBehaviour.TryCleanupPending());

        Assert.Equal(2, retained.Attempts);
        Assert.True(dependency.WasAvailableDuringRetry);
        Assert.True(dependency.IsDisposed);
        Assert.False(replacementBehaviour.HasValue);
        Assert.False(replacementBehaviour.HasPendingCleanup);
    }

    private sealed class TestResource : IRetryableCleanup
    {
        public bool FailCleanup { get; set; }

        public int CleanupAttempts { get; private set; }

        public bool TryCleanup()
        {
            CleanupAttempts++;
            return !FailCleanup;
        }
    }

    private sealed class SubscriptionCounter
    {
        public int Active { get; set; }

        public int MaximumActive { get; set; }
    }

    private sealed class SubscribedResource : IRetryableCleanup
    {
        private readonly SubscriptionCounter subscriptions;
        private bool cleaned;

        public SubscribedResource(SubscriptionCounter subscriptions)
        {
            this.subscriptions = subscriptions;
            subscriptions.Active++;
            subscriptions.MaximumActive = Math.Max(subscriptions.MaximumActive, subscriptions.Active);
        }

        public bool FailCleanup { get; set; }

        public bool TryCleanup()
        {
            if (FailCleanup)
            {
                return false;
            }

            if (!cleaned)
            {
                cleaned = true;
                subscriptions.Active--;
            }

            return true;
        }
    }

    private sealed class CleanupDependency : IDisposable
    {
        public bool IsDisposed { get; private set; }

        public bool WasAvailableDuringRetry { get; set; }

        public void Dispose() => IsDisposed = true;
    }

    private sealed class DependencyBoundCleanup : IRetryableCleanup
    {
        private readonly CleanupDependency dependency;

        public DependencyBoundCleanup(CleanupDependency dependency)
        {
            this.dependency = dependency;
        }

        public int Attempts { get; private set; }

        public bool TryCleanup()
        {
            Attempts++;
            if (Attempts == 1) return false;
            dependency.WasAvailableDuringRetry = !dependency.IsDisposed;
            return dependency.WasAvailableDuringRetry;
        }
    }
}
