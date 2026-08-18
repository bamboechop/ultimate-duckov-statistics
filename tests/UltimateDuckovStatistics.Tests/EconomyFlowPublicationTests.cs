using UltimateDuckovStatistics.Adapters;
using UltimateDuckovStatistics.Core.Domain;

namespace UltimateDuckovStatistics.Tests;

public sealed class EconomyFlowPublicationTests
{
    [Fact]
    [Trait("Category", "M9")]
    [Trait("Category", "Persistence")]
    public void PartialFanOutRetriesOnlyTheDestinationThatHasNotAcceptedTheFlow()
    {
        var profileCalls = 0;
        var runCalls = 0;
        var runReady = false;
        var publisher = new EconomyFlowPublication(
            _ =>
            {
                profileCalls++;
                return true;
            },
            _ =>
            {
                runCalls++;
                return runReady;
            },
            _ => { });
        var flow = new CurrencyFlowRecorded
        {
            EventId = "economy:activation:money:1",
            RunId = "run-one",
            GameplayContext = GameplayContext.Raid
        };

        Assert.False(publisher.Publish(flow));
        runReady = true;
        Assert.True(publisher.Publish(flow));

        Assert.Equal(1, profileCalls);
        Assert.Equal(2, runCalls);
    }
}
