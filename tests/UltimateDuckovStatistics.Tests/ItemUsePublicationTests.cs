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
}
