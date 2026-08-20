using UltimateDuckovStatistics.Core;

namespace UltimateDuckovStatistics.Tests;

public sealed class ProductInfoTests
{
    [Fact]
    public void VersionAndSchemaArePinnedForWorldTimeAndSleep()
    {
        Assert.Equal("0.12.0", ProductInfo.Version);
        Assert.Equal(12, ProductInfo.SchemaVersion);
    }
}
