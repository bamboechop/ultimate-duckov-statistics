using UltimateDuckovStatistics.Core;

namespace UltimateDuckovStatistics.Tests;

public sealed class ProductInfoTests
{
    [Fact]
    public void VersionAndSchemaArePinnedForMultiMapRoutes()
    {
        Assert.Equal("0.8.0", ProductInfo.Version);
        Assert.Equal(8, ProductInfo.SchemaVersion);
    }
}
