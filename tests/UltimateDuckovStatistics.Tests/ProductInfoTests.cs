using UltimateDuckovStatistics.Core;

namespace UltimateDuckovStatistics.Tests;

public sealed class ProductInfoTests
{
    [Fact]
    public void VersionAndSchemaArePinnedForPerformanceHardening()
    {
        Assert.Equal("0.9.0", ProductInfo.Version);
        Assert.Equal(9, ProductInfo.SchemaVersion);
    }
}
