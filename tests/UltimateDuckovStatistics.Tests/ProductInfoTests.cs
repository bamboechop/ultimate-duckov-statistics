using UltimateDuckovStatistics.Core;

namespace UltimateDuckovStatistics.Tests;

public sealed class ProductInfoTests
{
    [Fact]
    public void VersionAndSchemaArePinnedForContainers()
    {
        Assert.Equal("0.7.0", ProductInfo.Version);
        Assert.Equal(7, ProductInfo.SchemaVersion);
    }
}
