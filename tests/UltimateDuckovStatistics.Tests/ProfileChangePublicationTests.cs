using UltimateDuckovStatistics.Adapters;

namespace UltimateDuckovStatistics.Tests;

public sealed class ProfileChangePublicationTests
{
    [Fact]
    [Trait("Category", "Persistence")]
    [Trait("Category", "M9")]
    public void FailedActivationSubscriberDoesNotSkipBaselineResetSubscriber()
    {
        var resetCalls = 0;
        var failures = new List<Exception>();
        Action subscribers = () => throw new IOException("activation persistence failed");
        subscribers += () => resetCalls++;

        ProfileChangePublication.PublishIndependently(subscribers, failures.Add);

        Assert.Equal(1, resetCalls);
        Assert.IsType<IOException>(Assert.Single(failures));
    }
}
