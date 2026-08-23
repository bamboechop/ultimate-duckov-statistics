using UltimateDuckovStatistics.Core;

namespace UltimateDuckovStatistics.Tests;

public sealed class ProductInfoTests
{
    [Fact]
    public void VersionAndSchemaArePinnedForCraftedItemStatistics()
    {
        Assert.Equal("0.13.0", ProductInfo.Version);
        Assert.Equal(13, ProductInfo.SchemaVersion);
    }
}
