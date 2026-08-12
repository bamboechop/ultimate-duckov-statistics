using UltimateDuckovStatistics.Adapters;

namespace UltimateDuckovStatistics.Tests;

public sealed class NativeContainerAdapterPublicationTests
{
    [Fact]
    [Trait("Category", "Container")]
    public void ProfilePublicationFailureDoesNotSkipActiveRunPublication()
    {
        var expected = new IOException("Profile write failed.");
        var profileAttempts = 0;
        var activeRunAttempts = 0;

        var actual = Assert.Throws<IOException>(() => NativeContainerAdapter.PublishIndependently(
            () =>
            {
                profileAttempts++;
                throw expected;
            },
            () => activeRunAttempts++));

        Assert.Same(expected, actual);
        Assert.Equal(1, profileAttempts);
        Assert.Equal(1, activeRunAttempts);
    }
}
