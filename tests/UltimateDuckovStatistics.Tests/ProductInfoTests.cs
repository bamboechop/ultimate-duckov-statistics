using UltimateDuckovStatistics.Core;

namespace UltimateDuckovStatistics.Tests;

public sealed class ProductInfoTests
{
    [Fact]
    public void VersionAndSchemaArePinnedForPerformanceHardening()
    {
        Assert.Equal("0.8.1", ProductInfo.Version);
        Assert.Equal(8, ProductInfo.SchemaVersion);
    }
}
