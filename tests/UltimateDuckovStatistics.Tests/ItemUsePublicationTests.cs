using UltimateDuckovStatistics.Adapters;

namespace UltimateDuckovStatistics.Tests;

public sealed class ItemUsePublicationTests
{
    [Fact]
    [Trait("Category", "ItemUse")]
    [Trait("Category", "Persistence")]
    public void FailedProfilePersistenceDoesNotSkipAcceptedActiveRunPublication()
    {
        var profileAttempts = 0;
        var activeRunAttempts = 0;

        var published = ItemUsePublication.PublishIndependently(
            () =>
            {
                profileAttempts++;
                return false;
            },
            () =>
            {
                activeRunAttempts++;
                return true;
            });

        Assert.True(published);
        Assert.Equal(1, profileAttempts);
        Assert.Equal(1, activeRunAttempts);
    }

    [Fact]
    [Trait("Category", "M9")]
    [Trait("Category", "Persistence")]
    public void ThrowingProfileEconomyDestinationStillAttemptsActiveRunDestination()
    {
        var activeRunAttempts = 0;

        Assert.Throws<IOException>(() => ItemUsePublication.PublishIndependently(
            () => throw new IOException("profile write failed"),
            () =>
            {
                activeRunAttempts++;
                return true;
            }));

        Assert.Equal(1, activeRunAttempts);
    }

    [Fact]
    [Trait("Category", "M9")]
    [Trait("Category", "Persistence")]
    public void ThrowingActiveRunEconomyDestinationDoesNotUndoProfileDestination()
    {
        var profileAttempts = 0;

        Assert.Throws<IOException>(() => ItemUsePublication.PublishIndependently(
            () =>
            {
                profileAttempts++;
                return true;
            },
            () => throw new IOException("active-run write failed")));

        Assert.Equal(1, profileAttempts);
    }
}
