using UltimateDuckovStatistics.Core;

namespace UltimateDuckovStatistics.Tests;

public sealed class ProductInfoTests
{
    [Fact]
    public void VersionAndSchemaArePinnedForCraftingResourceConsumption()
    {
        Assert.Equal("0.16.0", ProductInfo.Version);
        Assert.Equal(16, ProductInfo.SchemaVersion);
    }
}
