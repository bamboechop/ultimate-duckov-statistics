using UltimateDuckovStatistics.Core.Compatibility;

namespace UltimateDuckovStatistics.Tests;

public sealed class RetryableCleanupOwnerTests
{
    [Fact]
    [Trait("Category", "Run")]
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
}
