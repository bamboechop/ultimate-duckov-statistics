using UltimateDuckovStatistics.Core;

namespace UltimateDuckovStatistics.Tests;

public sealed class ProductInfoTests
{
    [Fact]
    public void VersionAndSchemaArePinnedForCurrentEconomyHoldings()
    {
        Assert.Equal("0.15.0", ProductInfo.Version);
        Assert.Equal(15, ProductInfo.SchemaVersion);
    }
}
